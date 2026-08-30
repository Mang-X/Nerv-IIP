namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;

public partial record OeeProductionFactId : IGuidStronglyTypedId;

public enum OeeHistoricalDimensionStatus
{
    Resolved,
    LegacyUnresolved,
    MissingHierarchy,
    MissingTimezone,
    InvalidTimezone,
    MissingShiftDefinition,
    InvalidShiftDefinition,
    ReportOutsideShiftWindow,
    InvalidLocalTime,
    AmbiguousLocalTime
}

public sealed record OeeHistoricalDimensionSnapshot(
    string? SiteCode,
    string? WorkshopCode,
    string? LineCode,
    string? ShiftCode,
    string? SiteTimezone,
    TimeOnly? ShiftStartsAt,
    TimeOnly? ShiftEndsAt,
    bool? ShiftCrossesMidnight,
    int? ShiftPaidMinutes,
    int? ShiftBreakMinutes,
    DateOnly? BusinessDate,
    DateTimeOffset? ShiftBucketStartUtc,
    DateTimeOffset? ShiftBucketEndUtc,
    OeeHistoricalDimensionStatus Status)
{
    public static OeeHistoricalDimensionSnapshot LegacyUnresolved { get; } = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        OeeHistoricalDimensionStatus.LegacyUnresolved);

    public static OeeHistoricalDimensionSnapshot MissingTimezone { get; } =
        LegacyUnresolved with { Status = OeeHistoricalDimensionStatus.MissingTimezone };
}

public sealed class OeeProductionFact : Entity<OeeProductionFactId>, IAggregateRoot
{
    private OeeProductionFact()
    {
    }

    private OeeProductionFact(
        string organizationId,
        string environmentId,
        string sourceReportNo,
        string workCenterId,
        string deviceAssetId,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        string? reversedReportNo,
        DateTimeOffset aggregationOccurredAtUtc,
        OeeHistoricalDimensionSnapshot historicalDimension)
    {
        Id = new OeeProductionFactId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        SourceReportNo = IndustrialTelemetryText.Required(sourceReportNo, nameof(sourceReportNo));
        WorkCenterId = IndustrialTelemetryText.Required(workCenterId, nameof(workCenterId));
        DeviceAssetId = IndustrialTelemetryText.Required(deviceAssetId, nameof(deviceAssetId));
        GoodQuantity = goodQuantity;
        ScrapQuantity = scrapQuantity;
        ReworkQuantity = reworkQuantity;
        UomCode = IndustrialTelemetryText.Required(uomCode, nameof(uomCode));
        TheoreticalRatePerHour = theoreticalRatePerHour;
        ReportedAtUtc = reportedAtUtc;
        ReversedReportNo = reversedReportNo;
        AggregationOccurredAtUtc = aggregationOccurredAtUtc;
        SiteCode = historicalDimension.SiteCode;
        WorkshopCode = historicalDimension.WorkshopCode;
        LineCode = historicalDimension.LineCode;
        ShiftCode = historicalDimension.ShiftCode;
        SiteTimezone = historicalDimension.SiteTimezone;
        ShiftStartsAt = historicalDimension.ShiftStartsAt;
        ShiftEndsAt = historicalDimension.ShiftEndsAt;
        ShiftCrossesMidnight = historicalDimension.ShiftCrossesMidnight;
        ShiftPaidMinutes = historicalDimension.ShiftPaidMinutes;
        ShiftBreakMinutes = historicalDimension.ShiftBreakMinutes;
        BusinessDate = historicalDimension.BusinessDate;
        ShiftBucketStartUtc = historicalDimension.ShiftBucketStartUtc;
        ShiftBucketEndUtc = historicalDimension.ShiftBucketEndUtc;
        HistoricalDimensionStatus = historicalDimension.Status;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceReportNo { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReworkQuantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public decimal? TheoreticalRatePerHour { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }
    public string? ReversedReportNo { get; private set; }
    public DateTimeOffset AggregationOccurredAtUtc { get; private set; }
    public string? SiteCode { get; private set; }
    public string? WorkshopCode { get; private set; }
    public string? LineCode { get; private set; }
    public string? ShiftCode { get; private set; }
    public string? SiteTimezone { get; private set; }
    public TimeOnly? ShiftStartsAt { get; private set; }
    public TimeOnly? ShiftEndsAt { get; private set; }
    public bool? ShiftCrossesMidnight { get; private set; }
    public int? ShiftPaidMinutes { get; private set; }
    public int? ShiftBreakMinutes { get; private set; }
    public DateOnly? BusinessDate { get; private set; }
    public DateTimeOffset? ShiftBucketStartUtc { get; private set; }
    public DateTimeOffset? ShiftBucketEndUtc { get; private set; }
    public OeeHistoricalDimensionStatus HistoricalDimensionStatus { get; private set; }

    public static OeeProductionFact Project(
        string organizationId,
        string environmentId,
        string sourceReportNo,
        string workCenterId,
        string deviceAssetId,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        OeeHistoricalDimensionSnapshot historicalDimension)
    {
        return new OeeProductionFact(
            organizationId,
            environmentId,
            sourceReportNo,
            workCenterId,
            deviceAssetId,
            goodQuantity,
            scrapQuantity,
            reworkQuantity,
            uomCode,
            theoreticalRatePerHour,
            reportedAtUtc,
            null,
            reportedAtUtc,
            historicalDimension);
    }

    public OeeProductionFact ProjectReversal(
        string sourceReportNo,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        DateTimeOffset reportedAtUtc)
    {
        return new OeeProductionFact(
            OrganizationId,
            EnvironmentId,
            sourceReportNo,
            WorkCenterId,
            DeviceAssetId,
            goodQuantity,
            scrapQuantity,
            reworkQuantity,
            UomCode,
            TheoreticalRatePerHour,
            reportedAtUtc,
            SourceReportNo,
            AggregationOccurredAtUtc,
            GetHistoricalDimensionSnapshot());
    }

    private OeeHistoricalDimensionSnapshot GetHistoricalDimensionSnapshot() => new(
        SiteCode,
        WorkshopCode,
        LineCode,
        ShiftCode,
        SiteTimezone,
        ShiftStartsAt,
        ShiftEndsAt,
        ShiftCrossesMidnight,
        ShiftPaidMinutes,
        ShiftBreakMinutes,
        BusinessDate,
        ShiftBucketStartUtc,
        ShiftBucketEndUtc,
        HistoricalDimensionStatus);
}
