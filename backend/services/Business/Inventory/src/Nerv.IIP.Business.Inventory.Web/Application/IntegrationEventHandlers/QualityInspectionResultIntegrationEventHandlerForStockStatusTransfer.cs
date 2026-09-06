using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
    ISender sender,
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ILogger<QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer>? logger = null)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-inventory.quality-inspection-result";

    /// <summary>
    /// 不驱动库存状态转移的检验来源环节（#2976）。
    ///
    /// 只有 <c>first-article</c>：首件是工作中心上的**单件取样**，Inventory 里不存在与之对应的
    /// quality 状态台账。#2779 起首件把 <c>{workOrderId}:{operationId}</c> 编进来源单据身份后，
    /// 标准生成的编码就已经撑爆 <c>stock_movements.idempotency_key</c>，异常逃出 CAP 消费者变成
    /// poison message。
    ///
    /// **这是一条按取值逐个裁定的名单，不是按「在制品/成品」这类事实断言推出来的。** 其余五个取值
    /// 一律放行＝沿用今天的行为，本次不对它们做任何判断；<c>operation</c> 曾被考虑纳入本名单，实测
    /// 显示它今天存在**会成功过账**的形态（无库存维度且恰好命中一条 quality 台账时落 2 条流水），
    /// 挡掉它属于跨 Quality/Inventory 的产品语义决策，不在 #2976 范围内。
    ///
    /// 完备性由 <c>QualityInspectionSourceTypeGateContractTests</c> 机器化钉住：本名单与
    /// <see cref="StockBearingSourceTypes"/> 必须**恰好**划分 <c>QualityInspectionSourceTypes.All</c>，
    /// Quality 新增第七个取值时该契约变红，逼迫作者显式归类，而不是静默落进某一边。
    /// </summary>
    public static readonly IReadOnlySet<string> NonStockBearingSourceTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QualityInspectionSourceTypes.FirstArticle,
        };

    /// <summary>
    /// 放行的来源环节：本消费者对它们**沿用今天的行为**，不代表它们都一定有对应库存台账。
    /// 放行之后的三条出口里有两条是**静默成功落库**（<c>payload.StockRelease</c> 已给出库位、
    /// 或 payload 四件套齐全），只有「解析不出唯一 quality 台账」那条会抛 <c>KnownException</c>。
    /// 所以这里不能把「放行」当成「出错一定会响」——放行的失败方向既可能是响，也可能是把别人的
    /// quality 库存静默转成 unrestricted/restricted/blocked。
    /// </summary>
    public static readonly IReadOnlySet<string> StockBearingSourceTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QualityInspectionSourceTypes.Receiving,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceTypes.Final,
            QualityInspectionSourceTypes.Maintenance,
            QualityInspectionSourceTypes.CustomerReturn,
        };

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            [
                QualityIntegrationEventTypes.InspectionPassed,
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                QualityIntegrationEventTypes.InspectionRejected
            ],
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(InspectionResultIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payloadSourceType = integrationEvent.Payload.SourceType?.Trim() ?? string.Empty;
        if (NonStockBearingSourceTypes.Contains(payloadSourceType))
        {
            // gate-and-skip：跳过但**必须留痕**（#3180 的设计声明，#3186 复审实证后维持）。
            //
            // 本处曾被提议删掉留痕，理由是「该分支恒定触发、日志内容恒定」——**两条都不成立**：
            // ① 触发条件取自 payload（外部输入），六个来源环节里只有 first-article 进这个分支、
            //    其余五个不进，是**条件触发**；
            // ② 模板里的 {EventId} 逐条事件都不同，而**哪一条消息被丢了正是留痕的全部意义**。
            //
            // 更要命的是这条痕迹**没有任何替代**：本分支在 IsAlreadyProcessedAsync 之前、任何
            // dbContext 写入之前就 return；IntegrationEventConsumerGuard 只在**信封校验失败**时写
            // dead letter，而被挡的事件信封是合法的，走不到那里；CAP 的 FailedThresholdCallback
            // 只在 handler 抛异常时触发，本分支正常返回。删掉它，生产环境一条被丢弃的 first-article
            // 事件就是**无流水、无 ledger 变化、无 DLQ、无日志**的零记录静默丢弃。
            //
            // 可替代的只有**放行侧**的负向对照（「放行的取值不得触发本留痕」）：那一半已由
            // 「每个 stock-bearing 取值都产生 2 条 status-transfer 流水」承接，实测严格更强。
            logger?.LogInformation(
                "Consumer {Consumer} skipped quality inspection result {EventId} because inspection source type '{SourceType}' does not carry inventory stock.",
                ConsumerName,
                integrationEvent.EventId,
                payloadSourceType);
            return;
        }

        var targetStatus = integrationEvent.EventType switch
        {
            QualityIntegrationEventTypes.InspectionPassed => StockQualityStatus.Unrestricted,
            QualityIntegrationEventTypes.InspectionConditionalReleased => StockQualityStatus.Restricted,
            QualityIntegrationEventTypes.InspectionRejected => StockQualityStatus.Blocked,
            _ => throw new InvalidOperationException("Quality inspection event was not filtered by the consumer guard."),
        };

        if (await IsAlreadyProcessedAsync(integrationEvent, cancellationToken))
        {
            return;
        }

        const string sourceStatus = StockQualityStatus.Quality;
        var payload = integrationEvent.Payload;
        if (payload.StockRelease is not null)
        {
            // 非法取值表达成 KnownException：与紧接着的 `!= sourceStatus` 判的是同一个不变量
            // （来源状态必须是 quality），此前却抛 ArgumentOutOfRangeException——把调用方的输入
            // 错误伪装成本服务的编程缺陷（#3186）。
            //
            // **这不改变它在 CAP 上的投递结局。** IntegrationEventConsumerGuard.HandleAsync 末尾是
            // 裸的 await handler(...)（无 try/catch），backend/common/Messaging 下 KnownException
            // 零命中，生产代码零 ISubscribeFilter 注册——两种异常一样逃逸出消费者、一样按
            // FailedThresholdCallback 重试耗尽后进死信（死信只把异常当不透明字符串读）。
            // 这一处能被吸收，取决于 #877 的 gate-and-skip 落地；届时按本服务已有的写法
            // （InventoryMovementRequestedIntegrationEventHandlerForPostingMovement 的
            // `catch (KnownException)` → 发补偿事件）加一条按类型的 catch 即可，不必再动本行。
            if (!StockQualityStatus.TryNormalize(payload.StockRelease.SourceQualityStatus, out var releaseSourceStatus))
            {
                throw new KnownException(StockQualityStatus.UnsupportedMessage(payload.StockRelease.SourceQualityStatus));
            }

            var payloadTargetStatus = targetStatus;
            if (!string.IsNullOrWhiteSpace(payload.StockRelease.TargetQualityStatus)
                && !StockQualityStatus.TryNormalize(payload.StockRelease.TargetQualityStatus, out payloadTargetStatus))
            {
                throw new KnownException(StockQualityStatus.UnsupportedMessage(payload.StockRelease.TargetQualityStatus));
            }

            if (payloadTargetStatus != targetStatus)
            {
                throw new KnownException("Quality inspection stock release target status must match the inspection event type.");
            }

            if (releaseSourceStatus != sourceStatus)
            {
                throw new KnownException("Quality inspection stock release can only transfer stock from quality status.");
            }

            await SendStatusTransferAsync(
                integrationEvent,
                sourceStatus,
                targetStatus,
                StockLocator.FromStockRelease(payload.StockRelease),
                cancellationToken);
            return;
        }

        if (TryGetPayloadStockLocator(payload, out var payloadStockLocator))
        {
            await SendStatusTransferAsync(
                integrationEvent,
                sourceStatus,
                targetStatus,
                payloadStockLocator,
                cancellationToken);
            return;
        }

        var candidates = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SkuCode == payload.SkuCode
                && x.QualityStatus == InventoryQualityStatuses.Quality
                && x.OnHandQuantity >= payload.InspectedQuantity)
            .OrderBy(x => x.SiteCode)
            .ThenBy(x => x.LocationCode)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (candidates.Count != 1)
        {
            throw new KnownException("Quality inspection result cannot be applied automatically because Inventory could not resolve exactly one matching quality stock ledger.");
        }

        var ledger = candidates.Single();
        await SendStatusTransferAsync(
            integrationEvent,
            sourceStatus,
            targetStatus,
            StockLocator.FromLedger(ledger),
            cancellationToken);
    }

    private Task SendStatusTransferAsync(
        InspectionResultIntegrationEvent integrationEvent,
        string sourceStatus,
        string targetStatus,
        StockLocator stockLocator,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        return sender.Send(
            new PostStockStatusTransferCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                sourceStatus,
                targetStatus,
                InventoryMovementSourceServices.Quality,
                payload.SourceDocumentId,
                payload.InspectionRecordId,
                integrationEvent.IdempotencyKey,
                payload.SkuCode,
                stockLocator.UomCode,
                stockLocator.SiteCode,
                stockLocator.LocationCode,
                stockLocator.LotNo,
                stockLocator.SerialNo,
                stockLocator.OwnerType,
                stockLocator.OwnerId,
                payload.InspectedQuantity,
                stockLocator.ProductionDate,
                stockLocator.ExpiryDate),
            cancellationToken);
    }

    private static bool TryGetPayloadStockLocator(InspectionResultPayload payload, out StockLocator stockLocator)
    {
        if (string.IsNullOrWhiteSpace(payload.UomCode)
            || string.IsNullOrWhiteSpace(payload.SiteCode)
            || string.IsNullOrWhiteSpace(payload.LocationCode)
            || string.IsNullOrWhiteSpace(payload.OwnerType))
        {
            stockLocator = default;
            return false;
        }

        stockLocator = new StockLocator(
            payload.UomCode,
            payload.SiteCode,
            payload.LocationCode,
            NormalizeOptionalLocator(payload.LotNo),
            NormalizeOptionalLocator(payload.SerialNo),
            payload.OwnerType,
            NormalizeOptionalLocator(payload.OwnerId),
            null,
            null);
        return true;
    }

    private async Task<bool> IsAlreadyProcessedAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var outboundKey = $"{integrationEvent.IdempotencyKey}:out";
        var inboundKey = $"{integrationEvent.IdempotencyKey}:in";
        var outboundExists = await dbContext.StockMovements.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SourceService == InventoryMovementSourceServices.Quality
                && x.SourceDocumentId == payload.SourceDocumentId
                && x.IdempotencyKey == outboundKey,
            cancellationToken);
        if (!outboundExists)
        {
            return false;
        }

        return await dbContext.StockMovements.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.SourceService == InventoryMovementSourceServices.Quality
                && x.SourceDocumentId == payload.SourceDocumentId
                && x.IdempotencyKey == inboundKey,
            cancellationToken);
    }

    private static string? NormalizeOptionalLocator(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private readonly record struct StockLocator(
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string? SerialNo,
        string OwnerType,
        string? OwnerId,
        DateOnly? ProductionDate,
        DateOnly? ExpiryDate)
    {
        public static StockLocator FromStockRelease(StockReleaseDimensionPayload stockRelease)
        {
            return new StockLocator(
                stockRelease.UomCode,
                stockRelease.SiteCode,
                stockRelease.LocationCode,
                NormalizeOptionalLocator(stockRelease.LotNo),
                NormalizeOptionalLocator(stockRelease.SerialNo),
                stockRelease.OwnerType,
                NormalizeOptionalLocator(stockRelease.OwnerId),
                null,
                null);
        }

        public static StockLocator FromLedger(StockLedger ledger)
        {
            return new StockLocator(
                ledger.UomCode,
                ledger.SiteCode,
                ledger.LocationCode,
                ledger.LotNo,
                ledger.SerialNo,
                ledger.OwnerType,
                ledger.OwnerId,
                ledger.ProductionDate,
                ledger.ExpiryDate);
        }
    }
}
