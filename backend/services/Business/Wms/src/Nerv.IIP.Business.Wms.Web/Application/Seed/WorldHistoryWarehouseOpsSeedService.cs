using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using System.Text;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的
/// **仓储自动化 / 盘点执行 / 来料退货 / 现场当前队列**块。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行：WCS 任务要绑库里真实存在的
/// <c>warehouse_tasks</c> 行，退货要挂库里真实存在的收货入库单。
///
/// 领域事件说明：本块涉及的聚合工厂方法与状态迁移会 <c>AddDomainEvent</c>，
/// 且部分事件有跨服务消费者（WCS 适配器、库存盘点冻结、库存移动）。
/// 历史事实不得驱动下游，因此每个聚合写入前一律 <c>ClearDomainEvents()</c>。
/// </summary>
public sealed class WorldHistoryWarehouseOpsSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批处理的仓储作业任务数。</summary>
    public const int BatchSize = 500;

    private const string OwnerType = "company";

    public async Task<WorldHistoryWarehouseOpsSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var countExecutions = await SeedCountExecutionsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var supplierReturns = await SeedSupplierReturnsAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var currentQueue = await SeedCurrentWorkQueueAsync(
            organizationId,
            environmentId,
            asOfDate,
            scale,
            cancellationToken);
        var (workPools, memberships) = await SeedWorkPoolsAsync(
            organizationId,
            environmentId,
            cancellationToken);
        var assignments = await SeedWarehouseAssignmentsAsync(
            organizationId,
            environmentId,
            cancellationToken);
        var (wcsTasks, circuits) = await SeedWcsTasksAsync(organizationId, environmentId, cancellationToken);

        var validation = await new WorldHistoryWarehouseOpsValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryWarehouseOpsSeedReport(
            CountExecutionsWritten: countExecutions,
            SupplierReturnRequestsWritten: supplierReturns,
            CurrentQueue: currentQueue,
            WorkPoolsWritten: workPools,
            WorkPoolMembershipsWritten: memberships,
            Assignments: assignments,
            WcsTasksWritten: wcsTasks,
            WcsDispatchCircuitsWritten: circuits,
            Validation: validation);
    }

    #region 盘点执行

    private async Task<int> SeedCountExecutionsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, scale);
        var written = 0;
        for (var batchStart = 0; batchStart < plans.Count; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var countNumbers = batch.Select(x => x.CountNo).ToArray();
            var existing = (await dbContext.CountExecutions
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        countNumbers.Contains(x.CountNo))
                    .Select(x => x.CountNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var plan in batch.Where(x => !existing.Contains(x.CountNo)))
            {
                var execution = CountExecution.Create(
                    organizationId,
                    environmentId,
                    plan.CountNo,
                    plan.SkuCode,
                    plan.UomCode,
                    plan.SiteCode,
                    plan.LocationCode,
                    plan.ExpectedQuantity);

                if (plan.HasInventoryCountTask)
                {
                    // 跨库拿不到库存盘点任务的 GUID；两侧共用的盘点单号就是对账键。
                    execution.MarkInventoryCountTaskCreated(plan.CountNo);
                }

                if (plan.IsCompleted)
                {
                    execution.Complete(plan.CountedQuantity, execution.Version);
                }

                execution.ClearDomainEvents();
                dbContext.CountExecutions.Add(execution);
                Backdate(execution, x => x.CreatedAtUtc, plan.StartedAtUtc.UtcDateTime);
                Backdate(execution, x => x.CompletedAtUtc, (DateTime?)plan.CompletedAtUtc?.UtcDateTime);
                written++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    #endregion

    #region 来料退货

    private async Task<int> SeedSupplierReturnsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var drafts = BuildSupplierReturnDrafts(asOfDate, scale);
        var written = 0;
        for (var batchStart = 0; batchStart < drafts.Count; batchStart += BatchSize)
        {
            var batch = drafts.Skip(batchStart).Take(BatchSize).ToArray();
            var returnNumbers = batch.Select(x => x.SupplierReturnNo).ToArray();
            var inboundOrderNumbers = batch.Select(x => x.InboundOrderNo).Distinct(StringComparer.Ordinal).ToArray();

            var existing = (await dbContext.SupplierReturnRequests
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        returnNumbers.Contains(x.SupplierReturnNo))
                    .Select(x => x.SupplierReturnNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            // 退货必须挂在真实落库的收货入库单上；一期收货没写进来的（缩放边界）宁可不写。
            var realInboundOrders = (await dbContext.InboundOrders
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        inboundOrderNumbers.Contains(x.InboundOrderNo))
                    .Select(x => x.InboundOrderNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var draft in batch.Where(x =>
                !existing.Contains(x.SupplierReturnNo) && realInboundOrders.Contains(x.InboundOrderNo)))
            {
                var request = SupplierReturnRequest.Create(
                    organizationId,
                    environmentId,
                    draft.InboundOrderNo,
                    WorldHistoryWmsSpec.LineNo,
                    draft.InspectionRecordId,
                    draft.SkuCode,
                    draft.UomCode,
                    WorldHistorySpec.SiteCode,
                    WorldHistoryPhase2Spec.QualityHoldLocationCode,
                    draft.LotNo,
                    serialNo: null,
                    OwnerType,
                    ownerId: null,
                    draft.Quantity,
                    draft.Reason);
                request.ClearDomainEvents();
                dbContext.SupplierReturnRequests.Add(request);
                Backdate(request, x => x.CreatedAtUtc, draft.CreatedAtUtc.UtcDateTime);
                written++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    /// <summary>退货草稿（纯函数，校验器复用同一列表）。</summary>
    public static IReadOnlyList<WorldHistorySupplierReturnDraft> BuildSupplierReturnDrafts(DateOnly asOfDate, double scale)
    {
        var drafts = new List<WorldHistorySupplierReturnDraft>(64);
        foreach (var purchase in WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale)
                     .Where(plan => plan.IsReceived && WorldHistoryWarehouseOpsSpec.HasSupplierReturn(plan.PurchaseReceiptNo)))
        {
            var receiptNo = purchase.PurchaseReceiptNo;
            var inboundOrderNo = WorldHistoryPhase2Spec.InboundOrderNo(receiptNo);
            var inspectionRecordId = WorldHistoryWarehouseOpsSpec.ReturnInspectionRecordReference(receiptNo);
            var receiptDay = WorldHistoryWmsSpec.ClampToHistory(purchase.ReceiptDate, asOfDate);
            // 上架后复检：放在收货后第 3 个工作日，仍夹在历史区间内。
            var reviewDay = WorldHistoryWmsSpec.ClampToHistory(
                WorldHistoryCalendar.AddWorkingDays(receiptDay, 3), asOfDate);

            drafts.Add(new WorldHistorySupplierReturnDraft(
                SupplierReturnNo: $"RTS-{inboundOrderNo}-{WorldHistoryWmsSpec.LineNo}-{inspectionRecordId}",
                InboundOrderNo: inboundOrderNo,
                InspectionRecordId: inspectionRecordId,
                SkuCode: purchase.SkuCode,
                UomCode: purchase.UomCode,
                LotNo: WorldHistoryProcurementSpec.PurchasedLotNo(purchase.PurchaseOrderNo),
                Quantity: WorldHistoryWarehouseOpsSpec.ReturnQuantity(receiptNo, purchase.Quantity),
                Reason: WorldHistoryWarehouseOpsSpec.ReturnReason(receiptNo),
                CreatedAtUtc: WorldHistoryPhase2Spec.MomentOn(reviewDay, receiptNo, "supplier-return")));
        }

        return drafts;
    }

    #endregion

    #region 受控当前作业队列

    private async Task<WorldHistoryWarehouseCurrentQueueSeedReport> SeedCurrentWorkQueueAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var spec = WorldHistoryWarehouseOpsSpec.BuildCurrentQueue(asOfDate, scale);
        var inboundDrafts = spec.ReceiptOrders
            .Concat(spec.PutawayOrders)
            .ToArray();
        var inboundNumbers = inboundDrafts
            .Select(draft => draft.InboundOrderNo)
            .ToArray();
        var inboundOrders = (await dbContext.InboundOrders
                .Include(order => order.Lines)
                .Where(order => order.OrganizationId == organizationId
                    && order.EnvironmentId == environmentId
                    && inboundNumbers.Contains(order.InboundOrderNo))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(order => order.InboundOrderNo, StringComparer.Ordinal);

        var allTaskNumbers = inboundDrafts
            .Select(draft => draft.WarehouseTaskNo)
            .OfType<string>()
            .Concat(spec.OutboundOrders.Select(draft => draft.WarehouseTaskNo))
            .ToArray();
        var existingTaskNumbers = (await dbContext.WarehouseTasks
                .AsNoTracking()
                .Where(task => task.OrganizationId == organizationId
                    && task.EnvironmentId == environmentId
                    && allTaskNumbers.Contains(task.TaskNo))
                .Select(task => task.TaskNo)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var inboundWritten = 0;
        var putawayWritten = 0;
        foreach (var draft in inboundDrafts)
        {
            if (!inboundOrders.TryGetValue(draft.InboundOrderNo, out var order))
            {
                order = InboundOrder.Create(
                    organizationId,
                    environmentId,
                    draft.InboundOrderNo,
                    draft.SourceDocumentType,
                    draft.SourceDocumentId,
                    WorldHistorySpec.SiteCode,
                    [
                        new InboundOrderLineDraft(
                            WorldHistoryWmsSpec.LineNo,
                            draft.SkuCode,
                            draft.UomCode,
                            draft.Quantity,
                            draft.StagingLocationCode,
                            draft.LotNo,
                            SerialNo: null,
                            draft.QualityStatus,
                            OwnerType,
                            OwnerId: null),
                    ]);
                order.ClearDomainEvents();
                dbContext.InboundOrders.Add(order);
                Backdate(order, entity => entity.CreatedAtUtc, draft.CreatedAtUtc.UtcDateTime);
                inboundOrders.Add(order.InboundOrderNo, order);
                inboundWritten++;
            }

            if (draft.WarehouseTaskNo is null
                || existingTaskNumbers.Contains(draft.WarehouseTaskNo))
            {
                continue;
            }

            var task = order.CreatePutawayTask(
                draft.WarehouseTaskNo,
                WorldHistoryWmsSpec.LineNo,
                draft.PutawayFromLocationCode!,
                draft.PutawayToLocationCode!,
                draft.Quantity);
            task.ClearDomainEvents();
            dbContext.WarehouseTasks.Add(task);
            Backdate(task, entity => entity.CreatedAtUtc, draft.CreatedAtUtc.UtcDateTime.AddMinutes(15));
            existingTaskNumbers.Add(task.TaskNo);
            putawayWritten++;
        }

        var outboundNumbers = spec.OutboundOrders
            .Select(draft => draft.OutboundOrderNo)
            .ToArray();
        var outboundOrders = (await dbContext.OutboundOrders
                .Include(order => order.Lines)
                .Where(order => order.OrganizationId == organizationId
                    && order.EnvironmentId == environmentId
                    && outboundNumbers.Contains(order.OutboundOrderNo))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(order => order.OutboundOrderNo, StringComparer.Ordinal);
        var outboundWritten = 0;
        var pickingWritten = 0;
        var reviewReadyWritten = 0;
        foreach (var draft in spec.OutboundOrders)
        {
            if (!outboundOrders.TryGetValue(draft.OutboundOrderNo, out var order))
            {
                order = OutboundOrder.Create(
                    organizationId,
                    environmentId,
                    draft.OutboundOrderNo,
                    draft.SourceDocumentType,
                    draft.SourceDocumentId,
                    WorldHistorySpec.SiteCode,
                    [
                        new OutboundOrderLineDraft(
                            WorldHistoryWmsSpec.LineNo,
                            draft.SkuCode,
                            draft.UomCode,
                            draft.Quantity,
                            draft.PickFromLocationCode,
                            draft.LotNo,
                            SerialNo: null,
                            WorldHistoryWmsSpec.Unrestricted,
                            OwnerType,
                            OwnerId: null),
                    ]);
                order.ClearDomainEvents();
                dbContext.OutboundOrders.Add(order);
                Backdate(order, entity => entity.CreatedAtUtc, draft.CreatedAtUtc.UtcDateTime);
                outboundOrders.Add(order.OutboundOrderNo, order);
                outboundWritten++;
            }

            if (existingTaskNumbers.Contains(draft.WarehouseTaskNo))
            {
                continue;
            }

            var task = order.CreatePickingTask(
                draft.WarehouseTaskNo,
                WorldHistoryWmsSpec.LineNo,
                draft.PickFromLocationCode,
                draft.PickToLocationCode,
                draft.Quantity);
            if (draft.ReviewReady)
            {
                task.RecordProgress(draft.Quantity);
                Backdate(
                    task,
                    entity => entity.CompletedAtUtc,
                    (DateTime?)draft.CreatedAtUtc.UtcDateTime.AddMinutes(25));
                reviewReadyWritten++;
            }

            order.ClearDomainEvents();
            task.ClearDomainEvents();
            dbContext.WarehouseTasks.Add(task);
            Backdate(task, entity => entity.CreatedAtUtc, draft.CreatedAtUtc.UtcDateTime.AddMinutes(10));
            existingTaskNumbers.Add(task.TaskNo);
            pickingWritten++;
        }

        var totalWritten = inboundWritten
            + putawayWritten
            + outboundWritten
            + pickingWritten;
        if (totalWritten > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return new WorldHistoryWarehouseCurrentQueueSeedReport(
            InboundOrdersWritten: inboundWritten,
            PutawayTasksWritten: putawayWritten,
            OutboundOrdersWritten: outboundWritten,
            PickingTasksWritten: pickingWritten,
            ReviewReadyOrdersWritten: reviewReadyWritten);
    }

    #endregion

    #region 现场作业池与可执行队列

    private async Task<(int Pools, int Memberships)> SeedWorkPoolsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var poolCodes = WorldHistoryWarehouseOpsSpec.WorkPools
            .Select(pool => pool.PoolCode)
            .ToArray();
        var existingPools = (await dbContext.WarehouseWorkPools
                .AsNoTracking()
                .Where(pool => pool.OrganizationId == organizationId
                    && pool.EnvironmentId == environmentId
                    && poolCodes.Contains(pool.PoolCode))
                .Select(pool => pool.PoolCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var effectiveFromUtc = WorldHistoryCalendar.GoLiveDate.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Utc);
        var poolsWritten = 0;
        foreach (var spec in WorldHistoryWarehouseOpsSpec.WorkPools
                     .Where(spec => !existingPools.Contains(spec.PoolCode)))
        {
            var pool = WarehouseWorkPool.Create(
                organizationId,
                environmentId,
                spec.PoolCode,
                spec.DisplayName,
                spec.SiteCode);
            dbContext.WarehouseWorkPools.Add(pool);
            Backdate(pool, entity => entity.CreatedAtUtc, effectiveFromUtc);
            poolsWritten++;
        }

        var principalIds = WorldHistoryWarehouseOpsSpec.WorkPoolPrincipalIds.ToArray();
        var existingMemberships = (await dbContext.WarehouseWorkPoolMemberships
                .AsNoTracking()
                .Where(membership => membership.OrganizationId == organizationId
                    && membership.EnvironmentId == environmentId
                    && principalIds.Contains(membership.PrincipalId)
                    && poolCodes.Contains(membership.PoolCode))
                .Select(membership => new { membership.PoolCode, membership.PrincipalId })
                .ToArrayAsync(cancellationToken))
            .Select(row => MembershipKey(row.PoolCode, row.PrincipalId))
            .ToHashSet(StringComparer.Ordinal);
        var membershipsWritten = 0;
        foreach (var spec in WorldHistoryWarehouseOpsSpec.WorkPools)
        {
            foreach (var principalId in principalIds
                         .Where(principalId =>
                             !existingMemberships.Contains(MembershipKey(spec.PoolCode, principalId))))
            {
                var membership = WarehouseWorkPoolMembership.Create(
                    organizationId,
                    environmentId,
                    spec.PoolCode,
                    principalId,
                    effectiveFromUtc);
                dbContext.WarehouseWorkPoolMemberships.Add(membership);
                Backdate(membership, entity => entity.CreatedAtUtc, effectiveFromUtc);
                membershipsWritten++;
            }
        }

        if (poolsWritten + membershipsWritten > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return (poolsWritten, membershipsWritten);
    }

    /// <summary>作业池成员资格的唯一键（池 × 主体），供幂等补写与校验器共用。</summary>
    private static string MembershipKey(string poolCode, string principalId) =>
        $"{poolCode} {principalId}";

    private async Task<WorldHistoryWarehouseAssignmentSeedReport> SeedWarehouseAssignmentsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var inboundOrders = await dbContext.InboundOrders
            .Where(order => order.OrganizationId == organizationId
                && order.EnvironmentId == environmentId
                && order.SiteCode == WorldHistorySpec.SiteCode
                && order.Status == InboundOrderStatus.Open
                && order.AssignedPoolCode == null)
            .OrderBy(order => order.InboundOrderNo)
            .ToArrayAsync(cancellationToken);
        var inboundDirect = AssignInboundOrders(inboundOrders);

        var outboundOrders = await dbContext.OutboundOrders
            .Where(order => order.OrganizationId == organizationId
                && order.EnvironmentId == environmentId
                && order.SiteCode == WorldHistorySpec.SiteCode
                && order.Status == OutboundOrderStatus.Open
                && order.AssignedPoolCode == null)
            .OrderBy(order => order.OutboundOrderNo)
            .ToArrayAsync(cancellationToken);
        var outboundDirect = AssignOutboundOrders(outboundOrders);

        var warehouseTasks = await dbContext.WarehouseTasks
            .Where(task => task.OrganizationId == organizationId
                && task.EnvironmentId == environmentId
                && task.SiteCode == WorldHistorySpec.SiteCode
                && task.Status == WarehouseTaskStatus.Open
                && (task.TaskType == WarehouseTaskType.Putaway
                    || task.TaskType == WarehouseTaskType.Picking)
                && task.AssignedPoolCode == null)
            .OrderBy(task => task.TaskNo)
            .ToArrayAsync(cancellationToken);
        var manualTasks = warehouseTasks
            .Where(task => WorldHistoryWarehouseOpsSpec.IsCurrentQueueTask(task.TaskNo)
                || !WorldHistoryWarehouseOpsSpec.IsDispatched(task.TaskNo))
            .ToArray();
        var putawayTasks = manualTasks
            .Where(task => task.TaskType == WarehouseTaskType.Putaway)
            .ToArray();
        var pickingTasks = manualTasks
            .Where(task => task.TaskType == WarehouseTaskType.Picking)
            .ToArray();
        var taskDirect = AssignWarehouseTasks(putawayTasks)
            + AssignWarehouseTasks(pickingTasks);

        var countExecutions = await dbContext.CountExecutions
            .Where(execution => execution.OrganizationId == organizationId
                && execution.EnvironmentId == environmentId
                && execution.SiteCode == WorldHistorySpec.SiteCode
                && execution.Status == CountExecutionStatus.Open
                && execution.AssignedPoolCode == null)
            .OrderBy(execution => execution.CountNo)
            .ToArrayAsync(cancellationToken);
        var countDirect = AssignCountExecutions(countExecutions);

        var total = inboundOrders.Length
            + outboundOrders.Length
            + putawayTasks.Length
            + pickingTasks.Length
            + countExecutions.Length;
        if (total > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return new WorldHistoryWarehouseAssignmentSeedReport(
            InboundOrdersAssigned: inboundOrders.Length,
            OutboundOrdersAssigned: outboundOrders.Length,
            PutawayTasksAssigned: putawayTasks.Length,
            PickingTasksAssigned: pickingTasks.Length,
            CountExecutionsAssigned: countExecutions.Length,
            DirectAssignments: inboundDirect
                + outboundDirect
                + taskDirect
                + countDirect);
    }

    private static int AssignInboundOrders(IReadOnlyList<InboundOrder> orders)
    {
        var direct = 0;
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            var principalId = DemoOperatorFor(order.InboundOrderNo, index);
            order.AssignWorkPool(
                WorldHistoryWarehouseOpsSpec.ReceivingPoolCode,
                principalId,
                order.Version);
            order.ClearDomainEvents();
            direct += principalId is null ? 0 : 1;
        }

        return direct;
    }

    private static int AssignOutboundOrders(IReadOnlyList<OutboundOrder> orders)
    {
        var direct = 0;
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            var principalId = DemoOperatorFor(order.OutboundOrderNo, index);
            order.AssignWorkPool(
                WorldHistoryWarehouseOpsSpec.ShippingPoolCode,
                principalId,
                order.Version);
            order.ClearDomainEvents();
            direct += principalId is null ? 0 : 1;
        }

        return direct;
    }

    private static int AssignWarehouseTasks(IReadOnlyList<WarehouseTask> tasks)
    {
        var direct = 0;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            var principalId = DemoOperatorFor(task.TaskNo, index);
            var poolCode = task.TaskType == WarehouseTaskType.Picking
                ? WorldHistoryWarehouseOpsSpec.ShippingPoolCode
                : WorldHistoryWarehouseOpsSpec.ReceivingPoolCode;
            task.Assign(poolCode, principalId, task.Version);
            task.ClearDomainEvents();
            direct += principalId is null ? 0 : 1;
        }

        return direct;
    }

    private static int AssignCountExecutions(IReadOnlyList<CountExecution> executions)
    {
        var direct = 0;
        for (var index = 0; index < executions.Count; index++)
        {
            var execution = executions[index];
            var principalId = DemoOperatorFor(execution.CountNo, index);
            execution.AssignWorkPool(
                WorldHistoryWarehouseOpsSpec.CountPoolCode,
                principalId,
                execution.Version);
            execution.ClearDomainEvents();
            direct += principalId is null ? 0 : 1;
        }

        return direct;
    }

    private static string? DemoOperatorFor(string resourceReference, int index) =>
        index switch
        {
            0 => WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
            1 => null,
            _ => WorldHistoryWarehouseOpsSpec.IsDirectDemoAssignment(resourceReference)
                ? WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId
                : null,
        };

    #endregion

    #region WCS 下发任务与熔断链路

    private async Task<(int Tasks, int Circuits)> SeedWcsTasksAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var failuresByDevice = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var written = 0;
        var offset = 0;

        while (true)
        {
            var batch = await dbContext.WarehouseTasks
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                // 按任务号（org+env 内唯一）排序：强类型 id 在 InMemory provider 下不可比较，
                // 而分页必须有稳定序，否则批与批之间会漏行或重行。
                .OrderBy(x => x.TaskNo)
                .Skip(offset)
                .Take(BatchSize)
                .ToArrayAsync(cancellationToken);
            if (batch.Length == 0)
            {
                break;
            }

            offset += batch.Length;
            var dispatched = batch
                .Where(x => !WorldHistoryWarehouseOpsSpec.IsCurrentQueueTask(x.TaskNo)
                    && WorldHistoryWarehouseOpsSpec.IsDispatched(x.TaskNo))
                .ToArray();
            if (dispatched.Length == 0)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }

            var externalIds = dispatched.Select(x => WorldHistoryWarehouseOpsSpec.ExternalTaskId(x.TaskNo)).ToArray();
            var existing = (await dbContext.WcsTasks
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        externalIds.Contains(x.ExternalTaskId))
                    .Select(x => x.ExternalTaskId)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var task in dispatched)
            {
                var externalTaskId = WorldHistoryWarehouseOpsSpec.ExternalTaskId(task.TaskNo);
                var device = WorldHistoryWarehouseOpsSpec.DeviceFor(task.FromLocationCode, task.ToLocationCode);
                var outcome = WorldHistoryWarehouseOpsSpec.OutcomeFor(task.TaskNo);
                var dispatchedAtUtc = task.CreatedAtUtc.AddMinutes(5);
                var settledAtUtc = (task.CompletedAtUtc ?? task.CreatedAtUtc.AddMinutes(25)) > dispatchedAtUtc
                    ? task.CompletedAtUtc ?? task.CreatedAtUtc.AddMinutes(25)
                    : dispatchedAtUtc.AddMinutes(20);

                if (outcome == WorldHistoryWcsOutcome.Failed)
                {
                    RememberFailure(failuresByDevice, device.DeviceId, settledAtUtc);
                }

                if (existing.Contains(externalTaskId))
                {
                    continue;
                }

                var wcsTask = WcsTask.Dispatch(
                    organizationId,
                    environmentId,
                    task.Id,
                    device.AdapterType,
                    externalTaskId,
                    WorldHistoryWarehouseOpsSpec.DispatchPayload(
                        task.TaskNo, task.FromLocationCode, task.ToLocationCode, task.PlannedQuantity),
                    device.DeviceId);
                task.PrepareLegacyWcsHistoryReplay(task.Version);
                task.Assign(
                    task.TaskType == WarehouseTaskType.Picking
                        ? WorldHistoryWarehouseOpsSpec.ShippingPoolCode
                        : WorldHistoryWarehouseOpsSpec.ReceivingPoolCode,
                    assignedOperatorUserId: null,
                    task.Version);
                dbContext.WcsTasks.Add(wcsTask);
                var claimReference = wcsTask.Id.Id.ToString("D");
                task.ClaimWcsExecution(claimReference, task.Version);

                switch (outcome)
                {
                    case WorldHistoryWcsOutcome.Completed:
                        wcsTask.Complete(WorldHistoryWarehouseOpsSpec.CompletionPayload(task.TaskNo, task.PlannedQuantity));
                        task.RecordWcsProgress(task.PlannedQuantity, claimReference);
                        break;
                    case WorldHistoryWcsOutcome.Failed:
                        var failure = WorldHistoryWarehouseOpsSpec.FailureFor(task.TaskNo);
                        wcsTask.Fail(failure.Code, failure.Message, settledAtUtc);
                        break;
                    case WorldHistoryWcsOutcome.Cancelled:
                        wcsTask.Cancel();
                        task.Cancel();
                        break;
                    case WorldHistoryWcsOutcome.Dispatched:
                    default:
                        break;
                }

                wcsTask.ClearDomainEvents();
                task.ClearDomainEvents();
                Backdate(wcsTask, x => x.DispatchedAtUtc, dispatchedAtUtc);
                Backdate(task, x => x.ExecutionClaimedAtUtc, (DateTime?)dispatchedAtUtc);
                Backdate(task, x => x.StartedAtUtc, (DateTime?)dispatchedAtUtc);
                if (outcome == WorldHistoryWcsOutcome.Completed)
                {
                    Backdate(wcsTask, x => x.CompletedAtUtc, (DateTime?)settledAtUtc);
                    Backdate(task, x => x.CompletedAtUtc, (DateTime?)settledAtUtc);
                }

                written++;
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }

        var circuits = await SeedDispatchCircuitsAsync(organizationId, environmentId, failuresByDevice, cancellationToken);
        return (written, circuits);
    }

    /// <summary>只保留每条链路最近若干次失败：历史失败上千次时逐条回放既无意义也拖慢 seed。</summary>
    private static void RememberFailure(Dictionary<string, List<DateTime>> failuresByDevice, string deviceId, DateTime failedAtUtc)
    {
        if (!failuresByDevice.TryGetValue(deviceId, out var failures))
        {
            failures = new List<DateTime>(WorldHistoryWarehouseOpsSpec.CircuitReplayFailures + 1);
            failuresByDevice[deviceId] = failures;
        }

        failures.Add(failedAtUtc);
        if (failures.Count <= WorldHistoryWarehouseOpsSpec.CircuitReplayFailures)
        {
            return;
        }

        failures.Sort();
        failures.RemoveAt(0);
    }

    private async Task<int> SeedDispatchCircuitsAsync(
        string organizationId,
        string environmentId,
        Dictionary<string, List<DateTime>> failuresByDevice,
        CancellationToken cancellationToken)
    {
        var deviceIds = WorldHistoryWarehouseOpsSpec.Devices.Select(x => x.DeviceId).ToArray();
        var existing = (await dbContext.WcsDispatchCircuits
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    deviceIds.Contains(x.DeviceId))
                .Select(x => x.DeviceId)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var device in WorldHistoryWarehouseOpsSpec.Devices.Where(x => !existing.Contains(x.DeviceId)))
        {
            var circuit = WcsDispatchCircuit.Create(organizationId, environmentId, device.AdapterType, device.DeviceId);
            if (failuresByDevice.TryGetValue(device.DeviceId, out var failures) && failures.Count > 0)
            {
                failures.Sort();
                // 先记一次运维复位，再回放最近几次失败：结果是「有复位痕迹 + 少量连续失败」且链路闭合。
                circuit.Reset(failures[0].AddMinutes(-1));
                foreach (var failedAtUtc in failures)
                {
                    circuit.RecordFailure(failedAtUtc, WorldHistoryWarehouseOpsSpec.CircuitFailureThreshold);
                }

                if (circuit.IsOpen)
                {
                    // 熔断打开会挡住演示当场的真实下发，历史链路一律收敛在闭合态。
                    circuit.Reset(failures[^1].AddMinutes(30));
                }
            }

            circuit.ClearDomainEvents();
            dbContext.WcsDispatchCircuits.Add(circuit);
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return written;
    }

    #endregion

    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

}

/// <summary>一张历史来料退货申请的草稿。</summary>
public sealed record WorldHistorySupplierReturnDraft(
    string SupplierReturnNo,
    string InboundOrderNo,
    string InspectionRecordId,
    string SkuCode,
    string UomCode,
    string LotNo,
    decimal Quantity,
    string Reason,
    DateTimeOffset CreatedAtUtc);

/// <summary>一次仓储自动化 / 盘点 / 退货块生成的产出摘要。</summary>
public sealed record WorldHistoryWarehouseOpsSeedReport(
    int CountExecutionsWritten,
    int SupplierReturnRequestsWritten,
    WorldHistoryWarehouseCurrentQueueSeedReport CurrentQueue,
    int WorkPoolsWritten,
    int WorkPoolMembershipsWritten,
    WorldHistoryWarehouseAssignmentSeedReport Assignments,
    int WcsTasksWritten,
    int WcsDispatchCircuitsWritten,
    WorldHistoryWarehouseOpsValidationReport Validation);

public sealed record WorldHistoryWarehouseCurrentQueueSeedReport(
    int InboundOrdersWritten,
    int PutawayTasksWritten,
    int OutboundOrdersWritten,
    int PickingTasksWritten,
    int ReviewReadyOrdersWritten)
{
    public int TotalWritten => InboundOrdersWritten
        + PutawayTasksWritten
        + OutboundOrdersWritten
        + PickingTasksWritten;
}

public sealed record WorldHistoryWarehouseAssignmentSeedReport(
    int InboundOrdersAssigned,
    int OutboundOrdersAssigned,
    int PutawayTasksAssigned,
    int PickingTasksAssigned,
    int CountExecutionsAssigned,
    int DirectAssignments)
{
    public int TotalAssignments => InboundOrdersAssigned
        + OutboundOrdersAssigned
        + PutawayTasksAssigned
        + PickingTasksAssigned
        + CountExecutionsAssigned;
}

/// <summary>
/// 仓储自动化 / 盘点 / 退货块的一致性校验器（fail-closed）。
///
/// 覆盖：盘点条数与计划一致、盘点差异 = 实盘 − 账面、盘点单号号段与隔离、
/// WCS 任务全部绑真实仓储作业任务、熔断链路收敛在闭合态、退货挂真实入库单、
/// 时间戳落在 <c>[上线日, asOfDate]</c> 且不在周日。
/// </summary>
public sealed class WorldHistoryWarehouseOpsValidator(ApplicationDbContext dbContext)
{
    private const decimal QuantityTolerance = 0.000001m;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    public async Task<WorldHistoryWarehouseOpsValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var plans = WorldHistoryCountSpec.BuildCountPlans(asOfDate, scale);
        var planByCountNo = plans.ToDictionary(x => x.CountNo, StringComparer.Ordinal);
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var executions = await dbContext.CountExecutions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);

        foreach (var missing in planByCountNo.Keys
                     .Where(countNo => executions.All(x => !string.Equals(x.CountNo, countNo, StringComparison.Ordinal)))
                     .Take(5))
        {
            failures.Add($"计划中的盘点执行 {missing} 未落库。");
        }

        foreach (var execution in executions)
        {
            if (!planByCountNo.TryGetValue(execution.CountNo, out var plan))
            {
                failures.Add($"库内盘点执行 {execution.CountNo} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            if (Math.Abs(execution.ExpectedQuantity - plan.ExpectedQuantity) > QuantityTolerance)
            {
                failures.Add($"盘点执行 {execution.CountNo} 的账面量 {execution.ExpectedQuantity} 与计划 {plan.ExpectedQuantity} 不符。");
            }

            if (execution.CountedQuantity is { } counted &&
                Math.Abs((counted - execution.ExpectedQuantity) - (execution.VarianceQuantity ?? 0m)) > QuantityTolerance)
            {
                failures.Add($"盘点执行 {execution.CountNo} 的差异量与「实盘 − 账面」不符。");
            }

            if (plan.HasInventoryCountTask && execution.InventoryCountTaskId is null)
            {
                failures.Add($"盘点执行 {execution.CountNo} 有差异却没有记录库存盘点任务引用。");
            }

            CheckTimestamp(execution.CountNo, execution.CreatedAtUtc, lowerBound, upperBound, failures);
            foreach (var infix in ReservedInfixes)
            {
                if (execution.CountNo.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"盘点执行 {execution.CountNo} 落进了保留号段 '{infix}'。");
                }
            }
        }

        var warehouseTasks = await dbContext.WarehouseTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);
        var warehouseTasksById = warehouseTasks.ToDictionary(task => task.Id);
        var wcsTasks = await dbContext.WcsTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);

        foreach (var wcsTask in wcsTasks)
        {
            if (!warehouseTasksById.TryGetValue(wcsTask.WarehouseTaskId, out var warehouseTask))
            {
                failures.Add($"WCS 任务 {wcsTask.ExternalTaskId} 绑在一张不存在的仓储作业任务上。");
            }
            else
            {
                var expectedPool = warehouseTask.TaskType == WarehouseTaskType.Picking
                    ? WorldHistoryWarehouseOpsSpec.ShippingPoolCode
                    : WorldHistoryWarehouseOpsSpec.ReceivingPoolCode;
                var expectedClaim = wcsTask.Id.Id.ToString("D");
                if (!string.Equals(warehouseTask.AssignedPoolCode, expectedPool, StringComparison.Ordinal)
                    || warehouseTask.ExecutionChannel != WarehouseTaskExecutionChannel.Wcs
                    || !string.Equals(warehouseTask.ExecutionClaimedBy, expectedClaim, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"WCS 任务 {wcsTask.ExternalTaskId} 的父任务没有保持作业池与 WCS 执行归属。");
                }

                var expectedParentStatus = wcsTask.Status switch
                {
                    WcsTaskStatus.Completed => WarehouseTaskStatus.Completed,
                    WcsTaskStatus.Cancelled => WarehouseTaskStatus.Cancelled,
                    _ => WarehouseTaskStatus.InProgress,
                };
                if (warehouseTask.Status != expectedParentStatus)
                {
                    failures.Add(
                        $"WCS 任务 {wcsTask.ExternalTaskId} 状态为 {wcsTask.Status}，" +
                        $"父任务状态却为 {warehouseTask.Status}。");
                }

                if (wcsTask.Status == WcsTaskStatus.Completed
                    && Math.Abs(warehouseTask.ExecutedQuantity - warehouseTask.PlannedQuantity)
                    > QuantityTolerance)
                {
                    failures.Add($"WCS 任务 {wcsTask.ExternalTaskId} 已完成，但父任务执行量未闭合。");
                }
            }

            if (!WorldHistoryWarehouseOpsSpec.Devices.Any(device =>
                    string.Equals(device.DeviceId, wcsTask.DeviceId, StringComparison.Ordinal)))
            {
                failures.Add($"WCS 任务 {wcsTask.ExternalTaskId} 指向了世界观之外的设备 {wcsTask.DeviceId}。");
            }

            CheckTimestamp(wcsTask.ExternalTaskId, wcsTask.DispatchedAtUtc, lowerBound, upperBound, failures);
            if (wcsTask.CompletedAtUtc is { } completedAtUtc && completedAtUtc < wcsTask.DispatchedAtUtc)
            {
                failures.Add($"WCS 任务 {wcsTask.ExternalTaskId} 的回执时间早于下发时间。");
            }
        }

        var circuits = await dbContext.WcsDispatchCircuits
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);
        foreach (var circuit in circuits.Where(x => x.OpenedAtUtc is not null))
        {
            failures.Add($"WCS 下发链路 {circuit.DeviceId} 停在熔断打开态，会挡住演示当场的真实下发。");
        }

        var inboundOrders = await dbContext.InboundOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);
        var inboundOrderNumbers = inboundOrders
            .Select(order => order.InboundOrderNo)
            .ToHashSet(StringComparer.Ordinal);
        var supplierReturns = await dbContext.SupplierReturnRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);
        foreach (var supplierReturn in supplierReturns)
        {
            if (!inboundOrderNumbers.Contains(supplierReturn.InboundOrderNo))
            {
                failures.Add($"退货申请 {supplierReturn.SupplierReturnNo} 挂在一张不存在的入库单上。");
            }

            if (supplierReturn.Quantity <= 0m)
            {
                failures.Add($"退货申请 {supplierReturn.SupplierReturnNo} 的退货数量非正。");
            }

            CheckTimestamp(supplierReturn.SupplierReturnNo, supplierReturn.CreatedAtUtc, lowerBound, upperBound, failures);
        }

        var poolCodes = WorldHistoryWarehouseOpsSpec.WorkPools
            .Select(spec => spec.PoolCode)
            .ToArray();
        var workPools = await dbContext.WarehouseWorkPools
            .AsNoTracking()
            .Where(pool => pool.OrganizationId == organizationId
                && pool.EnvironmentId == environmentId
                && poolCodes.Contains(pool.PoolCode))
            .ToArrayAsync(cancellationToken);
        foreach (var spec in WorldHistoryWarehouseOpsSpec.WorkPools)
        {
            var pool = workPools.SingleOrDefault(candidate =>
                string.Equals(candidate.PoolCode, spec.PoolCode, StringComparison.Ordinal));
            if (pool is null)
            {
                failures.Add($"现场作业池 {spec.PoolCode} 未落库。");
                continue;
            }

            if (!pool.Active
                || !string.Equals(pool.DisplayName, spec.DisplayName, StringComparison.Ordinal)
                || !string.Equals(pool.SiteCode, spec.SiteCode, StringComparison.Ordinal))
            {
                failures.Add($"现场作业池 {spec.PoolCode} 的名称、站点或启用状态与规格不符。");
            }
        }

        // 期望口径不是「只有 emp049 有资格」——那是把缺陷锁成契约（PC 端仓储域会因此整域 403）。
        // 真正的不变量有两条：① 账面上干过活的人（历史单据/盘点的全部 ExecutorUserId）
        // 必须在每个池里都有有效资格；② 演示要用的主管与管理员同样是在册成员。
        var memberships = await dbContext.WarehouseWorkPoolMemberships
            .AsNoTracking()
            .Where(membership => membership.OrganizationId == organizationId
                && membership.EnvironmentId == environmentId
                && poolCodes.Contains(membership.PoolCode))
            .ToArrayAsync(cancellationToken);
        var historicalExecutorIds = WorldHistoryPhase2Spec.Storekeepers
            .Select(person => person.UserId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var spec in WorldHistoryWarehouseOpsSpec.WorkPools)
        {
            var effectivePrincipalIds = memberships
                .Where(membership =>
                    string.Equals(membership.PoolCode, spec.PoolCode, StringComparison.Ordinal)
                    && membership.IsEffectiveAt(upperBound))
                .Select(membership => membership.PrincipalId)
                .ToHashSet(StringComparer.Ordinal);

            var missingExecutors = historicalExecutorIds
                .Except(effectivePrincipalIds, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missingExecutors.Length > 0)
            {
                failures.Add(
                    $"作业池 {spec.PoolCode} 在 {asOfDate:yyyy-MM-dd} 未覆盖历史单据的执行人 " +
                    $"{string.Join("、", missingExecutors)}——账面上干过活的人在系统里没有资格干活。");
            }

            var missingMembers = WorldHistoryWarehouseOpsSpec.WorkPoolPrincipalIds
                .Except(effectivePrincipalIds, StringComparer.Ordinal)
                .Except(historicalExecutorIds, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missingMembers.Length > 0)
            {
                failures.Add(
                    $"作业池 {spec.PoolCode} 在 {asOfDate:yyyy-MM-dd} 缺少成员 " +
                    $"{string.Join("、", missingMembers)} 的有效资格。");
            }
        }

        var assignedInboundOrders = inboundOrders
            .Where(order => order.SiteCode == WorldHistorySpec.SiteCode
                && order.Status == InboundOrderStatus.Open)
            .ToArray();
        foreach (var order in assignedInboundOrders)
        {
            CheckAssignment(
                "入库单",
                order.InboundOrderNo,
                order.AssignedPoolCode,
                WorldHistoryWarehouseOpsSpec.ReceivingPoolCode,
                order.AssignedOperatorUserId,
                failures);
        }

        var outboundOrders = await dbContext.OutboundOrders
            .AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && order.EnvironmentId == environmentId
                && order.SiteCode == WorldHistorySpec.SiteCode
                && order.Status == OutboundOrderStatus.Open)
            .ToArrayAsync(cancellationToken);
        foreach (var order in outboundOrders)
        {
            CheckAssignment(
                "出库单",
                order.OutboundOrderNo,
                order.AssignedPoolCode,
                WorldHistoryWarehouseOpsSpec.ShippingPoolCode,
                order.AssignedOperatorUserId,
                failures);
        }

        var assignedWarehouseTasks = warehouseTasks
            .Where(task => task.SiteCode == WorldHistorySpec.SiteCode
                && task.Status == WarehouseTaskStatus.Open
                && (task.TaskType == WarehouseTaskType.Putaway
                    || task.TaskType == WarehouseTaskType.Picking)
                && (WorldHistoryWarehouseOpsSpec.IsCurrentQueueTask(task.TaskNo)
                    || !WorldHistoryWarehouseOpsSpec.IsDispatched(task.TaskNo)))
            .ToArray();
        foreach (var task in assignedWarehouseTasks)
        {
            CheckAssignment(
                task.TaskType == WarehouseTaskType.Putaway ? "上架任务" : "拣货任务",
                task.TaskNo,
                task.AssignedPoolCode,
                task.TaskType == WarehouseTaskType.Putaway
                    ? WorldHistoryWarehouseOpsSpec.ReceivingPoolCode
                    : WorldHistoryWarehouseOpsSpec.ShippingPoolCode,
                task.AssignedOperatorUserId,
                failures);
        }

        var assignedCountExecutions = executions
            .Where(execution => execution.SiteCode == WorldHistorySpec.SiteCode
                && execution.Status == CountExecutionStatus.Open)
            .ToArray();
        foreach (var execution in assignedCountExecutions)
        {
            CheckAssignment(
                "盘点执行",
                execution.CountNo,
                execution.AssignedPoolCode,
                WorldHistoryWarehouseOpsSpec.CountPoolCode,
                execution.AssignedOperatorUserId,
                failures);
        }

        if (failures.Count > 0)
        {
            throw new WorldHistoryWarehouseOpsConsistencyException(failures);
        }

        return new WorldHistoryWarehouseOpsValidationReport(
            CountExecutionsChecked: executions.Length,
            CompletedCountExecutionsChecked: executions.Count(x => x.Status == CountExecutionStatus.Completed),
            VarianceCountExecutionsChecked: executions.Count(x => (x.VarianceQuantity ?? 0m) != 0m),
            WcsTasksChecked: wcsTasks.Length,
            CompletedWcsTasksChecked: wcsTasks.Count(x => x.Status == WcsTaskStatus.Completed),
            FailedWcsTasksChecked: wcsTasks.Count(x => x.Status == WcsTaskStatus.Failed),
            WcsDispatchCircuitsChecked: circuits.Length,
            SupplierReturnRequestsChecked: supplierReturns.Length,
            WorkPoolsChecked: workPools.Length,
            WorkPoolMembershipsChecked: memberships.Length,
            AssignedInboundOrdersChecked: assignedInboundOrders.Length,
            AssignedPutawayTasksChecked: assignedWarehouseTasks.Count(
                task => task.TaskType == WarehouseTaskType.Putaway),
            AssignedPickingTasksChecked: assignedWarehouseTasks.Count(
                task => task.TaskType == WarehouseTaskType.Picking),
            AssignedOutboundOrdersChecked: outboundOrders.Length,
            AssignedCountExecutionsChecked: assignedCountExecutions.Length);
    }

    private static void CheckAssignment(
        string resourceType,
        string resourceReference,
        string? assignedPoolCode,
        string expectedPoolCode,
        string? assignedOperatorUserId,
        List<string> failures)
    {
        if (!string.Equals(assignedPoolCode, expectedPoolCode, StringComparison.Ordinal))
        {
            failures.Add(
                $"{resourceType} {resourceReference} 未归入预期现场作业池 {expectedPoolCode}。");
        }

        if (assignedOperatorUserId is not null
            && !string.Equals(
                assignedOperatorUserId,
                WorldHistoryWarehouseOpsSpec.DemoWarehousePrincipalId,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{resourceType} {resourceReference} 被派给了世界观之外的人员 {assignedOperatorUserId}。");
        }
    }

    private static void CheckTimestamp(
        string reference,
        DateTime timestampUtc,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (timestampUtc < lowerBound || timestampUtc > upperBound)
        {
            failures.Add($"{reference} 的时间戳 {timestampUtc:O} 落在历史区间之外。");
        }

        if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(timestampUtc)))
        {
            failures.Add($"{reference} 的时间戳 {timestampUtc:O} 落在周日（停产保养日）。");
        }
    }
}

/// <summary>仓储自动化 / 盘点 / 退货块校验器的产出摘要。</summary>
public sealed record WorldHistoryWarehouseOpsValidationReport(
    int CountExecutionsChecked,
    int CompletedCountExecutionsChecked,
    int VarianceCountExecutionsChecked,
    int WcsTasksChecked,
    int CompletedWcsTasksChecked,
    int FailedWcsTasksChecked,
    int WcsDispatchCircuitsChecked,
    int SupplierReturnRequestsChecked,
    int WorkPoolsChecked,
    int WorkPoolMembershipsChecked,
    int AssignedInboundOrdersChecked,
    int AssignedPutawayTasksChecked,
    int AssignedPickingTasksChecked,
    int AssignedOutboundOrdersChecked,
    int AssignedCountExecutionsChecked);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryWarehouseOpsConsistencyException : InvalidOperationException
{
    public WorldHistoryWarehouseOpsConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryWarehouseOpsConsistencyException()
        : base("World-history warehouse operations consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryWarehouseOpsConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryWarehouseOpsConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败（仓储自动化 / 盘点 / 退货），共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
