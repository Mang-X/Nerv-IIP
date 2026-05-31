using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class FiniteCapacityScheduler
{
    public const string AlgorithmVersion = "aps-lite-v1";

    public SchedulePlanContract Schedule(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var state = SchedulerState.From(problem, planId, generatedAtUtc);
        state.ReserveLockedAssignments();
        state.ScheduleOpenOperations();
        return state.ToPlan();
    }
}

file sealed class SchedulerState
{
    private readonly SchedulingProblemContract problem;
    private readonly string planId;
    private readonly DateTimeOffset generatedAtUtc;
    private readonly Dictionary<string, SchedulingResourceContract> resources;
    private readonly Dictionary<string, SchedulingCalendarContract> calendars;
    private readonly List<ScheduleAssignmentContract> assignments = [];
    private readonly List<ScheduleConflictContract> conflicts = [];
    private readonly List<UnscheduledOperationContract> unscheduledOperations = [];
    private readonly List<ScheduleChangeContract> changeSummary = [];
    private readonly HashSet<OperationKey> failedOperationKeys = [];
    private int conflictNumber;

    private SchedulerState(SchedulingProblemContract problem, string planId, DateTimeOffset generatedAtUtc)
    {
        this.problem = problem;
        this.planId = planId;
        this.generatedAtUtc = generatedAtUtc;
        resources = problem.Resources.ToDictionary(x => x.ResourceId, StringComparer.Ordinal);
        calendars = problem.Calendars.ToDictionary(x => x.CalendarId, StringComparer.Ordinal);
    }

    public static SchedulerState From(SchedulingProblemContract problem, string planId, DateTimeOffset generatedAtUtc)
    {
        return new SchedulerState(problem, planId, generatedAtUtc);
    }

    public void ReserveLockedAssignments()
    {
        foreach (var locked in problem.LockedAssignments
                     .OrderBy(x => x.StartUtc)
                     .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                     .ThenBy(x => x.OperationId, StringComparer.Ordinal))
        {
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
            assignments.Add(assignment);
            changeSummary.Add(new ScheduleChangeContract(
                locked.OrderId,
                locked.OperationId,
                ScheduleChangeTypeContract.Preserved,
                "Locked assignment reserved before APS lite scheduling."));

            if (!resources.TryGetValue(locked.ResourceId, out var resource)
                || locked.StartUtc < problem.HorizonStartUtc
                || locked.EndUtc > problem.HorizonEndUtc
                || !IsInsideCalendar(resource, locked.StartUtc, locked.EndUtc)
                || IsUnavailable(resource, locked.StartUtc, locked.EndUtc))
            {
                AddConflict(
                    ScheduleConflictReasonCodeContract.InvalidLockedAssignment,
                    ScheduleConflictSeverityContract.Error,
                    locked.OrderId,
                    locked.OperationId,
                    locked.ResourceId,
                    "Locked assignment is outside the scheduling horizon, calendar, resource set, or availability.");
            }
        }
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
                    "Operation predecessor could not be scheduled in this problem.");
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
                            "Operation predecessor could not be scheduled in this problem.");
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

            assignments.Add(result);
            scheduledOperationKeys.Add(OperationKey.From(result));
            changeSummary.Add(new ScheduleChangeContract(
                result.OrderId,
                result.OperationId,
                result.EndUtc > item.Operation.DueUtc ? ScheduleChangeTypeContract.Delayed : ScheduleChangeTypeContract.Added,
                result.EndUtc > item.Operation.DueUtc
                    ? "Assignment finishes after due date."
                    : "Scheduled by APS lite."));

            if (result.EndUtc > item.Operation.DueUtc)
            {
                AddConflict(
                    ScheduleConflictReasonCodeContract.DueDate,
                    ScheduleConflictSeverityContract.Warning,
                    result.OrderId,
                    result.OperationId,
                    result.ResourceId,
                    "Assignment finishes after due date.");
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

        return new SchedulePlanContract(
            ContractVersion: problem.ContractVersion,
            PlanId: planId,
            ProblemId: problem.ProblemId,
            ProblemFingerprint: Fingerprint(problem),
            AlgorithmVersion: FiniteCapacityScheduler.AlgorithmVersion,
            Status: SchedulePlanStatusContract.Preview,
            GeneratedAtUtc: generatedAtUtc,
            Assignments: orderedAssignments,
            ResourceLoads: BuildResourceLoads(orderedAssignments),
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
                    ConflictReasonCode: conflictByOperation.GetValueOrDefault(operationKey));
            }).ToList());
    }

    private ScheduleAssignmentContract? TrySchedule(OperationWorkItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Operation.QualityBlockReason))
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, item.Operation.QualityBlockReason);
            return null;
        }

        var openEndedMaterialBlock = ApplicableOpenEndedMaterialBlocks(item).FirstOrDefault();
        if (openEndedMaterialBlock is not null)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.Material, MaterialBlockMessage(openEndedMaterialBlock));
            return null;
        }

        var qualityBlocks = ApplicableOperationQualityBlocks(item).ToList();
        var openEndedQualityBlock = qualityBlocks.FirstOrDefault(x => x.BlockedUntilUtc is null);
        if (openEndedQualityBlock is not null)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, openEndedQualityBlock.ReasonCode);
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
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.OutsideHorizon, "Operation earliest start is outside the scheduling horizon.");
            return null;
        }

        var candidates = EligibleResources(item).ToList();
        if (candidates.Count == 0)
        {
            AddUnscheduled(item, ScheduleConflictReasonCodeContract.NoEligibleResource, "No eligible resource can run the required capability.");
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
                AddUnscheduled(item, ScheduleConflictReasonCodeContract.Quality, openEndedResourceQualityBlocks[0]!.ReasonCode);
                return null;
            }

            AddUnscheduled(item, ScheduleConflictReasonCodeContract.OutsideHorizon, "No feasible capacity slot exists inside the scheduling horizon.");
            return null;
        }

        var selected = feasibleSlots[0];
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
            ExplanationCode: "scheduled");
    }

    private IEnumerable<SchedulingResourceContract> EligibleResources(OperationWorkItem item)
    {
        var eligibleIds = item.Operation.EligibleResourceIds.ToHashSet(StringComparer.Ordinal);
        return resources.Values
            .Where(resource => eligibleIds.Contains(resource.ResourceId))
            .Where(resource => resource.CapabilityCodes.Contains(item.Operation.RequiredCapabilityCode, StringComparer.Ordinal))
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
        foreach (var shift in calendar.ShiftWindows
                     .OrderBy(x => x.StartUtc)
                     .ThenBy(x => x.EndUtc)
                     .Where(x => x.EndUtc > earliestStart && x.StartUtc < problem.HorizonEndUtc))
        {
            var candidate = Max(earliestStart, shift.StartUtc, problem.HorizonStartUtc);
            var latestEnd = Min(shift.EndUtc, problem.HorizonEndUtc);

            while (candidate + duration <= latestEnd)
            {
                var end = candidate + duration;
                var blockingEnd = BlockingEnd(resource, resourceQualityBlocks, candidate, end);
                if (blockingEnd is null)
                {
                    return (candidate, end);
                }

                candidate = blockingEnd.Value;
            }
        }

        return null;
    }

    private DateTimeOffset? BlockingEnd(
        SchedulingResourceContract resource,
        IReadOnlyCollection<SchedulingQualityBlockContract> resourceQualityBlocks,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var qualityBlockEnd = resourceQualityBlocks
            .Where(x => x.BlockedUntilUtc.HasValue)
            .Where(x => Overlaps(startUtc, endUtc, startUtc, x.BlockedUntilUtc!.Value))
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

        var capacity = Math.Max(1, resource.CapacityUnits);
        return CapacityBlockEnd(resource, startUtc, endUtc, capacity);
    }

    private DateTimeOffset? CapacityBlockEnd(
        SchedulingResourceContract resource,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int capacity)
    {
        var overlappingAssignments = assignments
            .Where(x => x.ResourceId == resource.ResourceId)
            .Where(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc))
            .ToList();
        if (overlappingAssignments.Count < capacity)
        {
            return null;
        }

        var boundaries = overlappingAssignments
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

            var concurrentAssignments = overlappingAssignments.Count(x =>
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
            && calendar.ShiftWindows.Any(x => x.StartUtc <= startUtc && x.EndUtc >= endUtc);
    }

    private bool IsUnavailable(SchedulingResourceContract resource, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        return problem.UnavailabilityWindows
            .Where(x => AppliesTo(x, resource))
            .Any(x => Overlaps(startUtc, endUtc, x.StartUtc, x.EndUtc));
    }

    private IReadOnlyCollection<ScheduleResourceLoadContract> BuildResourceLoads(IReadOnlyCollection<ScheduleAssignmentContract> orderedAssignments)
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
                    var assignedMinutes = orderedAssignments
                        .Where(x => x.ResourceId == resource.ResourceId)
                        .Sum(x => OverlapMinutes(x.StartUtc, x.EndUtc, shift.StartUtc, shift.EndUtc));
                    var unavailableMinutes = problem.UnavailabilityWindows
                        .Where(x => AppliesTo(x, resource))
                        .Sum(x => OverlapMinutes(x.StartUtc, x.EndUtc, shift.StartUtc, shift.EndUtc));
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

    private void AddUnscheduled(
        OperationWorkItem item,
        ScheduleConflictReasonCodeContract reasonCode,
        string? message)
    {
        failedOperationKeys.Add(OperationKey.From(item));
        var reasonMessage = string.IsNullOrWhiteSpace(message) ? reasonCode.ToString() : message;
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

    private static string MaterialBlockMessage(SchedulingMaterialReadinessContract materialReadiness)
    {
        return materialReadiness.ReasonCodes.Count == 0
            ? "material-unavailable"
            : string.Join(",", materialReadiness.ReasonCodes);
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
}
