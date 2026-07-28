using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

/// <summary>
/// L1 设备域历史（Maintenance 侧）的 fail-closed 一致性校验器（设定集 §7）：
/// 1. 维修工单与共享报警计划逐条对上（工单号 / 源报警号 / 设备 / 完工态）；
/// 2. 完工工单的停机分钟、维修起止与计划一致——报警侧（IndustrialTelemetry）按同一字面量生成，
///    因此「每台设备的报警数/停机时长与维修工单对账吻合」按构造成立，且 MTBF/MTTR 可算
///    （每张故障工单都有 OpenedAt / RepairStartedAt / CompletedAt / DowntimeMinutes）；
/// 3. 点检记录条数与计划频次展开完全一致；
/// 4. 本引擎计划的 NextDueOn 已推进到截止日之后（无 catch-up 开单欠账）；
/// 5. 引用的停机原因码全部存在于目录；
/// 6.（四期）完工工单的备件消耗行与 <see cref="WorldHistorySparePartSpec"/> 逐条一致，
///    且工单的备件金额 = 行金额合计；
/// 7.（四期）设备状态投影覆盖 §3 全部 46 台设备、与 MasterData 启用态一致，
///    且**没有一台带在办维修工单的设备被标成停用**（否则该设备的 PM 计划会被静默暂停，
///    形成「设备在修 → 保养计划却停了」的自相矛盾）。
/// 任何一条不满足直接抛 <see cref="InvalidOperationException"/>。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    public async Task<WorldHistoryMaintenanceValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var alarmPlans = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, scale);
        var workOrderPlans = alarmPlans.Where(x => x.HasWorkOrder).ToArray();

        var workOrdersChecked = await ValidateWorkOrdersAsync(organizationId, environmentId, workOrderPlans, cancellationToken);
        var inspectionsChecked = await ValidateInspectionsAsync(organizationId, environmentId, asOfDate, cancellationToken);
        await ValidatePlanCursorsAsync(organizationId, environmentId, asOfDate, cancellationToken);
        await ValidateDowntimeReasonsAsync(organizationId, environmentId, cancellationToken);
        var sparePartLinesChecked = await ValidateSparePartLinesAsync(organizationId, environmentId, workOrderPlans, cancellationToken);
        var deviceStatesChecked = await ValidateDeviceStatesAsync(organizationId, environmentId, cancellationToken);

        var sample = workOrderPlans
            .Take(20)
            .Select(x => $"{x.WorkOrderNo} ← {x.ExternalAlarmId} ({x.DeviceAssetId}, {x.Severity}, "
                + $"downtime {x.DowntimeMinutes} min{(x.CompletedAtUtc is null ? ", still open" : string.Empty)})")
            .ToArray();

        return new WorldHistoryMaintenanceValidationReport(
            workOrdersChecked,
            workOrderPlans.Count(x => x.CompletedAtUtc is null),
            inspectionsChecked,
            workOrderPlans.Where(x => x.CompletedAtUtc is not null).Sum(x => x.DowntimeMinutes),
            sparePartLinesChecked,
            deviceStatesChecked,
            sample);
    }

    /// <summary>备件消耗行与 Spec 逐条对账（含工单备件金额），并确认未完工工单没有备件行。</summary>
    private async Task<int> ValidateSparePartLinesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> workOrderPlans,
        CancellationToken cancellationToken)
    {
        var seeded = await dbContext.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceReferenceId != null && x.SourceReferenceId.StartsWith("MWO-2026-"))
            .Select(x => new
            {
                x.SourceReferenceId,
                x.SparePartCostAmount,
                Lines = x.SparePartLines.Select(line => new { line.SkuCode, line.Quantity, line.UomCode }).ToList(),
            })
            .ToArrayAsync(cancellationToken);
        var byReference = seeded.ToDictionary(x => x.SourceReferenceId!, StringComparer.Ordinal);

        var linesChecked = 0;
        foreach (var plan in workOrderPlans)
        {
            if (!byReference.TryGetValue(plan.WorkOrderNo!, out var workOrder))
            {
                throw Fail($"work order '{plan.WorkOrderNo}' is missing while checking spare part lines.");
            }

            if (plan.CompletedAtUtc is null)
            {
                // 未完工工单不记备件——备件在完工登记时才入账（领域模型只在 Complete 收 spareParts）。
                if (workOrder.Lines.Count > 0)
                {
                    throw Fail($"open work order '{plan.WorkOrderNo}' must not carry spare part lines.");
                }

                continue;
            }

            var expected = WorldHistorySparePartSpec.BuildIssues(plan.WorkOrderNo!, plan.FailureCauseCode);
            if (workOrder.Lines.Count != expected.Count)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' spare part line count mismatch: "
                    + $"expected {expected.Count} but found {workOrder.Lines.Count}.");
            }

            var actualBySku = workOrder.Lines.ToDictionary(x => x.SkuCode, StringComparer.Ordinal);
            foreach (var issue in expected)
            {
                if (!actualBySku.TryGetValue(issue.SkuCode, out var line))
                {
                    throw Fail($"work order '{plan.WorkOrderNo}' is missing spare part line '{issue.SkuCode}'.");
                }

                if (line.Quantity != issue.Quantity || line.UomCode != issue.UomCode)
                {
                    throw Fail($"work order '{plan.WorkOrderNo}' spare part line '{issue.SkuCode}' "
                        + "quantity/uom does not match the deterministic spec.");
                }
            }

            var expectedAmount = WorldHistorySparePartSpec.TotalAmount(expected);
            if (workOrder.SparePartCostAmount != expectedAmount)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' spare part cost {workOrder.SparePartCostAmount} "
                    + $"does not equal the line total {expectedAmount}.");
            }

            linesChecked += workOrder.Lines.Count;
        }

        return linesChecked;
    }

    /// <summary>设备状态投影：46 台全覆盖、全部启用、与在办维修工单不矛盾。</summary>
    private async Task<int> ValidateDeviceStatesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var states = await dbContext.MaintenanceDeviceStates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.DeviceAssetId, x.Disabled })
            .ToArrayAsync(cancellationToken);
        var byDevice = states.ToDictionary(x => x.DeviceAssetId, x => x.Disabled, StringComparer.Ordinal);

        foreach (var device in WorldHistoryDeviceSpec.Devices)
        {
            if (!byDevice.TryGetValue(device.DeviceAssetId, out var disabled))
            {
                throw Fail($"device state projection is missing device '{device.DeviceAssetId}'.");
            }

            if (disabled)
            {
                throw Fail($"device '{device.DeviceAssetId}' is projected as disabled but MasterData (§3) "
                    + "lists every device as active.");
            }
        }

        // 在办维修工单的设备绝不能是停用态——停用会静默暂停该设备的 PM 计划。
        var openWorkOrderDevices = await dbContext.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Open)
            .Select(x => x.DeviceAssetId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        foreach (var deviceAssetId in openWorkOrderDevices)
        {
            if (byDevice.TryGetValue(deviceAssetId, out var disabled) && disabled)
            {
                throw Fail($"device '{deviceAssetId}' has an open maintenance work order but is projected as "
                    + "disabled (its preventive plans would be silently paused).");
            }
        }

        return states.Length;
    }

    private async Task<int> ValidateWorkOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> workOrderPlans,
        CancellationToken cancellationToken)
    {
        var seeded = await dbContext.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceReferenceId != null && x.SourceReferenceId.StartsWith("MWO-2026-"))
            .Select(x => new
            {
                x.SourceReferenceId,
                x.SourceAlarmId,
                x.DeviceAssetId,
                x.Status,
                x.OpenedAtUtc,
                x.RepairStartedAtUtc,
                x.CompletedAtUtc,
                x.DowntimeMinutes,
                x.ActualLaborMinutes,
                x.DowntimeReasonCode,
            })
            .ToArrayAsync(cancellationToken);
        if (seeded.Length != workOrderPlans.Count)
        {
            throw Fail($"work order count mismatch: expected {workOrderPlans.Count} but found {seeded.Length}.");
        }

        var byReference = seeded.ToDictionary(x => x.SourceReferenceId!, StringComparer.Ordinal);
        var completedDowntimeTotal = 0;
        foreach (var plan in workOrderPlans)
        {
            if (!byReference.TryGetValue(plan.WorkOrderNo!, out var workOrder))
            {
                throw Fail($"work order '{plan.WorkOrderNo}' is missing.");
            }

            if (workOrder.SourceAlarmId != plan.ExternalAlarmId || workOrder.DeviceAssetId != plan.DeviceAssetId)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' does not reference alarm '{plan.ExternalAlarmId}'.");
            }

            if (workOrder.OpenedAtUtc != plan.RaisedAtUtc)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' opened time does not match the alarm raise time.");
            }

            if (plan.CompletedAtUtc is null)
            {
                // 开放尾部：现场演示可能手工完工，这里只要求它存在且引用正确。
                continue;
            }

            if (workOrder.Status != MaintenanceWorkOrderStatus.Completed)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' should be completed but is '{workOrder.Status}'.");
            }

            if (workOrder.DowntimeMinutes != plan.DowntimeMinutes
                || workOrder.ActualLaborMinutes != plan.LaborMinutes
                || workOrder.DowntimeReasonCode != plan.DowntimeReasonCode)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' downtime/labor facts do not match the shared plan.");
            }

            if (workOrder.RepairStartedAtUtc != plan.RepairStartedAtUtc || workOrder.CompletedAtUtc != plan.CompletedAtUtc)
            {
                throw Fail($"work order '{plan.WorkOrderNo}' repair window does not match the shared plan (MTBF/MTTR input).");
            }

            completedDowntimeTotal += workOrder.DowntimeMinutes!.Value;
        }

        var expectedDowntimeTotal = workOrderPlans.Where(x => x.CompletedAtUtc is not null).Sum(x => x.DowntimeMinutes);
        if (completedDowntimeTotal != expectedDowntimeTotal)
        {
            throw Fail($"total downtime mismatch: expected {expectedDowntimeTotal} min but found {completedDowntimeTotal} min.");
        }

        return seeded.Length;
    }

    private async Task<int> ValidateInspectionsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var seedPlanIds = await dbContext.MaintenancePlans
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanCode.StartsWith("PM-WH-"))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var expectedPlans = WorldHistoryDeviceSpec.BuildMaintenancePlans();
        if (seedPlanIds.Length != expectedPlans.Count)
        {
            throw Fail($"maintenance plan count mismatch: expected {expectedPlans.Count} but found {seedPlanIds.Length}.");
        }

        var expected = WorldHistoryDeviceSpec.BuildInspections(asOfDate).Count;
        var actual = await dbContext.MaintenanceInspections
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanId != null && seedPlanIds.Contains(x.PlanId))
            .CountAsync(cancellationToken);
        if (actual != expected)
        {
            throw Fail($"inspection count mismatch: expected {expected} (plan cadence) but found {actual}.");
        }

        return actual;
    }

    private async Task ValidatePlanCursorsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var overduePlans = await dbContext.MaintenancePlans
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.PlanCode.StartsWith("PM-WH-"))
            .Where(x => x.NextDueOn != null && x.NextDueOn <= asOfDate)
            .Select(x => x.PlanCode)
            .ToArrayAsync(cancellationToken);
        if (overduePlans.Length > 0)
        {
            throw Fail($"{overduePlans.Length} seeded plans still have overdue calendar cursors "
                + $"(catch-up storm risk), e.g. '{overduePlans[0]}'.");
        }
    }

    private async Task ValidateDowntimeReasonsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var codes = WorldHistoryDeviceSpec.DowntimeReasons.Select(x => x.Code).ToArray();
        var found = await dbContext.DowntimeReasons
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && codes.Contains(x.ReasonCode))
            .CountAsync(cancellationToken);
        if (found != codes.Length)
        {
            throw Fail($"downtime reason catalog incomplete: expected {codes.Length} codes but found {found}.");
        }
    }

    private static InvalidOperationException Fail(string message) =>
        new($"World-history maintenance seed validation failed: {message}");
}

/// <summary>Maintenance 侧校验结论（写入启动日志与 PR 实测表）。</summary>
public sealed record WorldHistoryMaintenanceValidationReport(
    int WorkOrdersChecked,
    int OpenWorkOrders,
    int InspectionsChecked,
    int CompletedDowntimeMinutes,
    int SparePartLinesChecked,
    int DeviceStatesChecked,
    IReadOnlyList<string> Sample);
