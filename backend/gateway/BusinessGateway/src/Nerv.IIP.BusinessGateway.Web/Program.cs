using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Nerv.IIP.BusinessGateway.Web;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;

const string BusinessConsoleCorsPolicy = "business-console-cors";
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business Gateway";
            s.Version = "v1";
            s.DocumentProcessors.Add(new SchedulingEnumOpenApiDocumentProcessor());
            s.DocumentProcessors.Add(new WmsWarehouseTaskOpenApiDocumentProcessor());
            s.DocumentProcessors.Add(new MesListDisplayOpenApiDocumentProcessor());
        };
    });
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddNervIipCaching(builder.Configuration, "business-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "business-gateway");
builder.Services.AddNervIipLocalization();
builder.Services.AddNervIipInternalServiceTokenProvider(builder.Configuration, builder.Environment);
builder.Services.Configure<BusinessGatewayAuthorizationOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.Configure<BusinessGatewayInventoryForwardedPermissionOptions>(builder.Configuration.GetSection("Inventory:ForwardedPermissions"));
builder.Services.AddSingleton<BusinessGatewayDownstreamHealthState>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AcceptLanguageForwardingHandler>();
builder.Services.AddScoped<BusinessConsoleSearchService>();
builder.Services.AddScoped<BusinessGatewayDataScopeFilter>();
var iamBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Iam:BaseUrl", "http://localhost:5102");
var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
var inventoryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Inventory:BaseUrl", "http://localhost:5109");
var qualityBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Quality:BaseUrl", "http://localhost:5110");
var productEngineeringBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "ProductEngineering:BaseUrl", "http://localhost:5108");
var demandPlanningBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "DemandPlanning:BaseUrl", "http://localhost:5112");
var erpBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Erp:BaseUrl", "http://localhost:5118");
var wmsBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Wms:BaseUrl", "http://localhost:5115");
var approvalBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Approval:BaseUrl", "http://localhost:5114");
var barcodeLabelBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "BarcodeLabel:BaseUrl", "http://localhost:5113");
var notificationBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Notification:BaseUrl", "http://localhost:5106");
var fileStorageBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "FileStorage:BaseUrl", "http://localhost:5104");
var mesBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Mes:BaseUrl", "http://localhost:5111");
var schedulingBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Scheduling:BaseUrl", "http://localhost:5120");
var industrialTelemetryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "IndustrialTelemetry:BaseUrl", "http://localhost:5116");
var maintenanceBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Maintenance:BaseUrl", "http://localhost:5117");
var appHubBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "AppHub:BaseUrl", "http://localhost:5101");
builder.Services.AddHttpClient<IBusinessGatewayAuthorizationClient, HttpBusinessGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = iamBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessIamDirectoryClient, HttpBusinessIamDirectoryClient>(client =>
{
    client.BaseAddress = iamBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessMasterDataClient, HttpBusinessMasterDataClient>(client =>
{
    client.BaseAddress = masterDataBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessInventoryClient, HttpBusinessInventoryClient>(client =>
{
    client.BaseAddress = inventoryBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessQualityClient, HttpBusinessQualityClient>(client =>
{
    client.BaseAddress = qualityBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessProductEngineeringClient, HttpBusinessProductEngineeringClient>(client =>
{
    client.BaseAddress = productEngineeringBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessPlanningClient, HttpBusinessPlanningClient>(client =>
{
    client.BaseAddress = demandPlanningBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessErpClient, HttpBusinessErpClient>(client =>
{
    client.BaseAddress = erpBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessWmsClient, HttpBusinessWmsClient>(client =>
{
    client.BaseAddress = wmsBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessApprovalClient, HttpBusinessApprovalClient>(client =>
{
    client.BaseAddress = approvalBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessBarcodeLabelClient, HttpBusinessBarcodeLabelClient>(client =>
{
    client.BaseAddress = barcodeLabelBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessNotificationClient, HttpBusinessNotificationClient>(client =>
{
    client.BaseAddress = notificationBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessFileStorageClient, HttpBusinessFileStorageClient>(client =>
{
    client.BaseAddress = fileStorageBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessMesClient, HttpBusinessMesClient>(client =>
{
    client.BaseAddress = mesBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessSchedulingClient, HttpBusinessSchedulingClient>(client =>
{
    client.BaseAddress = schedulingBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessIndustrialTelemetryClient, HttpBusinessIndustrialTelemetryClient>(client =>
{
    client.BaseAddress = industrialTelemetryBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessMaintenanceClient, HttpBusinessMaintenanceClient>(client =>
{
    client.BaseAddress = maintenanceBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IBusinessAppHubClient, HttpBusinessAppHubClient>(client => client.BaseAddress = appHubBaseAddress)
    .AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddBusinessGatewayAuthentication(builder.Configuration, builder.Environment);
var allowedCorsOrigins = ResolveGatewayCorsOrigins(builder.Configuration, builder.Environment);
builder.Services.AddCors(options =>
{
    options.AddPolicy(BusinessConsoleCorsPolicy, policy =>
        policy.WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // CORS preflight must not consume user/IP quota; actual API calls remain limited below.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("cors-preflight");
        }

        var key = context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Security:RateLimit:PermitLimit", 300),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("Security:RateLimit:WindowSeconds", 60)),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseCors(BusinessConsoleCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Serializer.Options.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
    c.Serializer.Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    c.Endpoints.NameGenerator = BusinessGatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

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

// Keep this gateway-local until another gateway shares the same production security policy shape.
static string[] ResolveGatewayCorsOrigins(IConfiguration configuration, IWebHostEnvironment environment)
{
    var origins = configuration.GetSection("Security:Cors:AllowedOrigins").Get<string[]>()
        ?? SplitOrigins(configuration["Security:Cors:AllowedOrigins"]);
    if (origins.Length > 0)
    {
        return origins;
    }

    if (environment.IsDevelopment())
    {
        return ["http://localhost:5105", "http://localhost:5125"];
    }

    throw new InvalidOperationException("Security:Cors:AllowedOrigins is required outside Development.");
}

static string[] SplitOrigins(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

public partial class Program;
