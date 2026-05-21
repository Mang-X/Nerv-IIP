using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class OperationTaskRequestedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskCreatedDomainEvent, OperationTaskRequestedIntegrationEvent>
{
    public OperationTaskRequestedIntegrationEvent Convert(OperationTaskCreatedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;

        return new OperationTaskRequestedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationTaskRequested",
            1,
            task.RequestedAtUtc,
            "ops",
            task.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            task.RequestedBy,
            $"ops:operation-task-requested:{task.Id.Id}",
            new OperationTaskRequestedPayload(
                task.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                task.RequestedBy,
                task.RequestedAtUtc));
    }
}
