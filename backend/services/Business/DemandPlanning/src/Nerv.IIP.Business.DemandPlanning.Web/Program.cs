using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Newtonsoft.Json;
using Prometheus;


var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-demand-planning");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
    var productEngineeringBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "ProductEngineering:BaseUrl", "http://localhost:5108");
    var inventoryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Inventory:BaseUrl", "http://localhost:5109");
    var erpBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Erp:BaseUrl", "http://localhost:5118");
    var mesBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Mes:BaseUrl", "http://localhost:5111");
    builder.Services.AddHttpClient<IPlanningParameterSnapshotClient, HttpPlanningMasterDataPlanningParameterSnapshotClient>(client =>
    {
        client.BaseAddress = masterDataBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IPlanningProductEngineeringSnapshotClient, HttpPlanningProductEngineeringSnapshotClient>(client =>
    {
        client.BaseAddress = productEngineeringBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IPlanningInventorySnapshotClient, HttpPlanningInventorySnapshotClient>(client =>
    {
        client.BaseAddress = inventoryBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IPlanningScheduledReceiptSnapshotClient, HttpPlanningErpScheduledReceiptSnapshotClient>(client =>
    {
        client.BaseAddress = erpBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IPlanningSuggestionDownstreamBridge, HttpMesPlanningSuggestionDownstreamBridge>(client =>
    {
        client.BaseAddress = mesBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business DemandPlanning";
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
        connectionString = "Host=localhost;Database=nerv_iip_demand_planning_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddDemandPlanningPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<DemandPlanningCodingService>();
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    if (string.Equals(builder.Configuration["Planning:InputProvider"], "Fixture", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddScoped<IPlanningInputSnapshotProvider, DemandPlanningFixtureInputSnapshotProvider>();
    }
    else
    {
        builder.Services.AddScoped<IPlanningInputSnapshotProvider, DemandPlanningUpstreamInputSnapshotProvider>();
    }
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
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = DemandPlanningFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessDemandPlanning in Development. Use an explicit migrator, release script or migration bundle outside Development.");
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
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
            DemandPlanningEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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

    if (environment.IsDevelopment())
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
