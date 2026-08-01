using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎（MES 侧）的形状、跨服务一致性与幂等性证据。
/// 真实 PostgreSQL 的全量耗时实测在 <c>WorldHistorySeedPostgresTests</c>。
/// </summary>
public sealed class WorldHistorySeedServiceTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);
    private const double TestScale = 0.02d;

    /// <summary>
    /// 缺口分布用例的纵深：<c>0.02</c> 只排得出 2 张「已下达待开工」，凑不满一个配额块（10 张），
    /// 四档不可能齐全。<c>0.2</c> 有 20 张已下达（两个整块），四档必然在场。
    /// </summary>
    private const double ShortageScale = 0.2d;

    [Fact]
    public async Task History_seed_writes_the_full_work_order_chain_and_passes_its_own_validator()
    {
        await using var dbContext = CreateDbContext();

        var report = await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var plans = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale).Where(x => x.HasWorkOrder).ToArray();
        Assert.Equal(plans.Length, report.OrderWorkOrdersWritten);
        Assert.True(report.ReworkWorkOrdersWritten > 0);
        Assert.NotEmpty(report.Validation.Sample);

        var rework = await dbContext.WorkOrders
            .FirstAsync(workOrder => workOrder.WorkOrderIdValue.StartsWith("WO-2026-R", StringComparison.Ordinal));
        Assert.Equal("mes", rework.SourcePlanReference?.SourceSystem);
        Assert.Equal("rework-work-order", rework.SourcePlanReference?.SourceDocumentType);
        Assert.Null(rework.SourcePlanReference?.SourceDemandReference);

        // #1374 · 补产工单的**来源订单**必须与 Inventory/Wms/Quality/BarcodeLabel 四侧一致。
        // 只断言条数是抓不住的：候选池判据变了，条数恰恰不变，变的是每张单指向谁。
        var reworkPairs = await dbContext.WorkOrders
            .Where(workOrder => workOrder.WorkOrderIdValue.StartsWith("WO-2026-R", StringComparison.Ordinal))
            .OrderBy(workOrder => workOrder.WorkOrderIdValue)
            .Select(workOrder => new
            {
                ReworkWorkOrderNo = workOrder.WorkOrderIdValue,
                SourceWorkOrderNo = workOrder.SourcePlanReference!.SourceDocumentId,
            })
            .ToArrayAsync();
        Assert.Equal(WorldHistoryReworkSourceGoldenVector.ReworkCount, reworkPairs.Length);
        Assert.Equal(
            WorldHistoryReworkSourceGoldenVector.Digest,
            WorldHistoryReworkSourceGoldenVector.DigestOf(
                reworkPairs.Select(pair => (pair.ReworkWorkOrderNo, pair.SourceWorkOrderNo))));

        // 设定集 §7：约 3600 张工单 = 3200 订单工单 + 12.5% 内部补产。
        var expectedRework = (int)Math.Round(plans.Length * WorldHistoryMesSpec.ReworkWorkOrderRatio, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedRework, report.ReworkWorkOrdersWritten);
        Assert.Equal(plans.Length + expectedRework, await dbContext.WorkOrders.CountAsync());

        // 每单 6–8 道工序任务。
        var taskCounts = await dbContext.OperationTasks
            .GroupBy(x => x.WorkOrderId)
            .Select(group => group.Count())
            .ToArrayAsync();
        Assert.All(taskCounts, count => Assert.InRange(count, 6, 8));
        Assert.Contains(taskCounts, count => count == 6);
        Assert.Contains(taskCounts, count => count == 8);
    }

    /// <summary>
    /// 生成必须对任意 asOfDate 成立，不能只在固定测试日期上绿：2026-07-27（周日后首个工作日）
    /// 曾让尾部单的 OrderDate/ReleaseDate/ProductionStartDate 压到同一天，班次随机时刻倒挂出
    /// 「首次报工早于工单创建」，校验器 fail-closed 拦停 MES 启动（demo 栈三连败的根因）。
    /// 周日与春节内的 asOfDate 一并覆盖日历吸附边界。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task History_seed_passes_its_validator_for_any_as_of_date(int year, int month, int day)
    {
        await using var dbContext = CreateDbContext();

        var report = await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", new DateOnly(year, month, day), TestScale);

        Assert.NotEmpty(report.Validation.Sample);
    }

    [Fact]
    public async Task Closed_work_orders_balance_reports_receipts_and_the_sales_order_quantity()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var settled = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale)
            .First(plan => plan.Stage == WorldHistoryOrderStage.Settled);

        var workOrder = await dbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == settled.WorkOrderNo);
        Assert.Equal(WorkOrder.ClosedStatus, workOrder.Status);

        // 这是整条链的核心不变量：好品产出 == 销售订单数量，投料按报废量放大。
        Assert.Equal(settled.Quantity, workOrder.CompletedQuantity);
        Assert.Equal(workOrder.Quantity, workOrder.CompletedQuantity + workOrder.ScrapQuantity);

        var reports = await dbContext.ProductionReports.Where(x => x.WorkOrderId == settled.WorkOrderNo).ToArrayAsync();
        Assert.InRange(reports.Length, 2, 5);
        Assert.Equal(workOrder.CompletedQuantity, reports.Sum(x => x.GoodQuantity));
        Assert.Equal(workOrder.ScrapQuantity, reports.Sum(x => x.ScrapQuantity));

        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleAsync(x => x.WorkOrderId == settled.WorkOrderNo);
        Assert.Equal("Posted", receipt.Status);
        Assert.Equal(settled.Quantity, receipt.PostedQuantity);

        // 齐套快照与领料单齐全。
        Assert.Equal(4, await dbContext.MaterialRequirements.CountAsync(x => x.WorkOrderId == settled.WorkOrderNo));
        Assert.True(await dbContext.MaterialIssueRequests.CountAsync(x => x.WorkOrderId == settled.WorkOrderNo) >= 4);

        // 订单工单来源指回真实的 DemandPlanning 计划建议，跨服务只靠业务编码引用。
        Assert.NotNull(workOrder.SourcePlanReference);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference!.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference.SourceDocumentType);
        Assert.Equal(WorldHistorySpec.PlanningSuggestionIdForSalesOrder(settled.SalesOrderNo).ToString(), workOrder.SourcePlanReference.SourceDocumentId);
        Assert.Equal(settled.SalesOrderNo, workOrder.SourcePlanReference.SourceDemandReference);
    }

    /// <summary>
    /// #1373：种子绕过下达命令，于是齐套证明位从没盖过章，开工门禁对**全世界**工单
    /// 一律加 <c>MATERIAL_REQUIREMENT_SNAPSHOT_MISSING</c>——总览按纯数量口径显示「缺料 0」，
    /// 逐工序「开工」按钮却全被拦。齐套需求还只挂首序任务，OP-20 之后连需求行都取不到，
    /// 期望证明位被翻成 <c>no-requirements</c>，就算盖了章也对不上。
    /// 这条用例走真实的门禁评估器，钉住「首序真能开工 + 全工序都不因缺证明位被拦」。
    /// </summary>
    [Fact]
    public async Task History_seed_stamps_the_kitting_proof_so_no_operation_is_blocked_by_a_missing_snapshot()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        // #1408 起「已下达待开工」档里有约四成缺料单，缺料单的开工本来就该被拦。
        // 这条用例钉的是**齐套档能开工**，所以必须显式取一张齐套的单，
        // 而不是「按单号升序的第一张 released」——那个下标会随缺口分布漂到缺料单上。
        var workOrder = await FirstKittedReleasedWorkOrderAsync(dbContext);

        Assert.Equal(
            WorkOrder.MaterialRequirementSnapshotCapturedStatus,
            workOrder.MaterialRequirementSnapshotStatus);
        Assert.NotNull(workOrder.MaterialRequirementSnapshotEvaluatedAtUtc);
        // 门禁要求版本印章与工单生产版本逐字相等，否则等同于没有证明。
        Assert.Equal(workOrder.ProductionVersionId, workOrder.MaterialRequirementSnapshotProductionVersionId);

        // 齐套需求是工单级事实（与运行时快照提供方同形），任何一道工序都能取到。
        var requirements = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x => x.WorkOrderId == workOrder.WorkOrderIdValue)
            .ToArrayAsync();
        Assert.NotEmpty(requirements);
        Assert.All(requirements, requirement => Assert.Null(requirement.OperationTaskId));

        var tasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == workOrder.WorkOrderIdValue)
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync();
        var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext).EvaluateManyAsync(
            tasks,
            new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            CancellationToken.None);

        Assert.All(readiness.Values, entry => Assert.DoesNotContain(
            entry.BlockReasons,
            reason => reason.StartsWith("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", StringComparison.Ordinal)));
        // 首序工序无阻塞：这是走查里点不动的那颗按钮。
        Assert.Contains("start", readiness[tasks[0].OperationTaskIdValue].AllowedActions);
    }

    /// <summary>
    /// #1408：#1384 把可用/已备料一律写满需求量，全世界零缺口——「缺料 → MRP 采购建议 → 请购
    /// → 采购订单 → 收货 → 入库 → 齐套转绿 → 开工」这条链没有起点。
    ///
    /// <para>
    /// 本用例走**真实的齐套读面**（不是断言字段非空），钉死两条演示路径同时成立：
    /// 已下达档里既有能直接开工的齐套单，也有被物料门禁按 <c>MATERIAL_SHORTAGE</c> 拦住的缺料单；
    /// 而在制 / 已完工的工单一张都不许缺料，否则 #1384 解开的逐工序开工当场丢失。
    /// </para>
    /// </summary>
    [Fact]
    public async Task History_seed_leaves_material_shortages_only_on_released_work_orders()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, ShortageScale);

        var shortWorkOrders = await ShortWorkOrderIdsAsync(dbContext);
        Assert.NotEmpty(shortWorkOrders);

        var released = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.Status == WorkOrder.ReleasedStatus)
            .Select(x => x.WorkOrderIdValue)
            .ToArrayAsync();
        var releasedSet = released.ToHashSet(StringComparer.Ordinal);

        // ① 缺口只在已下达待开工档：在制/已完工的单缺料就会拦死逐工序开工（#1384）。
        Assert.All(shortWorkOrders, workOrderId => Assert.Contains(workOrderId, releasedSet));
        // ② 已下达档同时有齐套单，「齐套即可开工」这条路径才有对象。
        Assert.Contains(released, workOrderId => !shortWorkOrders.Contains(workOrderId));

        // ③ 缺口深度不是单值：轻度/部分/严重三档同时在场（缺口占需求的比例分布）。
        var shortageRatios = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x => x.RequiredQuantity > x.AvailableQuantity + x.StagedQuantity)
            .Select(x => (x.RequiredQuantity - x.AvailableQuantity - x.StagedQuantity) / x.RequiredQuantity)
            .ToArrayAsync();
        Assert.Contains(shortageRatios, ratio => ratio < 0.25m);
        Assert.Contains(shortageRatios, ratio => ratio is >= 0.25m and < 1m);
        Assert.Contains(shortageRatios, ratio => ratio == 1m);

        // ④ 严重档缺的是**采购件**：MRP 的 make/buy 分流据此判成 planned-purchase，
        //    缺料才走得到「MRP 建议采购」这一步。
        var wholeMaterialGaps = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x => x.AvailableQuantity + x.StagedQuantity == 0m)
            .Select(x => x.MaterialId)
            .Distinct()
            .ToArrayAsync();
        Assert.NotEmpty(wholeMaterialGaps);
        Assert.All(wholeMaterialGaps, materialId =>
            Assert.StartsWith("RM-SPR-", materialId, StringComparison.Ordinal));
    }

    /// <summary>缺料单的开工必须真的被物料门禁拦住，齐套单必须真的放行——两条都走真实评估器。</summary>
    [Fact]
    public async Task Shortage_work_orders_are_blocked_by_the_material_gate_while_kitted_ones_start()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, ShortageScale);

        var shortWorkOrders = await ShortWorkOrderIdsAsync(dbContext);
        var shortWorkOrderId = shortWorkOrders.Order(StringComparer.Ordinal).First();
        var kitted = await FirstKittedReleasedWorkOrderAsync(dbContext);
        var evaluatedAtUtc = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var evaluator = new MesOperationTaskActionReadinessEvaluator(dbContext);

        var shortTasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == shortWorkOrderId)
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync();
        var shortReadiness = await evaluator.EvaluateManyAsync(shortTasks, evaluatedAtUtc, CancellationToken.None);
        var firstShort = shortReadiness[shortTasks[0].OperationTaskIdValue];
        Assert.DoesNotContain("start", firstShort.AllowedActions);
        Assert.Contains(
            firstShort.BlockReasons,
            reason => reason.StartsWith("MATERIAL_SHORTAGE", StringComparison.Ordinal));
        // 证明位仍然盖着章：缺料是缺口问题，不是「没做齐套检查」（#1384 的证明位不受影响）。
        Assert.DoesNotContain(
            firstShort.BlockReasons,
            reason => reason.StartsWith("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", StringComparison.Ordinal));

        var kittedTasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == kitted.WorkOrderIdValue)
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync();
        var kittedReadiness = await evaluator.EvaluateManyAsync(kittedTasks, evaluatedAtUtc, CancellationToken.None);
        Assert.Contains("start", kittedReadiness[kittedTasks[0].OperationTaskIdValue].AllowedActions);
    }

    /// <summary>齐套读面必须把缺口讲清楚：状态 Blocked、行级 Shortage、并给出「卡在哪个环节」。</summary>
    [Fact]
    public async Task Material_readiness_read_face_reports_the_shortage_and_its_stage()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, ShortageScale);

        var shortWorkOrderId = (await ShortWorkOrderIdsAsync(dbContext)).Order(StringComparer.Ordinal).First();
        var readiness = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", shortWorkOrderId),
            CancellationToken.None);

        Assert.Equal("Blocked", readiness.ReadinessStatus);
        Assert.NotEmpty(readiness.BlockingReasons);

        var shortage = Assert.Single(readiness.Items.Where(x => x.ShortageQuantity > 0m).Take(1));
        Assert.Equal("Shortage", shortage.Status);
        // 已下达未开工的单一张领料都没发，缺口卡在「备料」环节——读面据此给出下一步动作。
        Assert.Equal(MesMaterialShortageStages.AwaitingPreparation, shortage.ShortageStage);
        // 齐套行不许再出现「可用 == 已备料 == 需求」这种把同一批料数两遍的写法。
        Assert.All(readiness.Items, item =>
            Assert.True(item.AvailableQuantity + item.StagedQuantity <= item.RequiredQuantity));
    }

    [Fact]
    public async Task MRP_backed_history_work_orders_trace_back_to_the_real_planning_suggestion()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        Assert.Equal(
            "d1e230af-fe8c-a75b-aea2-02ed501caab6",
            WorldHistorySpec.PlanningSuggestionIdForSalesOrder("SO-2026-00001").ToString());

        var plan = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale)
            .First(candidate =>
                candidate.HasWorkOrder &&
                candidate.Stage != WorldHistoryOrderStage.Cancelled &&
                candidate.OrderDate < new DateOnly(2026, 5, 25));
        var suggestionId = WorldHistorySpec.PlanningSuggestionIdForSalesOrder(plan.SalesOrderNo).ToString();
        var workOrder = await dbContext.WorkOrders
            .SingleAsync(candidate => candidate.WorkOrderIdValue == plan.WorkOrderNo);

        Assert.NotNull(workOrder.SourcePlanReference);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference!.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference.SourceDocumentType);
        Assert.Equal(suggestionId, workOrder.SourcePlanReference.SourceDocumentId);
        Assert.Equal(plan.SalesOrderNo, workOrder.SourcePlanReference.SourceDemandReference);

        var traceability = await new GetWorkOrderTraceabilityQueryHandler(dbContext).Handle(
            new GetWorkOrderTraceabilityQuery("org-001", "env-dev", plan.WorkOrderNo),
            CancellationToken.None);

        Assert.Contains(traceability.Nodes, node =>
            node.NodeId == suggestionId && node.NodeType == "PlanningSuggestion");
        Assert.Contains(traceability.Nodes, node =>
            node.NodeId == plan.SalesOrderNo && node.NodeType == "DemandSource");
        Assert.Contains(traceability.Edges, edge =>
            edge.FromNodeId == plan.SalesOrderNo &&
            edge.ToNodeId == suggestionId &&
            edge.RelationType == "pegged-to-plan");
        Assert.Contains(traceability.Edges, edge =>
            edge.FromNodeId == suggestionId &&
            edge.ToNodeId == plan.WorkOrderNo &&
            edge.RelationType == "converted-to-work-order");
    }

    [Fact]
    public async Task Work_order_execution_depth_follows_the_sales_order_stage()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var plans = WorldHistorySpec.BuildOrderPlans(AsOfDate, TestScale);

        // 已下达待开工：工序全部排队、无报工。
        var released = plans.First(plan => plan.Stage == WorldHistoryOrderStage.Released);
        var releasedWorkOrder = await dbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == released.WorkOrderNo);
        Assert.Equal(WorkOrder.ReleasedStatus, releasedWorkOrder.Status);
        Assert.Equal(0m, releasedWorkOrder.CompletedQuantity);
        Assert.Empty(await dbContext.ProductionReports.Where(x => x.WorkOrderId == released.WorkOrderNo).ToArrayAsync());
        Assert.All(
            await dbContext.OperationTasks.Where(x => x.WorkOrderId == released.WorkOrderNo).ToArrayAsync(),
            task => Assert.Equal(OperationTaskLifecycleStatus.Queued, task.Status));

        // 在制：部分工序完工、当前工序进行中、有部分报工但未完工入库。
        var inProgress = plans.First(plan => plan.Stage == WorldHistoryOrderStage.InProgress);
        var inProgressWorkOrder = await dbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == inProgress.WorkOrderNo);
        Assert.Equal(WorkOrder.StartedStatus, inProgressWorkOrder.Status);
        Assert.True(inProgressWorkOrder.CompletedQuantity > 0m);
        Assert.True(inProgressWorkOrder.CompletedQuantity < inProgressWorkOrder.Quantity);
        Assert.Empty(await dbContext.FinishedGoodsReceiptRequests.Where(x => x.WorkOrderId == inProgress.WorkOrderNo).ToArrayAsync());

        var inProgressTasks = await dbContext.OperationTasks.Where(x => x.WorkOrderId == inProgress.WorkOrderNo).ToArrayAsync();
        Assert.Contains(inProgressTasks, task => task.Status == OperationTaskLifecycleStatus.Completed);
        Assert.Contains(inProgressTasks, task => task.Status == OperationTaskLifecycleStatus.InProgress);
        Assert.Contains(inProgressTasks, task => task.Status == OperationTaskLifecycleStatus.Queued);

        // 废弃单不产生工单。
        var cancelled = plans.First(plan => plan.Stage == WorldHistoryOrderStage.Cancelled);
        Assert.Null(await dbContext.WorkOrders.SingleOrDefaultAsync(x => x.WorkOrderIdValue == cancelled.WorkOrderNo));
    }

    [Fact]
    public async Task Operation_tasks_fall_inside_shift_windows_and_carry_l0_facts()
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        var operatorIds = WorldHistoryMesSpec.Operators.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);
        var completed = await dbContext.OperationTasks
            .Where(x => x.Status == OperationTaskLifecycleStatus.Completed)
            .ToArrayAsync();
        Assert.NotEmpty(completed);

        Assert.All(completed, task =>
        {
            Assert.NotNull(task.ExistingStartUtc);
            Assert.NotNull(task.ExistingEndUtc);
            Assert.True(task.ExistingEndUtc >= task.ExistingStartUtc);

            // 报工人员必须是 L0 的 25 名班组成员之一，不得凭空捏造工号。
            Assert.NotNull(task.AssignedUserId);
            Assert.Contains(task.AssignedUserId!, operatorIds);

            // 工序时刻落在早班（本地 08:00–16:00）或中班（16:00–24:00）内，且不在周日。
            AssertInsideShiftWindow(task.ExistingStartUtc!.Value);
            AssertInsideShiftWindow(task.ExistingEndUtc!.Value);
        });

        // 性能终检工序带质检标志——这是二期质量域接管检验任务的预留引用点。
        var inspectionTasks = await dbContext.OperationTasks
            .Where(x => x.OperationSequence == WorldHistoryMesSpec.QualityInspectionSequence)
            .ToArrayAsync();
        Assert.NotEmpty(inspectionTasks);
        Assert.All(inspectionTasks, task => Assert.True(task.RequiresQualityInspection));
    }

    /// <summary>
    /// 派工看板缺口（演示走查）：历史上已开工/完工的工序不允许「已完成但未排程、无派工人」。
    /// 排程时间/周版方案号、派工人及姓名快照（与 L0 员工档案同一姓名公式）、设备绑定必须齐全，
    /// 且对任意 asOfDate 成立（含周日后首日与春节段，见 #1151 的单日期盲区教训）。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 8, 2)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 7, 31)]
    public async Task Started_or_completed_tasks_carry_schedule_and_dispatch_facts_for_any_as_of_date(
        int year, int month, int day)
    {
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync("org-001", "env-dev", new DateOnly(year, month, day), TestScale);

        var operatorsById = WorldHistoryMesSpec.Operators.ToDictionary(x => x.UserId, StringComparer.Ordinal);
        var touched = await dbContext.OperationTasks
            .Where(x => x.Status == OperationTaskLifecycleStatus.Completed ||
                x.Status == OperationTaskLifecycleStatus.InProgress)
            .ToArrayAsync();
        Assert.NotEmpty(touched);

        Assert.All(touched, task =>
        {
            // 派工人 + 姓名快照与 L0 员工档案逐字一致。
            Assert.NotNull(task.AssignedUserId);
            var expected = operatorsById[task.AssignedUserId!];
            Assert.Equal(expected.Name, task.AssignedUserName);

            // 排程事实：周版方案号段 SP-2026-W##，排程时间不晚于开工。
            Assert.NotNull(task.ScheduledAtUtc);
            Assert.NotNull(task.SchedulePlanId);
            Assert.Matches(@"^SP-\d{4}-W\d{2}$", task.SchedulePlanId!);
            Assert.True(task.ScheduledAtUtc <= task.ExistingStartUtc);

            // 设备绑定落在设定集 §3 对应工序的设备段内。
            Assert.NotNull(task.DeviceAssetId);
            Assert.Matches(@"^DEV-(CNC|GRD|ASM|CTG|TST|PKG)-\d{2}$", task.DeviceAssetId!);
        });

        // 未开工的排队工序保持未排程（真实的待排积压），不给全量伪造排程。
        var queued = await dbContext.OperationTasks
            .Where(x => x.Status == OperationTaskLifecycleStatus.Queued)
            .ToArrayAsync();
        Assert.All(queued, task => Assert.Null(task.ScheduledAtUtc));
    }

    [Fact]
    public async Task History_seed_is_idempotent_and_stays_out_of_the_reserved_number_segments()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, TestScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        Assert.True(first.OrderWorkOrdersWritten > 0);
        Assert.Equal(0, second.OrderWorkOrdersWritten);
        Assert.Equal(0, second.ReworkWorkOrdersWritten);

        var workOrderIds = await dbContext.WorkOrders.Select(x => x.WorkOrderIdValue).ToArrayAsync();
        Assert.All(workOrderIds, id =>
        {
            Assert.StartsWith("WO-2026-", id, StringComparison.Ordinal);
            Assert.DoesNotContain("-DEMO-", id, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", id, StringComparison.Ordinal);
        });
    }

    [Theory]
    // 黄金向量：与 ERP 侧 WorldHistorySeedServiceTests 逐字段相同的一份副本。
    // 任何一侧改动确定性派生，两侧测试都会同时变红——这是跨服务配对不漂移的锁。
    [InlineData(1, "FG-HJ-E1-R", "CUST-WB-001", 80, 300)]
    [InlineData(42, "FG-HJ-S1-L", "CUST-WB-003", 100, 264)]
    [InlineData(500, "FG-QJ-S1-R", "CUST-WB-002", 100, 350)]
    public void Order_plan_stays_on_the_shared_golden_vector(
        int index,
        string skuCode,
        string customerCode,
        int quantity,
        int unitPrice)
    {
        var plan = WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d).Single(x => x.Index == index);

        Assert.Equal(WorldHistorySpec.SalesOrderNo(index), plan.SalesOrderNo);
        Assert.Equal(WorldHistorySpec.WorkOrderNo(index), plan.WorkOrderNo);
        Assert.Equal(skuCode, plan.SkuCode);
        Assert.Equal(customerCode, plan.CustomerCode);
        Assert.Equal(quantity, plan.Quantity);
        Assert.Equal(unitPrice, plan.UnitPrice);
    }

    [Fact]
    public void Shared_engine_core_matches_the_erp_side_literals()
    {
        // 引擎核心（PRNG 根种子 / 上线日 / 节奏参数 / 班次）在两侧重复声明，这里锁住字面量。
        Assert.Equal(0xCFC630FB3054AF1EUL, WorldHistoryRandom.Fnv1a64("SO-2026-00001"));
        Assert.Equal(new DateOnly(2026, 1, 5), WorldHistoryCalendar.GoLiveDate);
        Assert.Equal(new DateOnly(2026, 2, 9), WorldHistoryCalendar.SpringFestivalStart);
        Assert.Equal(new DateOnly(2026, 2, 22), WorldHistoryCalendar.SpringFestivalEnd);
        Assert.Equal(105, WorldHistoryCalendar.BaseWeeklyOrders);
        Assert.Equal(TimeSpan.FromHours(8), WorldHistoryCalendar.SiteUtcOffset);
        Assert.Equal(3283, WorldHistorySpec.TotalOrders(AsOfDate, 1.0d));
    }

    [Fact]
    public void Work_center_mapping_matches_the_l0_routing_stages()
    {
        // L0 ProductEngineering 的 RoutingStages() 公式：本侧重算必须逐条一致。
        Assert.Equal("WC-TUB-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 10));
        Assert.Equal("WC-ROD-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 20));
        Assert.Equal("WC-GRD-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 30));
        Assert.Equal("WC-VA-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 40));
        Assert.Equal("WC-FA-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 50));
        Assert.Equal("WC-CT-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 60));
        Assert.Equal("WC-TS-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 70));
        Assert.Equal("WC-PK-01", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 80));

        // 后减振器总成走后减装配线；E1 平台（index 5）的下料/精车落在二号线。
        Assert.Equal("WC-RA-01", WorldHistoryMesSpec.WorkCenterCode("FG-HJ-P1-L", 50));
        Assert.Equal("WC-TUB-02", WorldHistoryMesSpec.WorkCenterCode("FG-QJ-E1-R", 10));

        // 主料与 L0 MBOM 前四行同码。
        Assert.Equal(
            ["SF-ROD-01", "SF-TUB-01", "SF-VLV-01", "RM-SPR-01"],
            WorldHistoryMesSpec.Components("FG-QJ-P1-L").Select(x => x.SkuCode));
    }

    [Fact]
    public void Operator_pool_matches_the_l0_twenty_five_team_members()
    {
        var operators = WorldHistoryMesSpec.Operators;

        // L0 §5：6 名班组长（EMP-004..009）+ 19 名操作工（EMP-010..028）。
        Assert.Equal(25, operators.Count);
        Assert.Equal("EMP-004", operators[0].EmployeeNo);
        Assert.Equal("user-emp-004", operators[0].UserId);
        Assert.Equal("EMP-028", operators[^1].EmployeeNo);
        Assert.All(operators, member => Assert.StartsWith("TEAM-WB-", member.TeamCode, StringComparison.Ordinal));

        // 三个车间都有人可派，否则某些工序会找不到报工人员。
        Assert.NotEmpty(WorldHistoryMesSpec.OperatorsIn(WorldHistoryWorkshop.Machining));
        Assert.NotEmpty(WorldHistoryMesSpec.OperatorsIn(WorldHistoryWorkshop.Assembly));
        Assert.NotEmpty(WorldHistoryMesSpec.OperatorsIn(WorldHistoryWorkshop.Surface));
    }

    private static void AssertInsideShiftWindow(DateTimeOffset moment)
    {
        var local = moment.ToOffset(WorldHistoryCalendar.SiteUtcOffset);
        Assert.NotEqual(DayOfWeek.Sunday, DateOnly.FromDateTime(local.Date).DayOfWeek);

        // 早班 08:00–16:00，中班 16:00–24:00：合起来就是本地 08:00 之后的任何时刻。
        Assert.True(
            local.Hour >= WorldHistoryCalendar.EarlyShiftStartLocalHour,
            $"Operation moment {local:O} falls outside the 08:00–24:00 two-shift window.");
    }

    /// <summary>
    /// #1408 · 缺口压在哪个采购件上，必须与 DemandPlanning 侧「MRP 建议采购哪个物料」逐字一致。
    /// 两侧只要差一个字，演示走到「建议采购 → 收货 → 齐套转绿」就会露出「建议采购 A、缺的是 B」。
    /// </summary>
    [Fact]
    public void Shortage_component_matches_the_cross_service_golden_vector()
    {
        var pairs = WorldHistoryShortageComponentGoldenVector.FinishedGoodSkus
            .Select(sku => (
                FinishedGoodSku: sku,
                ComponentSku: WorldHistoryMesSpec.Components(sku)[WorldHistoryMesSpec.PurchasedComponentIndex].SkuCode))
            .ToArray();

        Assert.All(pairs, pair => Assert.StartsWith("RM-SPR-", pair.ComponentSku, StringComparison.Ordinal));
        Assert.Equal(
            WorldHistoryShortageComponentGoldenVector.Digest,
            WorldHistoryShortageComponentGoldenVector.DigestOf(pairs));
    }

    /// <summary>取一张齐套（缺口为 0）的「已下达待开工」工单——齐套演示路径的对象。</summary>
    private static async Task<WorkOrder> FirstKittedReleasedWorkOrderAsync(ApplicationDbContext dbContext)
    {
        var shortWorkOrders = await ShortWorkOrderIdsAsync(dbContext);
        return await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.Status == WorkOrder.ReleasedStatus && !shortWorkOrders.Contains(x.WorkOrderIdValue))
            .OrderBy(x => x.WorkOrderIdValue)
            .FirstAsync();
    }

    /// <summary>缺口 &gt; 0 的工单号集合（缺口口径与开工门禁、齐套读面逐字一致）。</summary>
    private static async Task<HashSet<string>> ShortWorkOrderIdsAsync(ApplicationDbContext dbContext) =>
        (await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x => x.RequiredQuantity > x.AvailableQuantity + x.StagedQuantity)
            .Select(x => x.WorkOrderId)
            .Distinct()
            .ToArrayAsync())
        .ToHashSet(StringComparer.Ordinal);

    private static WorldHistorySeedService CreateSeed(ApplicationDbContext dbContext) =>
        new(dbContext, new StubProductionVersionResolver());

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-world-history-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// 生产版本解析走真实 HTTP（ProductEngineering 的 resolve 端点），单测里换成确定性桩：
    /// 本测试关心的是工单链的形状，解析路径本身由规模块既有测试覆盖。
    /// </summary>
    private sealed class StubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(sku => sku, sku => $"PV-{sku}", StringComparer.Ordinal));
    }
}
