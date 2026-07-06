using System.Net.Http.Headers;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.ConnectorHost.Connectors.Modbus;
using Nerv.IIP.ConnectorHost.Connectors.Mqtt;
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
builder.Services.AddHttpClient<IIndustrialTelemetrySamplesClient, HttpIndustrialTelemetrySamplesClient>((services, client) =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:IndustrialTelemetryBaseUrl"] ?? "http://localhost:5116");
    var token = services.GetRequiredService<IInternalServiceTokenProvider>().BearerToken;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
});
if (builder.Configuration.GetValue("OpcUa:Enabled", false))
{
    builder.Services.AddSingleton(_ => CreateOpcUaOptions(builder.Configuration));
    builder.Services.AddSingleton<IOpcUaCredentialResolver, EnvironmentOpcUaCredentialResolver>();
    builder.Services.AddSingleton<IOpcUaClient, OpcUaNetStandardClient>();
    builder.Services.AddSingleton<OpcUaConnector>();
    builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<OpcUaConnector>());
    builder.Services.AddSingleton<IIndustrialTelemetryCollectionConnector>(sp => sp.GetRequiredService<OpcUaConnector>());
}
if (builder.Configuration.GetValue("Modbus:Enabled", false))
{
    builder.Services.AddSingleton(_ => CreateModbusOptions(builder.Configuration));
    builder.Services.AddSingleton<IModbusTcpClient, ModbusTcpClient>();
    builder.Services.AddSingleton<ModbusConnector>();
    builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<ModbusConnector>());
    builder.Services.AddSingleton<IIndustrialTelemetryCollectionConnector>(sp => sp.GetRequiredService<ModbusConnector>());
}
if (builder.Configuration.GetValue("Mqtt:Enabled", false))
{
    builder.Services.AddSingleton(_ => CreateMqttOptions(builder.Configuration));
    builder.Services.AddSingleton<IMqttCredentialResolver, EnvironmentMqttCredentialResolver>();
    builder.Services.AddSingleton<IMqttSubscriptionClient, MqttNetSubscriptionClient>();
    builder.Services.AddSingleton<MqttConnector>();
    builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<MqttConnector>());
    builder.Services.AddSingleton<IIndustrialTelemetryCollectionConnector>(sp => sp.GetRequiredService<MqttConnector>());
}
builder.Services.AddSingleton<IReadOnlyList<IConnector>>(sp => sp.GetServices<IConnector>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IConnectorOperationExecutor>>(sp => sp.GetServices<IConnectorOperationExecutor>().ToList());
builder.Services.AddSingleton<IReadOnlyList<IIndustrialTelemetryCollectionConnector>>(sp => sp.GetServices<IIndustrialTelemetryCollectionConnector>().ToList());
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
builder.Services.AddSingleton<IndustrialTelemetryCollectorRunner>();
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

static ModbusConnectorOptions CreateModbusOptions(IConfiguration configuration)
{
    var registers = configuration.GetSection("Modbus:Registers").GetChildren()
        .Select(section => new ModbusRegisterMapping(
            Required(section, "DeviceAssetId"),
            Required(section, "TagKey"),
            section.GetValue<byte>("UnitId"),
            Enum.Parse<ModbusRegisterTable>(Required(section, "Table"), ignoreCase: true),
            section.GetValue<ushort>("Address"),
            section.GetValue<ushort>("RegisterCount", 1),
            section.GetValue("Scale", 1m),
            section.GetValue("Offset", 0m),
            section.GetValue("BucketSeconds", 60),
            Enum.Parse<ModbusRegisterDataType>(section["DataType"] ?? "UInt16", ignoreCase: true),
            Enum.Parse<ModbusWordOrder>(section["WordOrder"] ?? "BigEndian", ignoreCase: true)))
        .ToList();

    return new ModbusConnectorOptions(
        Required(configuration, "Modbus:ConnectorId"),
        Required(configuration, "ConnectorHost:ConnectorHostId"),
        Required(configuration, "ConnectorHost:OrganizationId"),
        Required(configuration, "ConnectorHost:EnvironmentId"),
        Required(configuration, "Modbus:Endpoint"),
        configuration["Modbus:CredentialReference"],
        registers,
        configuration.GetValue("Modbus:MaxReconnectAttempts", 1));
}

static MqttConnectorOptions CreateMqttOptions(IConfiguration configuration)
{
    var mappings = configuration.GetSection("Mqtt:TopicMappings").GetChildren()
        .Select(section => new MqttTopicMapping(
            Required(section, "DeviceAssetId"),
            Required(section, "TagKey"),
            Required(section, "TopicFilter"),
            Required(section, "ValueJsonPath"),
            section.GetValue("BucketSeconds", 60)))
        .ToList();

    return new MqttConnectorOptions(
        Required(configuration, "Mqtt:ConnectorId"),
        Required(configuration, "ConnectorHost:ConnectorHostId"),
        Required(configuration, "ConnectorHost:OrganizationId"),
        Required(configuration, "ConnectorHost:EnvironmentId"),
        Required(configuration, "Mqtt:Broker"),
        Required(configuration, "Mqtt:ClientId"),
        configuration["Mqtt:CredentialReference"],
        mappings,
        configuration.GetValue("Mqtt:MaxReconnectAttempts", 1));
}

static string Required(IConfiguration configuration, string key)
{
    return configuration[key] ?? throw new InvalidOperationException($"Configuration value '{key}' is required.");
}
