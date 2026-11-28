using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.TestUtilities;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class DynamicConnectorManagerTests
{
    [Fact]
    public async Task Desired_revision_replaces_collector_and_removed_configuration_stops_reporting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = deadline.Token;
        var context = ConnectorHostRuntimeContext.DefaultLocal;
        var snapshots = new ConnectorTargetSnapshotStore([], TimeProvider.System);
        using var http = new HttpClient();
        using var manager = new DynamicConnectorManager(new(http, context), context, new(), snapshots,
            new(NullLogger<IndustrialTelemetryCollectorRunner>.Instance), new UnusedSamples(),
            new ConnectorReportSignal(), new ConnectorManifestSignal(), TimeProvider.System,
            NullLogger<DynamicConnectorManager>.Instance);
        var config = new TelemetryConnectorConfigItem("workshop", "Workshop", "modbus",
            listener.LocalEndpoint.ToString()!, true, 60, [new("device-1", "pressure", "bar")],
            Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        try
        {
            await manager.ReconcileAsync([config], ct);
            using var first = await listener.AcceptTcpClientAsync(ct);
            await ReadRequestAsync(first, 0, ct);
            snapshots.TriggerRefresh(ct);
            var manifest = await ObserveManifestAsync();
            Assert.Equal("pressure", Assert.Single(manifest.Entries).TagKey);

            await manager.ReconcileAsync([config], ct);
            Assert.Same(manifest, Assert.Single(snapshots.GetCurrentTargets()).TagManifest);

            await manager.ReconcileAsync([config with
            {
                Revision = Guid.NewGuid(),
                Mappings = [new("device-1", "temperature", "°C", Address: 40003)]
            }], ct);
            using var second = await listener.AcceptTcpClientAsync(ct);
            await ReadRequestAsync(second, 2, ct);
            snapshots.TriggerRefresh(ct);
            Assert.Equal("temperature", Assert.Single((await ObserveManifestAsync()).Entries).TagKey);
        }
        finally
        {
            await manager.ReconcileAsync([], ct);
        }

        snapshots.TriggerRefresh(ct);
        Assert.Empty(snapshots.GetCurrentTargets());

        Task<ConnectorTagManifestSnapshot> ObserveManifestAsync() =>
            BoundedObservation.ObserveAsync(WaitForManifestAsync(), "connector manifest", () => "no target snapshot");

        async Task<ConnectorTagManifestSnapshot> WaitForManifestAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (snapshots.GetCurrentTargets().SingleOrDefault()?.TagManifest is not { } manifest)
            {
                await timer.WaitForNextTickAsync(ct);
            }

            return snapshots.GetCurrentTargets().Single().TagManifest!;
        }
    }

    private static async Task ReadRequestAsync(TcpClient client, byte address, CancellationToken ct)
    {
        var stream = client.GetStream();
        var frame = new byte[12];
        await stream.ReadExactlyAsync(frame, ct);
        Assert.Equal(address, frame[9]);
        await stream.WriteAsync(new byte[] { frame[0], frame[1], 0, 0, 0, 7, 1, 3, 4, 0x40, 0, 0, 0 }, ct);
    }

    private sealed class UnusedSamples : IIndustrialTelemetrySamplesClient
    {
        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test does not deliver a full sample bucket.");
    }
}
