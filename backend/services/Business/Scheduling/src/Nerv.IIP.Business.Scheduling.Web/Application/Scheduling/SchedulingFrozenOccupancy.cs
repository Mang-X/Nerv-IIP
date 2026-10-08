using System.Text.Json;
using System.Text.Json.Nodes;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed record SchedulingFrozenAssignmentSnapshot(
    ScheduleAssignmentContract Assignment,
    int Reasons);

public sealed record SchedulingFreezeSnapshot(
    DateTimeOffset AsOfUtc,
    TimeSpan DefaultWindow,
    IReadOnlyDictionary<string, TimeSpan> WorkCenterWindows,
    IReadOnlyCollection<SchedulingFrozenAssignmentSnapshot> Assignments)
{
    internal static SchedulingFreezeSnapshot From(
        SchedulingFreezePolicy policy,
        IReadOnlyCollection<SchedulingFrozenAssignment> assignments) => new(
            policy.AsOfUtc, policy.DefaultWindow, policy.WorkCenterWindows,
            assignments.Select(x => new SchedulingFrozenAssignmentSnapshot(x.Assignment, (int)x.Reasons)).ToArray());
}

internal static class SchedulingFrozenOccupancy
{
    public const string BaselineLockReasonCode = "baseline-freeze";
    private const string SnapshotProperty = "fixedWorkCenterReservations";
    private const string FreezeProperty = "freeze";

    public static string SerializeSnapshot(
        SchedulingProblemContract problem,
        IReadOnlyCollection<FixedWorkCenterReservation> reservations,
        SchedulingFreezeSnapshot? freeze = null,
        SchedulingEquipmentAvailabilitySnapshotContract? equipmentAvailability = null)
    {
        if (reservations.Count == 0 && freeze is null && equipmentAvailability is null)
        {
            return JsonSerializer.Serialize(problem, SchedulingJson.Options);
        }

        var snapshot = JsonSerializer.SerializeToNode(problem, SchedulingJson.Options)!.AsObject();
        if (reservations.Count > 0)
        {
            snapshot[SnapshotProperty] = JsonSerializer.SerializeToNode(
                reservations.OrderBy(x => x.OrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                    .ToArray(),
                SchedulingJson.Options);
        }
        if (freeze is not null)
        {
            snapshot[FreezeProperty] = JsonSerializer.SerializeToNode(freeze with
            {
                WorkCenterWindows = freeze.WorkCenterWindows
                        .OrderBy(x => x.Key, StringComparer.Ordinal)
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                Assignments = freeze.Assignments
                    .OrderBy(x => x.Assignment.OrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.Assignment.OperationId, StringComparer.Ordinal)
                    .ToArray()
            }, SchedulingJson.Options);
        }
        if (equipmentAvailability is not null)
        {
            snapshot["equipmentAvailability"] = JsonSerializer.SerializeToNode(equipmentAvailability, SchedulingJson.Options);
        }
        return snapshot.ToJsonString(SchedulingJson.Options);
    }

    public static IReadOnlyCollection<FixedWorkCenterReservation> ReadSnapshot(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return document.RootElement.TryGetProperty(SnapshotProperty, out var reservations)
            ? reservations.Deserialize<FixedWorkCenterReservation[]>(SchedulingJson.Options) ?? []
            : [];
    }

    public static SchedulingFreezeSnapshot? ReadFreezeSnapshot(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return document.RootElement.TryGetProperty(FreezeProperty, out var freeze)
            ? freeze.Deserialize<SchedulingFreezeSnapshot>(SchedulingJson.Options)
            : null;
    }

    public static SchedulePlanFreezeContextContract? ToContract(SchedulingFreezeSnapshot? freeze) =>
        freeze is null ? null : new(
            freeze.AsOfUtc,
            freeze.AsOfUtc + freeze.DefaultWindow,
            freeze.WorkCenterWindows.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new SchedulePlanFreezeWorkCenterWindowContract(x.Key, freeze.AsOfUtc + x.Value))
                .ToArray(),
            freeze.Assignments.OrderBy(x => x.Assignment.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.Assignment.OperationId, StringComparer.Ordinal)
                .Select(x => new SchedulePlanFrozenAssignmentContract(x.Assignment,
                    Enum.GetValues<SchedulePlanFreezeReasonContract>()
                        .Where(reason => (x.Reasons & (int)reason) != 0).ToArray()))
                .ToArray());

    public static IReadOnlyCollection<ScheduleAssignmentContract> ExternalFrozenAssignments(
        SchedulingFreezeSnapshot? freeze,
        SchedulingProblemContract problem,
        IReadOnlyCollection<FixedWorkCenterReservation> reservations)
    {
        if (freeze is null)
        {
            return [];
        }

        var scheduledKeys = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => (order.OrderId, operation.OperationId)))
            .ToHashSet();
        var actualKeys = reservations.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        return freeze.Assignments
            .Where(x => !scheduledKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)) &&
                !actualKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)))
            .Select(x => x.Assignment)
            .ToArray();
    }
}
