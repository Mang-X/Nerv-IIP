namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

public partial record DefectRecordId : IGuidStronglyTypedId;

public sealed class DefectRecord : Entity<DefectRecordId>, IAggregateRoot
{
    public const string OpenStatus = "Open";

    private DefectRecord()
    {
    }

    private DefectRecord(
        string organizationId,
        string environmentId,
        string defectNo,
        string workOrderId,
        string? operationTaskId,
        string defectCode,
        decimal quantity,
        DateTimeOffset recordedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        DefectNo = DomainGuard.Required(defectNo, nameof(defectNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        DefectCode = DomainGuard.Required(defectCode, nameof(defectCode));
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        Status = OpenStatus;
        RecordedAtUtc = recordedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DefectNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string DefectCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static DefectRecord Create(
        string organizationId,
        string environmentId,
        string defectNo,
        string workOrderId,
        string? operationTaskId,
        string defectCode,
        decimal quantity,
        DateTimeOffset recordedAtUtc)
    {
        return new DefectRecord(
            organizationId,
            environmentId,
            defectNo,
            workOrderId,
            operationTaskId,
            defectCode,
            quantity,
            recordedAtUtc);
    }
}
