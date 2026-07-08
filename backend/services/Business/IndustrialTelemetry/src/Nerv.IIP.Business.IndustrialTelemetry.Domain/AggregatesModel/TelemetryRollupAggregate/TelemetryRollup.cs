namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;

public partial record TelemetryRollupId : IGuidStronglyTypedId;

public enum TelemetryRollupGrain
{
    Hourly,
    Daily
}

public sealed class TelemetryRollup : Entity<TelemetryRollupId>, IAggregateRoot
{
    private TelemetryRollup()
    {
    }

    private TelemetryRollup(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        TelemetryRollupGrain grain,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        int sampleCount,
        decimal minValue,
        decimal maxValue,
        decimal averageValue,
        decimal firstValue,
        decimal lastValue,
        string sourceSequence)
    {
        if (windowEndUtc <= windowStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(windowEndUtc), "Rollup window end must be after start.");
        }

        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Rollup sample count must be positive.");
        }

        Id = new TelemetryRollupId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        TagKey = IndustrialTelemetryText.RequiredLower(tagKey, nameof(tagKey));
        Grain = grain;
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc;
        DailyWindowStartUtc = TruncateToDay(windowStartUtc);
        WindowEndUnixTimeMilliseconds = windowEndUtc.ToUnixTimeMilliseconds();
        SampleCount = sampleCount;
        MinValue = minValue;
        MaxValue = maxValue;
        AverageValue = averageValue;
        FirstValue = firstValue;
        LastValue = lastValue;
        SourceSequence = IndustrialTelemetryText.Required(sourceSequence, nameof(sourceSequence));
        RolledUpAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public TelemetryRollupGrain Grain { get; private set; }
    public DateTimeOffset WindowStartUtc { get; private set; }
    public DateTimeOffset WindowEndUtc { get; private set; }
    public DateTimeOffset DailyWindowStartUtc { get; private set; }
    public long WindowEndUnixTimeMilliseconds { get; private set; }
    public int SampleCount { get; private set; }
    public decimal MinValue { get; private set; }
    public decimal MaxValue { get; private set; }
    public decimal AverageValue { get; private set; }
    public decimal FirstValue { get; private set; }
    public decimal LastValue { get; private set; }
    public string SourceSequence { get; private set; } = string.Empty;
    public DateTimeOffset RolledUpAtUtc { get; private set; }

    public static TelemetryRollup Record(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        TelemetryRollupGrain grain,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        int sampleCount,
        decimal minValue,
        decimal maxValue,
        decimal averageValue,
        decimal firstValue,
        decimal lastValue,
        string sourceSequence)
    {
        return new TelemetryRollup(
            organizationId,
            environmentId,
            deviceAssetId,
            tagKey,
            grain,
            windowStartUtc,
            windowEndUtc,
            sampleCount,
            minValue,
            maxValue,
            averageValue,
            firstValue,
            lastValue,
            sourceSequence);
    }

    private static DateTimeOffset TruncateToDay(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
