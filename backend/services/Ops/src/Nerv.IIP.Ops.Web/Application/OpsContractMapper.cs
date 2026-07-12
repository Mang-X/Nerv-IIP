using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.Ops.Web.Application;

internal static class OpsContractMapper
{
    public static CreateOperationTaskInput ToDomainInput(this CreateOperationTaskRequest request)
    {
        return new CreateOperationTaskInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.InstanceKey,
            request.OperationCode,
            request.IdempotencyKey,
            request.RequestedBy,
            request.CorrelationId,
            request.Parameters);
    }

    public static ClaimOperationTasksInput ToDomainInput(this ClaimOperationTasksRequest request)
    {
        return new ClaimOperationTasksInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
            request.Take);
    }

    public static AbandonOperationTaskLeaseInput ToDomainInput(this AbandonOperationTaskLeaseRequest request)
    {
        return new AbandonOperationTaskLeaseInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
            request.LeaseId,
            request.AbandonReason);
    }

    public static HeartbeatOperationTaskLeaseInput ToDomainInput(this HeartbeatOperationTaskLeaseRequest request)
    {
        return new HeartbeatOperationTaskLeaseInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
            request.LeaseId,
            request.LeaseDurationSeconds);
    }

    public static SubmitAuditIntentInput ToDomainInput(this SubmitAuditIntentRequest request)
    {
        return new SubmitAuditIntentInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.OperationTaskId,
            request.Action,
            request.Actor,
            request.CorrelationId);
    }

    public static DecideOperationApprovalInput ToDomainInput(this DecideOperationApprovalRequest request)
    {
        return new DecideOperationApprovalInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.Actor,
            request.DecisionReason,
            request.CorrelationId);
    }

    public static OperationResultInput ToDomainInput(this OperationResult result)
    {
        return new OperationResultInput(
            result.Context,
            result.OperationTaskId,
            result.AttemptId,
            result.InstanceKey,
            result.OperationCode,
            result.StartedAtUtc,
            result.FinishedAtUtc,
            result.ExecutionStatus,
            result.Failure is null
                ? null
                : new OperationFailureFact(
                    result.Failure.Code,
                    result.Failure.Message,
                    result.Failure.Category,
                    result.Failure.Retryable,
                    result.Failure.Detail),
            result.Output);
    }

    public static CreateOperationTemplateInput ToDomainInput(this CreateOperationTemplateRequest request)
    {
        return new CreateOperationTemplateInput(
            request.OperationCode,
            request.DisplayName,
            request.ParameterSchemaJson,
            request.RiskLevel,
            request.DefaultMaxAttempts,
            request.DefaultLeaseDurationSeconds,
            request.RequiresApproval);
    }

    public static OperationTaskResponse ToContract(this OperationTaskDetailFact detail)
    {
        var attemptSummaries = detail.Attempts
            .Select(x => new OperationAttemptSummary(
                x.AttemptId,
                x.Status,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.Failure?.Code,
                x.LeaseId,
                x.LeasedAtUtc,
                x.LeasedUntilUtc,
                x.AttemptNo,
                Math.Max(0, (int)(x.LeasedUntilUtc - x.LeasedAtUtc).TotalSeconds),
                x.MaxAttempts,
                x.AbandonReason,
                x.Output))
            .ToList();

        var auditSummaries = detail.AuditRecords.Select(ToContract).ToList();
        var task = detail.Task;
        return new OperationTaskResponse(
            task.OperationTaskId,
            task.OrganizationId,
            task.EnvironmentId,
            task.InstanceKey,
            task.OperationCode,
            task.Status,
            task.RequestedBy,
            task.RequestedAtUtc,
            task.Approval is null
                ? null
                : new OperationApprovalSummary(
                    task.Approval.Status,
                    task.Approval.RequestedBy,
                    task.Approval.RequestedAtUtc,
                    task.Approval.DecidedBy,
                    task.Approval.DecidedAtUtc,
                    task.Approval.DecisionReason),
            detail.CurrentAttemptId,
            attemptSummaries,
            auditSummaries);
    }

    public static PagedOperationTaskListResponse ToContract(this OperationTaskListResult result)
    {
        return new PagedOperationTaskListResponse(
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.Items
                .Select(x => new OperationTaskListItem(
                    x.OperationTaskId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.InstanceKey,
                    x.OperationCode,
                    x.Status,
                    x.RequestedBy,
                    x.RequestedAtUtc,
                    x.CurrentAttemptId))
                .ToList());
    }

    public static AuditRecordListResponse ToContract(this AuditRecordListResult result)
    {
        return new AuditRecordListResponse(result.Items.Select(ToContract).ToList());
    }

    public static AuditIntentResponse ToContract(this AuditIntentResult result)
    {
        return new AuditIntentResponse(
            result.AuditRecordId,
            result.OperationTaskId,
            result.SequenceNo,
            result.PreviousIntegrityHash,
            result.Action,
            result.Actor,
            result.OccurredAtUtc,
            result.CorrelationId,
            result.IntegrityHash);
    }

    public static OperationTemplateResponse ToContract(this OperationTemplateFact template)
    {
        return new OperationTemplateResponse(
            template.OperationTemplateId,
            template.OperationCode,
            template.DisplayName,
            template.ParameterSchemaJson,
            template.RiskLevel,
            template.DefaultMaxAttempts,
            template.DefaultLeaseDurationSeconds,
            template.RequiresApproval,
            template.Enabled,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);
    }

    public static OperationTemplateListResponse ToContract(this OperationTemplateListResult result)
    {
        return new OperationTemplateListResponse(result.Items.Select(ToContract).ToList());
    }

    public static PendingOperationTasksResponse ToContract(this PendingOperationTasksResult result)
    {
        return new PendingOperationTasksResponse(
            result.Items
                .Select(x => new OperationTaskDispatchItem(
                    x.OperationTaskId,
                    x.AttemptId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.ConnectorHostId,
                    x.InstanceKey,
                    x.OperationCode,
                    x.CorrelationId,
                    x.Parameters,
                    x.LeaseId,
                    x.LeasedAtUtc,
                    x.LeasedUntilUtc,
                    x.AttemptNo,
                    x.LeaseDurationSeconds,
                    x.MaxAttempts))
                .ToList());
    }

    private static AuditRecordSummary ToContract(AuditRecordFact auditRecord)
    {
        return new AuditRecordSummary(
            auditRecord.AuditRecordId,
            auditRecord.OperationTaskId,
            auditRecord.SequenceNo,
            auditRecord.PreviousIntegrityHash,
            auditRecord.Action,
            auditRecord.Actor,
            auditRecord.OccurredAtUtc,
            auditRecord.CorrelationId,
            auditRecord.IntegrityHash);
    }
}
