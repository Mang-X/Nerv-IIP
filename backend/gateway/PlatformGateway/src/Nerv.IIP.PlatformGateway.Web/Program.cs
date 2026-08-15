using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Caching;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Http;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using Nerv.IIP.PlatformGateway.Web.Application.Resilience;
using NetCorePal.Extensions.AspNetCore;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Nerv.IIP.ServiceAuth;

const string ConsoleCorsPolicy = "console-cors";
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Platform Gateway";
            s.Version = "v1";
            // OpenAPI 快照必须机器无关：禁止 NJsonSchema 运行时探测 NuGet 全局缓存/SDK 目录
            // 读取依赖包 XML 文档（如 FastEndpoints.xml）。本机缓存是否解出过 XML
            // （NUGET_XMLDOC_MODE）会导致导出快照与 CI 重生成结果漂移。
            s.SchemaSettings.ResolveExternalXmlDocumentation = false;
        };
    });
builder.Services.AddNervIipCaching(builder.Configuration, "platform-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "platform-gateway");
builder.Services.AddVictoriaLogsClient(builder.Configuration);
builder.Services.AddNervIipLocalization();
builder.Services.AddNervIipInternalServiceAuthorization(builder.Configuration, builder.Environment);
builder.Services.Configure<GatewayAuthorizationOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddSingleton<GatewayDownstreamHealthState>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AcceptLanguageForwardingHandler>();
var appHubBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "AppHub:BaseUrl", "http://localhost:5101");
var opsBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "Ops:BaseUrl", "http://localhost:5103");
var fileStorageBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "FileStorage:BaseUrl", "http://localhost:5104");
var notificationBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "Notification:BaseUrl", "http://localhost:5106");
var iamBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "Iam:BaseUrl", "http://localhost:5102");
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = appHubBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = opsBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayNotificationClient, HttpGatewayNotificationClient>(client =>
{
    client.BaseAddress = notificationBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayFileStorageClient, HttpGatewayFileStorageClient>(client =>
{
    client.BaseAddress = fileStorageBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayAuthorizationClient, HttpGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = iamBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IGatewayIamAuthClient, HttpGatewayIamAuthClient>(client =>
{
    client.BaseAddress = iamBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayIamAdminClient, HttpGatewayIamAdminClient>(client =>
{
    client.BaseAddress = iamBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddGatewayNonIdempotentSafeResilience();
builder.Services.AddGatewayAuthentication(builder.Configuration, builder.Environment);
var allowedCorsOrigins = ResolveGatewayCorsOrigins(builder.Configuration, builder.Environment);
builder.Services.AddCors(options =>
{
    options.AddPolicy(ConsoleCorsPolicy, policy =>
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
app.UseCors(ConsoleCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = GatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

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
