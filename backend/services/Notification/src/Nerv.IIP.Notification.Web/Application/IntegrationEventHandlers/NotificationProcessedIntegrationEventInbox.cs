using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

internal static class NotificationProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        DateTimeOffset processedAtUtc,
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
                processedAtUtc),
            cancellationToken);
    }
}
