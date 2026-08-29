namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public partial record OperationLaborReportSnapshotId : IGuidStronglyTypedId;

public sealed class OperationLaborReportSnapshot : Entity<OperationLaborReportSnapshotId>, IAggregateRoot
{
    private OperationLaborReportSnapshot() { }

    private OperationLaborReportSnapshot(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        string reportNo,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        bool isReversal,
        string? reversedReportNo,
        string sourceEventId)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkOrderId = ErpText.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = ErpText.Required(operationTaskId, nameof(operationTaskId));
        // Uncosted MES reports historically allow a blank work center. Persist that
        // fact so the read model can report an unavailable basis without rejecting
        // an otherwise valid production event.
        WorkCenterId = workCenterId?.Trim() ?? string.Empty;
        ReportNo = ErpText.Required(reportNo, nameof(reportNo));
        GoodQuantity = goodQuantity;
        ScrapQuantity = scrapQuantity;
        ReworkQuantity = reworkQuantity;
        UomCode = ErpText.Required(uomCode, nameof(uomCode));
        TheoreticalRatePerHour = theoreticalRatePerHour;
        ReportedAtUtc = reportedAtUtc.Offset == TimeSpan.Zero
            ? reportedAtUtc
            : throw new ArgumentException("Timestamp must use UTC offset zero.", nameof(reportedAtUtc));
        IsReversal = isReversal;
        ReversedReportNo = isReversal && !string.IsNullOrWhiteSpace(reversedReportNo)
            ? reversedReportNo.Trim()
            : null;
        SourceEventId = ErpText.Required(sourceEventId, nameof(sourceEventId));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReworkQuantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public decimal? TheoreticalRatePerHour { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }
    public bool IsReversal { get; private set; }
    public string? ReversedReportNo { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;

    public static OperationLaborReportSnapshot Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        string reportNo,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        string uomCode,
        decimal? theoreticalRatePerHour,
        DateTimeOffset reportedAtUtc,
        bool isReversal,
        string? reversedReportNo,
        string sourceEventId)
        => new(
            organizationId, environmentId, workOrderId, operationTaskId, workCenterId,
            reportNo, goodQuantity, scrapQuantity, reworkQuantity, uomCode,
            theoreticalRatePerHour, reportedAtUtc, isReversal, reversedReportNo, sourceEventId);
}
