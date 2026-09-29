using System.Text.Json;
using System.Text.Json.Nodes;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal static class SchedulingFrozenOccupancy
{
    private const string SnapshotProperty = "fixedWorkCenterReservations";

    public static string SerializeSnapshot(
        SchedulingProblemContract problem,
        IReadOnlyCollection<FixedWorkCenterReservation> reservations)
    {
        if (reservations.Count == 0)
        {
            return JsonSerializer.Serialize(problem, SchedulingJson.Options);
        }

        var snapshot = JsonSerializer.SerializeToNode(problem, SchedulingJson.Options)!.AsObject();
        snapshot[SnapshotProperty] = JsonSerializer.SerializeToNode(
            reservations.OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .ToArray(),
            SchedulingJson.Options);
        return snapshot.ToJsonString(SchedulingJson.Options);
    }

    public static IReadOnlyCollection<FixedWorkCenterReservation> ReadSnapshot(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return document.RootElement.TryGetProperty(SnapshotProperty, out var reservations)
            ? reservations.Deserialize<FixedWorkCenterReservation[]>(SchedulingJson.Options) ?? []
            : [];
    }
}
