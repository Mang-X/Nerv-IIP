using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

/// <summary>
/// Turns a MES material issue request into warehouse work (出库单 + 拣货任务). Gate-and-skip: anything the
/// warehouse cannot act on is dead-lettered with a reason instead of throwing, so the message never
/// becomes a poison message.
/// </summary>
[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesMaterialIssueRequestedIntegrationEvent", ConsumerName)]
public sealed class MesMaterialIssueRequestedIntegrationEventHandler(
    ApplicationDbContext dbContext,
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<MesMaterialIssueRequestedIntegrationEventHandler>? logger = null)
    : IIntegrationEventHandler<MesMaterialIssueRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.mes-material-issue-requested";

    /// <summary>Warehouse source bin used when MES does not name one (the raw-material store).</summary>
    public const string DefaultSourceLocationCode = "WH-WB-RM-01";

    /// <summary>Line-side destination used when MES does not name one.</summary>
    public const string DefaultLineSideLocationCode = "WH-WB-LINE-01";

    private readonly IntegrationEventConsumerGuard<MesMaterialIssueRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.MaterialIssueRequested,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesMaterialIssueRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(MesMaterialIssueRequestedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesMaterialIssueRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        MesMaterialIssueRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
        {
            // 记录后跳过：非 MES 来源不是本消费者的事实，但静默丢弃会让「事件发了却什么都没发生」无从排查。
            logger?.LogDebug(
                "Skipping material issue request {EventId} from unexpected source service {SourceService}.",
                integrationEvent.EventId,
                integrationEvent.SourceService);
            return;
        }

        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.RequestNo) ||
            string.IsNullOrWhiteSpace(payload.MaterialId) ||
            payload.RequestedQuantity <= 0m)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "missing-payload-field",
                    "MES material issue request must carry a request number, a material and a positive quantity."),
                cancellationToken);
            return;
        }

        var siteCode = await ResolveSiteCodeAsync(
            payload.SiteCode,
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            cancellationToken);
        if (siteCode is null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "unresolved-site",
                    "WMS could not resolve a single fulfillment site for this organization/environment."),
                cancellationToken);
            return;
        }

        await sender.Send(
            new PrepareMesMaterialIssueOutboundCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.RequestNo.Trim(),
                payload.WorkOrderId,
                payload.OperationTaskId,
                payload.MaterialId.Trim(),
                payload.UomCode,
                payload.RequestedQuantity,
                siteCode,
                string.IsNullOrWhiteSpace(payload.SourceLocationCode) ? DefaultSourceLocationCode : payload.SourceLocationCode.Trim(),
                string.IsNullOrWhiteSpace(payload.LineSideLocationCode) ? DefaultLineSideLocationCode : payload.LineSideLocationCode.Trim(),
                payload.RequestedAtUtc),
            cancellationToken);
    }

    private async Task<string?> ResolveSiteCodeAsync(
        string? payloadSiteCode,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payloadSiteCode))
        {
            return payloadSiteCode.Trim();
        }

        // MES does not model warehouse sites; when it stays silent, the warehouse answers with its own
        // single operating site. Ambiguity is a configuration fact WMS must not guess at.
        var siteCodes = await dbContext.WarehouseWorkPools
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.SiteCode)
            .Distinct()
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (siteCodes.Length == 0)
        {
            siteCodes = await dbContext.OutboundOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => x.SiteCode)
                .Distinct()
                .Take(2)
                .ToArrayAsync(cancellationToken);
        }

        return siteCodes.Length == 1 ? siteCodes[0] : null;
    }
}
