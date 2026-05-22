using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.AspNetCore;
using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Nerv.IIP.ServiceAuth;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Platform Gateway";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipCaching(builder.Configuration, "platform-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "platform-gateway");
builder.Services.AddNervIipInternalServiceTokenProvider(builder.Configuration, builder.Environment);
builder.Services.Configure<GatewayAuthorizationOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5101");
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5103");
}).AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayNotificationClient, HttpGatewayNotificationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Notification:BaseUrl"] ?? "http://localhost:5106");
}).AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayAuthorizationClient, HttpGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<IGatewayIamAuthClient, HttpGatewayIamAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddGatewayNonIdempotentSafeResilience();
builder.Services.AddHttpClient<IGatewayIamAdminClient, HttpGatewayIamAdminClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddGatewayNonIdempotentSafeResilience();
builder.Services.AddGatewayAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = GatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

public partial class Program;
