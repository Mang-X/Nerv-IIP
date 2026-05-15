using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.Sdk.Auth;
using Nerv.IIP.Sdk.ConnectorProtocol;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(ConnectorHostRuntimeContext.DefaultLocal);
builder.Services.AddSingleton<IConnector>(_ => new DockerConnector([
    new DockerContainerDescriptor("local-demo-001", "nerv/demo-api:1.0.0", "demo-api", "running")
]));
builder.Services.AddSingleton(new ConnectorHostCredential("connector-host-001", "local-connector-secret", "org-001", "env-dev"));
builder.Services.AddHttpClient<IConnectorProtocolClient, HttpConnectorProtocolClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5103");
});
builder.Services.AddSingleton<ConnectorReportingLoop>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
