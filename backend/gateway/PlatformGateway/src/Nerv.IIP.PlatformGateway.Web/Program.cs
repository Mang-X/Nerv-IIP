using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Instances;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

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
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5103");
});
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5105");
});
builder.Services.AddHttpClient<IGatewayAuthorizationClient, HttpGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
builder.Services.AddHttpClient<IGatewayIamAuthClient, HttpGatewayIamAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});

var app = builder.Build();
app.UseNervIipCorrelation();
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
        _ => ctx.EndpointType.Name
    };
}).UseSwaggerGen();
app.Run();

public partial class Program;
