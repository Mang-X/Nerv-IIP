using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using System.Text;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **仓储自动化 / 盘点执行 / 来料退货**块。
///
/// 必须在 <see cref="WorldHistorySeedService"/> 之后运行：WCS 任务要绑库里真实存在的
/// <c>warehouse_tasks</c> 行，退货要挂库里真实存在的收货入库单。
///
/// 领域事件说明：<c>WcsTask</c> / <c>CountExecution</c> 的工厂方法与状态迁移都会
/// <c>AddDomainEvent</c>，且这些事件有跨服务消费者（WCS 适配器、库存盘点冻结）。
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
        var (wcsTasks, circuits) = await SeedWcsTasksAsync(organizationId, environmentId, cancellationToken);

        var validation = await new WorldHistoryWarehouseOpsValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryWarehouseOpsSeedReport(
            CountExecutionsWritten: countExecutions,
            SupplierReturnRequestsWritten: supplierReturns,
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
                    execution.Complete(plan.CountedQuantity);
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
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                // 按任务号（org+env 内唯一）排序：强类型 id 在 InMemory provider 下不可比较，
                // 而分页必须有稳定序，否则批与批之间会漏行或重行。
                .OrderBy(x => x.TaskNo)
                .Skip(offset)
                .Take(BatchSize)
                .Select(x => new WarehouseTaskProjection(
                    x.Id,
                    x.TaskNo,
                    x.FromLocationCode,
                    x.ToLocationCode,
                    x.PlannedQuantity,
                    x.CreatedAtUtc,
                    x.CompletedAtUtc))
                .ToArrayAsync(cancellationToken);
            if (batch.Length == 0)
            {
                break;
            }

            offset += batch.Length;
            var dispatched = batch
                .Where(x => WorldHistoryWarehouseOpsSpec.IsDispatched(x.TaskNo))
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

                switch (outcome)
                {
                    case WorldHistoryWcsOutcome.Completed:
                        wcsTask.Complete(WorldHistoryWarehouseOpsSpec.CompletionPayload(task.TaskNo, task.PlannedQuantity));
                        break;
                    case WorldHistoryWcsOutcome.Failed:
                        var failure = WorldHistoryWarehouseOpsSpec.FailureFor(task.TaskNo);
                        wcsTask.Fail(failure.Code, failure.Message, settledAtUtc);
                        break;
                    case WorldHistoryWcsOutcome.Cancelled:
                        wcsTask.Cancel();
                        break;
                    case WorldHistoryWcsOutcome.Dispatched:
                    default:
                        break;
                }

                wcsTask.ClearDomainEvents();
                dbContext.WcsTasks.Add(wcsTask);
                Backdate(wcsTask, x => x.DispatchedAtUtc, dispatchedAtUtc);
                if (outcome == WorldHistoryWcsOutcome.Completed)
                {
                    Backdate(wcsTask, x => x.CompletedAtUtc, (DateTime?)settledAtUtc);
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

    private sealed record WarehouseTaskProjection(
        WarehouseTaskId Id,
        string TaskNo,
        string FromLocationCode,
        string ToLocationCode,
        decimal PlannedQuantity,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc);
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
    int WcsTasksWritten,
    int WcsDispatchCircuitsWritten,
    WorldHistoryWarehouseOpsValidationReport Validation);

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

        var warehouseTaskIds = (await dbContext.WarehouseTasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
        var wcsTasks = await dbContext.WcsTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToArrayAsync(cancellationToken);

        foreach (var wcsTask in wcsTasks)
        {
            if (!warehouseTaskIds.Contains(wcsTask.WarehouseTaskId))
            {
                failures.Add($"WCS 任务 {wcsTask.ExternalTaskId} 绑在一张不存在的仓储作业任务上。");
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

        var inboundOrderNumbers = (await dbContext.InboundOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => x.InboundOrderNo)
                .ToArrayAsync(cancellationToken))
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
            SupplierReturnRequestsChecked: supplierReturns.Length);
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
    int SupplierReturnRequestsChecked);

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
