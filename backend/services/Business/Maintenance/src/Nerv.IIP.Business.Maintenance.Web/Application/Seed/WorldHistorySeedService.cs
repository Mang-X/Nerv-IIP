using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎 **Maintenance 侧（三期）**：
/// 维修工单（<c>MWO-2026-####</c>，约 120 张，完工含停机原因/工时/技师）、46 台设备的
/// 点检/保养计划（92 条）与按频次展开的点检记录。
///
/// 报警 → 维修工单的对应关系来自与 IndustrialTelemetry 侧同一字面量的
/// <see cref="WorldHistoryDeviceSpec.BuildAlarmPlans"/>：工单的 <c>SourceAlarmId</c> 指向
/// 遥测侧回填的报警号段（<c>WH-*:####</c>），停机分钟与报警持续时长一致——两侧按构造对账。
///
/// 四期补齐：**完工工单的备件消耗行**（<see cref="WorldHistorySparePartSpec"/>，MRO 号段）与
/// **设备状态投影**（<c>maintenance_device_states</c>，46 台设备镜像 MasterData 启用态）。
///
/// MAN-519 修订四条款（历史时间戳 / 独立号段 / fail-closed 校验器 / 幂等）与一期相同；
/// 工单号写在 <c>SourceReferenceId</c>（先例：固定案例 <c>MWO-DEMO-001</c>）。
/// 批量写入走 <c>SaveChangesAsync</c>（不派发领域事件）——绝不让 400 起历史报警在启动时
/// 经 CAP 触发 400 张自动工单。计划的 <c>NextDueOn</c> 在写完点检后推进到截止日之后，
/// 防止日历调度器启动时做 29 周的 catch-up 开单风暴。
///
/// <para><b>备件消耗**不**产生库存流水——这是刻意的边界</b>：
/// <c>MaintenanceSparePartIssuedDomainEvent</c> 有跨服务转换器
/// （<c>MaintenanceSparePartIssuedIntegrationEventConverter</c> → Inventory 的
/// <c>InventoryMovementRequestedIntegrationEvent</c> 出库）。历史备件若真去扣库存，会打破库存域
/// 「现存量 = 世界观流水代数和」的恒等式（WMS 那批已踩过，见提交 <c>e4deae3</c>）。
/// 何况 MRO 备件根本不在 L0 物料主数据与库存台账里（见 <see cref="WorldHistorySparePartSpec"/>），
/// 扣的也是不存在的账。因此写完聚合后显式 <c>ClearDomainEvents()</c>：
/// 历史事实只落 Maintenance 自己的账（备件行 + 工单备件金额），绝不驱动下游。
/// 表注释本身也早已写明 “Spare part demand lines recorded by Maintenance; not inventory balances.”。</para>
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    public const int BatchSize = 500;

    private const string PlanOwnerUserId = "user-emp-042";

    private int pendingWrites;
    private int sparePartLinesWritten;

    public async Task<WorldHistoryMaintenanceSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var alarmPlans = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, scale);

        sparePartLinesWritten = 0;
        var reasonsWritten = await SeedDowntimeReasonsAsync(organizationId, environmentId, cancellationToken);
        var deviceStatesWritten = await SeedDeviceStatesAsync(organizationId, environmentId, cancellationToken);
        var plansWritten = await SeedMaintenancePlansAsync(organizationId, environmentId, cancellationToken);
        var inspectionsWritten = await SeedInspectionsAsync(organizationId, environmentId, asOfDate, cancellationToken);
        await AdvancePlanDueDatesAsync(organizationId, environmentId, asOfDate, cancellationToken);
        var workOrdersWritten = await SeedWorkOrdersAsync(organizationId, environmentId, alarmPlans, cancellationToken);

        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistoryMaintenanceSeedReport(
            reasonsWritten,
            plansWritten,
            inspectionsWritten,
            workOrdersWritten,
            sparePartLinesWritten,
            deviceStatesWritten,
            validation);
    }

    /// <summary>
    /// 设备状态投影（<c>maintenance_device_states</c>）：46 台设备镜像 MasterData §3 的启用态。
    ///
    /// ⚠️ 这张表**不是**「维修视角的运行/停机/待料/保养中」状态机——它只有一个 <c>Disabled</c> 布尔位，
    /// 是 <c>DeviceAssetChangedIntegrationEvent</c> 的最新投影，唯一用途是给保养计划生成做闸门
    /// （停用设备不开 PM 单）。「设备在跑还是在修」的视图在 IndustrialTelemetry 的
    /// <c>device_state_snapshots</c>，维修态则由工单 <c>Status=Open</c> + <c>AssetUnavailable</c> 表达。
    ///
    /// 因此这里**全部写 <c>Disabled=false</c>**：L0 §3 的 46 台设备在主数据里没有一台是停用的，
    /// 硬编几台 <c>true</c> 会同时制造两处矛盾——主数据说启用、维修域说停用；而且会静默暂停那台设备的
    /// PM 计划，演示时「这台机器为什么不出保养单」无从解释。变更时刻取上线日（设备投用即启用）。
    /// </summary>
    private async Task<int> SeedDeviceStatesAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.MaintenanceDeviceStates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.DeviceAssetId)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        var commissionedAtUtc = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero);

        var written = 0;
        foreach (var device in WorldHistoryDeviceSpec.Devices.Where(x => !existingSet.Contains(x.DeviceAssetId)))
        {
            dbContext.MaintenanceDeviceStates.Add(MaintenanceDeviceState.Create(
                organizationId,
                environmentId,
                device.DeviceAssetId,
                disabled: false,
                commissionedAtUtc,
                DeviceStateSourceEventId(device.DeviceAssetId)));
            written++;
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    /// <summary>种子来源标记：真实 MasterData 事件（较晚的 <c>ChangedAtUtc</c>）到来时仍会正常覆盖本行。</summary>
    public static string DeviceStateSourceEventId(string deviceAssetId) =>
        $"{WorldHistoryDeviceSpec.SourceSystem}:device-state:{deviceAssetId}";

    private async Task<int> SeedDowntimeReasonsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var codes = WorldHistoryDeviceSpec.DowntimeReasons.Select(x => x.Code).ToArray();
        var existing = await dbContext.DowntimeReasons
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && codes.Contains(x.ReasonCode))
            .Select(x => x.ReasonCode)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var reason in WorldHistoryDeviceSpec.DowntimeReasons.Where(x => !existingSet.Contains(x.Code)))
        {
            dbContext.DowntimeReasons.Add(DowntimeReason.Create(
                organizationId, environmentId, reason.Code, reason.Description, reason.ReasonCategory, reason.LossCategory));
            written++;
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<int> SeedMaintenancePlansAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.MaintenancePlans
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanCode.StartsWith("PM-WH-"))
            .Select(x => x.PlanCode)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var planSpec in WorldHistoryDeviceSpec.BuildMaintenancePlans().Where(x => !existingSet.Contains(x.PlanCode)))
        {
            var plan = MaintenancePlan.Create(
                organizationId,
                environmentId,
                planSpec.DeviceAssetId,
                planSpec.PlanCode,
                planSpec.Interval,
                WorldHistoryCalendar.GoLiveDate,
                PlanOwnerUserId);
            dbContext.MaintenancePlans.Add(plan);
            BackdateOffset(plan, x => x.CreatedAtUtc, new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero));
            written++;
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private async Task<int> SeedInspectionsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var planIds = await dbContext.MaintenancePlans
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanCode.StartsWith("PM-WH-"))
            .Select(x => new { x.PlanCode, x.Id })
            .ToDictionaryAsync(x => x.PlanCode, x => x.Id, StringComparer.Ordinal, cancellationToken);

        var seedPlanIds = planIds.Values.ToArray();
        var existing = await dbContext.MaintenanceInspections
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanId != null && seedPlanIds.Contains(x.PlanId))
            .Select(x => new { x.PlanId, x.InspectedAtUtc })
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.Select(x => (x.PlanId!, x.InspectedAtUtc)).ToHashSet();

        var written = 0;
        foreach (var occurrence in WorldHistoryDeviceSpec.BuildInspections(asOfDate))
        {
            if (!planIds.TryGetValue(occurrence.PlanCode, out var planId)
                || existingSet.Contains((planId, occurrence.InspectedAtUtc)))
            {
                continue;
            }

            dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan(
                organizationId,
                environmentId,
                planId,
                occurrence.InspectorUserId,
                occurrence.Result,
                occurrence.InspectedAtUtc));
            written++;
            await FlushAsync(cancellationToken);
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    /// <summary>把本引擎计划的日历游标推进到截止日之后，防止调度器对 29 周欠账做 catch-up 开单。</summary>
    private async Task AdvancePlanDueDatesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanCode.StartsWith("PM-WH-"))
            .ToArrayAsync(cancellationToken);
        foreach (var plan in plans)
        {
            while (plan.IsDueOn(asOfDate))
            {
                plan.MarkGenerated(asOfDate);
            }
        }

        await FlushAsync(cancellationToken, force: true);
    }

    private async Task<int> SeedWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var workOrderPlans = alarmPlans.Where(x => x.HasWorkOrder).ToArray();
        var existing = await dbContext.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceReferenceId != null && x.SourceReferenceId.StartsWith("MWO-2026-"))
            .Select(x => x.SourceReferenceId!)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        // 截止日推进后的 catch-up：上次 seed 留在 Open 态的尾部工单，本次计划已给出完工时刻的补完工。
        var shouldBeCompleted = workOrderPlans
            .Where(plan => plan.CompletedAtUtc is not null && existingSet.Contains(plan.WorkOrderNo!))
            .ToDictionary(x => x.WorkOrderNo!, StringComparer.Ordinal);
        if (shouldBeCompleted.Count > 0)
        {
            var referenceIds = shouldBeCompleted.Keys.ToArray();
            var staleOpen = await dbContext.MaintenanceWorkOrders
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Where(x => x.SourceReferenceId != null && referenceIds.Contains(x.SourceReferenceId))
                .Where(x => x.Status == MaintenanceWorkOrderStatus.Open)
                .ToArrayAsync(cancellationToken);
            foreach (var workOrder in staleOpen)
            {
                CompleteFromPlan(workOrder, shouldBeCompleted[workOrder.SourceReferenceId!]);
            }

            await FlushAsync(cancellationToken, force: true);
        }

        var written = 0;
        foreach (var plan in workOrderPlans.Where(plan => !existingSet.Contains(plan.WorkOrderNo!)))
        {
            var workOrder = MaintenanceWorkOrder.OpenFromAlarm(
                organizationId,
                environmentId,
                plan.DeviceAssetId,
                plan.ExternalAlarmId,
                plan.Severity == "critical" ? "high" : "medium",
                openedBy: "industrialTelemetry",
                diagnosticDescription: $"报警 {plan.AlarmCode}：观测值 {plan.ObservedValue}{plan.UnitCode} 越限（阈值 {plan.ThresholdValue}{plan.UnitCode}）",
                failureModeCode: plan.FailureModeCode,
                failureCauseCode: plan.FailureCauseCode,
                assignedTechnicianUserId: plan.TechnicianUserId,
                estimatedLaborMinutes: plan.LaborMinutes,
                sourceReferenceId: plan.WorkOrderNo);
            dbContext.MaintenanceWorkOrders.Add(workOrder);

            // 先把 OpenedAtUtc 改写为历史时刻，再走领域动作——MarkRepairStarted 会校验不早于开单时间。
            BackdateOffset(workOrder, x => x.OpenedAtUtc, plan.RaisedAtUtc);
            if (plan.RepairStartedAtUtc is not null)
            {
                workOrder.MarkRepairStarted(plan.RepairStartedAtUtc.Value);
            }

            if (plan.CompletedAtUtc is not null)
            {
                CompleteFromPlan(workOrder, plan);
            }

            // 开放尾部工单同样清空领域事件（开单事件也有跨服务转换器）。
            workOrder.ClearDomainEvents();
            written++;
            await FlushAsync(cancellationToken);
        }

        await FlushAsync(cancellationToken, force: true);
        return written;
    }

    private void CompleteFromPlan(MaintenanceWorkOrder workOrder, WorldHistoryAlarmPlan plan)
    {
        if (plan.RepairStartedAtUtc is not null && workOrder.RepairStartedAtUtc is null)
        {
            workOrder.MarkRepairStarted(plan.RepairStartedAtUtc.Value);
        }

        var issues = WorldHistorySparePartSpec.BuildIssues(plan.WorkOrderNo!, plan.FailureCauseCode);
        workOrder.MarkAlarmCleared(plan.ClearedAtUtc);
        workOrder.Complete(
            result: "已修复并恢复运行",
            downtimeReasonCode: plan.DowntimeReasonCode,
            downtimeMinutes: plan.DowntimeMinutes,
            spareParts: issues.Select(x => new SparePartLineDraft(x.SkuCode, x.Quantity, x.UomCode)),
            actualLaborMinutes: plan.LaborMinutes,
            sparePartCostAmount: WorldHistorySparePartSpec.TotalAmount(issues),
            costCurrencyCode: WorldHistorySparePartSpec.CurrencyCode,
            actualTechnicianUserId: plan.TechnicianUserId);
        BackdateOffset(workOrder, x => x.CompletedAtUtc, plan.CompletedAtUtc!.Value);
        sparePartLinesWritten += issues.Count;

        // 历史事实不驱动下游：备件领用会转成 Inventory 出库集成事件，历史回填绝不能触发它（类注释）。
        workOrder.ClearDomainEvents();
    }

    private void BackdateOffset<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    private void BackdateOffset<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset?>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    private async Task FlushAsync(CancellationToken cancellationToken, bool force = false)
    {
        pendingWrites++;
        if (!force && pendingWrites < BatchSize)
        {
            return;
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        pendingWrites = 0;
    }
}

/// <summary>一次 L1 设备域历史生成（Maintenance 侧）的产出摘要。</summary>
public sealed record WorldHistoryMaintenanceSeedReport(
    int DowntimeReasonsWritten,
    int MaintenancePlansWritten,
    int InspectionsWritten,
    int WorkOrdersWritten,
    int SparePartLinesWritten,
    int DeviceStatesWritten,
    WorldHistoryMaintenanceValidationReport Validation);
