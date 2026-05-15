using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Sdk.ConnectorProtocol;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ReportingLoopTests
{
    [Fact]
    public async Task Reporting_cycle_sends_registration_before_heartbeat_and_state_snapshot()
    {
        var client = new RecordingConnectorProtocolClient();
        var loop = new ConnectorReportingLoop([new StaticConnector()], client, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(["registration:demo-api-001", "heartbeat:demo-api-001", "state:demo-api-001"], client.Calls);
    }

    [Fact]
    public async Task Failed_apphub_request_is_retried_on_next_cycle()
    {
        var client = new RecordingConnectorProtocolClient { FailFirstRegistration = true };
        var loop = new ConnectorReportingLoop([new StaticConnector()], client, ConnectorHostRuntimeContext.DefaultLocal);

        await Assert.ThrowsAsync<HttpRequestException>(() => loop.RunCycleAsync(CancellationToken.None));
        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, client.Calls.Count(x => x == "registration:demo-api-001"));
    }

    private sealed class StaticConnector : IConnector
    {
        public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ConnectorTarget> targets =
            [
                new("node-001", "local-docker", "docker", "demo-api", "Demo API", "1.0.0", "demo-api-001", "demo-api", "running", "healthy", [new ConnectorCapability("lifecycle.restart", "1.0", "lifecycle", ["restart"])], new Dictionary<string, string>())
            ];
            return Task.FromResult(targets);
        }
    }

    private sealed class RecordingConnectorProtocolClient : IConnectorProtocolClient
    {
        private bool _failed;
        public bool FailFirstRegistration { get; init; }
        public List<string> Calls { get; } = [];

        public Task SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default)
        {
            Calls.Add($"registration:{registration.InstanceKey}");
            if (FailFirstRegistration && !_failed)
            {
                _failed = true;
                throw new HttpRequestException("AppHub unavailable");
            }

            return Task.CompletedTask;
        }

        public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default)
        {
            Calls.Add($"heartbeat:{heartbeat.InstanceKey}");
            return Task.CompletedTask;
        }

        public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls.Add($"state:{snapshot.InstanceKey}");
            return Task.CompletedTask;
        }
    }
}
