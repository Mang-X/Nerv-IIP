using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportId : IGuidStronglyTypedId;

public sealed class ProductionReport : Entity<ProductionReportId>, IAggregateRoot
{
    public const string ManualSource = "manual";
    public const string TelemetrySource = "telemetry";

    private ProductionReport()
    {
    }

    private ProductionReport(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        decimal goodQuantity,
        decimal scrapQuantity,
        bool completesOperation,
        DateTimeOffset reportedAtUtc,
        decimal reworkQuantity,
        string? scrapReasonCode,
        string? defectRecordNo,
        string? producedLotNo,
        string? serialNo,
        string? reversedReportNo,
        string? reversalReason,
        ProductionReportOeeProjection? oeeProjection,
        string source,
        int materialMovementCount)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        GoodQuantity = goodQuantity;
        ScrapQuantity = scrapQuantity;
        ReworkQuantity = reworkQuantity;
        if (GoodQuantity == 0m && ScrapQuantity == 0m && ReworkQuantity == 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one reported quantity must be positive.");
        }

        CompletesOperation = completesOperation;
        ReportedAtUtc = reportedAtUtc;
        ScrapReasonCode = string.IsNullOrWhiteSpace(scrapReasonCode) ? null : scrapReasonCode.Trim();
        DefectRecordNo = string.IsNullOrWhiteSpace(defectRecordNo) ? null : defectRecordNo.Trim();
        ProducedLotNo = string.IsNullOrWhiteSpace(producedLotNo) ? null : producedLotNo.Trim();
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
        ReversedReportNo = string.IsNullOrWhiteSpace(reversedReportNo) ? null : reversedReportNo.Trim();
        ReversalReason = string.IsNullOrWhiteSpace(reversalReason) ? null : reversalReason.Trim();
        OeeWorkCenterId = string.IsNullOrWhiteSpace(oeeProjection?.WorkCenterId) ? null : oeeProjection.WorkCenterId.Trim();
        OeeDeviceAssetId = string.IsNullOrWhiteSpace(oeeProjection?.DeviceAssetId) ? null : oeeProjection.DeviceAssetId.Trim();
        OeeUomCode = string.IsNullOrWhiteSpace(oeeProjection?.UomCode) ? null : oeeProjection.UomCode.Trim();
        OeeTheoreticalRatePerHour = oeeProjection?.TheoreticalRatePerHour;
        Source = NormalizeSource(source);
        MaterialMovementCount = materialMovementCount >= 0 ? materialMovementCount : throw new ArgumentOutOfRangeException(nameof(materialMovementCount));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReworkQuantity { get; private set; }
    public string? ScrapReasonCode { get; private set; }
    public string? DefectRecordNo { get; private set; }
    public string? ProducedLotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string? ReversedReportNo { get; private set; }
    public string? ReversalReason { get; private set; }
    public string? OeeWorkCenterId { get; private set; }
    public string? OeeDeviceAssetId { get; private set; }
    public string? OeeUomCode { get; private set; }
    public decimal? OeeTheoreticalRatePerHour { get; private set; }
    public string Source { get; private set; } = ManualSource;
    public bool CompletesOperation { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }
    public int MaterialMovementCount { get; private set; }

    public bool IsReversal => !string.IsNullOrWhiteSpace(ReversedReportNo);

    public ProductionReportOeeProjection? GetOeeProjection()
    {
        return string.IsNullOrWhiteSpace(OeeWorkCenterId) || string.IsNullOrWhiteSpace(OeeUomCode)
            ? null
            : new ProductionReportOeeProjection(OeeWorkCenterId, OeeDeviceAssetId, OeeUomCode, OeeTheoreticalRatePerHour);
    }

    public static ProductionReport Record(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        decimal goodQuantity,
        decimal scrapQuantity,
        bool completesOperation,
        DateTimeOffset reportedAtUtc,
        decimal reworkQuantity = 0m,
        string? scrapReasonCode = null,
        string? defectRecordNo = null,
        string? producedLotNo = null,
        string? serialNo = null,
        ProductionReportOeeProjection? oeeProjection = null,
        string source = ManualSource,
        int materialMovementCount = 0)
    {
        DomainGuard.NonNegative(goodQuantity, nameof(goodQuantity));
        DomainGuard.NonNegative(scrapQuantity, nameof(scrapQuantity));
        DomainGuard.NonNegative(reworkQuantity, nameof(reworkQuantity));
        if (goodQuantity + scrapQuantity + reworkQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one reported quantity must be positive.");
        }

        var report = new ProductionReport(
            organizationId,
            environmentId,
            reportNo,
            workOrderId,
            operationTaskId,
            goodQuantity,
            scrapQuantity,
            completesOperation,
            reportedAtUtc,
            reworkQuantity,
            scrapReasonCode,
            defectRecordNo,
            producedLotNo,
            serialNo,
            null,
            null,
            oeeProjection,
            source,
            materialMovementCount);
        report.AddDomainEvent(new ProductionReportRecordedDomainEvent(report, report.GetOeeProjection()));
        return report;
    }

    public static ProductionReport Reverse(
        ProductionReport original,
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.IsReversal)
        {
            throw new InvalidOperationException("Reversal production reports cannot be reversed.");
        }

        var originalOeeProjection = original.GetOeeProjection();
        var report = new ProductionReport(
            original.OrganizationId,
            original.EnvironmentId,
            reportNo,
            original.WorkOrderId,
            original.OperationTaskId,
            -original.GoodQuantity,
            -original.ScrapQuantity,
            original.CompletesOperation,
            reportedAtUtc,
            -original.ReworkQuantity,
            original.ScrapReasonCode,
            original.DefectRecordNo,
            original.ProducedLotNo,
            original.SerialNo,
            original.ReportNo,
            reason,
            originalOeeProjection,
            original.Source,
            original.MaterialMovementCount);
        report.AddDomainEvent(new ProductionReportRecordedDomainEvent(report, report.GetOeeProjection()));
        return report;
    }

    public static bool IsSupportedSource(string source) =>
        string.Equals(source, ManualSource, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, TelemetrySource, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSource(string source)
    {
        var normalized = DomainGuard.Required(source, nameof(source)).ToLowerInvariant();
        if (!IsSupportedSource(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Production report source must be manual or telemetry.");
        }

        return normalized;
    }
}
