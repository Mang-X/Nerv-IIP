namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportMaterialConsumptionId : IGuidStronglyTypedId;

public sealed class ProductionReportMaterialConsumption : Entity<ProductionReportMaterialConsumptionId>, IAggregateRoot
{
    private ProductionReportMaterialConsumption()
    {
    }

    private ProductionReportMaterialConsumption(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        string materialId,
        string materialLotId,
        decimal consumedQuantity,
        string? materialIssueRequestNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        MaterialId = DomainGuard.Required(materialId, nameof(materialId));
        MaterialLotId = DomainGuard.Required(materialLotId, nameof(materialLotId));
        ConsumedQuantity = DomainGuard.Positive(consumedQuantity, nameof(consumedQuantity));
        MaterialIssueRequestNo = string.IsNullOrWhiteSpace(materialIssueRequestNo) ? null : materialIssueRequestNo.Trim();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string MaterialId { get; private set; } = string.Empty;
    public string MaterialLotId { get; private set; } = string.Empty;
    public decimal ConsumedQuantity { get; private set; }
    public string? MaterialIssueRequestNo { get; private set; }

    public static ProductionReportMaterialConsumption Record(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        string materialId,
        string materialLotId,
        decimal consumedQuantity,
        string? materialIssueRequestNo)
    {
        return new ProductionReportMaterialConsumption(
            organizationId,
            environmentId,
            reportNo,
            workOrderId,
            operationTaskId,
            materialId,
            materialLotId,
            consumedQuantity,
            materialIssueRequestNo);
    }
}
