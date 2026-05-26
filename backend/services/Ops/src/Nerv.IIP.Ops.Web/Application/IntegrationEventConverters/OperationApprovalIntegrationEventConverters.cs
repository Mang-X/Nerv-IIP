using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class OperationApprovalRequestedIntegrationEventConverter
    : IIntegrationEventConverter<OperationApprovalRequestedDomainEvent, OperationApprovalRequestedIntegrationEvent>
{
    public OperationApprovalRequestedIntegrationEvent Convert(OperationApprovalRequestedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var requestedAtUtc = task.ApprovalRequestedAtUtc ?? task.RequestedAtUtc;

        return new OperationApprovalRequestedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationApprovalRequested",
            1,
            requestedAtUtc,
            "ops",
            task.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            task.RequestedBy,
            $"ops:operation-approval-requested:{task.Id.Id}",
            new OperationApprovalRequestedPayload(
                task.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                task.RequestedBy,
                requestedAtUtc));
    }
}

public sealed class OperationApprovalApprovedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskApprovedDomainEvent, OperationApprovalApprovedIntegrationEvent>
{
    public OperationApprovalApprovedIntegrationEvent Convert(OperationTaskApprovedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;

        return new OperationApprovalApprovedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationApprovalApproved",
            1,
            domainEvent.ApprovedAtUtc,
            "ops",
            domainEvent.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            domainEvent.ApprovedBy,
            $"ops:operation-approval-approved:{task.Id.Id}",
            new OperationApprovalDecidedPayload(
                task.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                domainEvent.ApprovedBy,
                domainEvent.DecisionReason,
                domainEvent.ApprovedAtUtc));
    }
}

public sealed class OperationApprovalRejectedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskRejectedDomainEvent, OperationApprovalRejectedIntegrationEvent>
{
    public OperationApprovalRejectedIntegrationEvent Convert(OperationTaskRejectedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;

        return new OperationApprovalRejectedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationApprovalRejected",
            1,
            domainEvent.RejectedAtUtc,
            "ops",
            task.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            domainEvent.RejectedBy,
            $"ops:operation-approval-rejected:{task.Id.Id}",
            new OperationApprovalDecidedPayload(
                task.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                domainEvent.RejectedBy,
                domainEvent.DecisionReason,
                domainEvent.RejectedAtUtc));
    }
}
