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

    [CapSubscribe(nameof(NcrDispositionDecidedIntegrationEvent), Group = ConsumerName)]
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
            await dbContext.SaveChangesAsync(cancellationToken);
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
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.NcrId) ||
            string.IsNullOrWhiteSpace(payload.NcrCode) ||
            string.IsNullOrWhiteSpace(payload.DispositionType))
        {
            await AddBusinessDivergenceDeadLetterAsync(
                integrationEvent,
                "NCR disposition requires non-blank NCR id, NCR code, and disposition type.",
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var referenceId = payload.DispositionType.Trim().ToLowerInvariant() switch
        {
            QualityNcrDispositionTypes.Rework => payload.ReworkWorkOrderId,
            QualityNcrDispositionTypes.Scrap => payload.ScrapMovementId,
            QualityNcrDispositionTypes.ReturnToSupplier => payload.ReturnDocumentId,
            QualityNcrDispositionTypes.ConditionalRelease or QualityNcrDispositionTypes.SortAndScreen => null,
            _ => null,
        };
        try
        {
            defect.AcceptDisposition(
                payload.NcrId,
                payload.NcrCode,
                payload.DispositionType,
                referenceId,
                payload.ChangedAtUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await dbContext.Entry(defect).ReloadAsync(cancellationToken);
            await AddBusinessDivergenceDeadLetterAsync(
                integrationEvent,
                exception.Message,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task AddBusinessDivergenceDeadLetterAsync(
        NcrDispositionDecidedIntegrationEvent integrationEvent,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                "quality-ncr-disposition-divergence",
                failureMessage),
            cancellationToken);
    }
}
