namespace Nerv.IIP.Business.Mes.Web.Application.Scheduling;

public enum OperationTaskStatus
{
    Queued,
    InProgress,
    Completed,
    Cancelled,
}

public sealed record WorkCenterUnavailability(
    string WorkCenterId,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string Reason,
    string? DeviceAssetId = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed record ScheduleOperation(
    string WorkOrderId,
    string OperationTaskId,
    OperationTaskStatus Status,
    int OperationSequence,
    int Priority,
    DateTimeOffset DueUtc,
    DateTimeOffset EarliestStartUtc,
    TimeSpan Duration,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset? ExistingStartUtc = null,
    DateTimeOffset? ExistingEndUtc = null);

public sealed record ScheduledOperation(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Reason);

public sealed record RuleSchedulePlan(IReadOnlyCollection<ScheduledOperation> Assignments);

public sealed class RuleScheduler
{
    public RuleSchedulePlan Schedule(
        IReadOnlyCollection<ScheduleOperation> operations,
        IReadOnlyCollection<WorkCenterUnavailability> unavailabilities)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(unavailabilities);

        var assignments = new List<ScheduledOperation>();
        var workCenterAvailableAt = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations.Where(x => x.Status == OperationTaskStatus.InProgress))
        {
            var start = operation.ExistingStartUtc ?? operation.EarliestStartUtc;
            var end = operation.ExistingEndUtc ?? start.Add(operation.Duration);
            assignments.Add(new ScheduledOperation(
                operation.WorkOrderId,
                operation.OperationTaskId,
                operation.WorkCenterId,
                start,
                end,
                "in-progress-preserved"));
            workCenterAvailableAt[operation.WorkCenterId] = Max(GetAvailableAt(workCenterAvailableAt, operation.WorkCenterId), end);
        }

        var queue = operations
            .Where(x => x.Status == OperationTaskStatus.Queued)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.DueUtc)
            .ThenBy(x => x.OperationSequence)
            .ThenBy(x => x.EarliestStartUtc)
            .ToList();

        foreach (var operation in queue)
        {
            var candidate = ChooseEarliestCandidate(operation, unavailabilities, workCenterAvailableAt);
            assignments.Add(new ScheduledOperation(
                operation.WorkOrderId,
                operation.OperationTaskId,
                candidate.WorkCenterId,
                candidate.StartUtc,
                candidate.EndUtc,
                "rule-sequenced"));
            workCenterAvailableAt[candidate.WorkCenterId] = candidate.EndUtc;
        }

        return new RuleSchedulePlan(assignments.OrderBy(x => x.StartUtc).ThenBy(x => x.OperationTaskId).ToList());
    }

    private static CandidateSlot ChooseEarliestCandidate(
        ScheduleOperation operation,
        IReadOnlyCollection<WorkCenterUnavailability> unavailabilities,
        IReadOnlyDictionary<string, DateTimeOffset> workCenterAvailableAt)
    {
        var workCenters = new[] { operation.WorkCenterId }
            .Concat(operation.AlternativeWorkCenterIds)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return workCenters
            .Select(workCenterId =>
            {
                var earliest = Max(operation.EarliestStartUtc, GetAvailableAt(workCenterAvailableAt, workCenterId));
                var start = FindFirstAvailableStart(workCenterId, earliest, operation.Duration, unavailabilities);
                return new CandidateSlot(workCenterId, start, start.Add(operation.Duration));
            })
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.WorkCenterId == operation.WorkCenterId ? 0 : 1)
            .ThenBy(x => x.WorkCenterId, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static DateTimeOffset FindFirstAvailableStart(
        string workCenterId,
        DateTimeOffset start,
        TimeSpan duration,
        IReadOnlyCollection<WorkCenterUnavailability> unavailabilities)
    {
        var candidate = start;
        while (true)
        {
            var conflict = unavailabilities
                .Where(x => string.Equals(x.WorkCenterId, workCenterId, StringComparison.OrdinalIgnoreCase))
                .Where(x => Overlaps(candidate, candidate.Add(duration), x.FromUtc, x.ToUtc))
                .OrderBy(x => x.FromUtc)
                .FirstOrDefault();

            if (conflict is null)
            {
                return candidate;
            }

            if (conflict.ToUtc is null)
            {
                return DateTimeOffset.MaxValue - duration;
            }

            candidate = conflict.ToUtc.Value;
        }
    }

    private static bool Overlaps(DateTimeOffset start, DateTimeOffset end, DateTimeOffset unavailableFrom, DateTimeOffset? unavailableTo)
    {
        var unavailableEnd = unavailableTo ?? DateTimeOffset.MaxValue;
        return start < unavailableEnd && end > unavailableFrom;
    }

    private static DateTimeOffset GetAvailableAt(IReadOnlyDictionary<string, DateTimeOffset> values, string workCenterId)
    {
        return values.TryGetValue(workCenterId, out var value) ? value : DateTimeOffset.MinValue;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left >= right ? left : right;
    }

    private sealed record CandidateSlot(string WorkCenterId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
}
