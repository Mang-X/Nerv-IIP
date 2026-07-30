using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Seed;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.DistributedLocking;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Prometheus;


var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-wms");

    builder.Services.AddHealthChecks();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.Configure<WcsRetryOptions>(builder.Configuration.GetSection("Wcs:Retry"));
    builder.Services.AddMvc();
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    var inventoryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Inventory:BaseUrl", "http://localhost:5109");
    builder.Services.AddHttpClient<IWmsInventoryReservationClient, HttpWmsInventoryReservationClient>(client =>
    {
        client.BaseAddress = inventoryBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business WMS";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_wms_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddWmsPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<WorldHistorySeedService>();
    builder.Services.AddScoped<WorldHistoryWarehouseOpsSeedService>();
    builder.Services.AddScoped<WarehouseWorkScopeAuthorizer>();
    builder.Services.AddScoped<WarehouseAssignedResourceExecutionAuthorizer>();
    builder.Services.AddNervIipCommandLocking(
        builder.Configuration,
        builder.Environment,
        isTesting,
        WmsFacts.ServiceName);
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
    builder.Services.AddHttpClient<IWcsCancellationAdapter, HttpWcsCancellationAdapter>().UseHttpClientMetrics();
    builder.Services.AddContext().AddEnvContext().AddCapContextProcessor();
    builder.Services.AddNetCorePalServiceDiscoveryClient();
    if (isTesting)
    {
        builder.Services.AddIntegrationEvents(typeof(Program));
    }
    else
    {
        builder.Services.AddIntegrationEvents(typeof(Program))
            .UseCap<ApplicationDbContext>(b =>
            {
                b.RegisterServicesFromAssemblies(typeof(Program));
                b.AddContextIntegrationFilters();
            });

        builder.Services.AddCap(x =>
        {
            x.Version = builder.Configuration["Cap:Version"] ?? "v1";
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
            x.UseDashboard();
        });
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());
    builder.Services.AddScoped<ICommandLock<CompleteInboundOrderCommand>, CompleteInboundOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<CompleteOutboundOrderCommand>, CompleteOutboundOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<CompleteCountExecutionCommand>, CompleteCountExecutionCommandLock>();
    builder.Services.AddScoped<
        ICommandLock<StartWarehouseTaskCommand>,
        WarehouseTaskActionCommandLock<StartWarehouseTaskCommand>>();
    builder.Services.AddScoped<
        ICommandLock<RecordWarehouseTaskProgressActionCommand>,
        WarehouseTaskActionCommandLock<RecordWarehouseTaskProgressActionCommand>>();
    builder.Services.AddScoped<
        ICommandLock<ReportWarehouseTaskExceptionCommand>,
        WarehouseTaskActionCommandLock<ReportWarehouseTaskExceptionCommand>>();
    builder.Services.AddScoped<
        ICommandLock<CompleteWarehouseTaskActionCommand>,
        WarehouseTaskActionCommandLock<CompleteWarehouseTaskActionCommand>>();
    builder.Services.AddScoped<
        ICommandLock<DispatchWcsTaskCommand>,
        DispatchWcsTaskCommandLock>();
    builder.Services.AddScoped<
        ICommandLock<CompleteWcsTaskCommand>,
        WcsTaskCallbackCommandLock<CompleteWcsTaskCommand>>();
    builder.Services.AddScoped<
        ICommandLock<FailWcsTaskCommand>,
        WcsTaskCallbackCommandLock<FailWcsTaskCommand>>();
    builder.Services.AddScoped<
        ICommandLock<AssignInboundOrderCommand>,
        WarehouseAssignmentCommandLock<AssignInboundOrderCommand>>();
    builder.Services.AddScoped<
        ICommandLock<AssignPutawayTaskCommand>,
        WarehouseAssignmentCommandLock<AssignPutawayTaskCommand>>();
    builder.Services.AddScoped<
        ICommandLock<AssignOutboundOrderCommand>,
        WarehouseAssignmentCommandLock<AssignOutboundOrderCommand>>();
    builder.Services.AddScoped<
        ICommandLock<AssignPickingTaskCommand>,
        WarehouseAssignmentCommandLock<AssignPickingTaskCommand>>();
    builder.Services.AddScoped<
        ICommandLock<AssignCountExecutionCommand>,
        WarehouseAssignmentCommandLock<AssignCountExecutionCommand>>();
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = WmsFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessWms in Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // 《工厂世界观设定集》L1 背景历史（仓储域侧）。校验器 fail-closed：对账不平就让启动失败。
    // WMS 没有固定演示 seed，因此这里直接以 History 开关为准，并沿用「只在 Development 允许」的口径。
    if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "LeaderDemo:History:Enabled=true is only allowed for BusinessWms in Development.");
        }

        using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
            builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev",
            WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history WMS seed completed: {Inbound} inbound orders, {Outbound} outbound orders, " +
            "{Tasks} warehouse tasks, {Requests} Inventory movement requests; validator checked " +
            "{CheckedInbound} inbound / {CheckedOutbound} outbound / {CheckedTasks} tasks " +
            "({Putaway} putaway, {Picking} picking) / {CheckedRequests} posted requests.",
            report.InboundOrdersWritten,
            report.OutboundOrdersWritten,
            report.WarehouseTasksWritten,
            report.InventoryMovementRequestsWritten,
            report.Validation.InboundOrdersChecked,
            report.Validation.OutboundOrdersChecked,
            report.Validation.WarehouseTasksChecked,
            report.Validation.PutawayTasksChecked,
            report.Validation.PickingTasksChecked,
            report.Validation.PostedMovementRequestsChecked);
        foreach (var line in report.Validation.Sample)
        {
            app.Logger.LogInformation("World-history sample: {Document}", line);
        }

        // 仓储自动化 / 盘点执行 / 来料退货块必须排在单据块之后：
        // WCS 任务要绑真实落库的仓储作业任务，退货要挂真实落库的收货入库单。
        var opsReport = await scope.ServiceProvider.GetRequiredService<WorldHistoryWarehouseOpsSeedService>().SeedAsync(
            builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev",
            WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history WMS operations seed completed: {Counts} count executions, {Returns} supplier returns, " +
            "current queue {QueueInbound} inbound / {QueuePutaway} putaway / {QueueOutbound} outbound / " +
            "{QueuePicking} picking ({QueueReviewReady} review-ready), " +
            "{Pools} work pools, {Memberships} memberships, {Assignments} controlled assignments, " +
            "{WcsTasks} WCS tasks, {Circuits} dispatch circuits; validator checked {CheckedCounts} counts " +
            "({CompletedCounts} completed, {VarianceCounts} with variance) / {CheckedWcs} WCS tasks " +
            "({CompletedWcs} completed, {FailedWcs} failed) / {CheckedCircuits} circuits / {CheckedReturns} returns / " +
            "{CheckedPools} pools / {CheckedMemberships} memberships / assignments " +
            "{CheckedInbound}/{CheckedPutaway}/{CheckedPicking}/{CheckedOutbound}/{CheckedCount}.",
            opsReport.CountExecutionsWritten,
            opsReport.SupplierReturnRequestsWritten,
            opsReport.CurrentQueue.InboundOrdersWritten,
            opsReport.CurrentQueue.PutawayTasksWritten,
            opsReport.CurrentQueue.OutboundOrdersWritten,
            opsReport.CurrentQueue.PickingTasksWritten,
            opsReport.CurrentQueue.ReviewReadyOrdersWritten,
            opsReport.WorkPoolsWritten,
            opsReport.WorkPoolMembershipsWritten,
            opsReport.Assignments.TotalAssignments,
            opsReport.WcsTasksWritten,
            opsReport.WcsDispatchCircuitsWritten,
            opsReport.Validation.CountExecutionsChecked,
            opsReport.Validation.CompletedCountExecutionsChecked,
            opsReport.Validation.VarianceCountExecutionsChecked,
            opsReport.Validation.WcsTasksChecked,
            opsReport.Validation.CompletedWcsTasksChecked,
            opsReport.Validation.FailedWcsTasksChecked,
            opsReport.Validation.WcsDispatchCircuitsChecked,
            opsReport.Validation.SupplierReturnRequestsChecked,
            opsReport.Validation.WorkPoolsChecked,
            opsReport.Validation.WorkPoolMembershipsChecked,
            opsReport.Validation.AssignedInboundOrdersChecked,
            opsReport.Validation.AssignedPutawayTasksChecked,
            opsReport.Validation.AssignedPickingTasksChecked,
            opsReport.Validation.AssignedOutboundOrdersChecked,
            opsReport.Validation.AssignedCountExecutionsChecked);
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler();
    app.UseMiddleware<WmsLifecycleConflictMiddleware>();
    app.UseMiddleware<WarehouseTaskActionPersistenceConflictMiddleware>();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
            WmsEndpointContracts.TryGet(ctx.EndpointType, out var contract)
                ? contract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
    }).UseSwaggerGen();
    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics();

    await app.RunAsync();
}
catch (Exception ex)
{
    if (isTesting)
    {
        throw;
    }

    await Console.Error.WriteLineAsync($"Application terminated unexpectedly: {ex}");
}

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

static Uri ResolveServiceBaseAddress(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    string configurationKey,
    string developmentFallback)
{
    var configuredBaseUrl = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        return new Uri(configuredBaseUrl, UriKind.Absolute);
    }

    if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
    {
        return new Uri(developmentFallback, UriKind.Absolute);
    }

    throw new InvalidOperationException($"{configurationKey} is required outside Development.");
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
