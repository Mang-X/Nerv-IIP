namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;

public partial record TelemetryRawSampleId : IGuidStronglyTypedId;

public sealed class TelemetryRawSample : Entity<TelemetryRawSampleId>, IAggregateRoot
{
    private TelemetryRawSample()
    {
    }

    private TelemetryRawSample(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        int sampleCount,
        decimal minValue,
        decimal maxValue,
        decimal averageValue,
        decimal firstValue,
        decimal lastValue,
        string sourceSequence,
        string? sourceSystem,
        string? sourceConnector)
    {
        if (bucketEndUtc <= bucketStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketEndUtc), "Bucket end must be after bucket start.");
        }

        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be positive.");
        }

        Id = new TelemetryRawSampleId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        TagKey = IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
        BucketStartUtc = bucketStartUtc;
        BucketEndUtc = bucketEndUtc;
        BucketEndUnixTimeMilliseconds = bucketEndUtc.ToUnixTimeMilliseconds();
        SampleCount = sampleCount;
        MinValue = minValue;
        MaxValue = maxValue;
        AverageValue = averageValue;
        FirstValue = firstValue;
        LastValue = lastValue;
        SourceSequence = IndustrialTelemetryText.Required(sourceSequence, nameof(sourceSequence));
        SourceSystem = IndustrialTelemetryText.Optional(sourceSystem);
        SourceConnector = IndustrialTelemetryText.Optional(sourceConnector);
        RecordedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public DateTimeOffset BucketStartUtc { get; private set; }
    public DateTimeOffset BucketEndUtc { get; private set; }
    public long BucketEndUnixTimeMilliseconds { get; private set; }
    public int SampleCount { get; private set; }
    public decimal MinValue { get; private set; }
    public decimal MaxValue { get; private set; }
    public decimal AverageValue { get; private set; }
    public decimal FirstValue { get; private set; }
    public decimal LastValue { get; private set; }
    public string SourceSequence { get; private set; } = string.Empty;
    public string? SourceSystem { get; private set; }
    public string? SourceConnector { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static TelemetryRawSample Record(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        int sampleCount,
        decimal minValue,
        decimal maxValue,
        decimal averageValue,
        decimal firstValue,
        decimal lastValue,
        string sourceSequence,
        string? sourceSystem,
        string? sourceConnector)
    {
        return new TelemetryRawSample(
            organizationId,
            environmentId,
            deviceAssetId,
            tagKey,
            bucketStartUtc,
            bucketEndUtc,
            sampleCount,
            minValue,
            maxValue,
            averageValue,
            firstValue,
            lastValue,
            sourceSequence,
            sourceSystem,
            sourceConnector);
    }

    public bool HasSamePayload(TelemetryRawSample other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceAssetId == other.DeviceAssetId
            && TagKey == other.TagKey
            && SourceSystem == other.SourceSystem
            && SourceConnector == other.SourceConnector
            && SourceSequence == other.SourceSequence
            && BucketStartUtc == other.BucketStartUtc
            && BucketEndUtc == other.BucketEndUtc
            && SampleCount == other.SampleCount
            && MinValue == other.MinValue
            && MaxValue == other.MaxValue
            && AverageValue == other.AverageValue
            && FirstValue == other.FirstValue
            && LastValue == other.LastValue;
    }
}
