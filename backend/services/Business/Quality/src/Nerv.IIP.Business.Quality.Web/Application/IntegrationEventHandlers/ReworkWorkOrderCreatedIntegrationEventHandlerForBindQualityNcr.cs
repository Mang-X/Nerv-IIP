using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(nameof(ReworkWorkOrderCreatedIntegrationEvent), ConsumerName)]
public sealed class ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ReworkWorkOrderCreatedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-rework-work-order-created";

    private readonly IntegrationEventConsumerGuard<ReworkWorkOrderCreatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(ReworkWorkOrderCreatedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                integrationEvent.SourceService,
                QualityIntegrationEventSources.BusinessMes,
                StringComparison.Ordinal))
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.untrustedSource",
                $"Rework work order receipt source '{integrationEvent.SourceService}' is not MES.",
                cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (!Guid.TryParse(payload.SourceNcrId, out var ncrGuid))
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.invalidNcrId",
                $"Rework work order receipt NCR id '{payload.SourceNcrId}' is invalid.",
                cancellationToken);
            return;
        }

        var ncrId = new NonconformanceReportId(ncrGuid);
        var ncr = await dbContext.NonconformanceReports.SingleOrDefaultAsync(
            x => x.Id == ncrId
                && x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId,
            cancellationToken);
        if (ncr is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.ncrNotFoundInScope",
                $"NCR '{payload.SourceNcrId}' was not found in the receipt scope.",
                cancellationToken);
            return;
        }

        if (ncr.NcrCode != payload.SourceNcrCode
            || ncr.SkuCode != payload.SkuCode
            || ncr.DefectQuantity != payload.Quantity
            || ncr.BatchNo != payload.SourceLotNo
            || ncr.SerialNo != payload.SourceSerialNo)
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.payloadMismatch",
                $"Rework work order receipt does not match NCR '{payload.SourceNcrId}' source facts.",
                cancellationToken);
            return;
        }

        try
        {
            ncr.BindReworkWorkOrder(payload.ReworkWorkOrderId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.bindingConflict",
                $"NCR '{payload.SourceNcrId}' rejected MES rework work order '{payload.ReworkWorkOrderId}'.",
                cancellationToken);
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task DeadLetterAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken) =>
        deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
}
