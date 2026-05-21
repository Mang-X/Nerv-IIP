using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class OperationTaskClaimedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskDispatchedDomainEvent, OperationTaskClaimedIntegrationEvent>
{
    public OperationTaskClaimedIntegrationEvent Convert(OperationTaskDispatchedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var attempt = domainEvent.Attempt;
        var leasedAtUtc = attempt.LeasedAtUtc ?? attempt.StartedAtUtc;

        return new OperationTaskClaimedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationTaskClaimed",
            1,
            leasedAtUtc,
            "ops",
            task.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            attempt.ConnectorHostId,
            $"ops:operation-task-claimed:{task.Id.Id}:{attempt.Id.Id}",
            new OperationTaskClaimedPayload(
                task.Id.Id,
                attempt.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                attempt.LeaseId ?? string.Empty,
                leasedAtUtc,
                attempt.LeasedUntilUtc ?? leasedAtUtc,
                attempt.AttemptNo ?? 0,
                attempt.MaxAttempts ?? 0));
    }
}
