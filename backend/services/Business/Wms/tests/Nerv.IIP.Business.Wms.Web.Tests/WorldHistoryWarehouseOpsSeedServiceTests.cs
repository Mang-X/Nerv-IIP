using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Application.Seed;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「仓储自动化 / 盘点执行 / 来料退货 / 现场当前队列」块的形状与幂等性证据。
///
/// 四张表此前恒为 0 行，业务前端「WCS 任务」「盘点执行」「入库 · 退货」三页全空。
/// 断言覆盖：条数区间、号段格式、状态分布、引用完整性（WCS 绑真实仓储作业任务、
/// 退货挂真实入库单）、五类现场队列、作业池资格、熔断链路收敛闭合、幂等，以及 **5 个 asOfDate 边界**
/// （上线日 / 上线日+1 / 年中 / 演示当天 / 未来日）——单日期测试假绿的教训见 #1151。
/// </summary>
public sealed class WorldHistoryWarehouseOpsSeedServiceTests
{
    /// <summary>库写入类用例的规模：足够跑出四档盘点结局与四档 WCS 结局，又不让 InMemory provider 变慢。</summary>
    private const double TestScale = 0.3d;

    /// <summary>低于该条数不做分布类断言（上线日附近的缩放样本只有个位数事实）。</summary>
    private const int MinimumDistributionSample = 12;

    /// <summary>5 个 asOfDate 边界：上线日、上线日+1、年中、演示当天、未来日。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new()
        {
            { 2026, 1, 5 },
            { 2026, 1, 6 },
            { 2026, 4, 15 },
            { 2026, 7, 27 },
            { 2026, 12, 31 },
        };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_fills_all_four_tables_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);

        var report = await new WorldHistoryWarehouseOpsSeedService(db)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        await AssertCurrentQueueShapeAsync(db, asOfDate);

        // 四张表都不再是 0 行——这正是三页空白的直接原因。
        // WCS 与退货是**派生**事实：上线日附近上游单据链本来就只有个位数，
        // 因此断言的是「与上游派生出的条数逐条相等」而不是「一定大于 0」。
        Assert.True(report.CountExecutionsWritten > 0);
        Assert.Equal(WorldHistoryWarehouseOpsSpec.Devices.Count, report.WcsDispatchCircuitsWritten);

        var expectedWcsTasks = (await db.WarehouseTasks.Select(x => x.TaskNo).ToArrayAsync())
            .Where(taskNo => !WorldHistoryWarehouseOpsSpec.IsCurrentQueueTask(taskNo))
            .Count(WorldHistoryWarehouseOpsSpec.IsDispatched);
        var realInboundOrders = (await db.InboundOrders.Select(x => x.InboundOrderNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var expectedReturns = WorldHistoryWarehouseOpsSeedService
            .BuildSupplierReturnDrafts(asOfDate, TestScale)
            .Count(draft => realInboundOrders.Contains(draft.InboundOrderNo));

        Assert.Equal(expectedWcsTasks, report.WcsTasksWritten);
        Assert.Equal(expectedReturns, report.SupplierReturnRequestsWritten);
        Assert.Equal(4, report.CurrentQueue.InboundOrdersWritten);
        Assert.Equal(2, report.CurrentQueue.PutawayTasksWritten);
        Assert.Equal(4, report.CurrentQueue.OutboundOrdersWritten);
        Assert.Equal(4, report.CurrentQueue.PickingTasksWritten);
        Assert.Equal(2, report.CurrentQueue.ReviewReadyOrdersWritten);
        Assert.Equal(
            WorldHistoryCountSpec.CountPlanCount(asOfDate, TestScale),
            await db.CountExecutions.CountAsync());
        Assert.Equal(report.WcsTasksWritten, await db.WcsTasks.CountAsync());
        Assert.Equal(report.SupplierReturnRequestsWritten, await db.SupplierReturnRequests.CountAsync());
        Assert.Equal(WorldHistoryWarehouseOpsSpec.WorkPools.Count, await db.WarehouseWorkPools.CountAsync());
        Assert.Equal(
            WorldHistoryWarehouseOpsSpec.WorkPools.Count,
            await db.WarehouseWorkPoolMemberships.CountAsync());

        // 校验器（fail-closed）跑过并如实回报条数。
        Assert.Equal(report.CountExecutionsWritten, report.Validation.CountExecutionsChecked);
        Assert.Equal(report.WcsTasksWritten, report.Validation.WcsTasksChecked);
        Assert.Equal(report.SupplierReturnRequestsWritten, report.Validation.SupplierReturnRequestsChecked);
        Assert.Equal(WorldHistoryWarehouseOpsSpec.WorkPools.Count, report.Validation.WorkPoolsChecked);
        Assert.Equal(
            WorldHistoryWarehouseOpsSpec.WorkPools.Count,
            report.Validation.WorkPoolMembershipsChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_keeps_number_segments_and_status_distribution(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        // 号段格式（设定集 §9 的仓储作业补充段），且不侵占 L2/规模块。
        var countNumbers = await db.CountExecutions.Select(x => x.CountNo).ToArrayAsync();
        Assert.All(countNumbers, no => Assert.Matches(@"^CNT-2026-\d{4}$", no));
        var externalTaskIds = await db.WcsTasks.Select(x => x.ExternalTaskId).ToArrayAsync();
        Assert.All(externalTaskIds, id => Assert.StartsWith("WCS-WT-", id, StringComparison.Ordinal));
        var returnNumbers = await db.SupplierReturnRequests.Select(x => x.SupplierReturnNo).ToArrayAsync();
        Assert.All(returnNumbers, no => Assert.StartsWith("RTS-IB-", no, StringComparison.Ordinal));
        Assert.All(
            countNumbers.Concat(externalTaskIds).Concat(returnNumbers),
            no =>
            {
                Assert.DoesNotContain("-DEMO-", no, StringComparison.Ordinal);
                Assert.DoesNotContain("-SCALE-", no, StringComparison.Ordinal);
            });

        // 盘点：绝大多数已回单，最近一批仍在盘 + 作废的从未实盘（页面的「进行中盘点」）。
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, TestScale);
        var executions = await db.CountExecutions.ToArrayAsync();
        var open = executions.Count(x => x.Status == CountExecutionStatus.Open);
        Assert.Equal(plans.Count(x => !x.IsCompleted), open);
        Assert.True(open > 0);
        Assert.True(open < executions.Length || executions.Length == 1);
        Assert.All(executions, execution =>
        {
            Assert.True(execution.ExpectedQuantity > 0m);
            Assert.Contains(execution.LocationCode, WorldHistoryCountSpec.Dimensions.Select(x => x.LocationCode));
            if (execution.Status == CountExecutionStatus.Completed)
            {
                Assert.NotNull(execution.CountedQuantity);
                Assert.Equal(execution.CountedQuantity!.Value - execution.ExpectedQuantity, execution.VarianceQuantity);
            }
        });

        // WCS：已完成为主，另有执行中与异常。
        var wcsTasks = await db.WcsTasks.ToArrayAsync();
        Assert.All(wcsTasks, task =>
        {
            Assert.Contains(task.DeviceId, WorldHistoryWarehouseOpsSpec.Devices.Select(x => x.DeviceId));
            Assert.Contains(task.AdapterType, WorldHistoryWarehouseOpsSpec.Devices.Select(x => x.AdapterType));
            Assert.True(task.CompletedAtUtc is null || task.CompletedAtUtc >= task.DispatchedAtUtc);
        });
        if (wcsTasks.Length >= MinimumDistributionSample)
        {
            var completed = wcsTasks.Count(x => x.Status == WcsTaskStatus.Completed);
            Assert.True(completed * 2 > wcsTasks.Length, "已完成的 WCS 任务应当占多数。");
            Assert.Contains(wcsTasks, x => x.Status != WcsTaskStatus.Completed);
            Assert.All(
                wcsTasks.Where(x => x.Status == WcsTaskStatus.Failed),
                x => Assert.Contains(x.FailureCode, WorldHistoryWarehouseOpsSpec.Failures.Select(f => f.Code)));
        }

        // 熔断链路必须收敛在闭合态，否则会挡住演示当场的真实下发。
        var circuits = await db.WcsDispatchCircuits.ToArrayAsync();
        Assert.Equal(WorldHistoryWarehouseOpsSpec.Devices.Count, circuits.Length);
        Assert.All(circuits, circuit => Assert.False(circuit.IsOpen));

        // 退货：数量为正、原因是中文文案。
        var returns = await db.SupplierReturnRequests.ToArrayAsync();
        Assert.All(returns, request =>
        {
            Assert.True(request.Quantity > 0m);
            Assert.Contains(request.DispositionReason!, WorldHistoryWarehouseOpsSpec.ReturnReasons);
            Assert.Equal(WorldHistoryPhase2Spec.QualityHoldLocationCode, request.LocationCode);
        });
    }

    /// <summary>WCS 任务必须绑**库里真实存在**的仓储作业任务——这是「WCS 任务」页可下钻的前提。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Wcs_tasks_anchor_on_real_warehouse_tasks_and_returns_on_real_inbound_orders(
        int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var warehouseTasks = await db.WarehouseTasks.ToDictionaryAsync(x => x.Id);
        var wcsTasks = await db.WcsTasks.ToArrayAsync();
        Assert.All(
            wcsTasks,
            task =>
            {
                Assert.True(warehouseTasks.TryGetValue(task.WarehouseTaskId, out var warehouseTask));
                Assert.NotNull(warehouseTask);
                Assert.False(string.IsNullOrWhiteSpace(warehouseTask.AssignedPoolCode));
                Assert.Equal(WarehouseTaskExecutionChannel.Wcs, warehouseTask.ExecutionChannel);
                Assert.Equal(task.Id.Id.ToString("D"), warehouseTask.ExecutionClaimedBy);
                Assert.NotNull(warehouseTask.ExecutionClaimedAtUtc);
                switch (task.Status)
                {
                    case WcsTaskStatus.Dispatched:
                    case WcsTaskStatus.Failed:
                        Assert.Equal(WarehouseTaskStatus.InProgress, warehouseTask.Status);
                        warehouseTask.ValidateWcsExecution(task.Id.Id.ToString("D"));
                        break;
                    case WcsTaskStatus.Completed:
                        Assert.Equal(WarehouseTaskStatus.Completed, warehouseTask.Status);
                        break;
                    case WcsTaskStatus.Cancelled:
                        Assert.Equal(WarehouseTaskStatus.Cancelled, warehouseTask.Status);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected WCS task status: {task.Status}");
                }
            });

        var inboundOrderNumbers = (await db.InboundOrders.Select(x => x.InboundOrderNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var returns = await db.SupplierReturnRequests.ToArrayAsync();
        Assert.All(returns, request => Assert.Contains(request.InboundOrderNo, inboundOrderNumbers));
    }

    /// <summary>历史铺开之后（非上线日边界），WCS 任务与退货申请必须实际存在，否则两页仍然是空的。</summary>
    [Fact]
    public async Task Wcs_tasks_and_supplier_returns_are_populated_once_history_has_unrolled()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);

        var report = await new WorldHistoryWarehouseOpsSeedService(db)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.True(report.WcsTasksWritten > 0, "WCS 任务页不能再是空的。");
        Assert.True(report.SupplierReturnRequestsWritten > 0, "入库 · 退货页不能再是空的。");
        Assert.True(report.Validation.FailedWcsTasksChecked > 0, "WCS 异常态必须在场，否则演示讲不出异常处理。");
        Assert.True(report.Validation.VarianceCountExecutionsChecked > 0, "盘点差异必须在场。");
    }

    [Fact]
    public async Task Warehouse_operator_can_distinguish_self_pool_and_site_queues_for_all_five_flows()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);

        var report = await new WorldHistoryWarehouseOpsSeedService(db)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var assignmentShape =
            $"inbound={report.Assignments.InboundOrdersAssigned}, " +
            $"putaway={report.Assignments.PutawayTasksAssigned}, " +
            $"picking={report.Assignments.PickingTasksAssigned}, " +
            $"outbound={report.Assignments.OutboundOrdersAssigned}, " +
            $"count={report.Assignments.CountExecutionsAssigned}, " +
            $"direct={report.Assignments.DirectAssignments}";
        Assert.True(report.Assignments.InboundOrdersAssigned > 0, assignmentShape);
        Assert.True(report.Assignments.PutawayTasksAssigned > 0);
        Assert.True(report.Assignments.PickingTasksAssigned > 0);
        Assert.True(report.Assignments.OutboundOrdersAssigned > 0);
        Assert.True(report.Assignments.CountExecutionsAssigned > 0);
        Assert.True(report.Assignments.DirectAssignments > 0);

        var pools = await db.WarehouseWorkPools.AsNoTracking().ToArrayAsync();
        Assert.Equal(WorldHistoryWarehouseOpsSpec.WorkPools.Count, pools.Length);
        Assert.All(pools, pool =>
        {
            Assert.Equal(WorldHistorySpec.SiteCode, pool.SiteCode);
            Assert.True(pool.Active);
        });
        var memberships = await db.WarehouseWorkPoolMemberships.AsNoTracking().ToArrayAsync();
        Assert.Equal(WorldHistoryWarehouseOpsSpec.WorkPools.Count, memberships.Length);
        Assert.All(memberships, membership =>
        {
            Assert.Equal(WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId, membership.PrincipalId);
            Assert.True(membership.Active);
        });

        var authorizer = new WarehouseWorkScopeAuthorizer(
            db,
            new StaticTimeProvider(new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc)));
        var catalog = await authorizer.GetCatalogAsync(
            "org-001",
            "env-dev",
            WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
            [WorldHistorySpec.SiteCode],
            CancellationToken.None);

        Assert.Contains(catalog.Items, item => item.ScopeKind == "self");
        Assert.Equal(
            WorldHistoryWarehouseOpsSpec.WorkPools.Select(spec => spec.PoolCode).Order(),
            catalog.Items
                .Where(item => item.ScopeKind == "work-pool")
                .Select(item => item.ScopeId)
                .Order());
        Assert.Contains(
            catalog.Items,
            item => item.ScopeKind == "site" && item.ScopeId == WorldHistorySpec.SiteCode);

        var self = await ResolveScopeAsync(
            authorizer,
            "self",
            WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId);
        var receiving = await ResolveScopeAsync(
            authorizer,
            "work-pool",
            WorldHistoryWarehouseOpsSpec.ReceivingPoolCode);
        var shipping = await ResolveScopeAsync(
            authorizer,
            "work-pool",
            WorldHistoryWarehouseOpsSpec.ShippingPoolCode);
        var count = await ResolveScopeAsync(
            authorizer,
            "work-pool",
            WorldHistoryWarehouseOpsSpec.CountPoolCode);
        var site = await ResolveScopeAsync(
            authorizer,
            "site",
            WorldHistorySpec.SiteCode);

        var inboundHandler = new ListInboundOrdersQueryHandler(db);
        var inboundSelf = await inboundHandler.Handle(
            InboundQuery(self, $"{WorldHistoryWarehouseOpsSpec.CurrentInboundOrderPrefix}A-RECEIPT"),
            CancellationToken.None);
        var inboundPool = await inboundHandler.Handle(
            InboundQuery(receiving, $"{WorldHistoryWarehouseOpsSpec.CurrentInboundOrderPrefix}A-RECEIPT"),
            CancellationToken.None);
        var inboundSite = await inboundHandler.Handle(
            InboundQuery(site, $"{WorldHistoryWarehouseOpsSpec.CurrentInboundOrderPrefix}A-RECEIPT"),
            CancellationToken.None);
        var inboundIds = (await db.InboundOrders.AsNoTracking()
                .Select(order => order.Id)
                .ToArrayAsync())
            .ToHashSet();
        AssertScopedQueues(
            inboundSelf.Items,
            inboundPool.Items,
            inboundSite.Items,
            item => item.InboundOrderId,
            item => item.AssignedOperatorUserId,
            item => item.AssignedPoolCode,
            inboundIds,
            WorldHistoryWarehouseOpsSpec.ReceivingPoolCode);

        var taskHandler = new ListWarehouseTasksQueryHandler(db);
        var putawaySelf = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Putaway, self),
            CancellationToken.None);
        var putawayPool = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Putaway, receiving),
            CancellationToken.None);
        var putawaySite = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Putaway, site),
            CancellationToken.None);
        var warehouseTaskIds = (await db.WarehouseTasks.AsNoTracking()
                .Select(task => task.Id)
                .ToArrayAsync())
            .ToHashSet();
        AssertScopedQueues(
            putawaySelf.Items,
            putawayPool.Items,
            putawaySite.Items,
            item => item.WarehouseTaskId,
            item => item.AssignedOperatorUserId,
            item => item.AssignedPoolCode,
            warehouseTaskIds,
            WorldHistoryWarehouseOpsSpec.ReceivingPoolCode);

        var pickingSelf = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Picking, self),
            CancellationToken.None);
        var pickingPool = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Picking, shipping),
            CancellationToken.None);
        var pickingSite = await taskHandler.Handle(
            WarehouseTaskQuery(WarehouseTaskType.Picking, site),
            CancellationToken.None);
        AssertScopedQueues(
            pickingSelf.Items,
            pickingPool.Items,
            pickingSite.Items,
            item => item.WarehouseTaskId,
            item => item.AssignedOperatorUserId,
            item => item.AssignedPoolCode,
            warehouseTaskIds,
            WorldHistoryWarehouseOpsSpec.ShippingPoolCode);

        var outboundHandler = new ListOutboundOrdersQueryHandler(db);
        var outboundSelf = await outboundHandler.Handle(
            OutboundQuery(self, $"{WorldHistoryWarehouseOpsSpec.CurrentOutboundOrderPrefix}A-REVIEW"),
            CancellationToken.None);
        var outboundPool = await outboundHandler.Handle(
            OutboundQuery(shipping, $"{WorldHistoryWarehouseOpsSpec.CurrentOutboundOrderPrefix}A-REVIEW"),
            CancellationToken.None);
        var outboundSite = await outboundHandler.Handle(
            OutboundQuery(site, $"{WorldHistoryWarehouseOpsSpec.CurrentOutboundOrderPrefix}A-REVIEW"),
            CancellationToken.None);
        var outboundIds = (await db.OutboundOrders.AsNoTracking()
                .Select(order => order.Id)
                .ToArrayAsync())
            .ToHashSet();
        AssertScopedQueues(
            outboundSelf.Items,
            outboundPool.Items,
            outboundSite.Items,
            item => item.OutboundOrderId,
            item => item.AssignedOperatorUserId,
            item => item.AssignedPoolCode,
            outboundIds,
            WorldHistoryWarehouseOpsSpec.ShippingPoolCode);

        var countHandler = new ListCountExecutionsQueryHandler(db);
        var countSelf = await countHandler.Handle(
            CountQuery(self),
            CancellationToken.None);
        var countPool = await countHandler.Handle(
            CountQuery(count),
            CancellationToken.None);
        var countSite = await countHandler.Handle(
            CountQuery(site),
            CancellationToken.None);
        var countIds = (await db.CountExecutions.AsNoTracking()
                .Select(execution => execution.Id)
                .ToArrayAsync())
            .ToHashSet();
        AssertScopedQueues(
            countSelf.Items,
            countPool.Items,
            countSite.Items,
            item => item.CountExecutionId,
            item => item.AssignedOperatorUserId,
            item => item.AssignedPoolCode,
            countIds,
            WorldHistoryWarehouseOpsSpec.CountPoolCode);

        Assert.DoesNotContain(
            await db.InboundOrders.AsNoTracking()
                .Where(order => order.Status != InboundOrderStatus.Open)
                .ToArrayAsync(),
            order => order.AssignedPoolCode is not null || order.AssignedOperatorUserId is not null);
        Assert.DoesNotContain(
            await db.OutboundOrders.AsNoTracking()
                .Where(order => order.Status != OutboundOrderStatus.Open)
                .ToArrayAsync(),
            order => order.AssignedPoolCode is not null || order.AssignedOperatorUserId is not null);
        Assert.DoesNotContain(
            await db.WarehouseTasks.AsNoTracking()
                .Where(task => task.Status != WarehouseTaskStatus.Open
                    && task.ExecutionChannel != WarehouseTaskExecutionChannel.Wcs)
                .ToArrayAsync(),
            task => task.AssignedPoolCode is not null || task.AssignedOperatorUserId is not null);
        Assert.DoesNotContain(
            await db.CountExecutions.AsNoTracking()
                .Where(execution => execution.Status != CountExecutionStatus.Open)
                .ToArrayAsync(),
            execution => execution.AssignedPoolCode is not null
                || execution.AssignedOperatorUserId is not null);
    }

    [Fact]
    public async Task Current_queue_is_idempotent_across_a_full_history_seed_restart()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        await using var db = CreateDbContext();
        var historySeed = new WorldHistorySeedService(db);
        var operationsSeed = new WorldHistoryWarehouseOpsSeedService(db);

        var firstHistory = await historySeed.SeedAsync(
            "org-001",
            "env-dev",
            asOfDate,
            TestScale);
        var firstOperations = await operationsSeed.SeedAsync(
            "org-001",
            "env-dev",
            asOfDate,
            TestScale);
        var inboundOrders = await db.InboundOrders.CountAsync();
        var outboundOrders = await db.OutboundOrders.CountAsync();
        var warehouseTasks = await db.WarehouseTasks.CountAsync();

        var secondHistory = await historySeed.SeedAsync(
            "org-001",
            "env-dev",
            asOfDate,
            TestScale);
        var secondOperations = await operationsSeed.SeedAsync(
            "org-001",
            "env-dev",
            asOfDate,
            TestScale);

        Assert.True(firstHistory.InboundOrdersWritten > 0);
        Assert.Equal(14, firstOperations.CurrentQueue.TotalWritten);
        Assert.Equal(0, secondHistory.InboundOrdersWritten);
        Assert.Equal(0, secondHistory.OutboundOrdersWritten);
        Assert.Equal(0, secondHistory.WarehouseTasksWritten);
        Assert.Equal(0, secondHistory.InventoryMovementRequestsWritten);
        Assert.Equal(
            firstHistory.Validation.InboundOrdersChecked,
            secondHistory.Validation.InboundOrdersChecked);
        Assert.Equal(
            firstHistory.Validation.OutboundOrdersChecked,
            secondHistory.Validation.OutboundOrdersChecked);
        Assert.Equal(0, secondOperations.CurrentQueue.TotalWritten);
        Assert.Equal(0, secondOperations.Assignments.TotalAssignments);
        Assert.Equal(inboundOrders, await db.InboundOrders.CountAsync());
        Assert.Equal(outboundOrders, await db.OutboundOrders.CountAsync());
        Assert.Equal(warehouseTasks, await db.WarehouseTasks.CountAsync());
        await AssertCurrentQueueShapeAsync(db, asOfDate);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Warehouse_ops_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        var seed = new WorldHistoryWarehouseOpsSeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        var countExecutions = await db.CountExecutions.CountAsync();
        var wcsTasks = await db.WcsTasks.CountAsync();
        var circuits = await db.WcsDispatchCircuits.CountAsync();
        var returns = await db.SupplierReturnRequests.CountAsync();
        var workPools = await db.WarehouseWorkPools.CountAsync();
        var memberships = await db.WarehouseWorkPoolMemberships.CountAsync();
        var inboundOrders = await db.InboundOrders.CountAsync();
        var outboundOrders = await db.OutboundOrders.CountAsync();
        var warehouseTasks = await db.WarehouseTasks.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.Equal(0, second.CountExecutionsWritten);
        Assert.Equal(0, second.WcsTasksWritten);
        Assert.Equal(0, second.WcsDispatchCircuitsWritten);
        Assert.Equal(0, second.SupplierReturnRequestsWritten);
        Assert.Equal(0, second.WorkPoolsWritten);
        Assert.Equal(0, second.WorkPoolMembershipsWritten);
        Assert.Equal(0, second.CurrentQueue.TotalWritten);
        Assert.Equal(0, second.CurrentQueue.ReviewReadyOrdersWritten);
        Assert.Equal(0, second.Assignments.TotalAssignments);
        Assert.Equal(countExecutions, await db.CountExecutions.CountAsync());
        Assert.Equal(wcsTasks, await db.WcsTasks.CountAsync());
        Assert.Equal(circuits, await db.WcsDispatchCircuits.CountAsync());
        Assert.Equal(returns, await db.SupplierReturnRequests.CountAsync());
        Assert.Equal(workPools, await db.WarehouseWorkPools.CountAsync());
        Assert.Equal(memberships, await db.WarehouseWorkPoolMemberships.CountAsync());
        Assert.Equal(inboundOrders, await db.InboundOrders.CountAsync());
        Assert.Equal(outboundOrders, await db.OutboundOrders.CountAsync());
        Assert.Equal(warehouseTasks, await db.WarehouseTasks.CountAsync());
        Assert.True(first.CountExecutionsWritten > 0);
    }

    /// <summary>设定集 §7 要求的全量规模：循环盘点每周一次 × 6 个组合，约 150–220 条。</summary>
    [Fact]
    public void Full_scale_volumes_match_the_world_bible_shape()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, 1.0d);

        Assert.InRange(plans.Count, 150, 220);
        Assert.Equal(
            WorldHistoryCountSpec.CountDays(asOfDate).Count * WorldHistoryCountSpec.CountsPerCountDay,
            plans.Count);
        Assert.DoesNotContain(WorldHistoryCountSpec.CountDays(asOfDate), date => date.DayOfWeek == DayOfWeek.Sunday);

        // 四档结局必然在场（配额分层，不是概率抽样）。
        foreach (var outcome in Enum.GetValues<WorldHistoryCountOutcome>())
        {
            Assert.Contains(plans, plan => plan.Outcome == outcome);
        }

        // 差异 ⇔ 走审批：账实相符 / 作废 / 在盘一律零差异，走审批的一律非零差异。
        Assert.All(plans, plan => Assert.Equal(plan.VarianceQuantity != 0m, plan.HasInventoryAdjustment));
        Assert.All(plans, plan => Assert.True(plan.CountedQuantity > 0m));
        // 作废与在盘的从未实盘，因此没有回单时刻。
        Assert.All(plans.Where(x => !x.IsCompleted), plan => Assert.Null(plan.CompletedAtUtc));
        Assert.All(plans.Where(x => x.IsCompleted), plan => Assert.NotNull(plan.CompletedAtUtc));
    }

    /// <summary>
    /// 跨服务黄金向量：<c>WorldHistoryCountSpec</c> 在仓储与库存两侧按同一字面量重复声明，
    /// 两侧各有一份**逐字相同**的本用例。任一侧改动而另一侧没跟上，两边的盘点单号 /
    /// 差异量就会漂移，跨域对账当场失效。
    /// </summary>
    [Fact]
    public void Count_plan_golden_vector_matches_the_mirrored_spec()
    {
        var plans = WorldHistoryCountSpec.BuildCountPlans(new DateOnly(2026, 7, 27), 1.0d);

        Assert.Equal(WorldHistoryCountGoldenVector.PlanCount, plans.Count);
        Assert.Equal(WorldHistoryCountGoldenVector.Digest, WorldHistoryCountGoldenVector.DigestOf(plans));
    }

    /// <summary>盘点维度必须全部落在有期初批的常驻库位上，否则库存侧找不到台账、整批盘点会被跳过。</summary>
    [Fact]
    public void Count_dimensions_resolve_to_opening_lots_on_storage_locations()
    {
        Assert.NotEmpty(WorldHistoryCountSpec.Dimensions);
        Assert.All(WorldHistoryCountSpec.Dimensions, dimension =>
        {
            Assert.Equal(WorldHistorySpec.SiteCode, dimension.SiteCode);
            Assert.Equal($"LOT-OPENING-{dimension.SkuCode}", dimension.LotNo);
            Assert.Equal(WorldHistoryPhase2Spec.StorageLocationFor(dimension.SkuCode), dimension.LocationCode);
            Assert.NotEqual(WorldHistoryPhase2Spec.FinishedGoodsLocationCode, dimension.LocationCode);
        });

        // 与库存域 WorldHistoryInventorySpec.OpeningLotNo 同字面量（跨域对账的字面量锚点）。
        Assert.Equal("LOT-OPENING-SF-ROD-01", WorldHistoryCountSpec.OpeningLotNo("SF-ROD-01"));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_count_execution_disappears()
    {
        var asOfDate = new DateOnly(2026, 7, 26);
        await using var db = CreateDbContext();
        await SeedDocumentChainAsync(db, asOfDate);
        await new WorldHistoryWarehouseOpsSeedService(db).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var execution = await db.CountExecutions.FirstAsync();
        db.CountExecutions.Remove(execution);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryWarehouseOpsConsistencyException>(() =>
            new WorldHistoryWarehouseOpsValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, TestScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    private static Task<WarehouseWorkScopeSelection> ResolveScopeAsync(
        WarehouseWorkScopeAuthorizer authorizer,
        string scopeKind,
        string scopeId) =>
        authorizer.ResolveAsync(
            new WarehouseWorkScopeRequest(
                "org-001",
                "env-dev",
                WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
                [WorldHistorySpec.SiteCode],
                scopeKind,
                scopeId,
                WorldHistorySpec.SiteCode),
            CancellationToken.None);

    private static ListInboundOrdersQuery InboundQuery(
        WarehouseWorkScopeSelection selection,
        string? keyword = null) =>
        new(
            "org-001",
            "env-dev",
            Take: 500,
            Status: InboundOrderStatus.Open.ToString(),
            Keyword: keyword,
            AssignedOperatorUserIds: Operators(selection),
            AssignedPoolCodes: Pools(selection),
            SiteCodes: selection.SiteCodes);

    private static ListOutboundOrdersQuery OutboundQuery(
        WarehouseWorkScopeSelection selection,
        string? keyword = null) =>
        new(
            "org-001",
            "env-dev",
            Take: 500,
            Status: OutboundOrderStatus.Open.ToString(),
            Keyword: keyword,
            AssignedOperatorUserIds: Operators(selection),
            AssignedPoolCodes: Pools(selection),
            SiteCodes: selection.SiteCodes);

    private static ListWarehouseTasksQuery WarehouseTaskQuery(
        WarehouseTaskType taskType,
        WarehouseWorkScopeSelection selection) =>
        new(
            "org-001",
            "env-dev",
            taskType,
            Take: 500,
            Status: WarehouseTaskStatus.Open.ToString(),
            AssignedOperatorUserIds: Operators(selection),
            AssignedPoolCodes: Pools(selection),
            SiteCodes: selection.SiteCodes);

    private static ListCountExecutionsQuery CountQuery(WarehouseWorkScopeSelection selection) =>
        new(
            "org-001",
            "env-dev",
            Take: 500,
            Status: CountExecutionStatus.Open.ToString(),
            AssignedOperatorUserIds: Operators(selection),
            AssignedPoolCodes: Pools(selection),
            SiteCodes: selection.SiteCodes);

    private static IReadOnlyCollection<string>? Operators(
        WarehouseWorkScopeSelection selection) =>
        selection.AssignedOperatorUserId is null
            ? null
            : [selection.AssignedOperatorUserId];

    private static IReadOnlyCollection<string>? Pools(
        WarehouseWorkScopeSelection selection) =>
        selection.AssignedOperatorUserId is null
            ? selection.PoolCodes
            : null;

    private static void AssertScopedQueues<TItem, TId>(
        IReadOnlyCollection<TItem> self,
        IReadOnlyCollection<TItem> pool,
        IReadOnlyCollection<TItem> site,
        Func<TItem, TId> id,
        Func<TItem, string?> assignedOperator,
        Func<TItem, string?> assignedPool,
        IReadOnlySet<TId> realIds,
        string expectedPool)
        where TId : notnull
    {
        Assert.NotEmpty(self);
        Assert.NotEmpty(pool);
        Assert.NotEmpty(site);
        Assert.Contains(
            pool,
            item => string.Equals(
                assignedOperator(item),
                WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
                StringComparison.Ordinal));
        Assert.Contains(pool, item => assignedOperator(item) is null);

        var poolIds = pool.Select(id).ToHashSet();
        var siteIds = site.Select(id).ToHashSet();
        Assert.All(self, item =>
        {
            Assert.Contains(id(item), realIds);
            Assert.Contains(id(item), poolIds);
            Assert.Contains(id(item), siteIds);
            Assert.Equal(
                WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
                assignedOperator(item));
            Assert.Equal(expectedPool, assignedPool(item));
        });
        Assert.All(pool, item =>
        {
            Assert.Contains(id(item), realIds);
            Assert.Equal(expectedPool, assignedPool(item));
        });
        Assert.All(site, item =>
        {
            Assert.Contains(id(item), realIds);
            Assert.Equal(expectedPool, assignedPool(item));
        });
    }

    private static async Task AssertCurrentQueueShapeAsync(
        ApplicationDbContext db,
        DateOnly asOfDate)
    {
        var spec = WorldHistoryWarehouseOpsSpec.BuildCurrentQueue(asOfDate);
        var inboundNumbers = spec.ReceiptOrders
            .Concat(spec.PutawayOrders)
            .Select(draft => draft.InboundOrderNo)
            .ToArray();
        var inbounds = (await db.InboundOrders.AsNoTracking()
                .Include(order => order.Lines)
                .Where(order => inboundNumbers.Contains(order.InboundOrderNo))
                .ToArrayAsync())
            .ToDictionary(order => order.InboundOrderNo, StringComparer.Ordinal);
        var outboundNumbers = spec.OutboundOrders
            .Select(draft => draft.OutboundOrderNo)
            .ToArray();
        var outbounds = (await db.OutboundOrders.AsNoTracking()
                .Include(order => order.Lines)
                .Where(order => outboundNumbers.Contains(order.OutboundOrderNo))
                .ToArrayAsync())
            .ToDictionary(order => order.OutboundOrderNo, StringComparer.Ordinal);
        var taskNumbers = spec.PutawayOrders
            .Select(draft => draft.WarehouseTaskNo!)
            .Concat(spec.OutboundOrders.Select(draft => draft.WarehouseTaskNo))
            .ToArray();
        var tasks = (await db.WarehouseTasks.AsNoTracking()
                .Where(task => taskNumbers.Contains(task.TaskNo))
                .ToArrayAsync())
            .ToDictionary(task => task.TaskNo, StringComparer.Ordinal);

        Assert.Equal(inboundNumbers.Length, inbounds.Count);
        Assert.Equal(outboundNumbers.Length, outbounds.Count);
        Assert.Equal(taskNumbers.Length, tasks.Count);

        foreach (var draft in spec.ReceiptOrders.Concat(spec.PutawayOrders))
        {
            var order = inbounds[draft.InboundOrderNo];
            var line = Assert.Single(order.Lines);
            Assert.NotEqual(default, order.Id);
            Assert.Equal(draft.SourceDocumentType, order.SourceDocumentType);
            Assert.Equal(draft.SourceDocumentId, order.SourceDocumentId);
            Assert.Equal(WorldHistorySpec.SiteCode, order.SiteCode);
            Assert.Equal(InboundOrderStatus.Open, order.Status);
            Assert.Equal(draft.SkuCode, line.SkuCode);
            Assert.Equal(draft.UomCode, line.UomCode);
            Assert.Equal(draft.Quantity, line.ReceivedQuantity);
            Assert.Equal(draft.StagingLocationCode, line.StagingLocationCode);
            Assert.Equal(draft.LotNo, line.LotNo);
            Assert.Equal(draft.CreatedAtUtc.UtcDateTime, order.CreatedAtUtc);

            if (draft.WarehouseTaskNo is null)
            {
                continue;
            }

            var task = tasks[draft.WarehouseTaskNo];
            Assert.NotEqual(default, task.Id);
            Assert.Equal(order.InboundOrderNo, task.SourceOrderNo);
            Assert.Equal(WorldHistoryWmsSpec.LineNo, task.SourceOrderLineNo);
            Assert.Equal(draft.SkuCode, task.SkuCode);
            Assert.Equal(draft.UomCode, task.UomCode);
            Assert.Equal(draft.Quantity, task.PlannedQuantity);
            Assert.Equal(draft.PutawayFromLocationCode, task.FromLocationCode);
            Assert.Equal(draft.PutawayToLocationCode, task.ToLocationCode);
            Assert.Equal(draft.LotNo, task.LotNo);
            Assert.Equal(WarehouseTaskStatus.Open, task.Status);
        }

        foreach (var draft in spec.OutboundOrders)
        {
            var order = outbounds[draft.OutboundOrderNo];
            var line = Assert.Single(order.Lines);
            var task = tasks[draft.WarehouseTaskNo];
            Assert.NotEqual(default, order.Id);
            Assert.NotEqual(default, task.Id);
            Assert.Equal(draft.SourceDocumentType, order.SourceDocumentType);
            Assert.Equal(draft.SourceDocumentId, order.SourceDocumentId);
            Assert.Equal(WorldHistorySpec.SiteCode, order.SiteCode);
            Assert.Equal(OutboundOrderStatus.Open, order.Status);
            Assert.Equal(draft.SkuCode, line.SkuCode);
            Assert.Equal(draft.UomCode, line.UomCode);
            Assert.Equal(draft.Quantity, line.RequestedQuantity);
            Assert.Equal(draft.PickFromLocationCode, line.PickLocationCode);
            Assert.Equal(draft.LotNo, line.LotNo);
            Assert.Equal(order.OutboundOrderNo, task.SourceOrderNo);
            Assert.Equal(WorldHistoryWmsSpec.LineNo, task.SourceOrderLineNo);
            Assert.Equal(draft.SkuCode, task.SkuCode);
            Assert.Equal(draft.UomCode, task.UomCode);
            Assert.Equal(draft.Quantity, task.PlannedQuantity);
            Assert.Equal(draft.PickFromLocationCode, task.FromLocationCode);
            Assert.Equal(draft.PickToLocationCode, task.ToLocationCode);
            Assert.Equal(draft.LotNo, task.LotNo);
            Assert.Equal(
                draft.ReviewReady ? WarehouseTaskStatus.Completed : WarehouseTaskStatus.Open,
                task.Status);
            Assert.Equal(
                draft.ReviewReady ? draft.Quantity : 0m,
                task.ExecutedQuantity);
        }
    }

    private static async Task SeedDocumentChainAsync(ApplicationDbContext dbContext, DateOnly asOfDate) =>
        await new WorldHistorySeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-world-history-ops-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow, TimeSpan.Zero);
    }
}
