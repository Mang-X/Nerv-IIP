using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus;

public sealed record ModbusConnectorOptions(
    string ConnectorId,
    string ConnectorHostId,
    string OrganizationId,
    string EnvironmentId,
    string Endpoint,
    string? CredentialReference,
    IReadOnlyList<ModbusRegisterMapping> Registers,
    int MaxReconnectAttempts = 1,
    string? CollectionConnectorId = null)
{
    public string EffectiveCollectionConnectorId => CollectionConnectorId ?? $"modbus-{ConnectorId}";
}

public sealed record ModbusConnectionOptions(string Endpoint, string? CredentialReference);

public sealed record ModbusRegisterMapping(
    string DeviceAssetId,
    string TagKey,
    byte UnitId,
    ModbusRegisterTable Table,
    ushort Address,
    ushort RegisterCount,
    decimal Scale,
    decimal Offset,
    int BucketSeconds,
    ModbusRegisterDataType DataType = ModbusRegisterDataType.UInt16,
    ModbusWordOrder WordOrder = ModbusWordOrder.BigEndian,
    string? SamplingPolicy = null,
    bool Enabled = true);

public enum ModbusRegisterTable
{
    HoldingRegisters,
    InputRegisters
}

public enum ModbusRegisterDataType
{
    UInt16,
    Int16,
    UInt32,
    Int32,
    Float32
}

public enum ModbusWordOrder
{
    BigEndian,
    LittleEndian
}

public sealed record ModbusRegisterSample(
    byte UnitId,
    ModbusRegisterTable Table,
    ushort Address,
    decimal Value,
    DateTimeOffset ObservedAtUtc);

public sealed record ModbusConnectorState(
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    long ReceivedSamples,
    long PostedBuckets,
    long DroppedSamples,
    long ErrorCount,
    long ReconnectCount,
    DateTimeOffset? LastSampleAtUtc,
    DateTimeOffset? LastPostedBucketEndUtc)
{
    public IReadOnlyDictionary<string, decimal> Metrics => new Dictionary<string, decimal>
    {
        ["receivedSamples"] = ReceivedSamples,
        ["postedBuckets"] = PostedBuckets,
        ["droppedSamples"] = DroppedSamples,
        ["errorCount"] = ErrorCount,
        ["reconnectCount"] = ReconnectCount
    };
}

public interface IModbusTcpClient
{
    Task ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersAsync(
        ModbusRegisterMapping mapping,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);

    Task ProbeAsync(ModbusRegisterMapping mapping, CancellationToken cancellationToken);
}

internal sealed class ModbusTelemetryBucket(ModbusRegisterMapping mapping, DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
{
    private decimal _sum;

    public ModbusRegisterMapping Mapping { get; } = mapping;
    public DateTimeOffset BucketStartUtc { get; } = bucketStartUtc;
    public DateTimeOffset BucketEndUtc { get; } = bucketEndUtc;
    public int SampleCount { get; private set; }
    public decimal MinValue { get; private set; }
    public decimal MaxValue { get; private set; }
    public decimal FirstValue { get; private set; }
    public decimal LastValue { get; private set; }
    public decimal AverageValue => SampleCount == 0 ? 0 : _sum / SampleCount;

    public void Add(decimal value)
    {
        if (SampleCount == 0)
        {
            MinValue = value;
            MaxValue = value;
            FirstValue = value;
        }
        else
        {
            MinValue = Math.Min(MinValue, value);
            MaxValue = Math.Max(MaxValue, value);
        }

        LastValue = value;
        _sum += value;
        SampleCount++;
    }
}
