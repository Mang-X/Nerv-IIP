using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **仓储域侧**（二期）。
///
/// 产出（设定集 §7）：与一期 ERP/MES 单据一一对应的收货入库单、完工入库单、发货出库单、领料出库单，
/// 每单一条作业行、一条上架 / 拣货任务，全部走到终态（入库 Completed、出库 Completed）。
///
/// 与其余域的一致性靠 <see cref="WorldHistoryWmsSpec.BuildDocuments"/> 一个确定性纯函数达成：
/// 源单据号与时刻全部由共享形状推出，两侧不通信、不跨库查询、不建跨 schema 外键。
///
/// <para>
/// 裁决点 · **<c>InventoryMovementRequest</c> 落库并直接标记已过账**。
/// 正常运行时这些请求行是驱动 Inventory 过账的出站记录，靠 CAP 投递闭环。
/// 历史生成时两个服务各自独立建账，不能也不该依赖消息投递，因此：
/// 请求行**照落**（它们是真实存在过的仓储事实，缺了就会让「入库单为什么改变了库存」无从追溯），
/// 但库存侧的流水由共享规格自己生成、不从这些请求推导；
/// 请求行的 <c>InventoryMovementId</c> 记的是两侧共用的确定性幂等键
/// （如 <c>PR-2026-0001:receipt-in</c>）而不是 Inventory 的 GUID 主键——跨库拿不到它，
/// 而幂等键恰好是两侧唯一索引里都存在的那一列，足以对账。
/// </para>
///
/// 领域事件说明：本仓栈里 <c>DbContext.SaveChangesAsync()</c> 不派发领域事件（派发只发生在
/// netcorepal 的 UnitOfWork/命令管线上），因此这里可以放心调用会 <c>AddDomainEvent</c> 的聚合方法。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批单据数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 200;

    private const string InspectionPassedEventType = "quality.InspectionPassed";

    public async Task<WorldHistoryWmsSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var documents = WorldHistoryWmsSpec.BuildDocuments(asOfDate, scale);
        var counters = new SeedCounters();

        await WriteInboundOrdersAsync(organizationId, environmentId, documents.InboundOrders, counters, cancellationToken);
        await WriteOutboundOrdersAsync(organizationId, environmentId, documents.OutboundOrders, counters, cancellationToken);

        // fail-closed：单据终态 / 任务数量 / 计划对账 / 时间戳边界对不上就让 seed 失败。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryWmsSeedReport(
            InboundOrdersWritten: counters.InboundOrders,
            OutboundOrdersWritten: counters.OutboundOrders,
            WarehouseTasksWritten: counters.WarehouseTasks,
            InventoryMovementRequestsWritten: counters.MovementRequests,
            Validation: validation);
    }

    #region 入库单

    private async Task WriteInboundOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryInboundDocument> documents,
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        for (var batchStart = 0; batchStart < documents.Count; batchStart += BatchSize)
        {
            var batch = documents.Skip(batchStart).Take(BatchSize).ToArray();
            var orderNumbers = batch.Select(x => x.InboundOrderNo).ToArray();
            var existing = (await dbContext.InboundOrders
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        orderNumbers.Contains(x.InboundOrderNo))
                    .Select(x => x.InboundOrderNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var document in batch.Where(x => !existing.Contains(x.InboundOrderNo)))
            {
                WriteInboundChain(organizationId, environmentId, document, counters);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private void WriteInboundChain(
        string organizationId,
        string environmentId,
        WorldHistoryInboundDocument document,
        SeedCounters counters)
    {
        var order = InboundOrder.Create(
            organizationId,
            environmentId,
            document.InboundOrderNo,
            document.SourceDocumentType,
            document.SourceDocumentId,
            WorldHistorySpec.SiteCode,
            [
                new InboundOrderLineDraft(
                    WorldHistoryWmsSpec.LineNo,
                    document.SkuCode,
                    document.UomCode,
                    document.Quantity,
                    document.StagingLocationCode,
                    document.LotNo,
                    SerialNo: null,
                    document.QualityStatus,
                    WorldHistoryWmsSpec.OwnerType,
                    OwnerId: null),
            ]);
        dbContext.InboundOrders.Add(order);
        counters.InboundOrders++;

        // 上架时机由领域层的门禁决定，两条路径不能互换：
        // - 免检行（完工入库）只能在 Open 状态下建上架任务，收单后单据即不可变；
        // - 待检行（采购收货）必须先收单进 PendingQualityCheck、拿到放行结论后才允许上架。
        WarehouseTask task;
        if (document.RequiresQualityInspection)
        {
            WriteMovementRequests(order.Complete(document.MovementIdempotencyKey), document.CompletedAtUtc, counters);
            order.ApplyInspectionResult(
                InspectionPassedEventType,
                document.InspectionRecordId!,
                document.SkuCode,
                document.LotNo,
                serialNo: null,
                document.Quantity,
                dispositionReason: null);
            task = CreatePutawayTask(order, document);
        }
        else
        {
            task = CreatePutawayTask(order, document);
            WriteMovementRequests(order.Complete(document.MovementIdempotencyKey), document.CompletedAtUtc, counters);
        }

        task.RecordProgress(document.Quantity);
        dbContext.WarehouseTasks.Add(task);
        counters.WarehouseTasks++;

        Backdate(order, x => x.CreatedAtUtc, document.CreatedAtUtc.UtcDateTime);
        Backdate(order, x => x.CompletedAtUtc, (DateTime?)document.CompletedAtUtc.UtcDateTime);
        Backdate(task, x => x.CreatedAtUtc, document.TaskCreatedAtUtc.UtcDateTime);
        Backdate(task, x => x.CompletedAtUtc, (DateTime?)document.TaskCompletedAtUtc.UtcDateTime);
    }

    private static WarehouseTask CreatePutawayTask(InboundOrder order, WorldHistoryInboundDocument document) =>
        order.CreatePutawayTask(
            document.WarehouseTaskNo,
            WorldHistoryWmsSpec.LineNo,
            document.PutawayFromLocationCode,
            document.PutawayToLocationCode,
            document.Quantity);

    #endregion

    #region 出库单

    private async Task WriteOutboundOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryOutboundDocument> documents,
        SeedCounters counters,
        CancellationToken cancellationToken)
    {
        for (var batchStart = 0; batchStart < documents.Count; batchStart += BatchSize)
        {
            var batch = documents.Skip(batchStart).Take(BatchSize).ToArray();
            var orderNumbers = batch.Select(x => x.OutboundOrderNo).ToArray();
            var existing = (await dbContext.OutboundOrders
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        orderNumbers.Contains(x.OutboundOrderNo))
                    .Select(x => x.OutboundOrderNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var document in batch.Where(x => !existing.Contains(x.OutboundOrderNo)))
            {
                WriteOutboundChain(organizationId, environmentId, document, counters);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }

    private void WriteOutboundChain(
        string organizationId,
        string environmentId,
        WorldHistoryOutboundDocument document,
        SeedCounters counters)
    {
        var order = OutboundOrder.Create(
            organizationId,
            environmentId,
            document.OutboundOrderNo,
            document.SourceDocumentType,
            document.SourceDocumentId,
            WorldHistorySpec.SiteCode,
            [
                new OutboundOrderLineDraft(
                    WorldHistoryWmsSpec.LineNo,
                    document.SkuCode,
                    document.UomCode,
                    document.Quantity,
                    document.PickFromLocationCode,
                    document.LotNo,
                    SerialNo: null,
                    WorldHistoryWmsSpec.Unrestricted,
                    WorldHistoryWmsSpec.OwnerType,
                    OwnerId: null),
            ]);
        dbContext.OutboundOrders.Add(order);
        counters.OutboundOrders++;

        var task = order.CreatePickingTask(
            document.WarehouseTaskNo,
            WorldHistoryWmsSpec.LineNo,
            document.PickFromLocationCode,
            document.PickToLocationCode,
            document.Quantity);
        task.RecordProgress(document.Quantity);
        dbContext.WarehouseTasks.Add(task);
        counters.WarehouseTasks++;

        var requests = order.CompletePackReview(document.PackReviewNo, passed: true, document.MovementIdempotencyKey);
        WriteMovementRequests(requests, document.CompletedAtUtc, counters);
        order.MarkInventoryPostingCompleted();

        Backdate(order, x => x.CreatedAtUtc, document.CreatedAtUtc.UtcDateTime);
        Backdate(order, x => x.CompletedAtUtc, (DateTime?)document.CompletedAtUtc.UtcDateTime);
        Backdate(task, x => x.CreatedAtUtc, document.CreatedAtUtc.UtcDateTime);
        Backdate(task, x => x.CompletedAtUtc, (DateTime?)document.TaskCompletedAtUtc.UtcDateTime);
    }

    #endregion

    private void WriteMovementRequests(
        IReadOnlyCollection<InventoryMovementRequest> requests,
        DateTimeOffset postedAtUtc,
        SeedCounters counters)
    {
        foreach (var request in requests)
        {
            // 历史里这些请求早已过账；用两侧共用的幂等键当过账凭据（跨库拿不到 Inventory 的 GUID）。
            request.MarkPosted(request.IdempotencyKey);
            dbContext.InventoryMovementRequests.Add(request);
            Backdate(request, x => x.CreatedAtUtc, postedAtUtc.UtcDateTime);
            Backdate(request, x => x.PostedAtUtc, (DateTime?)postedAtUtc.UtcDateTime);
            counters.MovementRequests++;
        }
    }

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    private sealed class SeedCounters
    {
        public int InboundOrders { get; set; }
        public int OutboundOrders { get; set; }
        public int WarehouseTasks { get; set; }
        public int MovementRequests { get; set; }
    }
}

/// <summary>一次 L1 仓储域历史生成的产出摘要。</summary>
public sealed record WorldHistoryWmsSeedReport(
    int InboundOrdersWritten,
    int OutboundOrdersWritten,
    int WarehouseTasksWritten,
    int InventoryMovementRequestsWritten,
    WorldHistoryWmsValidationReport Validation);
