using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
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

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
