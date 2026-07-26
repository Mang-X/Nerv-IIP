using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Business.Erp.Web.Application.Wms;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
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
    builder.Services.AddNervIipObservability(builder.Configuration, "business-erp");

    builder.Services.AddHealthChecks();
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IErpIntegrationEventContextAccessor, HttpErpIntegrationEventContextAccessor>();
    var approvalBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Approval:BaseUrl", "http://localhost:5114");
    builder.Services.AddHttpClient<IPurchaseOrderApprovalClient, HttpPurchaseOrderApprovalClient>(client =>
    {
        client.BaseAddress = approvalBaseAddress;
    }).UseHttpClientMetrics();
    var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
    builder.Services.AddHttpClient<ICustomerCreditProfileReader, HttpCustomerCreditProfileReader>(client =>
    {
        client.BaseAddress = masterDataBaseAddress;
    }).UseHttpClientMetrics();
    var wmsBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Wms:BaseUrl", "http://localhost:5118");
    builder.Services.AddHttpClient<IWmsOutboundCancellationClient, HttpWmsOutboundCancellationClient>(client =>
    {
        client.BaseAddress = wmsBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IWmsInboundCancellationClient, HttpWmsInboundCancellationClient>(client =>
    {
        client.BaseAddress = wmsBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business ERP";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_erp_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddErpPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
    builder.Services.AddScoped<ErpCodingService>();
    builder.Services.AddScoped<SalesOrderDemandDemoSeedService>();
    builder.Services.AddScoped<LeaderDemoScaleSeedService>();
    builder.Services.AddScoped<WorldHistorySeedService>();
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
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
            x.UseConfiguredRecovery(builder.Configuration);
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
            x.UseDashboard();
        });
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = ErpFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    await using var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessERP in Development. Use an explicit migrator, release script or migration bundle outside Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
            ErpEndpointContracts.TryGet(ctx.EndpointType, out var contract)
                ? contract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
    }).UseSwaggerGen();
    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics();

    await app.StartAsync();
    if (builder.Configuration.GetValue<bool>("Erp:Seed:SalesOrderDemandDemo:Enabled"))
    {
        using var scope = app.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<SalesOrderDemandDemoSeedService>();
        var organizationId = builder.Configuration["Erp:Seed:OrganizationId"] ?? "org-001";
        var environmentId = builder.Configuration["Erp:Seed:EnvironmentId"] ?? "env-dev";
        await seed.SeedAsync(organizationId, environmentId);
        await scope.ServiceProvider.GetRequiredService<LeaderDemoScaleSeedService>().SeedAsync(
            organizationId,
            environmentId,
            builder.Configuration.GetValue("LeaderDemo:Scale:OrderCount", 0),
            DateTimeOffset.UtcNow);

        // 《工厂世界观设定集》L1 背景历史（ERP 侧）。校验器 fail-closed：账不平就让启动失败。
        if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
        {
            var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
                organizationId,
                environmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history ERP seed completed: {SalesOrders} sales orders, {PurchaseOrders} purchase orders; " +
                "validator checked {Orders} orders / {Deliveries} deliveries / {Receivables} receivables / " +
                "{CashReceipts} cash receipts / {Vouchers} vouchers.",
                report.SalesOrdersWritten,
                report.PurchaseOrdersWritten,
                report.Validation.OrdersChecked,
                report.Validation.DeliveriesChecked,
                report.Validation.ReceivablesChecked,
                report.Validation.CashReceiptsChecked,
                report.Validation.VouchersChecked);
            foreach (var line in report.Validation.Sample)
            {
                app.Logger.LogInformation("World-history sample: {Chain}", line);
            }
        }
    }

    await app.WaitForShutdownAsync();
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
