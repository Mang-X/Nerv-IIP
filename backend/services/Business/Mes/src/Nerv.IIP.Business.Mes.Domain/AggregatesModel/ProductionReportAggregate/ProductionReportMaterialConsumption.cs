using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportMaterialConsumptionId : IGuidStronglyTypedId;

public sealed class ProductionReportMaterialConsumption : Entity<ProductionReportMaterialConsumptionId>, IAggregateRoot
{
    public const string UnspecifiedUomCode = "UNSPECIFIED";
    public const int FailureMessageMaxLength = 500;

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
        string uomCode,
        decimal consumedQuantity,
        string materialIssueRequestNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        MaterialId = DomainGuard.Required(materialId, nameof(materialId));
        MaterialLotId = DomainGuard.Required(materialLotId, nameof(materialLotId));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        if (consumedQuantity == 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(consumedQuantity), "Consumed quantity cannot be zero.");
        }

        ConsumedQuantity = consumedQuantity;
        MaterialIssueRequestNo = DomainGuard.Required(materialIssueRequestNo, nameof(materialIssueRequestNo));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string MaterialId { get; private set; } = string.Empty;
    public string MaterialLotId { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal ConsumedQuantity { get; private set; }
    public string MaterialIssueRequestNo { get; private set; } = string.Empty;
    public string? InventoryPostingFailureCode { get; private set; }
    public string? InventoryPostingFailureMessage { get; private set; }
    public DateTimeOffset? InventoryPostingFailedAtUtc { get; private set; }

    public static ProductionReportMaterialConsumption Record(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        string materialId,
        string materialLotId,
        string uomCode,
        decimal consumedQuantity,
        string materialIssueRequestNo)
    {
        DomainGuard.Positive(consumedQuantity, nameof(consumedQuantity));
        var consumption = new ProductionReportMaterialConsumption(
            organizationId,
            environmentId,
            reportNo,
            workOrderId,
            operationTaskId,
            materialId,
            materialLotId,
            uomCode,
            consumedQuantity,
            materialIssueRequestNo);
        consumption.AddDomainEvent(new ProductionMaterialConsumedDomainEvent(consumption));
        return consumption;
    }

    public static ProductionReportMaterialConsumption Reverse(
        ProductionReportMaterialConsumption original,
        string reversalReportNo)
    {
        ArgumentNullException.ThrowIfNull(original);
        var consumption = new ProductionReportMaterialConsumption(
            original.OrganizationId,
            original.EnvironmentId,
            reversalReportNo,
            original.WorkOrderId,
            original.OperationTaskId,
            original.MaterialId,
            original.MaterialLotId,
            original.UomCode,
            -original.ConsumedQuantity,
            original.MaterialIssueRequestNo);
        consumption.AddDomainEvent(new ProductionMaterialConsumedDomainEvent(consumption));
        return consumption;
    }

    public void MarkInventoryPostingFailed(string failureCode, string failureMessage, DateTimeOffset failedAtUtc)
    {
        InventoryPostingFailureCode = DomainGuard.Required(failureCode, nameof(failureCode));
        InventoryPostingFailureMessage = NormalizeFailureMessage(failureMessage);
        InventoryPostingFailedAtUtc = failedAtUtc;
    }

    private static string NormalizeFailureMessage(string failureMessage)
    {
        var normalized = DomainGuard.Required(failureMessage, nameof(failureMessage));
        return normalized.Length <= FailureMessageMaxLength
            ? normalized
            : normalized[..FailureMessageMaxLength];
    }
}
