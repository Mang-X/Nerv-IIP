using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

/// <summary>
/// Closes the 领料 loop: records the warehouse outbound document WMS prepared for a material issue
/// request so the operator sees an authoritative 出库单 instead of an empty cell.
/// </summary>
[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsMaterialIssueOutboundPreparedIntegrationEvent", ConsumerName)]
public sealed class WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WmsMaterialIssueOutboundPreparedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.wms-material-issue-outbound-prepared";

    private readonly IntegrationEventConsumerGuard<WmsMaterialIssueOutboundPreparedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            WmsIntegrationEventTypes.MaterialIssueOutboundPrepared,
            WmsIntegrationEventVersions.V1));

    public Task HandleAsync(WmsMaterialIssueOutboundPreparedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(WmsMaterialIssueOutboundPreparedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WmsMaterialIssueOutboundPreparedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        WmsMaterialIssueOutboundPreparedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.MaterialIssueRequestNo) || string.IsNullOrWhiteSpace(payload.OutboundOrderNo))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "missing-payload-field",
                    "WMS material issue acknowledgement must carry both the request number and the outbound order number."),
                cancellationToken);
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var requestNo = payload.MaterialIssueRequestNo.Trim();
        var request = await dbContext.MaterialIssueRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.RequestNo == requestNo,
            cancellationToken);
        // Gate-and-skip: an acknowledgement for a request this environment does not own is not an error
        // the consumer can retry away, so it is recorded and skipped rather than thrown.
        request?.LinkWarehouseOutbound(payload.OutboundOrderNo, payload.PickingTaskNo, payload.PreparedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
