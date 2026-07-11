using DotNetCore.CAP;
using MediatR;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.MeasuringDeviceCalibrationDueIntegrationEvent", ConsumerName)]
public sealed class MeasuringDeviceCalibrationNotificationConsumer(
    ISender sender, ApplicationDbContext dbContext, IIntegrationEventDeadLetterStore deadLetterStore,
    IConfiguration configuration, TimeProvider timeProvider)
    : IIntegrationEventHandler<MeasuringDeviceCalibrationDueIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.quality-measuring-device-calibration";
    private readonly IntegrationEventConsumerGuard<MeasuringDeviceCalibrationDueIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, QualityIntegrationEventTypes.MeasuringDeviceCalibrationDue, QualityIntegrationEventVersions.V1));

    public Task HandleAsync(MeasuringDeviceCalibrationDueIntegrationEvent integrationEvent, CancellationToken cancellationToken) => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);
    [CapSubscribe(nameof(MeasuringDeviceCalibrationDueIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MeasuringDeviceCalibrationDueIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidAsync(MeasuringDeviceCalibrationDueIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.MeasuringDeviceId) || string.IsNullOrWhiteSpace(payload.DeviceCode)) return;
        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, timeProvider.GetUtcNow(), cancellationToken)) return;
        var overdue = string.Equals(payload.CalibrationState, "overdue", StringComparison.OrdinalIgnoreCase);
        var recipients = configuration.GetSection("Quality:MeasuringDevice:RecipientRefs").Get<string[]>() ?? ["role:quality-inspector"];
        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId,
            new SubmitNotificationIntentRequest(integrationEvent.SourceService, integrationEvent.EventType, integrationEvent.EventId,
                overdue ? NotificationContractConstants.IntentTypeTask : NotificationContractConstants.IntentTypeMessage,
                overdue ? NotificationContractConstants.SeverityWarning : NotificationContractConstants.SeverityInfo,
                integrationEvent.IdempotencyKey, new NotificationResourceRef("measuring-device", payload.MeasuringDeviceId, null),
                $"Calibration {payload.CalibrationState}: {payload.DeviceCode}",
                $"Measuring device {payload.DeviceCode} is {payload.CalibrationState}; calibration due at {payload.CalibrationDueAtUtc:O}.", recipients), timeProvider.GetUtcNow()), cancellationToken);
    }
}
