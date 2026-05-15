using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.Sdk.Auth;
using Nerv.IIP.Sdk.ConnectorProtocol;
using Nerv.IIP.Sdk.Ops;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(ConnectorHostRuntimeContext.DefaultLocal);
builder.Services.AddSingleton<DockerConnector>(_ => new DockerConnector([
    new DockerContainerDescriptor("local-demo-001", "nerv/demo-api:1.0.0", "demo-api", "running")
]));
builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IConnectorOperationExecutor>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IReadOnlyList<IConnector>>(sp => sp.GetServices<IConnector>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IConnectorOperationExecutor>>(sp => sp.GetServices<IConnectorOperationExecutor>().ToList());
builder.Services.AddSingleton(new ConnectorHostCredential("connector-host-001", "local-connector-secret", "org-001", "env-dev"));
builder.Services.AddHttpClient<IConnectorProtocolClient, HttpConnectorProtocolClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5103");
});
builder.Services.AddHttpClient<IOpsClient, HttpOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5105");
});
builder.Services.AddSingleton<ConnectorReportingLoop>();
builder.Services.AddSingleton<ConnectorOperationLoop>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
