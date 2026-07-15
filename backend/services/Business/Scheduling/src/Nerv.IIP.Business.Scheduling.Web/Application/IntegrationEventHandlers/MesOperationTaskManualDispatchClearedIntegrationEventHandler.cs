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

    private static readonly HashSet<string> RecognizedReasons =
        ["device-cleared", "operation-cancelled"];

    private readonly IntegrationEventConsumerGuard<MesOperationTaskManualDispatchClearedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName,
            MesIntegrationEventTypes.OperationTaskManualDispatchCleared, MesIntegrationEventVersions.V1));

    public Task HandleAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskManualDispatchClearedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        if (!IsValidEnvelopeProjectionIdentity(integrationEvent) ||
            !IsValidIdentity(payload.WorkOrderId) ||
            !IsValidIdentity(payload.OperationTaskId) ||
            !IsValidIdentity(payload.ResourceId) ||
            !IsValidIdentity(payload.WorkCenterId) ||
            payload.OperationSequence <= 0 ||
            payload.DispatchRevision <= 0 ||
            payload.EndUtc <= payload.StartUtc ||
            !RecognizedReasons.Contains(payload.ReasonCode))
        {
            await deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(
                ConsumerName, integrationEvent, "scheduling.mesManualDispatchCleared.invalidPayload",
                "MES manual dispatch clear requires real operation, resource, work center, a positive prior window and revision, and a recognized reason."), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ClearOverrideAsync(integrationEvent, cancellationToken);
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

            await ClearOverrideAsync(integrationEvent, cancellationToken, requireExisting: true);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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

    private static bool IsValidIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= 128;

    private static bool IsValidEnvelopeProjectionIdentity(
        MesOperationTaskManualDispatchClearedIntegrationEvent integrationEvent) =>
        IsValidIdentity(integrationEvent.OrganizationId, 64) &&
        IsValidIdentity(integrationEvent.EnvironmentId, 64) &&
        IsValidIdentity(integrationEvent.EventId, 128) &&
        IsValidIdentity(integrationEvent.Actor, 128);

    private static bool IsValidIdentity(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= maxLength;
}
