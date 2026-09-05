using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Remediation;

/// <param name="CreatedWorkOrdersScanned">扫到的 <c>created</c> 工单数（含未被选中的）。</param>
/// <param name="WorkOrdersReleased">实际补下达的工单数。</param>
/// <param name="OperationsReleased">补下达载荷里携带的工序总数。</param>
/// <param name="ExecutingOperationsRemediated">
/// 被补下达的工单里处于 <c>InProgress</c> / <c>Paused</c> 的工序数。
/// 它就是 #3119 判定 SQL 在本次补救前统计到的 <c>operations</c> 数，用来把「跑之前有多少行」
/// 与「跑之后 SQL 应当回 0 行」对上——报告本身即可复算判据，不必只依赖运维手抄读数。
/// </param>
public sealed record CreatedWorkOrderReleaseBackfillReport(
    int CreatedWorkOrdersScanned,
    int WorkOrdersReleased,
    int OperationsReleased,
    int ExecutingOperationsRemediated);

/// <summary>
/// <c>created</c> 存量工单的一次性补下达（#3119）。
///
/// <para><b>要救的是什么。</b>计划转工单在 <c>created</c> 状态就建好工序并抓齐套快照，随后工序可以开工、
/// 报工乃至完工，而工单仍停在 <c>created</c>（缺陷定性见 #3113）。本票同时给 MES 准入加了守卫
/// （开工与报工都拒 <c>created</c>），所以这批**已经在跑**的存量工单必须先拿到发布事实，否则守卫一上线
/// 它们既不能继续报工、也不在 #3000 的 Quality 投影回填选人范围内（那份回填显式排除 <c>created</c>）。
/// <b>因此部署顺序是硬约束：先跑本补救，再让守卫生效。</b></para>
///
/// <para><b>为什么不复用 <c>ReleaseWorkOrderCommand</c>。</b>那条路径要过设备、质量、齐套三道 readiness
/// （实测：对零缺口的探针工单调 <c>/release</c> 仍回 <c>equipment.downtime</c>）。
/// 而这些拒因恰恰就是这批工单当初没被下达的原因——用它补救等于**必然拒掉要救的人**。
/// 本命令走 <see cref="WorkOrder.MarkReleased(IReadOnlyCollection{OperationTask}, WorkOrderReleaseFactTime)"/>，
/// 与直投路径共用同一个聚合方法与同一份发布事实时刻口径，只是不过 readiness。</para>
///
/// <para><b>选谁：<c>created</c> ∧ 至少一道工序已经有执行事实。</b>
/// 票面原写「<c>created</c> × 有非 Cancelled 工序」，本实现**收窄**为「工序状态 ∈
/// {<c>InProgress</c>, <c>Paused</c>, <c>Completed</c>}」，理由是原口径会把**刚转单、工序全 <c>Queued</c>**
/// 的工单也一并强制发布——那是正常业务里等着人工下达的工单，强制发布等于绕过设备/质量/齐套三道 readiness
/// 替用户做了下达决定，且它们**不被本票的守卫困住**（守卫只拒开工与报工，正常下达按钮仍可用，#3118 已让它在界面可达）。
/// 收窄后仍满足票面验收：<c>created</c> × <c>InProgress</c>/<c>Paused</c> 的工序在补救后为 0。</para>
///
/// <para><b>这个选人口径在工序状态回流下是稳定的</b>（与 #3000 那份因回流而必须放宽的口径不同）：
/// 报工冲销会把 <c>Completed</c> 改回 <c>InProgress</c>——两者都已在集合内；
/// <c>Queued</c> / <c>ScheduleInvalidated</c> 想进入执行态只有 <c>OperationTask.Start</c> 一条路，
/// 而本票的准入守卫已经拒掉 <c>created</c> 工单的开工。所以补救跑完之后，
/// 「<c>created</c> 工单新长出执行态工序」这条路在系统层不再可达。</para>
///
/// <para><b>重跑安全。</b>补下达把工单翻成 <c>released</c>，第二次运行时它已不在 <c>created</c> 谓词里，
/// 因此 <see cref="CreatedWorkOrderReleaseBackfillReport.WorkOrdersReleased"/> 归零、不再发第二封发布事实。</para>
///
/// <para><b>量级边界，按实测写明。</b>本命令**必须 tracking**（要改工单状态），
/// 与 #3000 的 <c>AsNoTracking()</c> + 投影不同：**扫描面上每一张 <c>created</c> 工单都进变更跟踪器**，
/// 内存随**扫描量**而非选中量增长——十万张 <c>created</c> 工单时即使只选中 3 张，十万个实体全程驻留。
/// 整条命令跑在 <c>AddUnitOfWorkBehaviors()</c> 的**单个事务**里（好性质：中途失败不留半补救状态），
/// 但也因此**没有** <c>CommandTimeout</c>、没有分批、没有续跑参数。
/// 现网量级未知（无生产库通道）；若首次执行遇到超时，处置是先按 org/env 缩小范围而不是加重试。</para>
///
/// <para><b>载荷不过滤 <c>Cancelled</c> 工序</b>（#3000 的回填有该过滤，本命令没有）。
/// 当前不可达：<c>OperationTask.Cancel</c> 的生产调用点恰 1 处且整单取消，
/// 取消后的工单不满足 <c>created</c> 谓词。**这是调用点唯一性给的，不是结构性闭合**——
/// 将来若出现部分取消，混合工单会把 <c>Cancelled</c> 工序一并送进 Quality 的发布投影。</para>
/// </summary>
internal sealed record BackfillCreatedWorkOrderReleaseCommand
    : ICommand<CreatedWorkOrderReleaseBackfillReport>;

internal sealed class BackfillCreatedWorkOrderReleaseCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<BackfillCreatedWorkOrderReleaseCommand, CreatedWorkOrderReleaseBackfillReport>
{
    private const int WorkOrderPageSize = 200;

    /// <summary>
    /// 「已经有执行事实」的工序状态。<c>Cancelled</c> 不在内是因为它压根到不了这里：
    /// <c>OperationTask.Cancel</c> 的生产调用点恰 1 处（整单取消），它同时把工单翻成 <c>cancelled</c>，
    /// 那样的工单不满足本命令的 <c>created</c> 谓词。<c>Queued</c> / <c>ScheduleInvalidated</c>
    /// 是「还没开工」，不在内。
    /// </summary>
    internal static bool HasExecutionFacts(OperationTaskLifecycleStatus status) =>
        status is OperationTaskLifecycleStatus.InProgress
            or OperationTaskLifecycleStatus.Paused
            or OperationTaskLifecycleStatus.Completed;

    private static bool IsExecutingNow(OperationTaskLifecycleStatus status) =>
        status is OperationTaskLifecycleStatus.InProgress or OperationTaskLifecycleStatus.Paused;

    /// <summary>
    /// 续扫一页 <c>created</c> 工单。抽成方法与 #3000 同理：让「这条查询能被真实 provider 翻译、
    /// 且翻出来的是 keyset seek 而不是 OFFSET」可以被断言——EF Core InMemory 不翻译，一律放行。
    /// </summary>
    internal static IQueryable<WorkOrder> BuildPageQuery(
        ApplicationDbContext dbContext,
        string? lastOrganizationId,
        string? lastEnvironmentId,
        string? lastWorkOrderId)
    {
        var query = dbContext.WorkOrders.Where(x => x.Status == WorkOrder.CreatedStatus);
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
            .Take(WorkOrderPageSize);
    }

    public async Task<CreatedWorkOrderReleaseBackfillReport> Handle(
        BackfillCreatedWorkOrderReleaseCommand request,
        CancellationToken cancellationToken)
    {
        var scanned = 0;
        var released = 0;
        var operationsReleased = 0;
        var executingRemediated = 0;

        // 翻页按上一页最后一个工单身份续扫，不按偏移量。
        // **准确的理由**（上一版写的「谓词字段正是本命令自己在改的列、偏移量会让后面的工单左移」
        // 在本实现里其实没有发生）：整条命令跑在 `AddUnitOfWorkBehaviors()` 的单个事务里、
        // 末尾才 SaveChanges，因此翻页期间数据库里的 status 全程仍是 created，谓词集合不会收缩。
        // 保留 keyset 续扫的真实理由是它**不依赖**这条前提：一旦将来有人给本命令加中途提交、
        // 或让别的会话并发改 status，偏移量翻页会静默跳过整段而 CreatedWorkOrdersScanned 看不出来。
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
            // 终止条件是「某一页取回 0 行」，它要求游标每轮**严格前进**。seek 谓词一旦写成 `>=`，
            // 末页会恒回 ≥1 行、循环既不前进也不退出，变成一个事务内的无界重扫重发
            // （挂死加写放大，不是可见错值）。就地断言这条循环终止不变量。
            //
            // **捕获点在末页，不在中间页**（本轮实测，注释按实测写）：页大小 200 时，`>=` 在中间页
            // 只重复 1 行、仍前进 199 行，那里抓不到；但**末页**退化成只返回游标那一行，
            // 三分量逐一相等、三次 CompareOrdinal 全为 0，合取成立而抛出。
            // 实测把 seek 改成 `>= 0`：抛出「补下达分页游标没有前进：…(org-001, env-dev, WO-QUEUED-ONLY)…」，
            // 堆栈落在本断言。（该异常从共用夹具方法逃逸、波及全类，是**崩溃式变异**，
            // 红例数不等于防线宽度，独立鉴别力 1 份。）
            //
            // **它防不住什么，也写清**：把上面三行游标赋值删掉时 `lastWorkOrderId` 恒为 null、
            // 第一个合取项恒 false，本断言**一次都不会执行**——实测那份变异确实被杀，
            // 但杀它的是域守卫 `ThrowIfCannotRelease`（第二页重取同一批工单、EF identity map
            // 交回已改成 released 的跟踪实例），断言消息出现 0 次。别把它读成通用的「游标不前进」捕手。
            //
            // **强度如实写**：当前实现下 seek 是严格 `>`，本分支不可达，删掉整段全仓零红——
            // 它是**等价变异**、是给未来改动留的绊线，不为它造夹具
            // （造得出来的只能是把 seek 改坏的那份实现，不是可达输入）。
            // 另：失败条件是三分量同时 `<= 0` 的合取，**不是**字典序「未前进」的严格否定
            // （org 前进而 env/wo 回退时不触发）；对 `>=` 退化这一个目标够用，别当通用不变量读。
            // 等价性依赖上面 ORDER BY 三分量构成全序，与 #3000 同一条推理；排序键若变必须一并改写。
            if (lastWorkOrderId is not null
                && string.CompareOrdinal(lastOnPage.OrganizationId, lastOrganizationId) <= 0
                && string.CompareOrdinal(lastOnPage.EnvironmentId, lastEnvironmentId) <= 0
                && string.CompareOrdinal(lastOnPage.WorkOrderIdValue, lastWorkOrderId) <= 0)
            {
                throw new InvalidOperationException(
                    "补下达分页游标没有前进："
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
                    .OrderBy(x => x.OperationSequence)
                    .ThenBy(x => x.OperationTaskIdValue, StringComparer.Ordinal)
                    .ToArray();
                if (!tasks.Any(x => HasExecutionFacts(x.Status)))
                {
                    continue;
                }

                // 发布事实时刻与直投路径（#3117）、存量投影回填（#3000）共用
                // WorkOrderReleaseFactTime.NotLaterThan 这一处口径：
                // 候选取「该工单最早工序建单时刻」（存量数据里没有留下当初那一次下达的时刻），
                // 再按最早既有活动（最早报工 与 最早工序完工 中更早者）压到下界。
                // 少压任何一项，Quality 的 ApplyRelease 就会判「报工/完工早于发布」把整封发布事实打进死信。
                var earliestOperationEndUtc = tasks
                    .Where(x => x.ExistingEndUtc.HasValue)
                    .Select(x => (DateTimeOffset?)x.ExistingEndUtc!.Value)
                    .Min();
                var earliestReportedAtUtc = earliestReportByWorkOrder.TryGetValue(
                    (workOrder.OrganizationId, workOrder.EnvironmentId, workOrder.WorkOrderIdValue),
                    out var reportedAtUtc)
                    ? reportedAtUtc
                    : (DateTimeOffset?)null;
                var earliestExistingActivityAtUtc = earliestReportedAtUtc is { } report
                    ? (earliestOperationEndUtc is { } end && end < report ? end : report)
                    : earliestOperationEndUtc;

                workOrder.MarkReleased(
                    tasks,
                    WorkOrderReleaseFactTime.NotLaterThan(
                        tasks.Min(x => x.CreatedAtUtc),
                        earliestExistingActivityAtUtc));
                released++;
                operationsReleased += tasks.Length;
                executingRemediated += tasks.Count(x => IsExecutingNow(x.Status));
            }
        }

        return new CreatedWorkOrderReleaseBackfillReport(
            scanned,
            released,
            operationsReleased,
            executingRemediated);
    }
}
