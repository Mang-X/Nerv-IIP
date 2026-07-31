using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.EquipmentRuntime;
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
    builder.Services.AddNervIipObservability(builder.Configuration, "business-maintenance");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc();
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    var industrialTelemetryBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "IndustrialTelemetry:BaseUrl", "http://localhost:5116");
    builder.Services.AddHttpClient(HttpIndustrialTelemetryAssetRuntimeHoursProvider.ClientName, client =>
    {
        client.BaseAddress = industrialTelemetryBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o =>
        {
            o.IncludeAbstractValidators = true;
            o.DisableAutoDiscovery = true;
            o.Assemblies = [Assembly.GetExecutingAssembly()];
        })
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Maintenance";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o =>
    {
        o.SerializerOptions.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
        o.SerializerOptions.AddNetCorePalJsonConverters();
    });
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();
    builder.Services.Configure<MaintenanceCompletionOptions>(builder.Configuration.GetSection("Maintenance:Completion"));
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, MaintenanceIntegrationEventDeadLetterStore>();
    builder.Services.AddScoped<OpenWorkOrderWhenAlarmRaisedHandler>();
    builder.Services.AddScoped<MarkWorkOrderAlarmClearedHandler>();
    builder.Services.AddScoped<PauseMaintenancePlansWhenDeviceDisabledHandler>();
    builder.Services.AddScoped<ICommandLock<GenerateDueMaintenanceWorkOrdersCommand>, GenerateDueMaintenanceWorkOrdersCommandLock>();
    builder.Services.AddScoped<ICommandLock<CreateMaintenanceWorkOrderCommand>, CreateMaintenanceWorkOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<CompleteMaintenanceWorkOrderCommand>, CompleteMaintenanceWorkOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<AssignMaintenanceWorkOrderCommand>, AssignMaintenanceWorkOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<TransitionMaintenanceWorkOrderCommand>, TransitionMaintenanceWorkOrderCommandLock>();
    builder.Services.AddScoped<ICommandLock<ApplyMaintenanceDeviceStateCommand>, ApplyMaintenanceDeviceStateCommandLock>();
    builder.Services.AddScoped<ICommandLock<CreateMaintenancePlanCommand>, CreateMaintenancePlanCommandLock>();
    builder.Services.AddScoped<ICommandLock<UpdateMaintenancePlanCommand>, UpdateMaintenancePlanCommandLock>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddHostedService<MaintenancePlanDueScheduler>();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_maintenance_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddMaintenancePostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<MaintenanceUnavailableWindowRuntimeHoursProvider>();
    builder.Services.AddScoped<IAssetRuntimeHoursFallbackProvider>(sp => sp.GetRequiredService<MaintenanceUnavailableWindowRuntimeHoursProvider>());
    if (isTesting)
    {
        builder.Services.AddScoped<IAssetRuntimeHoursProvider, MaintenanceUnavailableWindowRuntimeHoursProvider>();
    }
    else
    {
        builder.Services.AddScoped<IAssetRuntimeHoursProvider, HttpIndustrialTelemetryAssetRuntimeHoursProvider>();
    }

    builder.Services.AddNervIipCommandLocking(
        builder.Configuration,
        builder.Environment,
        isTesting,
        MaintenanceFacts.ServiceName);
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<MaintenanceCodingService>();
    builder.Services.AddScoped<MaintenanceSeedService>();
    builder.Services.AddScoped<LeaderDemoSeedService>();
    builder.Services.AddScoped<WorldHistorySeedService>();
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
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = MaintenanceFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessMaintenance in Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // 点检保养计划 seed（默认随 autoMigrate 开启，或显式 Maintenance:Seed:Enabled）：
    // 全新环境补齐可选保养计划，供 PDA 点检页选计划 → 录测量值/超差/拍照走通（幂等只补缺失）。
    var seedEnabled = builder.Configuration.GetValue<bool>("Maintenance:Seed:Enabled") || autoMigrate;
    if (seedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<MaintenanceSeedService>();
        await seed.SeedAsync(
            builder.Configuration["Maintenance:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["Maintenance:Seed:EnvironmentId"] ?? "env-dev");
    }

    var leaderDemoSeedEnabled = builder.Configuration.GetValue<bool>("LeaderDemo:Seed:Enabled");
    if (leaderDemoSeedEnabled && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("LeaderDemo:Seed:Enabled=true is only allowed for BusinessMaintenance in Development.");
    }

    if (WorldHistoryConfiguration.IsEnabled(builder.Configuration) && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("LeaderDemo:History:Enabled=true is only allowed for BusinessMaintenance in Development.");
    }

    if (leaderDemoSeedEnabled)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(
            builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev");

        // 《工厂世界观设定集》L1 设备域历史（三期，Maintenance 侧）。校验器 fail-closed。
        if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
        {
            var historyStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
                builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
                builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev",
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            historyStopwatch.Stop();
            app.Logger.LogInformation(
                "World-history maintenance seed completed in {ElapsedSeconds:F1}s: {Reasons} downtime reasons, " +
                "{Plans} maintenance plans, {Inspections} inspections, {WorkOrders} work orders, " +
                "{SparePartLines} spare part lines, {DeviceStates} device states; validator checked " +
                "{WorkOrdersChecked} work orders / {InspectionsChecked} inspections / {SparePartLinesChecked} spare " +
                "part lines / {DeviceStatesChecked} device states, total completed downtime " +
                "{DowntimeMinutes} min ({OpenWorkOrders} open-tail work orders).",
                historyStopwatch.Elapsed.TotalSeconds,
                report.DowntimeReasonsWritten,
                report.MaintenancePlansWritten,
                report.InspectionsWritten,
                report.WorkOrdersWritten,
                report.SparePartLinesWritten,
                report.DeviceStatesWritten,
                report.Validation.WorkOrdersChecked,
                report.Validation.InspectionsChecked,
                report.Validation.SparePartLinesChecked,
                report.Validation.DeviceStatesChecked,
                report.Validation.CompletedDowntimeMinutes,
                report.Validation.OpenWorkOrders);
            foreach (var line in report.Validation.Sample)
            {
                app.Logger.LogInformation("World-history maintenance sample: {Chain}", line);
            }
        }
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = System.Net.HttpStatusCode.BadRequest });
    app.UseMiddleware<MaintenanceLifecycleConflictMiddleware>();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Serializer.Options.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
        c.Endpoints.NameGenerator = ctx =>
            MaintenanceEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
