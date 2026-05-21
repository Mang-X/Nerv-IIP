using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;
using Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Instances;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using NetCorePal.Extensions.AspNetCore;
using System.Net;
using Microsoft.Extensions.Http.Resilience;

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
builder.Services.Configure<GatewayAuthorizationOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5101");
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5103");
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
    c.Endpoints.NameGenerator = ctx => ctx.EndpointType.Name switch
    {
        nameof(ListInstancesEndpoint) => "listConsoleInstances",
        nameof(GetInstanceDetailEndpoint) => "getConsoleInstanceDetail",
        nameof(RestartInstanceEndpoint) => "restartConsoleInstance",
        nameof(GetConsoleOperationTaskEndpoint) => "getConsoleOperationTask",
        nameof(LoginConsoleUserEndpoint) => "loginConsoleUser",
        nameof(RefreshConsoleSessionEndpoint) => "refreshConsoleSession",
        nameof(LogoutConsoleSessionEndpoint) => "logoutConsoleSession",
        nameof(GetConsolePrincipalEndpoint) => "getConsolePrincipal",
        nameof(ListConsoleIamUsersEndpoint) => "listConsoleIamUsers",
        nameof(CreateConsoleIamUserEndpoint) => "createConsoleIamUser",
        nameof(UpdateConsoleIamUserEndpoint) => "updateConsoleIamUser",
        nameof(DisableConsoleIamUserEndpoint) => "disableConsoleIamUser",
        nameof(ResetConsoleIamUserPasswordEndpoint) => "resetConsoleIamUserPassword",
        nameof(ListConsoleIamRolesEndpoint) => "listConsoleIamRoles",
        nameof(CreateConsoleIamRoleEndpoint) => "createConsoleIamRole",
        nameof(UpdateConsoleIamRolePermissionsEndpoint) => "updateConsoleIamRolePermissions",
        nameof(ListConsoleIamPermissionsEndpoint) => "listConsoleIamPermissions",
        nameof(ListConsoleIamSessionsEndpoint) => "listConsoleIamSessions",
        nameof(RevokeConsoleIamSessionEndpoint) => "revokeConsoleIamSession",
        _ => ctx.EndpointType.Name
    };
}).UseSwaggerGen();
app.Run();

public partial class Program;
