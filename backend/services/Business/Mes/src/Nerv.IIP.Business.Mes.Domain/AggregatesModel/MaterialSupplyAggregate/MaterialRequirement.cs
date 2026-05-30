namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

public partial record MaterialRequirementId : IGuidStronglyTypedId;

public sealed class MaterialRequirement : Entity<MaterialRequirementId>, IAggregateRoot
{
    private MaterialRequirement()
    {
    }

    private MaterialRequirement(
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string? materialLotId,
        decimal requiredQuantity,
        decimal availableQuantity,
        decimal stagedQuantity,
        string sourceSystem,
        string sourceSnapshotId,
        DateTimeOffset capturedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        MaterialId = DomainGuard.Required(materialId, nameof(materialId));
        MaterialLotId = string.IsNullOrWhiteSpace(materialLotId) ? null : materialLotId.Trim();
        RequiredQuantity = DomainGuard.Positive(requiredQuantity, nameof(requiredQuantity));
        AvailableQuantity = DomainGuard.NonNegative(availableQuantity, nameof(availableQuantity));
        StagedQuantity = DomainGuard.NonNegative(stagedQuantity, nameof(stagedQuantity));
        SourceSystem = DomainGuard.Required(sourceSystem, nameof(sourceSystem));
        SourceSnapshotId = DomainGuard.Required(sourceSnapshotId, nameof(sourceSnapshotId));
        CapturedAtUtc = capturedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string MaterialId { get; private set; } = string.Empty;
    public string? MaterialLotId { get; private set; }
    public decimal RequiredQuantity { get; private set; }
    public decimal AvailableQuantity { get; private set; }
    public decimal StagedQuantity { get; private set; }
    public string SourceSystem { get; private set; } = string.Empty;
    public string SourceSnapshotId { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; private set; }

    public static MaterialRequirement Capture(
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string? materialLotId,
        decimal requiredQuantity,
        decimal availableQuantity,
        decimal stagedQuantity,
        string sourceSystem,
        string sourceSnapshotId,
        DateTimeOffset capturedAtUtc)
    {
        return new MaterialRequirement(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            materialId,
            materialLotId,
            requiredQuantity,
            availableQuantity,
            stagedQuantity,
            sourceSystem,
            sourceSnapshotId,
            capturedAtUtc);
    }
}
