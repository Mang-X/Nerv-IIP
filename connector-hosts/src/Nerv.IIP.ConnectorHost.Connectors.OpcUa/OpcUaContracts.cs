using System.Globalization;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed record OpcUaConnectorOptions(
    string ConnectorId,
    string ConnectorHostId,
    string OrganizationId,
    string EnvironmentId,
    string EndpointUrl,
    string SecurityPolicy,
    string SecurityMode,
    string? CredentialReference,
    string BrowseRootNodeId,
    IReadOnlyList<OpcUaTagSubscription> Tags,
    int MaxReconnectAttempts = 1,
    bool AutoAcceptUntrustedServerCertificates = false);

public sealed record OpcUaConnectionOptions(
    string EndpointUrl,
    string SecurityPolicy,
    string SecurityMode,
    string? CredentialReference,
    bool AutoAcceptUntrustedServerCertificates)
{
    public bool UsesSecurity =>
        !string.Equals(SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(SecurityMode, "None", StringComparison.OrdinalIgnoreCase);
}

public sealed record OpcUaUserCredential(string UserName, string Password);

public sealed record OpcUaTagSubscription(
    string DeviceAssetId,
    string TagKey,
    string NodeId,
    int SamplingIntervalMilliseconds,
    int BucketSeconds);

public sealed record OpcUaNode(string NodeId, string DisplayName, bool IsVariable);

public sealed record OpcUaDataChange(
    string NodeId,
    object? Value,
    DateTimeOffset SourceTimestampUtc,
    string Status);

public sealed record OpcUaWriteRequest(string NodeId, string Value);

public sealed record OpcUaWriteReceipt(string Status, string ReceiptCode, string Message);

public sealed record RecordIndustrialTelemetrySampleRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int SampleCount,
    decimal MinValue,
    decimal MaxValue,
    decimal AverageValue,
    string SourceSequence,
    string? SourceSystem,
    string? SourceConnector,
    string? DeviceState = null,
    DateTimeOffset? StateOccurredAtUtc = null);

public sealed record OpcUaConnectorState(
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    long ReceivedSamples,
    long PostedBuckets,
    long DroppedSamples,
    long ReconnectCount,
    long SubscriptionRecoveries,
    DateTimeOffset? LastSampleAtUtc,
    DateTimeOffset? LastPostedBucketEndUtc)
{
    public IReadOnlyDictionary<string, decimal> Metrics => new Dictionary<string, decimal>
    {
        ["receivedSamples"] = ReceivedSamples,
        ["postedBuckets"] = PostedBuckets,
        ["droppedSamples"] = DroppedSamples,
        ["reconnectCount"] = ReconnectCount,
        ["subscriptionRecoveries"] = SubscriptionRecoveries
    };
}

public interface IOpcUaClient
{
    Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken);

    Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken);

    Task SubscribeAsync(
        IReadOnlyList<OpcUaTagSubscription> tags,
        Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
        CancellationToken cancellationToken);

    Task<OpcUaWriteReceipt> WriteAsync(OpcUaWriteRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("This OPC UA client does not support writes.");
    }

    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface IOpcUaCredentialResolver
{
    ValueTask<OpcUaUserCredential?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken);
}

public interface IIndustrialTelemetrySamplesClient
{
    Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken);
}

public sealed class OpcUaConnectionLostException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class TelemetryBucket(OpcUaTagSubscription tag, DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
{
    private decimal _sum;

    public OpcUaTagSubscription Tag { get; } = tag;
    public DateTimeOffset BucketStartUtc { get; } = bucketStartUtc;
    public DateTimeOffset BucketEndUtc { get; } = bucketEndUtc;
    public int SampleCount { get; private set; }
    public decimal MinValue { get; private set; }
    public decimal MaxValue { get; private set; }
    public decimal AverageValue => SampleCount == 0 ? 0 : _sum / SampleCount;

    public void Add(decimal value)
    {
        if (SampleCount == 0)
        {
            MinValue = value;
            MaxValue = value;
        }
        else
        {
            MinValue = Math.Min(MinValue, value);
            MaxValue = Math.Max(MaxValue, value);
        }

        _sum += value;
        SampleCount++;
    }
}

internal static class OpcUaValueConversion
{
    public static bool TryConvertDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                result = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            case string stringValue:
                return decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
            case IConvertible convertible:
                try
                {
                    result = Convert.ToDecimal(convertible, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    result = 0;
                    return false;
                }
            default:
                result = 0;
                return false;
        }
    }
}
