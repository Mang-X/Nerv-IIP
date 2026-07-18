using System.Reflection;
using Opc.Ua;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests;

public sealed class OpcUaNetStandardClientConfigurationTests
{
    [Fact]
    public void Service_operation_timeout_uses_the_full_detection_budget()
    {
        using var client = new OpcUaNetStandardClient(
            new EnvironmentOpcUaCredentialResolver(),
            TimeSpan.FromSeconds(4));
        var method = typeof(OpcUaNetStandardClient).GetMethod(
            "CreateApplicationConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var configuration = Assert.IsType<ApplicationConfiguration>(method!.Invoke(
            client,
            [new OpcUaConnectionOptions("opc.tcp://localhost:4840", "None", "None", null, false)]));

        Assert.Equal(4_000, configuration.TransportQuotas.OperationTimeout);
    }
}
