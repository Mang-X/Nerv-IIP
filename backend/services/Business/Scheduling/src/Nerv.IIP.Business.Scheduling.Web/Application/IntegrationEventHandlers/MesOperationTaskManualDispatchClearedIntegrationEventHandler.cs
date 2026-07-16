using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(nameof(MesOperationTaskManualDispatchClearedIntegrationEvent), ConsumerName)]
public sealed class MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<MesOperationTaskManualDispatchClearedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-scheduling.mes-operation-manual-dispatch-cleared";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName, MesIntegrationEventTypes.OperationTaskManualDispatchCleared, MesIntegrationEventVersions.V1);

    public Task HandleAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleEnvelopeAsync(integrationEvent, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskManualDispatchClearedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleEnvelopeAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var inboxIdentity = MesOverrideConsumerPersistence.CreateInboxIdentity(integrationEvent);
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, inboxIdentity.Envelope, cancellationToken))
        {
            return;
        }

        var envelopeValidation = MesOverrideConsumerValidation.ValidateEnvelope(integrationEvent, ConsumerOptions);
        if (!envelopeValidation.IsValid)
        {
            await deadLetterStore.AddAsync(MesOverrideConsumerPersistence.CreateDeadLetter(
                ConsumerName, integrationEvent,
                envelopeValidation.FailureCode, envelopeValidation.Message), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await HandleValidEventAsync(integrationEvent, inboxIdentity, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        MesOverrideInboxIdentity inboxIdentity,
        CancellationToken cancellationToken)
    {
        if (!MesOverrideConsumerValidation.IsValidClear(integrationEvent, inboxIdentity))
        {
            await deadLetterStore.AddAsync(MesOverrideConsumerPersistence.CreateDeadLetter(
                ConsumerName, integrationEvent,
                "scheduling.mesManualDispatchCleared.invalidPayload",
                "MES manual dispatch clear requires real operation, resource, work center, a positive prior window and revision, and a recognized reason."), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ClearOverrideAsync(integrationEvent, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RetryAfterConcurrentUpdateAsync(integrationEvent, inboxIdentity.Envelope, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            MesOverrideConsumerPersistence.IsOverrideInsertConflict(exception, dbContext))
        {
            await RetryAfterConcurrentUpdateAsync(integrationEvent, inboxIdentity.Envelope, cancellationToken);
        }
    }

    private async Task RetryAfterConcurrentUpdateAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        Nerv.IIP.Contracts.IntegrationEvents.IIntegrationEventEnvelope inboxEnvelope,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, inboxEnvelope, cancellationToken))
        {
            return;
        }

        await ClearOverrideAsync(integrationEvent, cancellationToken, requireExisting: true);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearOverrideAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
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

            dbContext.ScheduleOperationOverrides.Add(ScheduleOperationOverride.CreateClearedMesDispatch(
                integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
                payload.WorkOrderId, payload.OperationTaskId, payload.OperationSequence,
                payload.ResourceId, payload.WorkCenterId, payload.StartUtc, payload.EndUtc,
                integrationEvent.EventId, integrationEvent.Actor, payload.DispatchRevision,
                integrationEvent.OccurredAtUtc, payload.ReasonCode, payload.ClearedAtUtc));
            return;
        }

        fact.TryClearMesDispatch(payload.DispatchRevision, integrationEvent.EventId,
            integrationEvent.Actor, integrationEvent.OccurredAtUtc, payload.ReasonCode,
            payload.ClearedAtUtc);
    }

}
