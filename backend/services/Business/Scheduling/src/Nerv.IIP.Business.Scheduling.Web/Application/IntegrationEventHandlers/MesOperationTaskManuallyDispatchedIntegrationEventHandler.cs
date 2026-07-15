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

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName, MesIntegrationEventTypes.OperationTaskManuallyDispatched, MesIntegrationEventVersions.V1);

    public Task HandleAsync(MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleEnvelopeAsync(integrationEvent, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskManuallyDispatchedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleEnvelopeAsync(
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
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
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
        MesOverrideInboxIdentity inboxIdentity,
        CancellationToken cancellationToken)
    {
        if (!MesOverrideConsumerValidation.IsValidDispatch(integrationEvent, inboxIdentity))
        {
            await deadLetterStore.AddAsync(MesOverrideConsumerPersistence.CreateDeadLetter(
                ConsumerName, integrationEvent,
                "scheduling.mesManualDispatch.invalidPayload",
                "MES manual dispatch requires real operation, resource, work center, and a positive scheduling window."), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await UpsertOverrideAsync(integrationEvent, cancellationToken);
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
        MesOperationTaskManuallyDispatchedIntegrationEvent integrationEvent,
        Nerv.IIP.Contracts.IntegrationEvents.IIntegrationEventEnvelope inboxEnvelope,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, inboxEnvelope, cancellationToken))
        {
            return;
        }

        await UpsertOverrideAsync(integrationEvent, cancellationToken, requireExisting: true);
        await dbContext.SaveChangesAsync(cancellationToken);
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
                ScheduleOperationOverrideLockReasonCodes.MesManualDispatch,
                ScheduleOperationOverrideSourceTypes.MesDispatch, integrationEvent.EventId,
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

}
