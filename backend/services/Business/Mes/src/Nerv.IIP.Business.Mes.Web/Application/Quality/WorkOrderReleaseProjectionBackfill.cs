using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <param name="WorkOrdersScanned">按在制判据选中的工单数。</param>
/// <param name="WorkOrdersPublished">实际补投了发布事实的工单数（存在未完工工序的那些）。</param>
/// <param name="OperationsPublished">补投载荷里携带的工序总数。</param>
public sealed record WorkOrderReleaseProjectionBackfillReport(
    int WorkOrdersScanned,
    int WorkOrdersPublished,
    int OperationsPublished);

/// <summary>续扫一页取回的工单身份与补投载荷所需字段。</summary>
internal sealed record WorkOrderReleaseProjectionPageRow(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderIdValue,
    string SkuId,
    decimal Quantity);

/// <summary>
/// 存量在制工单的发布事实补投（#3000）。Quality 订阅 <c>mes.WorkOrderReleased</c> 之前发布的工单，
/// 在 Quality 的 <c>PeriodicInspectionOperations</c> 里没有行，首件确认读面恒回 <c>not-synchronized</c>，
/// #2780 的报工门禁会持续拒绝且不靠报工自愈——只能补上发布投影。
/// </summary>
internal sealed record BackfillWorkOrderReleaseProjectionCommand
    : ICommand<WorkOrderReleaseProjectionBackfillReport>;

internal sealed class BackfillWorkOrderReleaseProjectionCommandHandler(
    ApplicationDbContext dbContext,
    IMesIntegrationEventOutboxPublisher publisher,
    TimeProvider timeProvider)
    : ICommandHandler<BackfillWorkOrderReleaseProjectionCommand, WorkOrderReleaseProjectionBackfillReport>
{
    private const int WorkOrderPageSize = 200;

    /// <summary>
    /// 「哪些工单的工序还会再撞首件门禁」由**报工路径自己的准入条件**表达，不另起一套工单状态白名单：
    /// 报工命令的准入只检查工序 <c>InProgress</c>，门禁调用点就紧跟其后，工单状态在准入判断里一次都不出现；
    /// 真正筛掉工单的是 <see cref="WorkOrder.NonExecutableStatuses"/>（<c>RecordProductionProgress</c> 的拒绝集合，
    /// 与本查询同源）。因此受门禁影响的工单 = 该集合的补集，其中 <c>completed</c> **在内**——
    /// 超收容差（计划量 ×1.2 硬上限）显式为「累计量已达计划量后继续报工」留了空间，
    /// 工单翻 <c>completed</c> 时工序往往仍是 <c>InProgress</c>。
    ///
    /// 补集里唯一还要再排除的是 <c>created</c>，判据不是「在不在制」而是**发布事实是否已经发生**：
    /// <c>created</c> 工单的发布事件还没发出（<c>ThrowIfCannotRelease</c> 允许它后续被发布），
    /// 此时补投等于凭空造一份发布事实，随后真正的发布事件到达时会与它时刻不等、被判为冲突事实进死信。
    /// </summary>
    private static readonly Expression<Func<WorkOrder, bool>> CanStillHitTheFirstArticleGate =
        workOrder => workOrder.Status != WorkOrder.CreatedStatus
            && !WorkOrder.NonExecutableStatuses.Contains(workOrder.Status);

    /// <summary>
    /// 工序维度用的是**和工单维度同一把尺子**：判「该工单的发布事实是否已经发生」，不是判「此刻会不会撞门禁」。
    /// 工序生命周期会回流——报工冲销（<c>ReverseProductionReportCommand</c>）经
    /// <c>OperationActualTimeSettlementCoordinator.VoidAsync</c> → <c>OperationTask.ReopenAfterReportReversal</c>
    /// 把 <see cref="OperationTaskLifecycleStatus.Completed"/> 改回 <see cref="OperationTaskLifecycleStatus.InProgress"/>，
    /// 该路径上的守卫只拒 <c>closed</c> 工单，拦不住存量工单；而它的前置 <c>ActualTimeSettlementRevision &gt; 0</c>
    /// 恰好是「当初正常结算完工」的标志。按「此刻未完工」筛人，这批工序复活后就永远拿不到发布投影、被门禁永久拒。
    ///
    /// 因此只排除 <see cref="OperationTaskLifecycleStatus.Cancelled"/>——它是唯一真终态：
    /// <c>OperationTask</c> 全类 9 处 <c>Status =</c> 赋值中，1 处是私有构造函数的初始赋值（不是状态转移），
    /// 其余 8 处状态转移**没有任何一处以 <c>Cancelled</c> 为来源**（<c>Cancel</c> 与 <c>MarkScheduleInvalidated</c>
    /// 对 <c>Cancelled</c> 直接 return，<c>ApplyScheduleAssignment</c> 只把 <c>ScheduleInvalidated</c> 转回 <c>Queued</c>）。
    /// </summary>
    private static bool HasReleaseFacts(OperationTaskLifecycleStatus status) =>
        status != OperationTaskLifecycleStatus.Cancelled;

    /// <summary>
    /// 续扫一页。抽成方法是为了让「这条查询能被真实 provider 翻译、且翻出来的是 keyset seek 而不是 OFFSET」
    /// 可以被断言——EF Core InMemory 不做翻译，把不可翻译的谓词或退化成 OFFSET 的写法一律放行。
    /// </summary>
    internal static IQueryable<WorkOrderReleaseProjectionPageRow> BuildPageQuery(
        ApplicationDbContext dbContext,
        string? lastOrganizationId,
        string? lastEnvironmentId,
        string? lastWorkOrderId)
    {
        var query = dbContext.WorkOrders
            .AsNoTracking()
            .Where(CanStillHitTheFirstArticleGate);
        if (lastWorkOrderId is not null)
        {
            query = query.Where(x =>
                string.Compare(x.OrganizationId, lastOrganizationId) > 0
                || (x.OrganizationId == lastOrganizationId
                    && (string.Compare(x.EnvironmentId, lastEnvironmentId) > 0
                        || (x.EnvironmentId == lastEnvironmentId
                            && string.Compare(x.WorkOrderIdValue, lastWorkOrderId) > 0))));
        }

        return query
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.EnvironmentId)
            .ThenBy(x => x.WorkOrderIdValue)
            .Take(WorkOrderPageSize)
            .Select(x => new WorkOrderReleaseProjectionPageRow(
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkOrderIdValue,
                x.SkuId,
                x.Quantity));
    }

    public async Task<WorkOrderReleaseProjectionBackfillReport> Handle(
        BackfillWorkOrderReleaseProjectionCommand request,
        CancellationToken cancellationToken)
    {
        var scanned = 0;
        var published = 0;
        var operationsPublished = 0;
        var occurredAtUtc = timeProvider.GetUtcNow();

        // 翻页按**上一页最后一个工单身份**续扫，不按偏移量：筛选谓词是可变状态，
        // 而回填是活体系统上的一次请求——一次普通报工就能把工单从 released/started 翻成 completed
        // 或经 Close/Cancel/MarkSplit/MarkMerged 退出集合，偏移量翻页会让后面的工单整段左移、被跳过，
        // 而 WorkOrdersScanned 只累加实际取到的行数，看不出这个缺口。
        // (OrganizationId, EnvironmentId, WorkOrderIdValue) 是 ak_work_orders_scope_work_order 上的
        // 唯一候选键，即唯一全序，不需要再补 tiebreaker。
        string? lastOrganizationId = null;
        string? lastEnvironmentId = null;
        string? lastWorkOrderId = null;
        while (true)
        {
            var page = await BuildPageQuery(dbContext, lastOrganizationId, lastEnvironmentId, lastWorkOrderId)
                .ToArrayAsync(cancellationToken);
            if (page.Length == 0)
            {
                break;
            }

            var lastOnPage = page[^1];
            // 终止条件是「某一页取回 0 行」，而这只在游标每轮**严格前进**时才会到达：
            // seek 谓词写成 `>=` 而不是 `>`，末页就恒回 ≥1 行，循环既不前进也不退出，
            // 变成一个事务内的无界重扫重发。那种失败是挂死加写放大，不是可见的错值——
            // 由 job 超时兜底会连带带走 `if: always()` 的证据，且 xUnit 的 Timeout 对
            // EF InMemory 这种同步完成、从不让出的 await 链根本不生效（实测 >10 分钟不触发）。
            // 所以把「游标严格前进」这条循环不变量就地断言：一旦不成立立即失败，且说得出原因。
            if (lastWorkOrderId is not null
                && string.CompareOrdinal(lastOnPage.OrganizationId, lastOrganizationId) <= 0
                && string.CompareOrdinal(lastOnPage.EnvironmentId, lastEnvironmentId) <= 0
                && string.CompareOrdinal(lastOnPage.WorkOrderIdValue, lastWorkOrderId) <= 0)
            {
                throw new InvalidOperationException(
                    "回填分页游标没有前进："
                    + $"上一页末尾为 ({lastOrganizationId}, {lastEnvironmentId}, {lastWorkOrderId})，"
                    + $"本页末尾为 ({lastOnPage.OrganizationId}, {lastOnPage.EnvironmentId}, {lastOnPage.WorkOrderIdValue})。"
                    + "续扫谓词必须是严格大于，否则扫描不会终止。");
            }

            lastOrganizationId = lastOnPage.OrganizationId;
            lastEnvironmentId = lastOnPage.EnvironmentId;
            lastWorkOrderId = lastOnPage.WorkOrderIdValue;

            scanned += page.Length;
            var workOrderIds = page.Select(x => x.WorkOrderIdValue).Distinct(StringComparer.Ordinal).ToArray();
            var operationTasks = await dbContext.OperationTasks
                .AsNoTracking()
                .Where(x => workOrderIds.Contains(x.WorkOrderId))
                .Select(x => new
                {
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.WorkOrderId,
                    x.OperationTaskIdValue,
                    x.OperationSequence,
                    x.WorkCenterId,
                    x.Status,
                    x.CreatedAtUtc,
                })
                .ToArrayAsync(cancellationToken);
            var earliestReports = await dbContext.ProductionReports
                .AsNoTracking()
                .Where(x => workOrderIds.Contains(x.WorkOrderId))
                .GroupBy(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
                .Select(group => new
                {
                    group.Key,
                    EarliestReportedAtUtc = group.Min(x => x.ReportedAtUtc),
                })
                .ToArrayAsync(cancellationToken);
            var earliestReportByWorkOrder = earliestReports.ToDictionary(
                x => (x.Key.OrganizationId, x.Key.EnvironmentId, x.Key.WorkOrderId),
                x => x.EarliestReportedAtUtc);

            foreach (var workOrder in page)
            {
                var tasks = operationTasks
                    .Where(x => x.OrganizationId == workOrder.OrganizationId
                        && x.EnvironmentId == workOrder.EnvironmentId
                        && string.Equals(x.WorkOrderId, workOrder.WorkOrderIdValue, StringComparison.Ordinal))
                    .ToArray();
                var released = tasks
                    .Where(x => HasReleaseFacts(x.Status))
                    .OrderBy(x => x.OperationSequence)
                    .ThenBy(x => x.OperationTaskIdValue, StringComparer.Ordinal)
                    .ToArray();
                if (released.Length == 0)
                {
                    continue;
                }

                // 存量数据里没有留下「当初那一次发布」的时刻（工单聚合不存发布时间，发布事件的
                // ReleasedAtUtc 是发布那一刻的 UtcNow，早已丢失）。Quality 的聚合要求发布时刻不晚于
                // 它已掌握的任何一条报工——报工时刻由调用方填，可以早于工序建单时刻——所以这里取
                // 「该工单最早的工序建单时刻」与「该工单最早的报工时刻」中更早的那个作为发布时刻下界。
                // Quality 收到的报工是 MES 这批报工的子集，因此该下界对它一定成立。
                var earliestTaskCreatedAtUtc = tasks.Min(x => x.CreatedAtUtc);
                var releasedAtUtc = earliestReportByWorkOrder.TryGetValue(
                    (workOrder.OrganizationId, workOrder.EnvironmentId, workOrder.WorkOrderIdValue),
                    out var earliestReportedAtUtc)
                    && earliestReportedAtUtc < earliestTaskCreatedAtUtc
                    ? earliestReportedAtUtc
                    : earliestTaskCreatedAtUtc;

                var idempotencyKey = EventIds.Idempotency(
                    "work-order-release-projection-backfill",
                    workOrder.OrganizationId,
                    workOrder.EnvironmentId,
                    workOrder.WorkOrderIdValue);
                await publisher.PublishAsync(
                    nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent),
                    new WorkOrderReleaseProjectionBackfilledIntegrationEvent(
                    $"evt-{Guid.CreateVersion7():N}",
                    MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
                    MesIntegrationEventVersions.V1,
                    occurredAtUtc,
                    MesIntegrationEventSources.BusinessMes,
                    idempotencyKey,
                    workOrder.WorkOrderIdValue,
                    workOrder.OrganizationId,
                    workOrder.EnvironmentId,
                    "system:mes",
                    idempotencyKey,
                    new WorkOrderReleasedPayload(
                        workOrder.WorkOrderIdValue,
                        workOrder.SkuId,
                        workOrder.Quantity,
                        releasedAtUtc,
                        released
                            .Select(x => new ReleasedOperationPayload(
                                x.OperationTaskIdValue,
                                x.OperationSequence,
                                x.WorkCenterId))
                            .ToArray())));
                published++;
                operationsPublished += released.Length;
            }

        }

        return new WorkOrderReleaseProjectionBackfillReport(scanned, published, operationsPublished);
    }
}
