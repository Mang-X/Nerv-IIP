using DotNetCore.CAP;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent", ConsumerName)]
public sealed class MarkWorkOrderAlarmClearedHandler(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<AlarmClearedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-maintenance.alarm-cleared";

    private readonly IntegrationEventConsumerGuard<AlarmClearedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.AlarmCleared,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public async Task HandleAsync(AlarmClearedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(AlarmClearedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(AlarmClearedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MaintenanceProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await sender.Send(
            new MarkMaintenanceWorkOrderAlarmClearedCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.Payload.ExternalAlarmId,
                integrationEvent.Payload.ClearedAtUtc),
            cancellationToken);
    }
}
