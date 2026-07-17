using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Modbus;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests;

public sealed class ModbusTelemetryCollectorTests
{
    [Fact]
    public async Task Successful_protocol_probe_marks_alive_without_changing_sample_counters()
    {
        var modbus = new ProbeSequenceModbusTcpClient([null]);
        var connector = CreateConnector(modbus, new RecordingIndustrialTelemetrySamplesClient());
        var monitor = Assert.IsAssignableFrom<IConnectorConnectionMonitor>(connector);

        await monitor.RunConnectionCheckAsync(CancellationToken.None);

        var health = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Equal("alive", health.Connection!.Status);
        Assert.Equal(1, modbus.ProbeCount);
        Assert.Equal(0, connector.CurrentState.ReceivedSamples);
        Assert.Equal(0, connector.CurrentState.DroppedSamples);
        Assert.Equal(0, connector.CurrentState.PostedBuckets);
    }

    [Fact]
    public async Task Protocol_error_after_tcp_connect_does_not_fabricate_alive_or_lost()
    {
        var modbus = new ProbeSequenceModbusTcpClient([new InvalidOperationException("invalid Modbus response")]);
        var connector = CreateConnector(modbus, new RecordingIndustrialTelemetrySamplesClient());
        var monitor = Assert.IsAssignableFrom<IConnectorConnectionMonitor>(connector);

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.RunConnectionCheckAsync(CancellationToken.None));

        var health = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Equal("unknown", health.Connection!.Status);
        Assert.Equal(1, modbus.ConnectCount);
    }

    [Fact]
    public async Task Transport_timeout_marks_lost_and_recovery_starts_new_alive_interval()
    {
        var modbus = new ProbeSequenceModbusTcpClient([new TimeoutException("simulated timeout"), null]);
        var connector = CreateConnector(modbus, new RecordingIndustrialTelemetrySamplesClient());
        var monitor = Assert.IsAssignableFrom<IConnectorConnectionMonitor>(connector);

        await Assert.ThrowsAsync<TimeoutException>(() => monitor.RunConnectionCheckAsync(CancellationToken.None));
        var lost = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("lost", lost.Status);
        Assert.Equal("transport", lost.ReasonCategory);
        Assert.Equal("modbus.probe-timeout", lost.DiagnosticCode);

        await monitor.RunConnectionCheckAsync(CancellationToken.None);

        var recovered = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("alive", recovered.Status);
        Assert.NotNull(recovered.ConnectedSinceUtc);
        Assert.Null(recovered.DisconnectedSinceUtc);
        Assert.True(recovered.ObservedAtUtc >= lost.ObservedAtUtc);
    }

    [Fact]
    public async Task Discover_uses_configured_collection_connector_id_for_instance_and_health()
    {
        var connector = CreateConnector(new FakeModbusTcpClient(), new RecordingIndustrialTelemetrySamplesClient(), collectionConnectorId: "line-a-primary");

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));

        Assert.Equal("line-a-primary", target.InstanceKey);
        Assert.Equal("line-a-primary", target.CollectionHealth!.ConnectorId);
    }

    [Fact]
    public async Task Missing_collection_connector_id_preserves_legacy_derived_identity()
    {
        var connector = CreateConnector(new FakeModbusTcpClient(), new RecordingIndustrialTelemetrySamplesClient());

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));

        Assert.Equal("modbus-modbus-line-1", target.InstanceKey);
    }

    [Fact]
    public async Task Run_cycle_polls_configured_registers_and_posts_bucketed_sample_with_stable_source_sequence()
    {
        var now = new DateTimeOffset(2026, 7, 5, 8, 1, 1, TimeSpan.Zero);
        var modbus = new FakeModbusTcpClient(
            new ModbusRegisterSample(1, ModbusRegisterTable.HoldingRegisters, 40001, 10m, new DateTimeOffset(2026, 7, 5, 8, 0, 10, TimeSpan.Zero)),
            new ModbusRegisterSample(1, ModbusRegisterTable.HoldingRegisters, 40001, 20m, new DateTimeOffset(2026, 7, 5, 8, 0, 40, TimeSpan.Zero)));
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(modbus, samples, () => now);
        var initialHealth = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Null(initialHealth.ReceivedCount);
        Assert.Null(initialHealth.DroppedCount);
        Assert.Null(initialHealth.ErrorCount);
        Assert.Null(initialHealth.LastSampleAtUtc);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal("tcp://plc-line-1:502", modbus.ConnectedEndpoint);
        Assert.Equal([(1, ModbusRegisterTable.HoldingRegisters, 40001, 2)], modbus.ReadRequests);
        var request = Assert.Single(samples.Requests);
        Assert.Equal("org-001", request.OrganizationId);
        Assert.Equal("env-dev", request.EnvironmentId);
        Assert.Equal("device-line-1", request.DeviceAssetId);
        Assert.Equal("temperature", request.TagKey);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero), request.BucketStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 8, 1, 0, TimeSpan.Zero), request.BucketEndUtc);
        Assert.Equal(2, request.SampleCount);
        Assert.Equal(10m, request.MinValue);
        Assert.Equal(20m, request.MaxValue);
        Assert.Equal(15m, request.AverageValue);
        Assert.Equal("modbus:modbus-line-1:temperature:1783238400000", request.SourceSequence);
        Assert.Equal("modbus", request.SourceSystem);
        Assert.Equal("connector-host-001/modbus-line-1", request.SourceConnector);
        var health = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth;
        Assert.NotNull(health);
        Assert.Equal("modbus-modbus-line-1", health.ConnectorId);
        Assert.Equal("modbus", health.SourceSystem);
        Assert.NotEqual(Guid.Empty, health.CounterEpoch);
        Assert.Equal(2, health.ReceivedCount);
        Assert.Equal(0, health.DroppedCount);
        Assert.Equal(0, health.ErrorCount);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 8, 0, 40, TimeSpan.Zero), health.LastSampleAtUtc);
    }

    [Fact]
    public async Task Run_cycle_restores_bucket_after_downstream_failure_so_retry_keeps_same_source_sequence()
    {
        var modbus = new SequencedModbusTcpClient(
            [
                [new ModbusRegisterSample(1, ModbusRegisterTable.HoldingRegisters, 40001, 42m, new DateTimeOffset(2026, 7, 5, 8, 0, 10, TimeSpan.Zero))],
                []
            ]);
        var samples = new FailOnceIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(modbus, samples);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.RunCollectionCycleAsync(CancellationToken.None));
        var connectionAfterPostFailure = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("alive", connectionAfterPostFailure.Status);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal("modbus:modbus-line-1:temperature:1783238400000", request.SourceSequence);
        Assert.Equal(2, samples.WriteAttempts);
        Assert.Equal(1, connector.CurrentState.ErrorCount);
    }

    [Fact]
    public async Task Discover_reports_degraded_health_when_register_mapping_is_empty()
    {
        var connector = new ModbusConnector(
            new ModbusConnectorOptions(
                ConnectorId: "modbus-line-1",
                ConnectorHostId: "connector-host-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Endpoint: "tcp://plc-line-1:502",
                CredentialReference: null,
                Registers: []),
            new FakeModbusTcpClient(),
            new RecordingIndustrialTelemetrySamplesClient(),
            () => new DateTimeOffset(2026, 7, 5, 8, 1, 1, TimeSpan.Zero));

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));
        Assert.Equal("running", target.ReportedStatus);
        Assert.Equal("degraded", target.HealthStatus);
        Assert.Equal("0", target.Metadata["registerCount"]);
    }

    [Fact]
    public async Task Run_cycle_keeps_empty_register_read_unknown_without_fabricating_received_or_dropped()
    {
        var modbus = new SequencedModbusTcpClient([[]]);
        var connector = CreateConnector(modbus, new RecordingIndustrialTelemetrySamplesClient());

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));
        Assert.Equal("running", target.ReportedStatus);
        Assert.Equal("healthy", target.HealthStatus);
        Assert.Null(target.CollectionHealth!.ReceivedCount);
        Assert.Null(target.CollectionHealth.DroppedCount);
        Assert.Equal("0", target.Metadata["reconnectCount"]);
    }

    [Fact]
    public async Task Invalid_raw_register_sample_counts_received_once_and_dropped_once()
    {
        var connector = CreateConnector(
            new FakeModbusTcpClient(new ModbusRegisterSample(2, ModbusRegisterTable.HoldingRegisters, 40001, 10m, DateTimeOffset.Parse("2026-07-05T08:00:10Z"))),
            new RecordingIndustrialTelemetrySamplesClient());

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(1, connector.CurrentState.ReceivedSamples);
        Assert.Equal(1, connector.CurrentState.DroppedSamples);
    }

    private static ModbusConnector CreateConnector(
        IModbusTcpClient modbus,
        IIndustrialTelemetrySamplesClient samples,
        Func<DateTimeOffset>? utcNow = null,
        string? collectionConnectorId = null)
    {
        return new ModbusConnector(
            new ModbusConnectorOptions(
                ConnectorId: "modbus-line-1",
                ConnectorHostId: "connector-host-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Endpoint: "tcp://plc-line-1:502",
                CredentialReference: null,
                Registers:
                [
                    new ModbusRegisterMapping(
                        DeviceAssetId: "device-line-1",
                        TagKey: "temperature",
                        UnitId: 1,
                        Table: ModbusRegisterTable.HoldingRegisters,
                        Address: 40001,
                        RegisterCount: 2,
                        Scale: 1m,
                        Offset: 0m,
                        BucketSeconds: 60)
                ],
                CollectionConnectorId: collectionConnectorId),
            modbus,
            samples,
            utcNow ?? (() => new DateTimeOffset(2026, 7, 5, 8, 1, 1, TimeSpan.Zero)));
    }

    private sealed class FakeModbusTcpClient(params ModbusRegisterSample[] samples) : IModbusTcpClient
    {
        public string? ConnectedEndpoint { get; private set; }
        public List<(byte UnitId, ModbusRegisterTable Table, ushort Address, ushort Count)> ReadRequests { get; } = [];

        public Task ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken)
        {
            ConnectedEndpoint = options.Endpoint;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersAsync(
            ModbusRegisterMapping mapping,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
        {
            ReadRequests.Add((mapping.UnitId, mapping.Table, mapping.Address, mapping.RegisterCount));
            return Task.FromResult<IReadOnlyList<ModbusRegisterSample>>(samples);
        }

        public Task ProbeAsync(ModbusRegisterMapping mapping, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedModbusTcpClient(IReadOnlyList<IReadOnlyList<ModbusRegisterSample>> batches) : IModbusTcpClient
    {
        private int _index;

        public Task ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersAsync(
            ModbusRegisterMapping mapping,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
        {
            var batch = _index < batches.Count ? batches[_index++] : [];
            return Task.FromResult(batch);
        }

        public Task ProbeAsync(ModbusRegisterMapping mapping, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeSequenceModbusTcpClient(IReadOnlyList<Exception?> probeOutcomes) : IModbusTcpClient
    {
        private int _probeIndex;

        public int ConnectCount { get; private set; }
        public int ProbeCount { get; private set; }

        public Task ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersAsync(
            ModbusRegisterMapping mapping,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ModbusRegisterSample>>([]);
        }

        public Task ProbeAsync(ModbusRegisterMapping mapping, CancellationToken cancellationToken)
        {
            ProbeCount++;
            var outcome = _probeIndex < probeOutcomes.Count ? probeOutcomes[_probeIndex++] : null;
            return outcome is null ? Task.CompletedTask : Task.FromException(outcome);
        }
    }

    private sealed class RecordingIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        public int WriteAttempts { get; private set; }
        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            WriteAttempts++;
            if (WriteAttempts == 1)
            {
                throw new InvalidOperationException("simulated downstream ingestion failure");
            }

            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
