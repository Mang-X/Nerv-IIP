using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Scheduling.Domain;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.CodeAnalysis;
using Newtonsoft.Json;
using Prometheus;

var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-scheduling");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "MasterData:BaseUrl", "http://localhost:5107");
    var productEngineeringBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "ProductEngineering:BaseUrl", "http://localhost:5108");
    var mesBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "Mes:BaseUrl", "http://localhost:5111");
    var industrialTelemetryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "IndustrialTelemetry:BaseUrl", "http://localhost:5116");
    var maintenanceBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "Maintenance:BaseUrl", "http://localhost:5117");
    builder.Services.AddHttpClient<ISchedulingProblemMasterDataClient, HttpSchedulingProblemMasterDataClient>(client =>
    {
        client.BaseAddress = masterDataBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<ISchedulingProblemProductEngineeringClient, HttpSchedulingProblemProductEngineeringClient>(client =>
    {
        client.BaseAddress = productEngineeringBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient(HttpSchedulingMaterialReadinessProvider.MesClientName, client =>
    {
        client.BaseAddress = mesBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient(HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName, client =>
    {
        client.BaseAddress = industrialTelemetryBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient(HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName, client =>
    {
        client.BaseAddress = maintenanceBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Scheduling";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o =>
    {
        o.SerializerOptions.AddNetCorePalJsonConverters();
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();
    builder.Services.AddSingleton<FiniteCapacityScheduler>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<ISchedulingProblemProducer, SchedulingProblemProducer>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ISchedulingIntegrationEventContextAccessor, HttpSchedulingIntegrationEventContextAccessor>();
    builder.Services.AddScoped<SchedulePlanInvalidatedIntegrationEventConverter>();
    if (isTesting)
    {
        builder.Services.AddScoped<ISchedulingEquipmentAvailabilityProvider, NoopSchedulingEquipmentAvailabilityProvider>();
        builder.Services.AddScoped<ISchedulingMaterialReadinessProvider, NoopSchedulingMaterialReadinessProvider>();
    }
    else
    {
        builder.Services.AddScoped<ISchedulingEquipmentAvailabilityProvider, HttpSchedulingEquipmentAvailabilityProvider>();
        builder.Services.AddScoped<ISchedulingMaterialReadinessProvider, HttpSchedulingMaterialReadinessProvider>();
    }

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("BusinessScheduling PostgreSQL persistence requires ConnectionStrings:PostgreSQL. Tests that replace persistence must still provide an explicit placeholder connection string.");
    }

    builder.Services.AddSchedulingPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
    builder.Services.AddScoped<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>();
    builder.Services.AddScoped<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>();
    builder.Services.AddScoped<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans>();
    builder.Services.AddScoped<StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans>();
    builder.Services.AddScoped<QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans>();
    builder.Services.AddScoped<WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans>();
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

    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = SchedulingFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessScheduling in Development. Use an explicit migrator, release script or migration bundle outside Development.");
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
        c.Serializer.Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        c.Endpoints.NameGenerator = ctx =>
            SchedulingEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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

static Uri ResolveServiceBaseAddress(IConfiguration configuration, string configurationKey, string fallback)
{
    var configuredBaseUrl = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        return new Uri(configuredBaseUrl, UriKind.Absolute);
    }

    return new Uri(fallback, UriKind.Absolute);
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
    public static string GetCodeAnalysisHtml()
    {
        var assemblies = new[] { typeof(Program).Assembly };
        return VisualizationHtmlBuilder.GenerateVisualizationHtml(
            CodeFlowAnalysisHelper.GetResultFromAssemblies(assemblies));
    }
}
