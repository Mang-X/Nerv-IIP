using Opc.Ua;
using Opc.Ua.Client;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests;

public sealed class OpcUaNetStandardClientConfigurationTests
{
    [Theory]
    [InlineData(null, 4_000)]
    [InlineData(2, 2_000)]
    public async Task Connect_passes_the_detection_budget_to_the_sdk_session(int? budgetSeconds, int expectedMilliseconds)
    {
        var sessionNotOpened = new InvalidOperationException("Test stopped at the SDK session boundary.");
        var options = new OpcUaConnectionOptions("opc.tcp://localhost:4840", "None", "None", null, false);
        using var client = new OpcUaNetStandardClient(
            new EnvironmentOpcUaCredentialResolver(),
            budgetSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            (configuration, _, _) =>
            {
                Assert.Equal(expectedMilliseconds, EndpointConfiguration.Create(configuration).OperationTimeout);
                return Task.FromException<Session>(sessionNotOpened);
            });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(options, CancellationToken.None));

        Assert.Same(sessionNotOpened, failure);
    }
}
