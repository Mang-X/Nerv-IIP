using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.NcrDispositionDecidedIntegrationEvent", ConsumerName)]
public sealed class NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<NcrDispositionDecidedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.quality-ncr-disposition";

    private readonly IntegrationEventConsumerGuard<NcrDispositionDecidedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.NcrDispositionDecidedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var defectNo = integrationEvent.Payload.SourceDocumentId;
        if (string.IsNullOrWhiteSpace(defectNo))
        {
            return;
        }

        defectNo = defectNo.Trim();
        var defect = await dbContext.DefectRecords.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.DefectNo == defectNo,
            cancellationToken);
        if (defect is null)
        {
            return;
        }

        var referenceId = integrationEvent.Payload.DispositionType.Trim().ToLowerInvariant() switch
        {
            QualityNcrDispositionTypes.Rework => integrationEvent.Payload.ReworkWorkOrderId,
            QualityNcrDispositionTypes.Scrap => integrationEvent.Payload.ScrapMovementId,
            QualityNcrDispositionTypes.ReturnToSupplier => integrationEvent.Payload.ReturnDocumentId,
            _ => integrationEvent.Payload.ReworkWorkOrderId ??
                integrationEvent.Payload.ScrapMovementId ??
                integrationEvent.Payload.ReturnDocumentId,
        };
        defect.AcceptDisposition(
            integrationEvent.Payload.NcrId,
            integrationEvent.Payload.NcrCode,
            integrationEvent.Payload.DispositionType,
            referenceId,
            integrationEvent.Payload.ChangedAtUtc);
    }
}
