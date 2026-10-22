namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationTaskStartAuthorizationId : IGuidStronglyTypedId;

/// <summary>
/// Immutable internal MES fact proving that a trusted caller authorized a specific
/// operation task to start before its preceding operations completed.
/// </summary>
public sealed class OperationTaskStartAuthorization : Entity<OperationTaskStartAuthorizationId>, IAggregateRoot
{
    private OperationTaskStartAuthorization()
    {
    }

    private OperationTaskStartAuthorization(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        string approvalChainId,
        int operationSequence,
        string reason,
        string authorizedBy,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset authorizedAtUtc,
        string resultStatus)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        ApprovalChainId = DomainGuard.Required(approvalChainId, nameof(approvalChainId));
        Reason = DomainGuard.Required(reason, nameof(reason));
        AuthorizedBy = DomainGuard.Required(authorizedBy, nameof(authorizedBy));
        CorrelationId = DomainGuard.Required(correlationId, nameof(correlationId));
        IdempotencyKey = DomainGuard.Required(idempotencyKey, nameof(idempotencyKey));
        ResultStatus = DomainGuard.Required(resultStatus, nameof(resultStatus));
        OperationSequence = operationSequence;
        AuthorizedAtUtc = authorizedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string ApprovalChainId { get; private set; } = string.Empty;
    public int OperationSequence { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string AuthorizedBy { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset AuthorizedAtUtc { get; private set; }
    public string ResultStatus { get; private set; } = string.Empty;

    public static OperationTaskStartAuthorization Record(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        string approvalChainId,
        int operationSequence,
        string reason,
        string authorizedBy,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset authorizedAtUtc,
        string resultStatus) =>
        new(
            organizationId,
            environmentId,
            operationTaskId,
            workOrderId,
            approvalChainId,
            operationSequence,
            reason,
            authorizedBy,
            correlationId,
            idempotencyKey,
            authorizedAtUtc,
            resultStatus);
}
