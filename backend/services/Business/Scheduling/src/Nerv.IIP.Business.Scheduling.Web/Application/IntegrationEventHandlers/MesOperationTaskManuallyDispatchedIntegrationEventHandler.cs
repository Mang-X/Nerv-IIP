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
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        if (!IsValidEnvelopeProjectionIdentity(integrationEvent) ||
            !IsValidIdentity(payload.WorkOrderId) ||
            !IsValidIdentity(payload.OperationTaskId) ||
            !IsValidIdentity(payload.ResourceId) ||
            !IsValidIdentity(payload.WorkCenterId) ||
            payload.OperationSequence <= 0 ||
            payload.DispatchRevision < 0 ||
            payload.EndUtc <= payload.StartUtc)
        {
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(
                ConsumerName, integrationEvent, "scheduling.mesManualDispatch.invalidPayload",
                "MES manual dispatch requires real operation, resource, work center, and a positive scheduling window."), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await UpsertOverrideAsync(integrationEvent, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext, ConsumerName, integrationEvent, cancellationToken))
            {
                return;
            }

            await UpsertOverrideAsync(integrationEvent, cancellationToken, requireExisting: true);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpsertOverrideAsync(
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken,
        bool requireExisting = false)
    {
        var payload = integrationEvent.Payload;
        var fact = await dbContext.ScheduleOperationOverrides.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.OperationId == payload.OperationTaskId, cancellationToken);
        if (fact is null)
        {
            if (requireExisting)
            {
                throw new DbUpdateException("Concurrent override insert did not produce a readable current fact.");
            }

            fact = ScheduleOperationOverride.Create(
                integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                payload.WorkOrderId, payload.OperationTaskId, payload.OperationSequence,
                payload.ResourceId, payload.WorkCenterId, payload.StartUtc, payload.EndUtc,
                "mes-manual-dispatch", "mes-dispatch", integrationEvent.EventId,
                integrationEvent.Actor, integrationEvent.OccurredAtUtc, integrationEvent.OccurredAtUtc);
            fact.TryApplyMesDispatch(payload.ResourceId, payload.WorkCenterId, payload.StartUtc,
                payload.EndUtc, integrationEvent.EventId, integrationEvent.Actor,
                payload.DispatchRevision, integrationEvent.OccurredAtUtc, integrationEvent.OccurredAtUtc);
            dbContext.ScheduleOperationOverrides.Add(fact);
            return;
        }

        fact.TryApplyMesDispatch(payload.ResourceId, payload.WorkCenterId, payload.StartUtc,
            payload.EndUtc, integrationEvent.EventId, integrationEvent.Actor,
            payload.DispatchRevision, integrationEvent.OccurredAtUtc, integrationEvent.OccurredAtUtc);
    }

    private static bool IsValidEnvelopeProjectionIdentity(
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent) =>
        IsValidIdentity(integrationEvent.OrganizationId, 64) &&
        IsValidIdentity(integrationEvent.EnvironmentId, 64) &&
        IsValidIdentity(integrationEvent.EventId) &&
        IsValidIdentity(integrationEvent.Actor);

    private static bool IsValidIdentity(string value, int maxLength = 128) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= maxLength;
}
