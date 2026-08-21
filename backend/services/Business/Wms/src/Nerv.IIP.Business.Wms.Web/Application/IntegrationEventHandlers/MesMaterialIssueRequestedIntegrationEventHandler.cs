using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    IOptions<WmsMaterialIssueLocationOptions>? locationOptions = null,
    ILogger<MesMaterialIssueRequestedIntegrationEventHandler>? logger = null)
    : IIntegrationEventHandler<MesMaterialIssueRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-wms.mes-material-issue-requested";

    private readonly WmsMaterialIssueLocationOptions locations =
        locationOptions?.Value ?? new WmsMaterialIssueLocationOptions();

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

        var sourceLocationCode = ResolveLocationCode(payload.SourceLocationCode, locations.SourceLocationCode);
        var lineSideLocationCode = ResolveLocationCode(payload.LineSideLocationCode, locations.LineSideLocationCode);
        if (sourceLocationCode is null || lineSideLocationCode is null)
        {
            // 这条死信的成因是部署配置缺失，不是数据问题：不打日志的话，运维只能翻死信表才知道
            // 「领料消息全部静默消失」。另外两条死信按事件内容判定，保持原状。
            logger?.LogWarning(
                "Material issue request {EventId} ({RequestNo}) has no usable warehouse location: " +
                "the event named source={PayloadSourceLocationCode} / line-side={PayloadLineSideLocationCode} and " +
                "MaterialIssue:SourceLocationCode / MaterialIssue:LineSideLocationCode are not configured.",
                integrationEvent.EventId,
                payload.RequestNo,
                payload.SourceLocationCode,
                payload.LineSideLocationCode);
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "unresolved-location",
                    "WMS has no source or line-side location for this material issue: MES did not name one and " +
                    "MaterialIssue:SourceLocationCode / MaterialIssue:LineSideLocationCode are not configured."),
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
                sourceLocationCode,
                lineSideLocationCode,
                payload.RequestedAtUtc),
            cancellationToken);
    }

    /// <summary>
    /// 库位只有两个来源：事件里 MES 明说的，或部署侧配置的默认库位。**没有领域内置默认值**——
    /// 过去这里兜底到世界观演示库位（`WH-WB-RM-01` / `WH-WB-LINE-01`），生产环境会把拣货工作
    /// 派到一个根本不存在的库位上（#1754）。两者都缺就进死信，让配置缺失显式暴露。
    /// </summary>
    private static string? ResolveLocationCode(string? payloadLocationCode, string configuredLocationCode)
    {
        if (!string.IsNullOrWhiteSpace(payloadLocationCode))
        {
            return payloadLocationCode.Trim();
        }

        return string.IsNullOrWhiteSpace(configuredLocationCode) ? null : configuredLocationCode.Trim();
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

/// <summary>
/// MES 领料在仓库侧落地的库位配置（配置节 <c>MaterialIssue</c>）。MES 不建模仓库库位，事件里
/// 常常不带库位；此时仓库用部署侧配置的默认库位，而不是猜一个演示库位（#1754）。
/// </summary>
public sealed class WmsMaterialIssueLocationOptions
{
    /// <summary>未指定来源库位时使用的默认发料库位；留空即视为未配置（消息进死信）。</summary>
    public string SourceLocationCode { get; init; } = string.Empty;

    /// <summary>未指定线边库位时使用的默认线边库位；留空即视为未配置（消息进死信）。</summary>
    public string LineSideLocationCode { get; init; } = string.Empty;
}
