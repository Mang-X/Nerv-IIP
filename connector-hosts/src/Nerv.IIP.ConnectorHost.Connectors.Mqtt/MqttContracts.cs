using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt;

public sealed record MqttConnectorOptions(
    string ConnectorId,
    string ConnectorHostId,
    string OrganizationId,
    string EnvironmentId,
    string Broker,
    string ClientId,
    string? CredentialReference,
    IReadOnlyList<MqttTopicMapping> TopicMappings,
    int MaxReconnectAttempts = 1,
    string? CollectionConnectorId = null)
{
    public string EffectiveCollectionConnectorId => CollectionConnectorId ?? $"mqtt-{ConnectorId}";
}

public sealed record MqttConnectionOptions(
    string Broker,
    string ClientId,
    string? CredentialReference);

public sealed record MqttCredential(string UserName, string Password);

public sealed record MqttTopicMapping(
    string DeviceAssetId,
    string TagKey,
    string TopicFilter,
    string ValueJsonPath,
    int BucketSeconds,
    string? SamplingPolicy = null,
    bool Enabled = true);

public sealed record MqttInboundMessage(
    string Topic,
    string Payload,
    DateTimeOffset ObservedAtUtc);

public sealed record MqttConnectorState(
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

public interface IMqttSubscriptionClient
{
    Task ConnectAndSubscribeAsync(
        MqttConnectionOptions options,
        IReadOnlyList<string> topicFilters,
        Func<MqttInboundMessage, CancellationToken, Task> onMessage,
        Action onDisconnected,
        CancellationToken cancellationToken);
}

public interface IMqttCredentialResolver
{
    ValueTask<MqttCredential?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken);
}

internal sealed class MqttTelemetryBucket(MqttTopicMapping mapping, DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
{
    private decimal _sum;

    public MqttTopicMapping Mapping { get; } = mapping;
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
