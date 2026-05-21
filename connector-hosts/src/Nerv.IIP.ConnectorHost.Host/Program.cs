using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.Sdk.Auth;
using Nerv.IIP.Sdk.ConnectorProtocol;
using Nerv.IIP.Sdk.Ops;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(_ => CreateRuntimeContext(builder.Configuration));
builder.Services.AddSingleton(_ => CreateConnectorCredential(builder.Configuration));
builder.Services.AddSingleton<IDockerProcessRunner, DockerProcessRunner>();
builder.Services.AddSingleton<IDockerCli, DockerCli>();
builder.Services.AddSingleton<DockerConnector>();
builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IConnectorOperationExecutor>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IReadOnlyList<IConnector>>(sp => sp.GetServices<IConnector>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IConnectorOperationExecutor>>(sp => sp.GetServices<IConnectorOperationExecutor>().ToList());
builder.Services.AddHttpClient<IConnectorProtocolClient, HttpConnectorProtocolClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5101");
});
builder.Services.AddHttpClient<IOpsClient, HttpOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5103");
});
builder.Services.AddSingleton<ConnectorReportingLoop>();
builder.Services.AddSingleton<ConnectorOperationLoop>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

static ConnectorHostRuntimeContext CreateRuntimeContext(IConfiguration configuration)
{
    return new ConnectorHostRuntimeContext(
        "1.0",
        "1.0",
        Required(configuration, "ConnectorHost:OrganizationId"),
        Required(configuration, "ConnectorHost:EnvironmentId"),
        Required(configuration, "ConnectorHost:ConnectorHostId"),
        DateTimeOffset.UtcNow);
}

static ConnectorHostCredential CreateConnectorCredential(IConfiguration configuration)
{
    var credential = new ConnectorHostCredential(
        Required(configuration, "ConnectorHost:ConnectorHostId"),
        Required(configuration, "ConnectorHost:ConnectorSecret"),
        Required(configuration, "ConnectorHost:OrganizationId"),
        Required(configuration, "ConnectorHost:EnvironmentId"));

    var validation = ConnectorHostAuthentication.Validate(credential);
    if (!validation.Succeeded)
    {
        throw new InvalidOperationException(validation.Error?.Message ?? "Connector Host credential is invalid.");
    }

    return credential;
}

static string Required(IConfiguration configuration, string key)
{
    return configuration[key] ?? throw new InvalidOperationException($"Configuration value '{key}' is required.");
}
