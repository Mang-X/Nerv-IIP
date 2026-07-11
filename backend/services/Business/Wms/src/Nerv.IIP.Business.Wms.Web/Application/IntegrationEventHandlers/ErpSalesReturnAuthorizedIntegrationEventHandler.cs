using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.SalesReturnAuthorizedIntegrationEvent", ConsumerName)]
public sealed class ErpSalesReturnAuthorizedIntegrationEventHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SalesReturnAuthorizedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.erp-sales-return-authorized";

    private readonly IntegrationEventConsumerGuard<SalesReturnAuthorizedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ErpIntegrationEventTypes.SalesReturnAuthorized,
            ErpIntegrationEventVersions.V1));

    public Task HandleAsync(SalesReturnAuthorizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(SalesReturnAuthorizedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(SalesReturnAuthorizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(SalesReturnAuthorizedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, ErpIntegrationEventSources.BusinessErp, StringComparison.OrdinalIgnoreCase))
        {
            await DeadLetterAsync(integrationEvent, "unexpected-source-service", "Only BusinessERP may authorize WMS customer-return inbound orders.", cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (payload.Lines.Count == 0)
        {
            await DeadLetterAsync(integrationEvent, "missing-payload-field", "ERP sales return authorization must contain at least one return line.", cancellationToken);
            return;
        }

        if (!await WmsProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var existing = await dbContext.InboundOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.SourceDocumentType == WmsSourceDocumentTypes.SalesReturnRma
            && x.SourceDocumentId == payload.RmaNo,
            cancellationToken);
        if (existing is null)
        {
            var inbound = InboundOrder.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.RmaNo,
                WmsSourceDocumentTypes.SalesReturnRma,
                payload.RmaNo,
                payload.SiteCode,
                payload.Lines.Select(line => new InboundOrderLineDraft(
                    line.SalesOrderLineNo,
                    line.SkuCode,
                    line.UomCode,
                    line.Quantity,
                    line.LocationCode,
                    line.LotNo,
                    null,
                    "quality",
                    "company",
                    null)));
            dbContext.InboundOrders.Add(inbound);
        }

    }

    private Task DeadLetterAsync(
        SalesReturnAuthorizedIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureMessage),
            cancellationToken);
    }
}
