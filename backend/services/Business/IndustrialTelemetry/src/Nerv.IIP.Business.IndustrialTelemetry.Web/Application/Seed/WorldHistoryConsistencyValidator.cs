using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

/// <summary>
/// L1 设备域历史（IndustrialTelemetry 侧）的 fail-closed 一致性校验器（设定集 §7）：
/// 1. 报警事实与共享报警计划逐条对上（数量、raised/cleared 状态、时间戳、severity）；
/// 2. 遥测日级聚合的行数与「工作日历 × 班次覆盖」的期望完全一致（周日/春节无生产遥测，辅助设备 7×24）；
/// 3. 每起带停机的报警在设备状态史里有对应的 <c>faulted</c> 段（OEE 可用率的输入）；
/// 4. OEE 产量事实按设备唯一理论节拍、数量为正；
/// 5. 抽样 20 起报警的全链（报警 → 状态 → 遥测越限窗口）人工可追，写入日志样本；
/// 6.（四期）设备控制指令台账与 <see cref="WorldHistoryControlCommandSpec"/> 逐条一致，
///    且**没有一条处于会触发下发的待执行态**（queued / approval-pending / dispatched）——
///    历史指令一律是 completed / failed / rejected 且带 FinishedAtUtc，
///    审批态不得停在 pending。这是安全条款：留一条待执行的历史指令，就等于在演示环境里
///    埋了一次真实写点的引信（理由见 <see cref="WorldHistoryControlCommandSpec"/>）。
/// 校验只认本引擎号段（<c>seed:world-history</c> / <c>WH-*</c>），与 L3 实时流互不干扰。
/// 任何一条不满足直接抛 <see cref="InvalidOperationException"/>，宁可启动失败也不放账不平的历史进演示环境。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    private const string SequencePrefix = "seed:world-history";

    public async Task<WorldHistoryDeviceValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var alarmPlans = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, scale);
        var alarmsChecked = await ValidateAlarmsAsync(organizationId, environmentId, alarmPlans, cancellationToken);
        var dailyRollupsChecked = await ValidateDailyRollupsAsync(organizationId, environmentId, asOfDate, cancellationToken);
        var statesChecked = await ValidateFaultedStatesAsync(organizationId, environmentId, alarmPlans, cancellationToken);
        var oeeFactsChecked = await ValidateOeeFactsAsync(organizationId, environmentId, cancellationToken);
        var controlCommandsChecked = await ValidateControlCommandsAsync(
            organizationId, environmentId, asOfDate, scale, cancellationToken);
        var sample = BuildSample(alarmPlans);

        return new WorldHistoryDeviceValidationReport(
            alarmsChecked,
            alarmPlans.Count(x => x.IsOpenAtAsOf),
            dailyRollupsChecked,
            statesChecked,
            oeeFactsChecked,
            controlCommandsChecked,
            sample);
    }

    /// <summary>
    /// 6) 控制指令台账逐条对账 + **终态硬条款**。
    ///
    /// 「没有会触发下发的待执行指令」在这里 fail-closed，而不是靠生成端自觉：生成端的配额将来
    /// 若被改坏，也必须在启动时就炸掉，绝不能把一条待审批的历史指令放进演示环境。
    /// </summary>
    private async Task<int> ValidateControlCommandsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var plans = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, scale);
        var seeded = await dbContext.DeviceControlCommands
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.OperationTaskId.StartsWith(WorldHistoryControlCommandSpec.OperationTaskPrefix))
            .Select(x => new
            {
                x.OperationTaskId,
                x.DeviceAssetId,
                x.CommandType,
                x.Status,
                x.ApprovalStatus,
                x.RequestedAtUtc,
                x.FinishedAtUtc,
            })
            .ToArrayAsync(cancellationToken);
        if (seeded.Length != plans.Count)
        {
            throw Fail($"device control command count mismatch: expected {plans.Count} but found {seeded.Length}.");
        }

        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate, TimeOnly.MaxValue, TimeSpan.Zero);
        var byTaskId = seeded.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            if (!byTaskId.TryGetValue(plan.OperationTaskId, out var command))
            {
                throw Fail($"device control command '{plan.OperationTaskId}' is missing.");
            }

            if (command.DeviceAssetId != plan.DeviceAssetId || command.CommandType != plan.CommandType)
            {
                throw Fail($"device control command '{plan.OperationTaskId}' does not match the deterministic spec.");
            }

            if (command.Status != plan.TerminalStatus || command.ApprovalStatus != plan.ExpectedApprovalStatus)
            {
                throw Fail($"device control command '{plan.OperationTaskId}' has status "
                    + $"'{command.Status}'/'{command.ApprovalStatus}' but the plan expects "
                    + $"'{plan.TerminalStatus}'/'{plan.ExpectedApprovalStatus}'.");
            }

            if (command.RequestedAtUtc < lowerBound || command.RequestedAtUtc > upperBound)
            {
                throw Fail($"device control command '{plan.OperationTaskId}' was requested at "
                    + $"{command.RequestedAtUtc:O}, outside the history window.");
            }
        }

        // 安全条款：终态齐全、无待执行态残留。
        foreach (var command in seeded)
        {
            if (WorldHistoryControlCommandSpec.PendingDispatchStatuses.Contains(command.Status, StringComparer.Ordinal))
            {
                throw Fail($"device control command '{command.OperationTaskId}' is still pending dispatch "
                    + $"(status '{command.Status}'); history must never leave a command that could actually fire.");
            }

            if (!WorldHistoryControlCommandSpec.TerminalStatuses.Contains(command.Status, StringComparer.Ordinal))
            {
                throw Fail($"device control command '{command.OperationTaskId}' has non-terminal status '{command.Status}'.");
            }

            if (command.FinishedAtUtc is null || command.FinishedAtUtc < command.RequestedAtUtc)
            {
                throw Fail($"device control command '{command.OperationTaskId}' has no monotonic finish time.");
            }

            if (string.Equals(command.ApprovalStatus, "pending", StringComparison.OrdinalIgnoreCase))
            {
                throw Fail($"device control command '{command.OperationTaskId}' still awaits approval; "
                    + "an approval decision on it would dispatch to the device.");
            }
        }

        return seeded.Length;
    }

    private async Task<int> ValidateAlarmsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var seeded = await dbContext.AlarmEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.ExternalAlarmId.StartsWith("WH-") && x.ExternalAlarmId.Contains(":"))
            .Select(x => new { x.ExternalAlarmId, x.DeviceAssetId, x.Severity, x.Status, x.RaisedAtUtc, x.ClearedAtUtc })
            .ToArrayAsync(cancellationToken);
        if (seeded.Length != alarmPlans.Count)
        {
            throw Fail($"alarm-event count mismatch: expected {alarmPlans.Count} but found {seeded.Length}.");
        }

        var byExternalId = seeded.ToDictionary(x => x.ExternalAlarmId, StringComparer.Ordinal);
        foreach (var plan in alarmPlans)
        {
            if (!byExternalId.TryGetValue(plan.ExternalAlarmId, out var alarm))
            {
                throw Fail($"alarm '{plan.ExternalAlarmId}' is missing.");
            }

            if (alarm.DeviceAssetId != plan.DeviceAssetId
                || alarm.Severity != plan.Severity
                || alarm.RaisedAtUtc != plan.RaisedAtUtc)
            {
                throw Fail($"alarm '{plan.ExternalAlarmId}' does not match the shared plan.");
            }

            // 开放尾部（IsOpenAtAsOf）不断言状态：运行时规则评估把回填号段视作本规则报警
            // （IsAlarmForRule 前缀匹配），设备实时恢复后会以 return-to-normal / 抑制清除它们——
            // 这是期望的闭环。非开放计划由 seed 的 catch-up 保证 cleared。
            if (!plan.IsOpenAtAsOf)
            {
                if (alarm.Status != "cleared")
                {
                    throw Fail($"alarm '{plan.ExternalAlarmId}' has status '{alarm.Status}' but the plan expects 'cleared'.");
                }

                if (alarm.ClearedAtUtc is null || alarm.ClearedAtUtc < alarm.RaisedAtUtc)
                {
                    throw Fail($"alarm '{plan.ExternalAlarmId}' has a non-monotonic clear time.");
                }
            }
        }

        return seeded.Length;
    }

    private async Task<int> ValidateDailyRollupsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var expected = 0;
        foreach (var device in WorldHistoryDeviceSpec.Devices)
        {
            var perDeviceDays = 0;
            for (var day = WorldHistoryCalendar.GoLiveDate; day < asOfDate; day = day.AddDays(1))
            {
                var dayStart = new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero);
                if (WorldHistoryDeviceSpec.ActiveMinutes(device, dayStart, dayStart.AddDays(1)) > 0)
                {
                    perDeviceDays++;
                }
            }

            expected += perDeviceDays * device.Class.Tags.Length;
        }

        var actual = await dbContext.TelemetryRollups
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.Grain == TelemetryRollupGrain.Daily)
            .Where(x => x.SourceSequence.StartsWith(SequencePrefix))
            .CountAsync(cancellationToken);
        if (actual != expected)
        {
            throw Fail($"daily rollup count mismatch: expected {expected} (working-calendar shape) but found {actual}.");
        }

        // 周日不该有任何非辅助设备的生产遥测（辅助设备 7×24 属例外）。
        var sundayViolations = await dbContext.TelemetryRollups
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.Grain == TelemetryRollupGrain.Daily)
            .Where(x => x.SourceSequence.StartsWith(SequencePrefix))
            .Where(x => !x.DeviceAssetId.StartsWith("DEV-AUX-"))
            .Select(x => x.WindowStartUtc)
            .ToArrayAsync(cancellationToken);
        var sundayCount = sundayViolations.Count(x => x.DayOfWeek == DayOfWeek.Sunday);
        if (sundayCount > 0)
        {
            throw Fail($"{sundayCount} production daily rollups fall on Sundays (calendar says the plant is down).");
        }

        return actual;
    }

    private async Task<int> ValidateFaultedStatesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans,
        CancellationToken cancellationToken)
    {
        var downtimeAlarms = alarmPlans.Where(x => x.HasWorkOrder).ToArray();
        var checkedCount = 0;
        foreach (var alarm in downtimeAlarms)
        {
            var exists = await dbContext.DeviceStateSnapshots
                .AsNoTracking()
                .AnyAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                    && x.DeviceAssetId == alarm.DeviceAssetId
                    && x.State == "faulted"
                    && x.OccurredAtUtc == alarm.RaisedAtUtc,
                    cancellationToken);
            if (!exists)
            {
                throw Fail($"downtime alarm '{alarm.ExternalAlarmId}' has no matching 'faulted' state snapshot.");
            }

            checkedCount++;
        }

        return checkedCount;
    }

    private async Task<int> ValidateOeeFactsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var perDevice = await dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Where(x => x.SourceReportNo.StartsWith("RPT-WH-"))
            .GroupBy(x => x.DeviceAssetId)
            .Select(x => new
            {
                DeviceAssetId = x.Key,
                Count = x.Count(),
                DistinctRates = x.Select(f => f.TheoreticalRatePerHour).Distinct().Count(),
                NonPositive = x.Count(f => f.GoodQuantity <= 0m),
            })
            .ToArrayAsync(cancellationToken);

        var productionDevices = WorldHistoryDeviceSpec.Devices.Count(x => x.Class.TheoreticalRatePerHour is not null);
        if (perDevice.Length != productionDevices)
        {
            throw Fail($"OEE facts cover {perDevice.Length} devices but {productionDevices} production devices are expected.");
        }

        foreach (var device in perDevice)
        {
            if (device.DistinctRates != 1)
            {
                throw Fail($"device '{device.DeviceAssetId}' has {device.DistinctRates} theoretical rates; OEE needs exactly one.");
            }

            if (device.NonPositive > 0)
            {
                throw Fail($"device '{device.DeviceAssetId}' has {device.NonPositive} OEE facts with non-positive good quantity.");
            }
        }

        return perDevice.Sum(x => x.Count);
    }

    private static IReadOnlyList<string> BuildSample(IReadOnlyList<WorldHistoryAlarmPlan> alarmPlans)
    {
        return alarmPlans
            .Where(x => x.HasWorkOrder)
            .Take(20)
            .Select(x => $"{x.ExternalAlarmId} → {x.DeviceAssetId}/{x.TagKey} {x.Severity} "
                + $"raised {x.RaisedAtUtc:yyyy-MM-dd HH:mm}Z, downtime {x.DowntimeMinutes} min, "
                + (x.WorkOrderNo is null ? "no work order" : $"work order {x.WorkOrderNo}")
                + (x.IsOpenAtAsOf ? " (still raised)" : string.Empty))
            .ToArray();
    }

    private static InvalidOperationException Fail(string message) =>
        new($"World-history device seed validation failed: {message}");
}

/// <summary>IndustrialTelemetry 侧校验结论（写入启动日志与 PR 实测表）。</summary>
public sealed record WorldHistoryDeviceValidationReport(
    int AlarmsChecked,
    int OpenAlarms,
    int DailyRollupsChecked,
    int FaultedStatesChecked,
    int OeeFactsChecked,
    int DeviceControlCommandsChecked,
    IReadOnlyList<string> Sample);
