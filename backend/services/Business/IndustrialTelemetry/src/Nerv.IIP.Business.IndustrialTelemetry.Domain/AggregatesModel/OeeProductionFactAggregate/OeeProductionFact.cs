namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;

public partial record OeeProductionFactId : IGuidStronglyTypedId;

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
        DateTimeOffset reportedAtUtc)
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
        DateTimeOffset reportedAtUtc)
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
            reportedAtUtc);
    }
}
