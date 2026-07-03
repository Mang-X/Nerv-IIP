using System.Net.Http.Headers;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;
using Nerv.IIP.ConnectorHost.Host;
using Nerv.IIP.Sdk.Auth;
using Nerv.IIP.Sdk.ConnectorProtocol;
using Nerv.IIP.Sdk.Ops;
using Nerv.IIP.ServiceAuth;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNervIipInternalServiceTokenProvider(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(_ => CreateRuntimeContext(builder.Configuration));
builder.Services.AddSingleton(_ => CreateConnectorCredential(builder.Configuration));
builder.Services.AddSingleton<IDockerProcessRunner, DockerProcessRunner>();
builder.Services.AddSingleton<IDockerCli, DockerCli>();
builder.Services.AddSingleton<DockerConnector>();
builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IConnectorOperationExecutor>(sp => sp.GetRequiredService<DockerConnector>());
if (builder.Configuration.GetValue("OpcUa:Enabled", false))
{
    builder.Services.AddSingleton(_ => CreateOpcUaOptions(builder.Configuration));
    builder.Services.AddSingleton<IOpcUaClient, OpcUaNetStandardClient>();
    builder.Services.AddHttpClient<IIndustrialTelemetrySamplesClient, HttpIndustrialTelemetrySamplesClient>((services, client) =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Platform:IndustrialTelemetryBaseUrl"] ?? "http://localhost:5116");
        var token = services.GetRequiredService<IInternalServiceTokenProvider>().BearerToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    });
    builder.Services.AddSingleton<OpcUaConnector>();
    builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<OpcUaConnector>());
    builder.Services.AddSingleton<IOpcUaCollectionConnector>(sp => sp.GetRequiredService<OpcUaConnector>());
}
builder.Services.AddSingleton<IReadOnlyList<IConnector>>(sp => sp.GetServices<IConnector>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IConnectorOperationExecutor>>(sp => sp.GetServices<IConnectorOperationExecutor>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IOpcUaCollectionConnector>>(sp => sp.GetServices<IOpcUaCollectionConnector>().ToList());
builder.Services.AddHttpClient<IConnectorProtocolClient, HttpConnectorProtocolClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5101");
});
builder.Services.AddHttpClient<IOpsClient, HttpOpsClient>((services, client) =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5103");
    var token = services.GetRequiredService<IInternalServiceTokenProvider>().BearerToken;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

static OpcUaConnectorOptions CreateOpcUaOptions(IConfiguration configuration)
{
    var tags = configuration.GetSection("OpcUa:Tags").GetChildren()
        .Select(section => new OpcUaTagSubscription(
            Required(section, "DeviceAssetId"),
            Required(section, "TagKey"),
            Required(section, "NodeId"),
            section.GetValue("SamplingIntervalMilliseconds", 1000),
            section.GetValue("BucketSeconds", 60)))
        .ToList();

    return new OpcUaConnectorOptions(
        Required(configuration, "OpcUa:ConnectorId"),
        Required(configuration, "ConnectorHost:ConnectorHostId"),
        Required(configuration, "ConnectorHost:OrganizationId"),
        Required(configuration, "ConnectorHost:EnvironmentId"),
        Required(configuration, "OpcUa:EndpointUrl"),
        configuration["OpcUa:SecurityPolicy"] ?? "None",
        configuration["OpcUa:SecurityMode"] ?? "None",
        configuration["OpcUa:CredentialReference"],
        configuration["OpcUa:BrowseRootNodeId"] ?? "ns=0;i=85",
        tags,
        configuration.GetValue("OpcUa:MaxReconnectAttempts", 1),
        configuration.GetValue("OpcUa:AutoAcceptUntrustedServerCertificates", false));
}

static string Required(IConfiguration configuration, string key)
{
    return configuration[key] ?? throw new InvalidOperationException($"Configuration value '{key}' is required.");
}
