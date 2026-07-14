using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(nameof(MesOperationTaskManuallyDispatchedIntegrationEvent), ConsumerName)]
public sealed class MesOperationTaskManuallyDispatchedIntegrationEventHandlerForUpsertOverride(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<MesOperationTaskManuallyDispatchedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.mes-operation-manually-dispatched";

    private readonly IntegrationEventConsumerGuard<MesOperationTaskManuallyDispatchedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName,
            MesIntegrationEventTypes.OperationTaskManuallyDispatched, MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskManuallyDispatchedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.WorkOrderId) ||
            string.IsNullOrWhiteSpace(payload.OperationTaskId) ||
            string.IsNullOrWhiteSpace(payload.ResourceId) ||
            string.IsNullOrWhiteSpace(payload.WorkCenterId) ||
            payload.EndUtc <= payload.StartUtc)
        {
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(
                ConsumerName, integrationEvent, "scheduling.mesManualDispatch.invalidPayload",
                "MES manual dispatch requires real operation, resource, work center, and a positive scheduling window."), cancellationToken);
            return;
        }

        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var fact = await dbContext.ScheduleOperationOverrides.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.OperationId == payload.OperationTaskId, cancellationToken);
        if (fact is null)
        {
            dbContext.ScheduleOperationOverrides.Add(ScheduleOperationOverride.Create(
                integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                payload.WorkOrderId, payload.OperationTaskId, payload.OperationSequence,
                payload.ResourceId, payload.WorkCenterId, payload.StartUtc, payload.EndUtc,
                "mes-manual-dispatch", "mes-dispatch", integrationEvent.EventId,
                integrationEvent.Actor, integrationEvent.OccurredAtUtc, integrationEvent.OccurredAtUtc));
        }
        else
        {
            fact.TryReplace(payload.ResourceId, payload.WorkCenterId, payload.StartUtc,
                payload.EndUtc, "mes-manual-dispatch", "mes-dispatch", integrationEvent.EventId,
                integrationEvent.Actor, integrationEvent.OccurredAtUtc, integrationEvent.OccurredAtUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
