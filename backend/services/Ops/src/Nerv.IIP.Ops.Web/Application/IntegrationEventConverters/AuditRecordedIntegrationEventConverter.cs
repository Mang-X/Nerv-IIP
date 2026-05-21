using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class AuditRecordedIntegrationEventConverter
    : IIntegrationEventConverter<AuditRecordedDomainEvent, AuditRecordedIntegrationEvent>
{
    public AuditRecordedIntegrationEvent Convert(AuditRecordedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var audit = domainEvent.AuditRecord;

        return new AuditRecordedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.AuditRecorded",
            1,
            audit.OccurredAtUtc,
            "ops",
            audit.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            audit.Actor,
            $"ops:audit-recorded:{audit.Id.Id}",
            new AuditRecordedPayload(
                audit.Id.Id,
                audit.OperationTaskId.Id,
                audit.Action,
                audit.Actor,
                audit.OccurredAtUtc));
    }
}
