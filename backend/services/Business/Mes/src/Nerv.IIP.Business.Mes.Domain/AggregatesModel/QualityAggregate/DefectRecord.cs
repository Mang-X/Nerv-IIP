using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

public partial record DefectRecordId : IGuidStronglyTypedId;

public sealed class DefectRecord : Entity<DefectRecordId>, IAggregateRoot
{
    public const string OpenStatus = "Open";
    public const string ReworkPendingStatus = "ReworkPending";
    public const string ScrapAcceptedStatus = "ScrapAccepted";
    public const string ReturnAcceptedStatus = "ReturnAccepted";
    public const string DispositionAcceptedStatus = "DispositionAccepted";
    private const string ReworkDispositionType = "rework";
    private const string ScrapDispositionType = "scrap";
    private const string ReturnToSupplierDispositionType = "return-to-supplier";
    private const string ConditionalReleaseDispositionType = "conditional-release";
    private const string SortAndScreenDispositionType = "sort-and-screen";

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
    public string? NcrId { get; private set; }
    public string? NcrCode { get; private set; }
    public string? DispositionType { get; private set; }
    public string? DispositionReferenceId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

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
        var defect = new DefectRecord(
            organizationId,
            environmentId,
            defectNo,
            workOrderId,
            operationTaskId,
            defectCode,
            quantity,
            recordedAtUtc);
        defect.AddDomainEvent(new DefectRaisedDomainEvent(defect));
        return defect;
    }

    public void AcceptDisposition(
        string ncrId,
        string ncrCode,
        string dispositionType,
        string? dispositionReferenceId,
        DateTimeOffset changedAtUtc)
    {
        NcrId = DomainGuard.Required(ncrId, nameof(ncrId));
        NcrCode = DomainGuard.Required(ncrCode, nameof(ncrCode));
        DispositionType = DomainGuard.Required(dispositionType, nameof(dispositionType));
        DispositionReferenceId = string.IsNullOrWhiteSpace(dispositionReferenceId) ? null : dispositionReferenceId.Trim();
        Status = DispositionType.Trim().ToLowerInvariant() switch
        {
            ReworkDispositionType => ReworkPendingStatus,
            ScrapDispositionType => ScrapAcceptedStatus,
            ReturnToSupplierDispositionType => ReturnAcceptedStatus,
            ConditionalReleaseDispositionType or SortAndScreenDispositionType => DispositionAcceptedStatus,
            _ => DispositionAcceptedStatus,
        };
        ClosedAtUtc = Status == ReworkPendingStatus ? null : changedAtUtc;
    }
}
