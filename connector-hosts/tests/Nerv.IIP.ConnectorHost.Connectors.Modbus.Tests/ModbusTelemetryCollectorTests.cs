using Nerv.IIP.ConnectorHost.Connectors.Modbus;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests;

public sealed class ModbusTelemetryCollectorTests
{
    [Fact]
    public async Task Run_cycle_polls_configured_registers_and_posts_bucketed_sample_with_stable_source_sequence()
    {
        var now = new DateTimeOffset(2026, 7, 5, 8, 1, 1, TimeSpan.Zero);
        var modbus = new FakeModbusTcpClient(
            new ModbusRegisterSample(1, ModbusRegisterTable.HoldingRegisters, 40001, 10m, new DateTimeOffset(2026, 7, 5, 8, 0, 10, TimeSpan.Zero)),
            new ModbusRegisterSample(1, ModbusRegisterTable.HoldingRegisters, 40001, 20m, new DateTimeOffset(2026, 7, 5, 8, 0, 40, TimeSpan.Zero)));
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(modbus, samples, () => now);

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
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal("modbus:modbus-line-1:temperature:1783238400000", request.SourceSequence);
        Assert.Equal(2, samples.WriteAttempts);
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
    public async Task Run_cycle_marks_empty_register_read_as_dropped_without_reconnect()
    {
        var modbus = new SequencedModbusTcpClient([[]]);
        var connector = CreateConnector(modbus, new RecordingIndustrialTelemetrySamplesClient());

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));
        Assert.Equal("running", target.ReportedStatus);
        Assert.Equal("degraded", target.HealthStatus);
        Assert.Equal("1", target.Metadata["droppedSamples"]);
        Assert.Equal("0", target.Metadata["reconnectCount"]);
    }

    private static ModbusConnector CreateConnector(
        IModbusTcpClient modbus,
        IIndustrialTelemetrySamplesClient samples,
        Func<DateTimeOffset>? utcNow = null)
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
                ]),
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
