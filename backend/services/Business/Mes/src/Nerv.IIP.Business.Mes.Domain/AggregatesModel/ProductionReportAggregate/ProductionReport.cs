using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportId : IGuidStronglyTypedId;

public sealed class ProductionReport : Entity<ProductionReportId>, IAggregateRoot
{
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
        string? serialNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        GoodQuantity = DomainGuard.NonNegative(goodQuantity, nameof(goodQuantity));
        ScrapQuantity = DomainGuard.NonNegative(scrapQuantity, nameof(scrapQuantity));
        ReworkQuantity = DomainGuard.NonNegative(reworkQuantity, nameof(reworkQuantity));
        if (GoodQuantity + ScrapQuantity + ReworkQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one reported quantity must be positive.");
        }

        CompletesOperation = completesOperation;
        ReportedAtUtc = reportedAtUtc;
        ScrapReasonCode = string.IsNullOrWhiteSpace(scrapReasonCode) ? null : scrapReasonCode.Trim();
        DefectRecordNo = string.IsNullOrWhiteSpace(defectRecordNo) ? null : defectRecordNo.Trim();
        ProducedLotNo = string.IsNullOrWhiteSpace(producedLotNo) ? null : producedLotNo.Trim();
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
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
    public bool CompletesOperation { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }

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
        string? serialNo = null)
    {
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
            serialNo);
        report.AddDomainEvent(new ProductionReportRecordedDomainEvent(report));
        return report;
    }

}
