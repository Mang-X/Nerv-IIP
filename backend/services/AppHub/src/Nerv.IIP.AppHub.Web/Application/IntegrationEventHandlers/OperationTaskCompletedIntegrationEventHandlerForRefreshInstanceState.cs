using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
    ISender sender,
    IServiceProvider services,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "apphub.refresh-instance-state";

    private readonly IntegrationEventConsumerGuard<OperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            "ops.OperationTaskCompleted",
            1));

    public async Task HandleAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var dbContext = services.GetService<ApplicationDbContext>();
        if (dbContext is not null &&
            !await AppHubProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(new RefreshInstanceStateAfterOperationCommand(integrationEvent), cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", ConsumerName)]
public sealed class OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(
    ISender sender,
    IServiceProvider services,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "apphub.refresh-instance-state";

    private readonly IntegrationEventConsumerGuard<OperationTaskFailedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            "ops.OperationTaskFailed",
            1));

    public async Task HandleAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var dbContext = services.GetService<ApplicationDbContext>();
        if (dbContext is not null &&
            !await AppHubProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(new RefreshInstanceStateAfterFailedOperationCommand(integrationEvent), cancellationToken);
    }
}

internal static class AppHubProcessedIntegrationEventInbox
{
    public static async Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var dedupeKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");

        if (dbContext.ProcessedIntegrationEvents.Local.Any(x => x.ConsumerName == consumerName && x.EventId == eventId))
        {
            return false;
        }

        if (await dbContext.ProcessedIntegrationEvents.AnyAsync(
            x => x.ConsumerName == consumerName && x.EventId == eventId,
            cancellationToken))
        {
            return false;
        }

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            consumerName,
            eventId,
            eventType,
            integrationEvent.EventVersion,
            sourceService,
            dedupeKey,
            DateTimeOffset.UtcNow));
        return true;
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }
}
