using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;

public partial record TelemetrySummaryId : IGuidStronglyTypedId;

public sealed class TelemetrySummary : Entity<TelemetrySummaryId>, IAggregateRoot
{
    private TelemetrySummary()
    {
    }

    private TelemetrySummary(
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
        string? sourceSequence,
        string? sourceSystem,
        string? sourceConnector,
        string? collectionConnectorId)
    {
        if (bucketEndUtc <= bucketStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketEndUtc), "Bucket end must be after bucket start.");
        }

        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be positive.");
        }

        Id = new TelemetrySummaryId(Guid.CreateVersion7());
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
        SourceSequence = IndustrialTelemetryText.Optional(sourceSequence);
        SourceSystem = IndustrialTelemetryText.Optional(sourceSystem);
        SourceConnector = IndustrialTelemetryText.Optional(sourceConnector);
        CollectionConnectorId = IndustrialTelemetryText.Optional(collectionConnectorId);
        RecordedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new TelemetrySampleRecordedDomainEvent(this));
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
    public string? SourceSequence { get; private set; }
    public string? SourceSystem { get; private set; }
    public string? SourceConnector { get; private set; }
    public string? CollectionConnectorId { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static TelemetrySummary Record(
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
        string? sourceSequence = null,
        string? sourceSystem = null,
        string? sourceConnector = null,
        string? collectionConnectorId = null)
    {
        return new TelemetrySummary(organizationId, environmentId, deviceAssetId, tagKey, bucketStartUtc, bucketEndUtc, sampleCount, minValue, maxValue, averageValue, sourceSequence, sourceSystem, sourceConnector, collectionConnectorId);
    }

    public bool IsSameSourceSequence(TelemetrySummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceAssetId == other.DeviceAssetId
            && TagKey == other.TagKey
            && SourceSystem == other.SourceSystem
            && SourceConnector == other.SourceConnector
            && SourceSequence is not null
            && SourceSequence == other.SourceSequence;
    }

    public bool HasSamePayload(TelemetrySummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsSameSourceSequence(other)
            && BucketStartUtc == other.BucketStartUtc
            && BucketEndUtc == other.BucketEndUtc
            && SampleCount == other.SampleCount
            && MinValue == other.MinValue
            && MaxValue == other.MaxValue
            && AverageValue == other.AverageValue
            && CollectionConnectorId == other.CollectionConnectorId;
    }

    public void RaiseProductionCountDeltaEvent(decimal deltaQuantity, string reportingMode, bool hasActiveAlarm)
    {
        if (deltaQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaQuantity), "Production count delta must be positive.");
        }

        this.AddDomainEvent(new TelemetryProductionCountDeltaDomainEvent(
            this,
            deltaQuantity,
            IndustrialTelemetryText.RequiredLower(reportingMode, nameof(reportingMode)),
            hasActiveAlarm));
    }
}
