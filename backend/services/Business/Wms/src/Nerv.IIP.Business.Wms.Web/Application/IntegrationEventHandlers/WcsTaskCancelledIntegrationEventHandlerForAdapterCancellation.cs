using DotNetCore.CAP;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
    ApplicationDbContext dbContext,
    IWcsCancellationAdapter adapter,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation> logger)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.wcs-task-cancelled-adapter";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        WmsIntegrationEventTypes.WcsTaskCancelled,
        WmsIntegrationEventVersions.V1)
    {
        IgnoreUnsupportedEventTypes = true
    };

    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, WmsIntegrationEventSources.BusinessWms, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WCS task cancellation payload must include external task id in PublicReference.",
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.AdapterType))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "WCS task cancellation payload must include AdapterType.",
                cancellationToken);
            return;
        }

        var reason = string.IsNullOrWhiteSpace(integrationEvent.Payload.DiagnosticMessage)
            ? "wms-task-cancelled"
            : integrationEvent.Payload.DiagnosticMessage.Trim();
        var request = new WcsCancellationRequest(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.AdapterType,
            integrationEvent.Payload.PublicReference,
            reason,
            integrationEvent.IdempotencyKey);

        if (!adapter.CanHandle(request, out var failureMessage))
        {
            await DeadLetterAsync(
                integrationEvent,
                "missing-wcs-adapter-endpoint",
                failureMessage ?? "WCS cancellation adapter is not configured for this event.",
                cancellationToken);
            return;
        }

        if (!await WmsProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        logger.LogInformation(
            "Sending WCS task cancellation to adapter {AdapterType} for external task {ExternalTaskId}.",
            integrationEvent.Payload.AdapterType,
            integrationEvent.Payload.PublicReference);
        await adapter.CancelAsync(request, cancellationToken);
    }

    private Task DeadLetterAsync(
        WmsIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
    }
}

internal static class WmsProcessedIntegrationEventInbox
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
