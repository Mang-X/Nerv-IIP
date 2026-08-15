using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>
/// 物料约束口径的配置解析。非法值绝不静默回落——回落方向恰好更宽松(缺料照排),
/// 会让一个拼错的配置看起来"生效了",与本服务其它配置一样在启动期直接失败。
/// </summary>
public static class SchedulingMaterialConstraintModeResolver
{
    public const string ConfigurationKey = "Scheduling:MaterialConstraintMode";

    public static SchedulingMaterialConstraintModeContract Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return SchedulingMaterialConstraintModeContract.Soft;
        }

        if (Enum.TryParse<SchedulingMaterialConstraintModeContract>(
                configuredValue.Trim(),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey}='{configuredValue}' 不是合法的物料约束口径。合法值:" +
            $"{string.Join(" / ", Enum.GetNames<SchedulingMaterialConstraintModeContract>())};留空即默认 " +
            $"{SchedulingMaterialConstraintModeContract.Soft}(缺料可排 + 物料风险标记)。");
    }
}

/// <summary>
/// 设备「状态未知」口径(DI 用的显式载体,避免把裸枚举注册进容器)。
/// </summary>
public sealed record SchedulingEquipmentUnknownModeOption(SchedulingEquipmentUnknownModeContract Mode)
{
    public static SchedulingEquipmentUnknownModeOption Default { get; } =
        new(SchedulingEquipmentUnknownModeContract.Soft);
}

/// <summary>
/// 设备「状态未知」口径的配置解析。与物料口径同样绝不静默回落——回落方向同样更宽松。
/// </summary>
public static class SchedulingEquipmentUnknownModeResolver
{
    public const string ConfigurationKey = "Scheduling:EquipmentUnknownMode";

    public static SchedulingEquipmentUnknownModeContract Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return SchedulingEquipmentUnknownModeContract.Soft;
        }

        if (Enum.TryParse<SchedulingEquipmentUnknownModeContract>(
                configuredValue.Trim(),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey}='{configuredValue}' 不是合法的设备状态未知口径。合法值:" +
            $"{string.Join(" / ", Enum.GetNames<SchedulingEquipmentUnknownModeContract>())};留空即默认 " +
            $"{SchedulingEquipmentUnknownModeContract.Soft}(状态未知可排 + 设备数据风险标记)。" +
            "真实停机/维护窗口在两种口径下都是硬阻。");
    }
}

/// <summary>
/// 质检口径的配置解析。与物料/设备两个口径同样绝不静默回落——回落方向同样更宽松。
/// </summary>
public static class SchedulingQualityConstraintModeResolver
{
    public const string ConfigurationKey = "Scheduling:QualityConstraintMode";

    public static SchedulingQualityConstraintModeContract Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return SchedulingQualityConstraintModeContract.Soft;
        }

        if (Enum.TryParse<SchedulingQualityConstraintModeContract>(
                configuredValue.Trim(),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey}='{configuredValue}' 不是合法的质检约束口径。合法值:" +
            $"{string.Join(" / ", Enum.GetNames<SchedulingQualityConstraintModeContract>())};留空即默认 " +
            $"{SchedulingQualityConstraintModeContract.Soft}(带质检标记的工序照排 + 预警级冲突)。" +
            "真实下达的质量封锁在两种口径下都是硬阻。");
    }
}

/// <summary>
/// APS lite 有限产能排程器。
/// 物料口径按产品裁决走软约束(默认):缺料工单照排,只在计划里带出「物料风险」,
/// 由 MES 侧的线边齐套硬门在开工时拦截。<see cref="SchedulingMaterialConstraintModeContract.Hard"/>
/// 保留旧的「缺料即不可排」行为,供需要严格口径的环境按配置切回。
/// </summary>
public sealed class FiniteCapacityScheduler(
    SchedulingMaterialConstraintModeContract materialConstraintMode = SchedulingMaterialConstraintModeContract.Soft,
    SchedulingQualityConstraintModeContract qualityConstraintMode = SchedulingQualityConstraintModeContract.Soft)
{
    public const string AlgorithmVersion = "aps-lite-v1";

    public SchedulingMaterialConstraintModeContract MaterialConstraintMode { get; } = materialConstraintMode;

    public SchedulingQualityConstraintModeContract QualityConstraintMode { get; } = qualityConstraintMode;

    public SchedulePlanContract Schedule(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return ScheduleNormalized(SchedulingProblemNormalizer.Normalize(problem), planId, generatedAtUtc);
    }

    internal SchedulePlanContract ScheduleNormalized(
        SchedulingProblemContract normalizedProblem,
        string planId,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(normalizedProblem);

        var state = SchedulerState.From(normalizedProblem, planId, generatedAtUtc, MaterialConstraintMode, QualityConstraintMode);
        state.ReserveLockedAssignments();
        state.ScheduleOpenOperations();
        return state.ToPlan();
    }
}

internal static class SchedulingProblemNormalizer
{
    public static SchedulingProblemContract Normalize(SchedulingProblemContract problem)
    {
        Validate(problem);

        return problem with
        {
            Orders = problem.Orders
                .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .Select(x => x with
                {
                    Operations = x.Operations
                        .OrderBy(y => y.OperationSequence)
                        .ThenBy(y => y.OperationId, StringComparer.Ordinal)
                        .Select(y => y with
                        {
                            PredecessorOperationIds = y.PredecessorOperationIds
                                .OrderBy(id => id, StringComparer.Ordinal)
                                .ToArray(),
                            EligibleResourceIds = y.EligibleResourceIds
                                .OrderBy(id => id, StringComparer.Ordinal)
                                .ToArray(),
                            RequiredSkillCodes = (y.RequiredSkillCodes ?? [])
                                .OrderBy(id => id, StringComparer.Ordinal)
                                .ToArray(),
                            RequiredToolingIds = (y.RequiredToolingIds ?? [])
                                .OrderBy(id => id, StringComparer.Ordinal)
                                .ToArray()
                        })
                        .ToArray()
                })
                .ToArray(),
            Resources = problem.Resources
                .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                .Select(x => x with
                {
                    CapabilityCodes = x.CapabilityCodes
                        .OrderBy(code => code, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray(),
            Calendars = problem.Calendars
                .OrderBy(x => x.CalendarId, StringComparer.Ordinal)
                .Select(x => x with
                {
                    ShiftWindows = x.ShiftWindows
                        .OrderBy(y => y.StartUtc)
                        .ThenBy(y => y.EndUtc)
                        .ThenBy(y => y.ReasonCode, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray(),
            UnavailabilityWindows = problem.UnavailabilityWindows
                .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
                .ThenBy(x => x.StartUtc)
                .ThenBy(x => x.EndUtc)
                .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
                .ToArray(),
            MaterialReadiness = problem.MaterialReadiness
                .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
                .ThenBy(x => x.MaterialReadyUtc)
                .ThenBy(x => x.IsReady)
                .Select(x => x with
                {
                    ReasonCodes = x.ReasonCodes
                        .OrderBy(code => code, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray(),
            QualityBlocks = problem.QualityBlocks
                .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeId, StringComparer.Ordinal)
                .ThenBy(x => x.BlockedUntilUtc)
                .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
                .ToArray(),
            LockedAssignments = problem.LockedAssignments
                .OrderBy(x => x.StartUtc)
                .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                .ThenBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationSequence)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .ThenBy(x => x.AssignmentId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static IReadOnlyCollection<string> ValidateForErrors(SchedulingProblemContract? problem)
    {
        if (problem is null)
        {
            return ["Problem is required."];
        }

        try
        {
            Validate(problem);
            return [];
        }
        catch (ArgumentException exception)
        {
            return [exception.Message];
        }
    }

    private static void Validate(SchedulingProblemContract problem)
    {
        RequireCollection(problem.Orders, "Orders", nameof(problem));
        RequireCollection(problem.Resources, "Resources", nameof(problem));
        RequireCollection(problem.Calendars, "Calendars", nameof(problem));
        RequireCollection(problem.UnavailabilityWindows, "UnavailabilityWindows", nameof(problem));
        RequireCollection(problem.MaterialReadiness, "MaterialReadiness", nameof(problem));
        RequireCollection(problem.QualityBlocks, "QualityBlocks", nameof(problem));
        RequireCollection(problem.LockedAssignments, "LockedAssignments", nameof(problem));

        if (string.IsNullOrWhiteSpace(problem.ProblemId))
        {
            throw new ArgumentException("ProblemId is required.", nameof(problem));
        }

        if (problem.HorizonEndUtc <= problem.HorizonStartUtc)
        {
            throw new ArgumentException("HorizonEndUtc must be greater than HorizonStartUtc.", nameof(problem));
        }

        ThrowIfDuplicate(problem.Resources, x => x.ResourceId, "resourceId", nameof(problem));
        ThrowIfDuplicate(problem.Calendars, x => x.CalendarId, "calendarId", nameof(problem));

        foreach (var calendar in problem.Calendars)
        {
            RequireNonEmpty(calendar.CalendarId, "calendarId", nameof(problem));
            RequireCollection(calendar.ShiftWindows, "ShiftWindows", nameof(problem));
            foreach (var shift in calendar.ShiftWindows)
            {
                RequireValidWindow(shift.StartUtc, shift.EndUtc, "calendar shift", nameof(problem));
            }
        }

        foreach (var resource in problem.Resources)
        {
            RequireNonEmpty(resource.ResourceId, "resourceId", nameof(problem));
            RequireNonEmpty(resource.CalendarId, "resource calendarId", nameof(problem));
            RequireCollection(resource.CapabilityCodes, "CapabilityCodes", nameof(problem));
        }

        foreach (var order in problem.Orders)
        {
            RequireNonEmpty(order.OrderId, "orderId", nameof(problem));
            RequireCollection(order.Operations, "Operations", nameof(problem));
            ThrowIfDuplicate(order.Operations, x => x.OperationId, $"operationId in order {order.OrderId}", nameof(problem));
            foreach (var operation in order.Operations)
            {
                RequireNonEmpty(operation.OperationId, "operationId", nameof(problem));
                RequireCollection(operation.PredecessorOperationIds, "PredecessorOperationIds", nameof(problem));
                RequireCollection(operation.EligibleResourceIds, "EligibleResourceIds", nameof(problem));
                if (operation.DurationMinutes <= 0)
                {
                    throw new ArgumentException(
                        $"DurationMinutes must be greater than zero for orderId '{order.OrderId}', operationId '{operation.OperationId}'.",
                        nameof(problem));
                }

                if (operation.SetupMinutes < 0)
                {
                    throw new ArgumentException(
                        $"SetupMinutes cannot be negative for orderId '{order.OrderId}', operationId '{operation.OperationId}'.",
                        nameof(problem));
                }
            }
        }

        foreach (var materialReadiness in problem.MaterialReadiness)
        {
            RequireCollection(materialReadiness.ReasonCodes, "ReasonCodes", nameof(problem));
        }

        foreach (var window in problem.UnavailabilityWindows)
        {
            RequireValidWindow(window.StartUtc, window.EndUtc, "unavailability window", nameof(problem));
        }

        foreach (var locked in problem.LockedAssignments)
        {
            RequireNonEmpty(locked.AssignmentId, "locked assignmentId", nameof(problem));
            RequireNonEmpty(locked.ResourceId, "locked resourceId", nameof(problem));
            RequireValidWindow(locked.StartUtc, locked.EndUtc, "locked assignment", nameof(problem));
        }
    }

    private static void RequireCollection<T>(IReadOnlyCollection<T>? values, string label, string paramName)
    {
        if (values is null)
        {
            throw new ArgumentException($"{label} is required.", paramName);
        }
    }

    private static void ThrowIfDuplicate<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector,
        string label,
        string paramName)
    {
        var duplicates = items
            .GroupBy(keySelector, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
        if (duplicates.Count != 0)
        {
            throw new ArgumentException($"Duplicate {label} values are not allowed: {string.Join(",", duplicates)}.", paramName);
        }
    }

    private static void RequireNonEmpty(string value, string label, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", paramName);
        }
    }

    private static void RequireValidWindow(DateTimeOffset startUtc, DateTimeOffset endUtc, string label, string paramName)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException($"{label} end must be greater than start.", paramName);
        }
    }
}

file sealed class SchedulerState
{
    private readonly SchedulingProblemContract problem;
    private readonly string planId;
    private readonly DateTimeOffset generatedAtUtc;
    private readonly Dictionary<string, SchedulingResourceContract> resources;
    private readonly Dictionary<string, SchedulingCalendarContract> calendars;
    private readonly Dictionary<string, IReadOnlyList<SchedulingTimeWindowContract>> continuousWindowsByCalendar = new(StringComparer.Ordinal);
    private readonly Dictionary<OperationKey, SchedulingOperationContract> operationByKey;
    private readonly List<ScheduleAssignmentContract> assignments = [];
    private readonly List<ScheduleConflictContract> conflicts = [];
    private readonly List<UnscheduledOperationContract> unscheduledOperations = [];
    private readonly List<ScheduleChangeContract> changeSummary = [];
    private readonly HashSet<OperationKey> failedOperationKeys = [];
    private readonly List<SchedulePlanMaterialRiskContract> materialRisks = [];
    private readonly List<SchedulePlanEquipmentRiskContract> equipmentRisks = [];
    private readonly SchedulingMaterialConstraintModeContract materialConstraintMode;
    private readonly SchedulingQualityConstraintModeContract qualityConstraintMode;
    private IReadOnlyCollection<ResourceOccupancy>? resourceOccupancyCache;
    private int conflictNumber;

    private SchedulerState(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc,
        SchedulingMaterialConstraintModeContract materialConstraintMode,
        SchedulingQualityConstraintModeContract qualityConstraintMode)
    {
        this.problem = problem;
        this.planId = planId;
        this.generatedAtUtc = generatedAtUtc;
        this.materialConstraintMode = materialConstraintMode;
        this.qualityConstraintMode = qualityConstraintMode;
        resources = problem.Resources.ToDictionary(x => x.ResourceId, StringComparer.Ordinal);
        calendars = problem.Calendars.ToDictionary(x => x.CalendarId, StringComparer.Ordinal);
        operationByKey = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => (
                Key: new OperationKey(order.OrderId, operation.OperationId),
                Operation: operation)))
            .ToDictionary(x => x.Key, x => x.Operation);
    }

    public static SchedulerState From(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc,
        SchedulingMaterialConstraintModeContract materialConstraintMode,
        SchedulingQualityConstraintModeContract qualityConstraintMode)
    {
        return new SchedulerState(problem, planId, generatedAtUtc, materialConstraintMode, qualityConstraintMode);
    }

    public void ReserveLockedAssignments()
    {
        foreach (var locked in problem.LockedAssignments
                     .OrderBy(x => x.StartUtc)
                     .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            var hasResource = resources.TryGetValue(locked.ResourceId, out var resource);
            var invalidLock = !hasResource
                || locked.StartUtc < problem.HorizonStartUtc
                || locked.EndUtc > problem.HorizonEndUtc
                || !IsInsideCalendar(resource!, locked.StartUtc, locked.EndUtc)
                || IsUnavailable(resource!, locked.StartUtc, locked.EndUtc);

            var assignment = new ScheduleAssignmentContract(
                AssignmentId: locked.AssignmentId,
                OrderId: locked.OrderId,
                OperationId: locked.OperationId,
                OperationSequence: locked.OperationSequence,
                ResourceId: locked.ResourceId,
                WorkCenterId: locked.WorkCenterId,
                StartUtc: locked.StartUtc,
                EndUtc: locked.EndUtc,
                IsLocked: true,
                ExplanationCode: locked.LockReasonCode);
            AddAssignment(assignment);
            changeSummary.Add(new ScheduleChangeContract(
                locked.OrderId,
                locked.OperationId,
                ScheduleChangeTypeContract.Preserved,
                "锁定工序已按原计划保留，未参与本次重排。"));

            if (invalidLock)
            {
                AddConflict(
                    ScheduleConflictReasonCodeContract.InvalidLockedAssignment,
                    ScheduleConflictSeverityContract.Error,
                    locked.OrderId,
                    locked.OperationId,
                    locked.ResourceId,
                    "锁定工序落在排程窗口、班次日历或可用资源之外，无法保留。");
            }

            // 锁定工序同样带设备数据风险:它已经占住这台设备的时段,状态盲区一样要提示。
            AddEquipmentRisk(
                locked.OrderId,
                locked.OperationId,
                locked.ResourceId,
                ApplicableEquipmentDataRisks(locked.ResourceId, locked.StartUtc, locked.EndUtc));

            // 锁定工序同样吃软约束语义:它已经占住计划位置,缺料照样得在开工前补齐,
            // 否则 MES 齐套硬门会在开工时拦下一个"看起来已排好"的工序。
            var lockedOrder = problem.Orders
                .FirstOrDefault(x => string.Equals(x.OrderId, locked.OrderId, StringComparison.Ordinal));
            if (materialConstraintMode == SchedulingMaterialConstraintModeContract.Soft
                && lockedOrder is not null
                && operationByKey.TryGetValue(
                    new OperationKey(locked.OrderId, locked.OperationId),
                    out var lockedOperation))
            {
                var lockedItem = new OperationWorkItem(lockedOrder, lockedOperation);
                var lockedMaterialBlocks = ApplicableOpenEndedMaterialBlocks(lockedItem).ToList();
                if (lockedMaterialBlocks.Count > 0)
                {
                    AddMaterialRisk(lockedItem, lockedMaterialBlocks);
                }
            }
        }

        ReportLockedCapacityConflicts();
    }

    public void ScheduleOpenOperations()
    {
        var operations = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => new OperationWorkItem(order, operation)))
            .OrderByDescending(x => x.Operation.IsRush)
            .ThenByDescending(x => x.Operation.Priority)
            .ThenBy(x => x.Operation.DueUtc)
            .ThenBy(x => x.Order.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation.OperationSequence)
            .ThenBy(x => x.Operation.OperationId, StringComparer.Ordinal)
            .ToList();

        var scheduledOperationKeys = new HashSet<OperationKey>(
            assignments.Select(OperationKey.From));
        var remaining = new Queue<OperationWorkItem>(operations);
        var stalledItems = new List<OperationWorkItem>();

        while (remaining.Count > 0)
        {
            var item = remaining.Dequeue();
            var itemKey = OperationKey.From(item);
            if (scheduledOperationKeys.Contains(itemKey) || failedOperationKeys.Contains(itemKey))
            {
                continue;
            }

            var predecessorKeys = item.Operation.PredecessorOperationIds
                .Select(id => new OperationKey(item.Order.OrderId, id))
                .ToList();
            if (predecessorKeys.Any(failedOperationKeys.Contains))
            {
                AddUnscheduled(
                    item,
                    ScheduleConflictReasonCodeContract.PredecessorUnscheduled,
                    "前序工序未能排入本次计划，本工序只能顺延等待。");
                continue;
            }

            if (predecessorKeys.Any(x => !scheduledOperationKeys.Contains(x)))
            {
                stalledItems.Add(item);
                if (remaining.Count > 0)
                {
                    continue;
                }

                if (stalledItems.Count == 0)
                {
                    continue;
                }

                if (stalledItems.Count == operations.Count(x =>
                        !scheduledOperationKeys.Contains(OperationKey.From(x))
                        && !failedOperationKeys.Contains(OperationKey.From(x))))
                {
                    foreach (var stalled in stalledItems)
                    {
                        AddUnscheduled(
                            stalled,
                            ScheduleConflictReasonCodeContract.PredecessorUnscheduled,
                            "前序工序未能排入本次计划，本工序只能顺延等待。");
                    }
                    break;
                }

                foreach (var stalled in stalledItems)
                {
                    remaining.Enqueue(stalled);
                }
                stalledItems.Clear();
                continue;
            }

            var result = TrySchedule(item);
            if (result is null)
            {
                if (remaining.Count == 0 && stalledItems.Count > 0)
                {
                    foreach (var stalled in stalledItems)
                    {
                        remaining.Enqueue(stalled);
                    }
                    stalledItems.Clear();
                }

                continue;
            }

            AddAssignment(result);
            scheduledOperationKeys.Add(OperationKey.From(result));
            changeSummary.Add(new ScheduleChangeContract(
                result.OrderId,
                result.OperationId,
                result.EndUtc > item.Operation.DueUtc ? ScheduleChangeTypeContract.Delayed : ScheduleChangeTypeContract.Added,
                result.EndUtc > item.Operation.DueUtc
                    ? "排入时间晚于交期，将造成延期。"
                    : "已按有限产能排入计划。"));

            if (result.EndUtc > item.Operation.DueUtc)
            {
                AddConflict(
                    ScheduleConflictReasonCodeContract.DueDate,
                    ScheduleConflictSeverityContract.Warning,
                    result.OrderId,
                    result.OperationId,
                    result.ResourceId,
                    "排入时间晚于交期，将造成延期。");
            }

            if (remaining.Count == 0 && stalledItems.Count > 0)
            {
                foreach (var stalled in stalledItems)
                {
                    remaining.Enqueue(stalled);
                }
                stalledItems.Clear();
            }
        }
    }

    public SchedulePlanContract ToPlan()
    {
        var orderedAssignments = assignments
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .ToList();
        var conflictByOperation = conflicts
            .Where(x => x.OperationId is not null)
            .GroupBy(x => new OperationKey(x.OrderId ?? string.Empty, x.OperationId!))
            .ToDictionary(x => x.Key, x => x.First().ReasonCode);

        var resourceOccupancies = BuildResourceOccupancies(orderedAssignments);
        var resourceLoads = BuildResourceLoads(resourceOccupancies);
        var orderedMaterialRisks = materialRisks
            .OrderBy(x => x.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .ToList();
        var materialRiskKeys = orderedMaterialRisks
            .Select(x => new OperationKey(x.OrderId, x.OperationId))
            .ToHashSet();
        var orderedEquipmentRisks = equipmentRisks
            .OrderBy(x => x.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .ToList();
        var equipmentRiskKeys = orderedEquipmentRisks
            .Select(x => new OperationKey(x.OrderId, x.OperationId))
            .ToHashSet();

        return new SchedulePlanContract(
            ContractVersion: problem.ContractVersion,
            PlanId: planId,
            ProblemId: problem.ProblemId,
            ProblemFingerprint: Fingerprint(problem),
            AlgorithmVersion: FiniteCapacityScheduler.AlgorithmVersion,
            Status: SchedulePlanStatusContract.Preview,
            GeneratedAtUtc: generatedAtUtc,
            Metrics: BuildMetrics(
                orderedAssignments,
                resourceOccupancies,
                resourceLoads,
                materialRiskKeys.Count,
                equipmentRiskKeys.Count),
            Assignments: orderedAssignments,
            ResourceLoads: resourceLoads,
            Conflicts: conflicts,
            UnscheduledOperations: unscheduledOperations,
            ChangeSummary: changeSummary,
            GanttItems: orderedAssignments.Select(x =>
            {
                var operationKey = OperationKey.From(x);
                return new GanttScheduleItemContract(
                    ItemId: $"gantt-{x.AssignmentId}",
                    OrderId: x.OrderId,
                    OperationId: x.OperationId,
                    OperationSequence: x.OperationSequence,
                    ResourceId: x.ResourceId,
                    WorkCenterId: x.WorkCenterId,
                    StartUtc: x.StartUtc,
                    EndUtc: x.EndUtc,
                    Status: SchedulePlanStatusContract.Preview,
                    HasConflict: conflictByOperation.ContainsKey(operationKey),
                    ConflictReasonCode: conflictByOperation.GetValueOrDefault(operationKey),
                    HasMaterialRisk: materialRiskKeys.Contains(operationKey),
                    HasEquipmentRisk: equipmentRiskKeys.Contains(operationKey));
            }).ToList(),
            // 工作日历与不可用窗口是排程输入的一部分,随计划一起带出:读面据此画工作日/非工作日、
            // 班次边界与维护/停机/换线/换型底纹,不必再单独取一次日历。
            Calendars: SchedulePlanCalendarProjector.ProjectCalendars(problem),
            BlockWindows: SchedulePlanCalendarProjector.ProjectBlockWindows(problem),
            // 物料软约束的产物:这些工序已排入计划,但开工前必须先备料。
            MaterialRisks: orderedMaterialRisks,
            // 设备软约束的产物:这些工序排在状态未知的设备上,开工前需人工确认设备可用。
            EquipmentRisks: orderedEquipmentRisks);
    }

    private SchedulePlanMetricsContract BuildMetrics(
        IReadOnlyCollection<ScheduleAssignmentContract> orderedAssignments,
        IReadOnlyCollection<ResourceOccupancy> resourceOccupancies,
        IReadOnlyCollection<ScheduleResourceLoadContract> resourceLoads,
        int materialRiskOperationCount,
        int equipmentRiskOperationCount)
    {
        var dueByOperation = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => (
                Key: new OperationKey(order.OrderId, operation.OperationId),
                operation.DueUtc)))
            .ToDictionary(x => x.Key, x => x.DueUtc);
        var optimizableAssignments = orderedAssignments.Where(x => !x.IsLocked).ToArray();
        var tardinessMinutes = 0;
        var lateOperationCount = 0;
        foreach (var assignment in optimizableAssignments)
        {
            var key = OperationKey.From(assignment);
            if (!dueByOperation.TryGetValue(key, out var dueUtc) || assignment.EndUtc <= dueUtc)
            {
                continue;
            }

            lateOperationCount++;
            tardinessMinutes += (int)Math.Ceiling((assignment.EndUtc - dueUtc).TotalMinutes);
        }

        var assignedMinutes = resourceOccupancies.Sum(x => (int)(x.EndUtc - x.StartUtc).TotalMinutes);
        var makespanMinutes = resourceOccupancies.Count == 0
            ? 0
            : (int)(resourceOccupancies.Max(x => x.EndUtc) - resourceOccupancies.Min(x => x.StartUtc)).TotalMinutes;
        var totalAvailableMinutes = resourceLoads.Sum(x => x.AvailableMinutes);
        var onTimeRate = optimizableAssignments.Length == 0
            ? 1m
            : Math.Round((decimal)(optimizableAssignments.Length - lateOperationCount) / optimizableAssignments.Length, 4);
        var averageResourceUtilization = totalAvailableMinutes == 0
            ? 0m
            : Math.Round((decimal)resourceLoads.Sum(x => x.AssignedMinutes) / totalAvailableMinutes, 4);

        return new SchedulePlanMetricsContract(
            ScheduledOperationCount: orderedAssignments.Count,
            UnscheduledOperationCount: unscheduledOperations.Count,
            AssignedMinutes: assignedMinutes,
            MakespanMinutes: makespanMinutes,
            TotalTardinessMinutes: tardinessMinutes,
            LateOperationCount: lateOperationCount,
            OnTimeRate: onTimeRate,
            AverageResourceUtilization: averageResourceUtilization,
            LockedOperationCount: orderedAssignments.Count(x => x.IsLocked),
            OptimizableOperationCount: optimizableAssignments.Length,
            MaterialRiskOperationCount: materialRiskOperationCount,
            EquipmentRiskOperationCount: equipmentRiskOperationCount);
    }

    private ScheduleAssignmentContract? TrySchedule(OperationWorkItem item)
    {
        if (!item.Operation.ToolingAvailable)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.Tooling, "所需工装不可用，或不适用于该工作中心与产品。");
            return null;
        }

        // 质检口径:路线上的「该工序需要质检」是开工/放行门槛,不是排产门槛(与物料 #1318、
        // 设备状态未知 #1325 同一裁决)。软约束(默认)下照排,只登记预警级冲突提示开工前需质量放行;
        // 硬约束下沿用旧行为,直接判为不可排。
        // 注意:这里只管路线上的常规检验标记;真实下达的质量封锁在下面单独处理,两种口径下都是硬阻。
        if (!string.IsNullOrWhiteSpace(item.Operation.QualityBlockReason))
        {
            if (qualityConstraintMode == SchedulingQualityConstraintModeContract.Hard)
            {
                AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, QualityBlockMessage(item.Operation.QualityBlockReason));
                return null;
            }

            AddConflict(
                ScheduleConflictReasonCodeContract.Quality,
                ScheduleConflictSeverityContract.Warning,
                item.Order.OrderId,
                item.Operation.OperationId,
                null,
                QualityGateWarningMessage(item.Operation.QualityBlockReason));
        }

        // 物料口径:齐套是开工门槛,不是排产门槛。
        // 软约束(默认)下缺料工序照排,只登记物料风险 + 预警级冲突,提示「需在开工前完成备料」;
        // 硬约束下沿用旧行为,缺料直接判为不可排。
        var openEndedMaterialBlocks = ApplicableOpenEndedMaterialBlocks(item).ToList();
        if (openEndedMaterialBlocks.Count > 0)
        {
            if (materialConstraintMode == SchedulingMaterialConstraintModeContract.Hard)
            {
                AddUnscheduled(item, ScheduleConflictReasonCodeContract.Material, MaterialBlockMessage(openEndedMaterialBlocks[0]));
                return null;
            }
        }

        var qualityBlocks = ApplicableOperationQualityBlocks(item).ToList();
        var openEndedQualityBlock = qualityBlocks.FirstOrDefault(x => x.BlockedUntilUtc is null);
        if (openEndedQualityBlock is not null)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, QualityBlockMessage(openEndedQualityBlock.ReasonCode));
            return null;
        }

        var earliestStartCandidates = new List<DateTimeOffset>
        {
            problem.HorizonStartUtc,
            item.Operation.EarliestStartUtc,
            item.Operation.MaterialReadyUtc ?? problem.HorizonStartUtc,
            LatestPredecessorEnd(item)
        };
        earliestStartCandidates.AddRange(ApplicableMaterialReadyTimes(item));
        earliestStartCandidates.AddRange(qualityBlocks.Select(x => x.BlockedUntilUtc!.Value));
        var earliestStart = earliestStartCandidates.Max();
        if (earliestStart >= problem.HorizonEndUtc)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.OutsideHorizon, "最早可开工时间已超出排程窗口。");
            return null;
        }

        var candidates = EligibleResources(item).ToList();
        if (candidates.Count == 0)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.NoEligibleResource, "没有具备所需工艺能力的可用资源。");
            return null;
        }

        var feasibleSlots = candidates
            .Select(resource => new ResourceSlot(resource, FindEarliestSlot(resource, item, earliestStart, item.Operation.DurationMinutes)))
            .Where(x => x.Slot is not null)
            .Select(x => new ResourceSlotValue(x.Resource, x.Slot!.Value.StartUtc, x.Slot.Value.EndUtc))
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.Resource.ResourceId == item.Operation.PrimaryResourceId ? 0 : 1)
            .ThenBy(x => x.Resource.SortKey, StringComparer.Ordinal)
            .ThenBy(x => x.Resource.ResourceId, StringComparer.Ordinal)
            .ToList();

        if (feasibleSlots.Count == 0)
        {
            var openEndedResourceQualityBlocks = candidates
                .Select(resource => ApplicableResourceQualityBlocks(item, resource)
                    .FirstOrDefault(block => block.BlockedUntilUtc is null))
                .ToList();
            if (openEndedResourceQualityBlocks.Count != 0
                && openEndedResourceQualityBlocks.All(block => block is not null))
            {
                AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, QualityBlockMessage(openEndedResourceQualityBlocks[0]!.ReasonCode));
                return null;
            }

            var reasonCode = InferNoFeasibleSlotReason(candidates, item, earliestStart);
            AddUnscheduled(item, reasonCode, NoFeasibleSlotMessage(reasonCode));
            return null;
        }

        var selected = feasibleSlots[0];
        // 工序确实排进去了才登记物料风险,避免给不可排工序挂上「已排但缺料」的假标记。
        if (openEndedMaterialBlocks.Count > 0)
        {
            AddMaterialRisk(item, openEndedMaterialBlocks);
        }

        // 设备数据风险同理:只对真正落到这台设备上的时段登记。
        AddEquipmentRisk(
            item.Order.OrderId,
            item.Operation.OperationId,
            selected.Resource.ResourceId,
            ApplicableEquipmentDataRisks(selected.Resource.ResourceId, selected.StartUtc, selected.EndUtc));

        return new ScheduleAssignmentContract(
            AssignmentId: $"assign-{item.Order.OrderId}-{item.Operation.OperationId}",
            OrderId: item.Order.OrderId,
            OperationId: item.Operation.OperationId,
            OperationSequence: item.Operation.OperationSequence,
            ResourceId: selected.Resource.ResourceId,
            WorkCenterId: selected.Resource.WorkCenterId,
            StartUtc: selected.StartUtc,
            EndUtc: selected.EndUtc,
            IsLocked: false,
            ExplanationCode: "scheduled",
            // SchedulingProblemProducer maps ProductEngineering routing operation code into RequiredCapabilityCode.
            StandardOperationCode: item.Operation.RequiredCapabilityCode);
    }

    private IEnumerable<SchedulingResourceContract> EligibleResources(OperationWorkItem item)
    {
        var eligibleIds = item.Operation.EligibleResourceIds.ToHashSet(StringComparer.Ordinal);
        var requiredCodes = RequiredCapabilityCodes(item.Operation).ToArray();
        return resources.Values
            .Where(resource => eligibleIds.Contains(resource.ResourceId))
            .Where(resource => requiredCodes.All(code => resource.CapabilityCodes.Contains(code, StringComparer.Ordinal)))
            .OrderBy(resource => resource.ResourceId == item.Operation.PrimaryResourceId ? 0 : 1)
            .ThenBy(resource => resource.SortKey, StringComparer.Ordinal)
            .ThenBy(resource => resource.ResourceId, StringComparer.Ordinal);
    }

    private IEnumerable<DateTimeOffset> ApplicableMaterialReadyTimes(OperationWorkItem item)
    {
        return problem.MaterialReadiness
            .Where(x => x.MaterialReadyUtc.HasValue)
            .Where(x => AppliesTo(x.ScopeType, x.ScopeId, item))
            .Select(x => x.MaterialReadyUtc!.Value);
    }

    private IEnumerable<SchedulingMaterialReadinessContract> ApplicableOpenEndedMaterialBlocks(OperationWorkItem item)
    {
        return problem.MaterialReadiness
            .Where(x => !x.IsReady && !x.MaterialReadyUtc.HasValue)
            .Where(x => AppliesTo(x.ScopeType, x.ScopeId, item));
    }

    private IEnumerable<SchedulingQualityBlockContract> ApplicableOperationQualityBlocks(OperationWorkItem item)
    {
        return problem.QualityBlocks
            .Where(x => !string.Equals(x.ScopeType, "resource", StringComparison.OrdinalIgnoreCase))
            .Where(x => AppliesTo(x.ScopeType, x.ScopeId, item));
    }

    private IEnumerable<SchedulingQualityBlockContract> ApplicableResourceQualityBlocks(
        OperationWorkItem item,
        SchedulingResourceContract resource)
    {
        return problem.QualityBlocks
            .Where(x => string.Equals(x.ScopeType, "resource", StringComparison.OrdinalIgnoreCase))
            .Where(x => AppliesTo(x.ScopeType, x.ScopeId, item, resource));
    }

    private DateTimeOffset LatestPredecessorEnd(OperationWorkItem item)
    {
        var predecessorEnds = item.Operation.PredecessorOperationIds
            .Select(id => assignments.FirstOrDefault(x =>
                x.OrderId == item.Order.OrderId && x.OperationId == id)?.EndUtc)
            .Where(x => x.HasValue)
            .Select(x => x!.Value);
        return predecessorEnds.DefaultIfEmpty(problem.HorizonStartUtc).Max();
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc)? FindEarliestSlot(
        SchedulingResourceContract resource,
        OperationWorkItem item,
        DateTimeOffset earliestStart,
        int durationMinutes)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
        {
            return null;
        }

        var resourceQualityBlocks = ApplicableResourceQualityBlocks(item, resource).ToList();
        if (resourceQualityBlocks.Any(x => x.BlockedUntilUtc is null))
        {
            return null;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        var setup = TimeSpan.FromMinutes(Math.Max(0, item.Operation.SetupMinutes));
        foreach (var shift in ContinuousWindows(calendar)
                     .Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc))
        {
            var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
            var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);

            while (candidate + duration <= latestEnd)
            {
                var setupAdjustedCandidate = ApplySetupGap(resource, candidate, setup);
                if (setupAdjustedCandidate > candidate)
                {
                    candidate = setupAdjustedCandidate;
                    continue;
                }

                var end = candidate + duration;
                var occupiedStart = CandidateOccupiedStart(resource, candidate, setup);
                if (occupiedStart < shift.StartUtc)
                {
                    // Keep setup occupancy inside the selected shift; load reconstruction uses the same invariant.
                    candidate = shift.StartUtc + setup;
                    continue;
                }

                var blockingEnd = BlockingEnd(resource, item.Operation, resourceQualityBlocks, occupiedStart, end);
                if (blockingEnd is null)
                {
                    return (candidate, end);
                }

                candidate = NextCandidateAfterBlock(resource, candidate, setup, blockingEnd.Value);
            }
        }

        return null;
    }

    private DateTimeOffset ApplySetupGap(
        SchedulingResourceContract resource,
        DateTimeOffset candidate,
        TimeSpan setup)
    {
        if (setup <= TimeSpan.Zero)
        {
            return candidate;
        }

        var previousEnd = assignments
            .Where(x => x.ResourceId == resource.ResourceId)
            .Where(x => x.EndUtc <= candidate)
            .Select(x => (DateTimeOffset?)x.EndUtc)
            .Max();
        if (!previousEnd.HasValue)
        {
            return candidate;
        }

        var setupComplete = previousEnd.Value + setup;
        return candidate < setupComplete ? setupComplete : candidate;
    }

    private DateTimeOffset CandidateOccupiedStart(
        SchedulingResourceContract resource,
        DateTimeOffset candidate,
        TimeSpan setup)
    {
        if (setup <= TimeSpan.Zero)
        {
            return candidate;
        }

        var hasPreviousAssignment = assignments
            .Any(x => x.ResourceId == resource.ResourceId && x.EndUtc <= candidate);
        return hasPreviousAssignment ? candidate - setup : candidate;
    }

    private DateTimeOffset NextCandidateAfterBlock(
        SchedulingResourceContract resource,
        DateTimeOffset candidate,
        TimeSpan setup,
        DateTimeOffset blockingEnd)
    {
        var hasPreviousAssignment = setup > TimeSpan.Zero
            && assignments.Any(x => x.ResourceId == resource.ResourceId && x.EndUtc <= candidate);
        return hasPreviousAssignment ? blockingEnd + setup : blockingEnd;
    }

    private static IEnumerable<string> RequiredCapabilityCodes(SchedulingOperationContract operation)
    {
        yield return operation.RequiredCapabilityCode;

        // APS lite models required skills and tooling as resource capability codes; no separate namespace exists yet.
        foreach (var skillCode in operation.RequiredSkillCodes ?? [])
        {
            yield return skillCode;
        }

    }

    private DateTimeOffset? BlockingEnd(
        SchedulingResourceContract resource,
        SchedulingOperationContract operation,
        IReadOnlyCollection<SchedulingQualityBlockContract> resourceQualityBlocks,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var qualityBlockEnd = resourceQualityBlocks
            .Where(x => x.BlockedUntilUtc.HasValue)
            .Where(x => Overlaps(startUtc, endUtc, QualityBlockStartUtc(x), x.BlockedUntilUtc!.Value))
            .Select(x => x.BlockedUntilUtc)
            .Min();
        if (qualityBlockEnd.HasValue)
        {
            return qualityBlockEnd;
        }

        var unavailabilityEnd = problem.UnavailabilityWindows
            .Where(x => AppliesTo(x, resource))
            .Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .Select(x => (DateTimeOffset?)x.EndUtc)
            .Min();
        if (unavailabilityEnd.HasValue)
        {
            return unavailabilityEnd;
        }

        var toolingEnd = ToolingBlockEnd(operation, startUtc, endUtc);
        if (toolingEnd.HasValue) return toolingEnd;

        var capacity = Math.Max(1, resource.CapacityUnits);
        return CapacityBlockEnd(resource, startUtc, endUtc, capacity);
    }

    private void ReportLockedCapacityConflicts()
    {
        var lockedAssignments = assignments
            .Where(x => x.IsLocked)
            .ToList();
        var overbookedAssignmentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resource in resources.Values)
        {
            var resourceLocks = lockedAssignments
                .Where(x => x.ResourceId == resource.ResourceId)
                .ToList();
            if (resourceLocks.Count <= Math.Max(1, resource.CapacityUnits))
            {
                continue;
            }

            var capacity = Math.Max(1, resource.CapacityUnits);
            var boundaries = resourceLocks
                .SelectMany(x => new[] { x.StartUtc, x.EndUtc })
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            for (var i = 0; i < boundaries.Count - 1; i++)
            {
                var segmentStart = boundaries[i];
                var segmentEnd = boundaries[i + 1];
                if (segmentStart >= segmentEnd)
                {
                    continue;
                }

                var concurrentLocks = resourceLocks
                    .Where(x => x.StartUtc < segmentEnd && x.EndUtc > segmentStart)
                    .ToList();
                if (concurrentLocks.Count <= capacity)
                {
                    continue;
                }

                foreach (var locked in concurrentLocks)
                {
                    overbookedAssignmentIds.Add(locked.AssignmentId);
                }
            }
        }

        foreach (var locked in lockedAssignments
                     .Where(x => overbookedAssignmentIds.Contains(x.AssignmentId))
                     .OrderBy(x => x.StartUtc)
                     .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            AddConflict(
                ScheduleConflictReasonCodeContract.InvalidLockedAssignment,
                ScheduleConflictSeverityContract.Error,
                locked.OrderId,
                locked.OperationId,
                locked.ResourceId,
                "锁定工序之间相互冲突，已超出该资源的有限产能。");
        }
    }

    private DateTimeOffset? CapacityBlockEnd(
        SchedulingResourceContract resource,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int capacity)
    {
        var overlappingOccupancies = GetResourceOccupancies(assignments)
            .Where(x => x.ResourceId == resource.ResourceId)
            .Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .ToList();
        if (overlappingOccupancies.Count < capacity)
        {
            return null;
        }

        var boundaries = overlappingOccupancies
            .SelectMany(x => new[]
            {
                Max(startUtc, x.StartUtc),
                Min(endUtc, x.EndUtc)
            })
            .Append(startUtc)
            .Append(endUtc)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var segmentStart = boundaries[i];
            var segmentEnd = boundaries[i + 1];
            if (segmentStart >= segmentEnd)
            {
                continue;
            }

            var concurrentAssignments = overlappingOccupancies.Count(x =>
                x.StartUtc < segmentEnd && x.EndUtc > segmentStart);
            if (concurrentAssignments >= capacity)
            {
                return segmentEnd;
            }
        }

        return null;
    }

    private bool IsInsideCalendar(SchedulingResourceContract resource, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        return calendars.TryGetValue(resource.CalendarId, out var calendar)
            && ContinuousWindows(calendar).Any(x => x.StartUtc <= startUtc && x.EndUtc >= endUtc);
    }

    /// <summary>
    /// 把日历里首尾相接或互相重叠的班次窗口合并成「连续可生产区间」。
    ///
    /// 为什么必须合并:班次是排班口径,不是产能边界。早班 08:00–20:00 与晚班 20:00–08:00 本就
    /// 首尾相接、连班生产,一道 16 小时的工序在现场是跨班次连做下去的;而排程侧要求整道工序落在
    /// 同一条窗口里(工序不可拆,<c>SplitPolicy = NonSplittable</c>),不合并就会把这类工序判成
    /// 「班次日历里没有能容纳该工序时长的完整窗口」而整条工艺路线级联未排(#1399 M9)。
    /// 同一份种子还会把多套班次(DAY/NIGHT 与 EARLY/MIDDLE)同时铺到每个工作日上,窗口之间
    /// 大量重叠,不合并连「同一时刻算几个窗口」都说不清。
    ///
    /// 只用于可行性判定与排入点搜索。问题快照与读面投影仍然保留逐班次的原始窗口,
    /// 这样图例上的「班次边界」和日历投影不受影响。
    /// </summary>
    private IReadOnlyList<SchedulingTimeWindowContract> ContinuousWindows(SchedulingCalendarContract calendar)
    {
        if (continuousWindowsByCalendar.TryGetValue(calendar.CalendarId, out var cached))
        {
            return cached;
        }

        var merged = new List<SchedulingTimeWindowContract>();
        foreach (var window in calendar.ShiftWindows.OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc))
        {
            if (merged.Count > 0 && window.StartUtc <= merged[^1].EndUtc)
            {
                // 相接(==)也要合并:20:00 结束的早班与 20:00 开始的晚班之间没有停产间隙。
                if (window.EndUtc > merged[^1].EndUtc)
                {
                    merged[^1] = merged[^1] with { EndUtc = window.EndUtc };
                }

                continue;
            }

            merged.Add(window);
        }

        continuousWindowsByCalendar[calendar.CalendarId] = merged;
        return merged;
    }

    private ScheduleConflictReasonCodeContract InferNoFeasibleSlotReason(
        IReadOnlyCollection<SchedulingResourceContract> candidates,
        OperationWorkItem item,
        DateTimeOffset earliestStart)
    {
        var durationMinutes = item.Operation.DurationMinutes;
        if (!candidates.Any(resource => HasCalendarFitIgnoringHorizonEnd(resource, earliestStart, durationMinutes)))
        {
            return ScheduleConflictReasonCodeContract.Calendar;
        }

        if (!candidates.Any(resource => HasCalendarFit(resource, earliestStart, durationMinutes)))
        {
            return ScheduleConflictReasonCodeContract.OutsideHorizon;
        }

        if ((item.Operation.RequiredToolingIds?.Count ?? 0) > 0 && !candidates.Any(resource => HasToolingSlot(resource, item.Operation, earliestStart)))
        {
            return ScheduleConflictReasonCodeContract.Tooling;
        }

        if (candidates.Any(resource => HasSlotIgnoringCapacity(resource, item, earliestStart, durationMinutes)))
        {
            return ScheduleConflictReasonCodeContract.Capacity;
        }

        if (candidates.Any(resource => HasSlotIgnoringEquipmentAndCapacity(resource, item, earliestStart, durationMinutes)))
        {
            return ScheduleConflictReasonCodeContract.Equipment;
        }

        return ScheduleConflictReasonCodeContract.OutsideHorizon;
    }

    private DateTimeOffset? ToolingBlockEnd(SchedulingOperationContract operation, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var required = (operation.RequiredToolingIds ?? []).ToHashSet(StringComparer.Ordinal);
        if (required.Count == 0) return null;
        return assignments.Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .Where(x => operationByKey.TryGetValue(OperationKey.From(x), out var assignedOperation)
                && (assignedOperation.RequiredToolingIds ?? []).Any(required.Contains))
            .Select(x => (DateTimeOffset?)x.EndUtc).Min();
    }

    private bool HasToolingSlot(SchedulingResourceContract resource, SchedulingOperationContract operation, DateTimeOffset earliestStart)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar)) return false;
        var duration = TimeSpan.FromMinutes(operation.DurationMinutes);
        foreach (var shift in ContinuousWindows(calendar).Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc))
        {
            var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
            var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);
            while (candidate + duration <= latestEnd)
            {
                var block = ToolingBlockEnd(operation, candidate, candidate + duration);
                if (!block.HasValue) return true;
                candidate = block.Value;
            }
        }
        return false;
    }

    private bool HasCalendarFit(
        SchedulingResourceContract resource,
        DateTimeOffset earliestStart,
        int durationMinutes)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
        {
            return false;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        return ContinuousWindows(calendar)
            .Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc)
            .Any(shift =>
            {
                var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
                var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);
                return candidate + duration <= latestEnd;
            });
    }

    private bool HasCalendarFitIgnoringHorizonEnd(
        SchedulingResourceContract resource,
        DateTimeOffset earliestStart,
        int durationMinutes)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
        {
            return false;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        return ContinuousWindows(calendar)
            .Where(x => x.EndUtc > earliestStart)
            .Any(shift =>
            {
                var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
                return candidate + duration <= shift.EndUtc;
            });
    }

    private bool HasSlotIgnoringCapacity(
        SchedulingResourceContract resource,
        OperationWorkItem item,
        DateTimeOffset earliestStart,
        int durationMinutes)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
        {
            return false;
        }

        var resourceQualityBlocks = ApplicableResourceQualityBlocks(item, resource).ToList();
        if (resourceQualityBlocks.Any(x => x.BlockedUntilUtc is null))
        {
            return false;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        foreach (var shift in ContinuousWindows(calendar)
                     .Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc))
        {
            var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
            var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);

            while (candidate + duration <= latestEnd)
            {
                var end = candidate + duration;
                var blockingEnd = BlockingEndIgnoringCapacity(resource, resourceQualityBlocks, candidate, end);
                if (blockingEnd is null)
                {
                    return true;
                }

                candidate = blockingEnd.Value;
            }
        }

        return false;
    }

    private bool HasSlotIgnoringEquipmentAndCapacity(
        SchedulingResourceContract resource,
        OperationWorkItem item,
        DateTimeOffset earliestStart,
        int durationMinutes)
    {
        if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
        {
            return false;
        }

        var resourceQualityBlocks = ApplicableResourceQualityBlocks(item, resource).ToList();
        if (resourceQualityBlocks.Any(x => x.BlockedUntilUtc is null))
        {
            return false;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        foreach (var shift in ContinuousWindows(calendar)
                     .Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc))
        {
            var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
            var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);

            while (candidate + duration <= latestEnd)
            {
                var end = candidate + duration;
                var blockingEnd = BlockingEndIgnoringEquipmentAndCapacity(resourceQualityBlocks, candidate, end);
                if (blockingEnd is null)
                {
                    return true;
                }

                candidate = blockingEnd.Value;
            }
        }

        return false;
    }

    private DateTimeOffset? BlockingEndIgnoringCapacity(
        SchedulingResourceContract resource,
        IReadOnlyCollection<SchedulingQualityBlockContract> resourceQualityBlocks,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var qualityBlockEnd = resourceQualityBlocks
            .Where(x => x.BlockedUntilUtc.HasValue)
            .Where(x => Overlaps(startUtc, endUtc, QualityBlockStartUtc(x), x.BlockedUntilUtc!.Value))
            .Select(x => x.BlockedUntilUtc)
            .Min();
        if (qualityBlockEnd.HasValue)
        {
            return qualityBlockEnd;
        }

        return problem.UnavailabilityWindows
            .Where(x => AppliesTo(x, resource))
            .Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .Select(x => (DateTimeOffset?)x.EndUtc)
            .Min();
    }

    private DateTimeOffset? BlockingEndIgnoringEquipmentAndCapacity(
        IReadOnlyCollection<SchedulingQualityBlockContract> resourceQualityBlocks,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var qualityBlockEnd = resourceQualityBlocks
            .Where(x => x.BlockedUntilUtc.HasValue)
            .Where(x => Overlaps(startUtc, endUtc, QualityBlockStartUtc(x), x.BlockedUntilUtc!.Value))
            .Select(x => x.BlockedUntilUtc)
            .Min();
        return qualityBlockEnd;
    }

    private DateTimeOffset QualityBlockStartUtc(SchedulingQualityBlockContract qualityBlock)
    {
        _ = qualityBlock;
        // Quality blocks currently expose only BlockedUntilUtc, so they are active from the scheduling horizon start.
        return problem.HorizonStartUtc;
    }

    private static string NoFeasibleSlotMessage(ScheduleConflictReasonCodeContract reasonCode)
    {
        return reasonCode switch
        {
            ScheduleConflictReasonCodeContract.Capacity => "排程窗口内产能已排满，找不到可用时段。",
            ScheduleConflictReasonCodeContract.Calendar => "班次日历里没有能容纳该工序时长的完整窗口。",
            ScheduleConflictReasonCodeContract.Equipment => "排程窗口内可用资源全部处于不可用状态（维护/停机）。",
            ScheduleConflictReasonCodeContract.Tooling => "所需工装被其他工单占用，窗口内排不开。",
            _ => "排程窗口内找不到可用的产能时段。"
        };
    }

    private bool IsUnavailable(SchedulingResourceContract resource, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        return problem.UnavailabilityWindows
            .Where(x => AppliesTo(x, resource))
            .Any(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc));
    }

    private void AddAssignment(ScheduleAssignmentContract assignment)
    {
        assignments.Add(assignment);
        resourceOccupancyCache = null;
    }

    private IReadOnlyCollection<ResourceOccupancy> GetResourceOccupancies(IReadOnlyCollection<ScheduleAssignmentContract> orderedAssignments)
    {
        if (ReferenceEquals(orderedAssignments, assignments))
        {
            return resourceOccupancyCache ??= BuildResourceOccupancies(orderedAssignments);
        }

        return BuildResourceOccupancies(orderedAssignments);
    }

    private IReadOnlyCollection<ResourceOccupancy> BuildResourceOccupancies(IReadOnlyCollection<ScheduleAssignmentContract> orderedAssignments)
    {
        var resourceOccupancies = new List<ResourceOccupancy>();
        var earliestOccupancyEndByResource = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var assignment in orderedAssignments
                     .OrderBy(x => x.StartUtc)
                     .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
            var startUtc = assignment.StartUtc;
            if (operationByKey.TryGetValue(OperationKey.From(assignment), out var operation)
                && operation.SetupMinutes > 0
                && earliestOccupancyEndByResource.TryGetValue(assignment.ResourceId, out var earliestEnd)
                && earliestEnd <= assignment.StartUtc)
            {
                // Placement guarantees this setup occupancy stays inside the selected shift.
                startUtc = assignment.StartUtc - TimeSpan.FromMinutes(operation.SetupMinutes);
            }

            resourceOccupancies.Add(new ResourceOccupancy(
                assignment.ResourceId,
                startUtc,
                assignment.EndUtc));
            if (!earliestOccupancyEndByResource.TryGetValue(assignment.ResourceId, out earliestEnd)
                || assignment.EndUtc < earliestEnd)
            {
                earliestOccupancyEndByResource[assignment.ResourceId] = assignment.EndUtc;
            }
        }

        return resourceOccupancies;
    }

    private IReadOnlyCollection<ScheduleResourceLoadContract> BuildResourceLoads(IReadOnlyCollection<ResourceOccupancy> resourceOccupancies)
    {
        return resources.Values
            .OrderBy(x => x.SortKey, StringComparer.Ordinal)
            .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
            .SelectMany(resource =>
            {
                if (!calendars.TryGetValue(resource.CalendarId, out var calendar))
                {
                    return [];
                }

                return calendar.ShiftWindows.Select(shift =>
                {
                    var assignedMinutes = resourceOccupancies
                        .Where(x => x.ResourceId == resource.ResourceId)
                        .Sum(x => OverlapMinutes(x.StartUtc, x.EndUtc, shift.StartUtc, shift.EndUtc));
                    var unavailableMinutes = MergedUnavailableMinutes(resource, shift.StartUtc, shift.EndUtc);
                    var capacity = Math.Max(1, resource.CapacityUnits);
                    var shiftMinutes = (int)(shift.EndUtc - shift.StartUtc).TotalMinutes;
                    var availableMinutes = Math.Max(0, (shiftMinutes - unavailableMinutes) * capacity);

                    return new ScheduleResourceLoadContract(
                        ResourceId: resource.ResourceId,
                        WindowStartUtc: shift.StartUtc,
                        WindowEndUtc: shift.EndUtc,
                        AssignedMinutes: assignedMinutes,
                        AvailableMinutes: availableMinutes,
                        Utilization: availableMinutes == 0 ? 0 : Math.Round((decimal)assignedMinutes / availableMinutes, 4));
                });
            })
            .Where(x => x.AssignedMinutes > 0 || x.AvailableMinutes > 0)
            .ToList();
    }

    private int MergedUnavailableMinutes(
        SchedulingResourceContract resource,
        DateTimeOffset shiftStartUtc,
        DateTimeOffset shiftEndUtc)
    {
        var windows = problem.UnavailabilityWindows
            .Where(x => AppliesTo(x, resource))
            .Select(x => (StartUtc: Max(x.StartUtc, shiftStartUtc), EndUtc: Min(x.EndUtc, shiftEndUtc)))
            .Where(x => x.StartUtc < x.EndUtc)
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ToList();
        if (windows.Count == 0)
        {
            return 0;
        }

        var totalMinutes = 0;
        var currentStart = windows[0].StartUtc;
        var currentEnd = windows[0].EndUtc;
        foreach (var window in windows.Skip(1))
        {
            if (window.StartUtc <= currentEnd)
            {
                currentEnd = Max(currentEnd, window.EndUtc);
                continue;
            }

            totalMinutes += (int)(currentEnd - currentStart).TotalMinutes;
            currentStart = window.StartUtc;
            currentEnd = window.EndUtc;
        }

        totalMinutes += (int)(currentEnd - currentStart).TotalMinutes;
        return totalMinutes;
    }

    private void AddUnscheduled(
        OperationWorkItem item,
        ScheduleConflictReasonCodeContract reasonCode,
        string? message)
    {
        failedOperationKeys.Add(OperationKey.From(item));
        // 未排原因必须是人话:没有具体说明时也要回落到中文原因,绝不把枚举名当消息发给读面。
        var reasonMessage = string.IsNullOrWhiteSpace(message)
            ? UnscheduledReasonText(reasonCode)
            : message;
        unscheduledOperations.Add(new UnscheduledOperationContract(
            item.Order.OrderId,
            item.Operation.OperationId,
            reasonCode,
            reasonMessage));
        changeSummary.Add(new ScheduleChangeContract(
            item.Order.OrderId,
            item.Operation.OperationId,
            ScheduleChangeTypeContract.Blocked,
            reasonMessage));
        AddConflict(
            reasonCode,
            ScheduleConflictSeverityContract.Error,
            item.Order.OrderId,
            item.Operation.OperationId,
            null,
            reasonMessage);
    }

    /// <summary>未排/冲突原因的中文兜底文案(读面直接展示,不做二次翻译)。</summary>
    private static string UnscheduledReasonText(ScheduleConflictReasonCodeContract reasonCode)
    {
        return reasonCode switch
        {
            ScheduleConflictReasonCodeContract.DueDate => "交期风险：排入时间晚于交期。",
            ScheduleConflictReasonCodeContract.Capacity => "产能不足：排程窗口内排不下。",
            ScheduleConflictReasonCodeContract.Calendar => "班次日历不可用：窗口内没有可用工作时间。",
            ScheduleConflictReasonCodeContract.Material => "物料未齐套：开工前需先完成备料。",
            ScheduleConflictReasonCodeContract.Quality => "质量限制：该工序被质量放行卡住。",
            ScheduleConflictReasonCodeContract.Equipment => "设备不可用：可用资源处于维护或停机。",
            ScheduleConflictReasonCodeContract.NoEligibleResource => "无可用资源：没有具备所需工艺能力的资源。",
            ScheduleConflictReasonCodeContract.OutsideHorizon => "超出排程窗口：最早可开工时间在窗口之外。",
            ScheduleConflictReasonCodeContract.InvalidLockedAssignment => "锁定无效：锁定的排程结果不成立。",
            ScheduleConflictReasonCodeContract.PredecessorUnscheduled => "前序未排产：前道工序没能排入本次计划。",
            ScheduleConflictReasonCodeContract.Tooling => "工装不可用：所需工装被占用或不适用。",
            _ => "排程受阻：请查看该工序的具体约束。"
        };
    }

    /// <summary>
    /// 登记物料风险:工序已排入计划,但开工前必须补齐这些物料(MES 侧齐套硬门仍会拦开工)。
    /// 同时补一条预警级冲突,让读面「冲突与风险」清单也能看到,但不影响可排性。
    /// </summary>
    private void AddMaterialRisk(
        OperationWorkItem item,
        IReadOnlyCollection<SchedulingMaterialReadinessContract> blocks)
    {
        var reasonCodes = blocks
            .SelectMany(x => x.ReasonCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var shortages = blocks
            .SelectMany(x => x.Shortages ?? [])
            .GroupBy(x => (x.MaterialId, x.MaterialLotId))
            .Select(x => new SchedulingMaterialShortageContract(
                x.Key.MaterialId,
                x.Key.MaterialLotId,
                x.Sum(y => y.RequiredQuantity),
                x.Sum(y => y.AvailableQuantity),
                x.Sum(y => y.ShortageQuantity)))
            .OrderBy(x => x.MaterialId, StringComparer.Ordinal)
            .ThenBy(x => x.MaterialLotId, StringComparer.Ordinal)
            .ToArray();
        var message = MaterialRiskMessage(shortages, reasonCodes);

        materialRisks.Add(new SchedulePlanMaterialRiskContract(
            item.Order.OrderId,
            item.Operation.OperationId,
            reasonCodes,
            shortages,
            message));
        AddConflict(
            ScheduleConflictReasonCodeContract.Material,
            ScheduleConflictSeverityContract.Warning,
            item.Order.OrderId,
            item.Operation.OperationId,
            null,
            message);
    }

    /// <summary>
    /// 登记设备数据风险:工序已排到这台设备上,但计划窗口内这台设备的运行时状态是盲区
    /// (无快照 / 快照过期 / 采集源不可达)。「不知道」不阻断排程,只补一条预警级冲突,
    /// 让读面提示「开工前请人工确认设备状态」。
    /// </summary>
    private void AddEquipmentRisk(
        string orderId,
        string operationId,
        string resourceId,
        IReadOnlyCollection<SchedulingEquipmentDataRiskContract> risks)
    {
        if (risks.Count == 0)
        {
            return;
        }

        var reasonCodes = risks
            .Select(x => x.ReasonCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var message = EquipmentRiskMessage(resourceId, reasonCodes);

        equipmentRisks.Add(new SchedulePlanEquipmentRiskContract(
            orderId,
            operationId,
            resourceId,
            reasonCodes,
            message));
        AddConflict(
            ScheduleConflictReasonCodeContract.Equipment,
            ScheduleConflictSeverityContract.Warning,
            orderId,
            operationId,
            resourceId,
            message);
    }

    private static string EquipmentRiskMessage(string resourceId, IReadOnlyCollection<string> reasonCodes)
    {
        const string Suffix = "已按计划排入,开工前请人工确认设备可用。";
        var detail = reasonCodes
            .Select(DescribeEquipmentRiskReason)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return detail.Length > 0
            ? $"设备 {resourceId} 状态未知({string.Join('、', detail)})。{Suffix}"
            : $"设备 {resourceId} 状态未知。{Suffix}";
    }

    private static string DescribeEquipmentRiskReason(string reasonCode)
    {
        return reasonCode switch
        {
            EquipmentRuntimeReasonCodes.SourceStale => "采集数据已过期",
            HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode => "采集源当前不可达",
            EquipmentRuntimeReasonCodes.TagMappingMissing => "采集点位未映射",
            _ => "缺少运行时状态"
        };
    }

    private IReadOnlyCollection<SchedulingEquipmentDataRiskContract> ApplicableEquipmentDataRisks(
        string resourceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var risks = problem.EquipmentDataRisks;
        if (risks is null || risks.Count == 0)
        {
            return [];
        }

        return risks
            .Where(x => string.Equals(x.ResourceId, resourceId, StringComparison.Ordinal))
            .Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .ToArray();
    }

    private static string MaterialRiskMessage(
        IReadOnlyCollection<SchedulingMaterialShortageContract> shortages,
        IReadOnlyCollection<string> reasonCodes)
    {
        const string Suffix = "已按计划排入,需在开工前完成备料。";
        if (shortages.Count > 0)
        {
            var detail = string.Join(
                '、',
                shortages
                    .Take(3)
                    .Select(x => $"{x.MaterialId} 缺 {x.ShortageQuantity:0.######}"));
            var more = shortages.Count > 3 ? $" 等 {shortages.Count} 项" : string.Empty;
            return $"物料未齐套:{detail}{more}。{Suffix}";
        }

        // 原因串来自 MES,形态是 `CODE: 中文事实`——上屏前把英文码剥掉,
        // 界面上不该出现 MATERIAL_SHORTAGE 这类码(MAN-698 台账 #35)。
        var readableReasons = SchedulingMaterialReasonText.DescribeForUser(reasonCodes);
        return readableReasons.Count > 0
            ? $"物料未齐套({string.Join('、', readableReasons)})。{Suffix}"
            : $"物料未齐套。{Suffix}";
    }

    private void AddConflict(
        ScheduleConflictReasonCodeContract reasonCode,
        ScheduleConflictSeverityContract severity,
        string? orderId,
        string? operationId,
        string? resourceId,
        string message)
    {
        conflictNumber++;
        conflicts.Add(new ScheduleConflictContract(
            ConflictId: $"conflict-{conflictNumber:0000}",
            ReasonCode: reasonCode,
            Severity: severity,
            OrderId: orderId,
            OperationId: operationId,
            ResourceId: resourceId,
            Message: message));
    }

    private static string Fingerprint(SchedulingProblemContract problem)
    {
        var json = JsonSerializer.Serialize(problem, SchedulingJson.Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool AppliesTo(SchedulingUnavailabilityWindowContract window, SchedulingResourceContract resource)
    {
        return string.Equals(window.ResourceId, resource.ResourceId, StringComparison.Ordinal)
            || string.Equals(window.WorkCenterId, resource.WorkCenterId, StringComparison.Ordinal);
    }

    private static bool AppliesTo(string scopeType, string scopeId, OperationWorkItem item)
    {
        return scopeType.ToLowerInvariant() switch
        {
            "operation" => string.Equals(scopeId, item.Operation.OperationId, StringComparison.Ordinal),
            "order" => string.Equals(scopeId, item.Order.OrderId, StringComparison.Ordinal),
            "sku" => string.Equals(scopeId, item.Order.SkuCode, StringComparison.Ordinal),
            "resource" => item.Operation.EligibleResourceIds.Contains(scopeId, StringComparer.Ordinal)
                || string.Equals(scopeId, item.Operation.PrimaryResourceId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool AppliesTo(
        string scopeType,
        string scopeId,
        OperationWorkItem item,
        SchedulingResourceContract resource)
    {
        return scopeType.ToLowerInvariant() switch
        {
            "resource" => string.Equals(scopeId, resource.ResourceId, StringComparison.Ordinal),
            _ => AppliesTo(scopeType, scopeId, item)
        };
    }

    /// <summary>
    /// 硬约束下工序排不出去时的未排原因,直接进读面的「未排原因」列——
    /// 同样要剥掉英文码(此前会渲染成「物料未齐套（material-shortage）」，
    /// 码是给程序看的，用户读不懂；MAN-698 台账 #35 遗留同型)。
    /// </summary>
    private static string MaterialBlockMessage(SchedulingMaterialReadinessContract materialReadiness)
    {
        var readableReasons = SchedulingMaterialReasonText.DescribeForUser(materialReadiness.ReasonCodes);
        return readableReasons.Count == 0
            ? "物料未齐套：开工前需先完成备料。"
            : $"物料未齐套（{string.Join('、', readableReasons)}）：开工前需先完成备料。";
    }

    /// <summary>质量放行原因来自上游码值,读面直接展示,所以给它套一层中文说明。</summary>
    private static string QualityBlockMessage(string? reasonCode)
    {
        return string.IsNullOrWhiteSpace(reasonCode)
            ? "质量限制：该工序被质量放行卡住。"
            : $"质量限制（{reasonCode}）：需先完成质量放行。";
    }

    /// <summary>
    /// 软口径下「路线要求质检」的提示语。措辞要和硬口径明确区分:这道工序**已经排进计划**了,
    /// 需要人做的是开工/流转前走质量放行,而不是"它没能排进来"。
    /// </summary>
    private static string QualityGateWarningMessage(string? reasonCode)
    {
        return string.IsNullOrWhiteSpace(reasonCode)
            ? "该工序需要质量放行：已排入计划，开工前请先完成质检。"
            : $"该工序需要质量放行（{reasonCode}）：已排入计划，开工前请先完成质检。";
    }

    private static bool Overlaps(DateTimeOffset start1, DateTimeOffset end1, DateTimeOffset start2, DateTimeOffset end2)
    {
        return start1 < end2 && end1 > start2;
    }

    private static int OverlapMinutes(DateTimeOffset start1, DateTimeOffset end1, DateTimeOffset start2, DateTimeOffset end2)
    {
        var start = Max(start1, start2);
        var end = Min(end1, end2);
        return start >= end ? 0 : (int)(end - start).TotalMinutes;
    }

    private static DateTimeOffset Max(params DateTimeOffset[] values)
    {
        return values.Max();
    }

    private static DateTimeOffset Min(params DateTimeOffset[] values)
    {
        return values.Min();
    }

    private sealed record OperationWorkItem(
        SchedulingOrderContract Order,
        SchedulingOperationContract Operation);

    private readonly record struct OperationKey(string OrderId, string OperationId)
    {
        public static OperationKey From(OperationWorkItem item)
        {
            return new OperationKey(item.Order.OrderId, item.Operation.OperationId);
        }

        public static OperationKey From(ScheduleAssignmentContract assignment)
        {
            return new OperationKey(assignment.OrderId, assignment.OperationId);
        }
    }

    private sealed record ResourceSlot(
        SchedulingResourceContract Resource,
        (DateTimeOffset StartUtc, DateTimeOffset EndUtc)? Slot);

    private sealed record ResourceSlotValue(
        SchedulingResourceContract Resource,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);

    private sealed record ResourceOccupancy(
        string ResourceId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}
