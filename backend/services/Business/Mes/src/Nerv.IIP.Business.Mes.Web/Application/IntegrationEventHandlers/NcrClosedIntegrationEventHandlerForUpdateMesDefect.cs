using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.NcrClosedIntegrationEvent", ConsumerName)]
public sealed class NcrClosedIntegrationEventHandlerForUpdateMesDefect(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<NcrClosedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.quality-ncr-closed";

    private readonly IntegrationEventConsumerGuard<NcrClosedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, QualityIntegrationEventTypes.NcrClosed, QualityIntegrationEventVersions.V1));

    public Task HandleAsync(NcrClosedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(NcrClosedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(NcrClosedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(NcrClosedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        if (payload.DispositionType != QualityNcrDispositionTypes.Scrap)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // 关闭回传没有 SourceDocumentId；沿首次处置已经建立的同范围 NCR 关联定位原不良。
        var defect = await dbContext.DefectRecords.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.NcrId == payload.NcrId,
            cancellationToken);
        if (defect is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var referenceId = string.IsNullOrWhiteSpace(payload.ScrapMovementId) ? null : payload.ScrapMovementId.Trim();
        if (referenceId is not null && MesDefectDispositionReferenceIdPolicy.ExceedsColumn(referenceId))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    MesDefectDispositionReferenceIdPolicy.OverlongFailureCode,
                    MesDefectDispositionReferenceIdPolicy.OverlongFailureMessage(referenceId)),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        defect.AcceptDisposition(payload.NcrId, payload.NcrCode, payload.DispositionType, referenceId, payload.ClosedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
