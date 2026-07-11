using DotNetCore.CAP;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
    ISender sender,
    IServiceProvider services,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState> logger)
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

    [CapSubscribe(nameof(OperationTaskCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var dbContext = services.GetService<ApplicationDbContext>();
        if (dbContext is null)
        {
            logger.LogWarning(
                "Skipping AppHub consumer inbox idempotency for {ConsumerName} because ApplicationDbContext is not registered.",
                ConsumerName);
        }
        else if (!await AppHubProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
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
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState> logger)
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

    [CapSubscribe(nameof(OperationTaskFailedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var dbContext = services.GetService<ApplicationDbContext>();
        if (dbContext is null)
        {
            logger.LogWarning(
                "Skipping AppHub consumer inbox idempotency for {ConsumerName} because ApplicationDbContext is not registered.",
                ConsumerName);
        }
        else if (!await AppHubProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(new RefreshInstanceStateAfterFailedOperationCommand(integrationEvent), cancellationToken);
    }
}

internal static class AppHubProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            consumerName,
            integrationEvent,
            record => new ProcessedIntegrationEvent(
                record.ConsumerName,
                record.EventId,
                record.EventType,
                record.EventVersion,
                record.SourceService,
                record.IdempotencyKey,
                record.ProcessedAtUtc),
            cancellationToken);
    }
}
