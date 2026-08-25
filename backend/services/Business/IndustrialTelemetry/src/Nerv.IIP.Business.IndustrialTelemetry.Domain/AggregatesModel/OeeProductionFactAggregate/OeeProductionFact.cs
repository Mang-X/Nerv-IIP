namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;

public partial record OeeProductionFactId : IGuidStronglyTypedId;

public sealed record OeeHistoricalDimensionSnapshot(
    string? SiteCode = null,
    string? WorkshopCode = null,
    string? LineCode = null,
    string? ShiftCode = null,
    string? SiteTimezone = null,
    TimeOnly? ShiftStartsAt = null,
    TimeOnly? ShiftEndsAt = null,
    bool? ShiftCrossesMidnight = null,
    int? ShiftPaidMinutes = null,
    int? ShiftBreakMinutes = null,
    DateOnly? BusinessDate = null,
    DateTimeOffset? DayBucketStartUtc = null,
    DateTimeOffset? DayBucketEndUtc = null,
    DateOnly? ShiftBusinessDate = null,
    DateTimeOffset? ShiftBucketStartUtc = null,
    DateTimeOffset? ShiftBucketEndUtc = null);

public sealed class OeeProductionFact : Entity<OeeProductionFactId>, IAggregateRoot
{
    private OeeProductionFact()
    {
    }

    private OeeProductionFact(
        string organizationId,
        string environmentId,
        string sourceReportNo,
        string? workCenterId,
        string? deviceAssetId,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        OeeHistoricalDimensionSnapshot? dimensionSnapshot,
        DateTimeOffset? aggregationOccurredAtUtc)
    {
        Id = new OeeProductionFactId(Guid.CreateVersion7());
        OrganizationId = IndustrialTelemetryText.Required(organizationId, nameof(organizationId));
        EnvironmentId = IndustrialTelemetryText.Required(environmentId, nameof(environmentId));
        SourceReportNo = IndustrialTelemetryText.Required(sourceReportNo, nameof(sourceReportNo));
        WorkCenterId = IndustrialTelemetryText.Optional(workCenterId);
        DeviceAssetId = IndustrialTelemetryText.Optional(deviceAssetId);
        GoodQuantity = goodQuantity;
        ScrapQuantity = scrapQuantity;
        ReworkQuantity = reworkQuantity;
        UomCode = IndustrialTelemetryText.Required(uomCode, nameof(uomCode));
        TheoreticalRatePerHour = theoreticalRatePerHour;
        ReportedAtUtc = reportedAtUtc;
        AggregationOccurredAtUtc = aggregationOccurredAtUtc ?? reportedAtUtc;
        SiteCode = IndustrialTelemetryText.Optional(dimensionSnapshot?.SiteCode);
        WorkshopCode = IndustrialTelemetryText.Optional(dimensionSnapshot?.WorkshopCode);
        LineCode = IndustrialTelemetryText.Optional(dimensionSnapshot?.LineCode);
        ShiftCode = IndustrialTelemetryText.Optional(dimensionSnapshot?.ShiftCode);
        SiteTimezone = IndustrialTelemetryText.Optional(dimensionSnapshot?.SiteTimezone);
        ShiftStartsAt = dimensionSnapshot?.ShiftStartsAt;
        ShiftEndsAt = dimensionSnapshot?.ShiftEndsAt;
        ShiftCrossesMidnight = dimensionSnapshot?.ShiftCrossesMidnight;
        ShiftPaidMinutes = dimensionSnapshot?.ShiftPaidMinutes;
        ShiftBreakMinutes = dimensionSnapshot?.ShiftBreakMinutes;
        BusinessDate = dimensionSnapshot?.BusinessDate;
        DayBucketStartUtc = dimensionSnapshot?.DayBucketStartUtc;
        DayBucketEndUtc = dimensionSnapshot?.DayBucketEndUtc;
        ShiftBusinessDate = dimensionSnapshot?.ShiftBusinessDate;
        ShiftBucketStartUtc = dimensionSnapshot?.ShiftBucketStartUtc;
        ShiftBucketEndUtc = dimensionSnapshot?.ShiftBucketEndUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceReportNo { get; private set; } = string.Empty;
    public string? WorkCenterId { get; private set; }
    public string? DeviceAssetId { get; private set; }
    public decimal GoodQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReworkQuantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public decimal? TheoreticalRatePerHour { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }
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
    public DateTimeOffset? DayBucketStartUtc { get; private set; }
    public DateTimeOffset? DayBucketEndUtc { get; private set; }
    public DateOnly? ShiftBusinessDate { get; private set; }
    public DateTimeOffset? ShiftBucketStartUtc { get; private set; }
    public DateTimeOffset? ShiftBucketEndUtc { get; private set; }

    public static OeeProductionFact Project(
        string organizationId,
        string environmentId,
        string sourceReportNo,
        string? workCenterId,
        string? deviceAssetId,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        OeeHistoricalDimensionSnapshot? dimensionSnapshot = null,
        DateTimeOffset? aggregationOccurredAtUtc = null)
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
            dimensionSnapshot,
            aggregationOccurredAtUtc);
    }

    public OeeHistoricalDimensionSnapshot HistoricalDimensionSnapshot() =>
        new(
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
            DayBucketStartUtc,
            DayBucketEndUtc,
            ShiftBusinessDate,
            ShiftBucketStartUtc,
            ShiftBucketEndUtc);
}
