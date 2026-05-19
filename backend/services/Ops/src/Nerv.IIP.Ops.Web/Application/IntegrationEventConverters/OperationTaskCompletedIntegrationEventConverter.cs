using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class OperationTaskCompletedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskCompletedDomainEvent, OperationTaskCompletedIntegrationEvent>
{
    public OperationTaskCompletedIntegrationEvent Convert(OperationTaskCompletedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var attempt = domainEvent.Attempt;
        var result = domainEvent.Result;
        var finishedAtUtc = result.FinishedAtUtc;

        return new OperationTaskCompletedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationTaskCompleted",
            1,
            finishedAtUtc,
            "ops",
            result.Context.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            attempt.ConnectorHostId,
            $"ops:operation-task-completed:{task.Id.Id}:{attempt.Id.Id}",
            new OperationTaskCompletedPayload(
                task.Id.Id,
                attempt.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                finishedAtUtc));
    }
}
