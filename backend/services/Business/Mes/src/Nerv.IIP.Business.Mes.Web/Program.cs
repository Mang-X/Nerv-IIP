using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.Mes.Web;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions.CAP;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");
builder.Services.AddNervIipObservability(builder.Configuration, "business-mes");

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business MES";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMemoryCache();
var productEngineeringBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "ProductEngineering:BaseUrl", "http://localhost:5108");
var inventoryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Inventory:BaseUrl", "http://localhost:5109");
var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
builder.Services.AddSingleton(new MesMaterialRequirementInventoryOptions
{
    DefaultSiteCode = builder.Configuration["Inventory:DefaultSiteCode"] ?? "production",
    SiteCodes = ResolveSiteCodes(builder.Configuration),
});
builder.Services.AddHttpClient<MesProductEngineeringHttpClient>(client =>
{
    client.BaseAddress = productEngineeringBaseAddress;
});
builder.Services.AddHttpClient<MesInventoryHttpClient>(client =>
{
    client.BaseAddress = inventoryBaseAddress;
});
builder.Services.AddHttpClient<MesMasterDataHttpClient>(client =>
{
    client.BaseAddress = masterDataBaseAddress;
});
builder.Services.AddScoped<IMesMaterialRequirementSnapshotProvider, HttpMesProductEngineeringMaterialRequirementSnapshotProvider>();
builder.Services.AddMediatR(configuration => configuration
    .RegisterServicesFromAssembly(typeof(Program).Assembly)
    .AddUnitOfWorkBehaviors());
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (!builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Host=localhost;Database=nerv_iip_mes_testing;Username=nerv;Password=nerv";
}

builder.Services.AddMesPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
builder.Services.AddScoped<IMesPlanningStore, PersistentMesPlanningStore>();
builder.Services.AddScoped<MesFoundationReadinessService>();
builder.Services.Configure<MesEngineeringChangeOptions>(
    builder.Configuration.GetSection("Mes:EngineeringChange"));
builder.Services.AddSingleton<RuleScheduler>();
builder.Services.AddScoped<MesCodingService>();
builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
builder.Services.AddMesCapIntegrationEvents(builder.Configuration, builder.Environment.EnvironmentName, isTesting);
builder.Services.AddSingleton(new MesRescheduleOptions
{
    AutoRescheduleOnAssetUnavailable = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetUnavailable", true),
    AutoRescheduleOnAssetRestored = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetRestored", true),
});
builder.Services.AddMesIntegrationEventConsumers();

var app = builder.Build();
app.UseNervIipCorrelation();
var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
if (autoMigrate && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessMES in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = ctx =>
        MesEndpointContracts.TryGet(ctx.EndpointType, out var contract)
            ? contract.OperationId
            : ToLowerCamelEndpointName(ctx.EndpointType.Name);
}).UseSwaggerGen();
app.Run();

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

static IReadOnlyCollection<string>? ResolveSiteCodes(IConfiguration configuration)
{
    var sectionValues = configuration.GetSection("Inventory:SiteCodes")
        .Get<string[]>()
        ?.Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .ToArray();
    if (sectionValues is { Length: > 0 })
    {
        return sectionValues;
    }

    var delimited = configuration["Inventory:SiteCodes"];
    if (string.IsNullOrWhiteSpace(delimited))
    {
        return null;
    }

    var values = delimited
        .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    return values.Length == 0 ? null : values;
}

/// <summary>
/// MES web application entry point.
/// </summary>
public partial class Program;
