using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public static class EquipmentAvailabilitySchedulingAdapter
{
    public static IReadOnlyCollection<SchedulingUnavailabilityWindowContract> ToUnavailabilityWindows(
        EquipmentRuntimeAvailabilityResponse availability)
    {
        ArgumentNullException.ThrowIfNull(availability);

        return availability.Items
            .Where(x => x.AvailabilityStatus != EquipmentRuntimeAvailabilityStatus.Available)
            .Where(x => x.EndUtc > x.StartUtc)
            .Select(x => new SchedulingUnavailabilityWindowContract(
                ResourceId: x.DeviceAssetId,
                WorkCenterId: x.WorkCenterId,
                StartUtc: x.StartUtc,
                EndUtc: x.EndUtc,
                ReasonCode: x.ReasonCode))
            .ToArray();
    }

    public static SchedulingProblemContract Apply(
        SchedulingProblemContract problem,
        EquipmentRuntimeAvailabilityResponse availability)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(availability);

        if (!string.Equals(problem.OrganizationId, availability.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(problem.EnvironmentId, availability.EnvironmentId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Equipment runtime availability context does not match scheduling problem context.",
                nameof(availability));
        }

        var windows = problem.UnavailabilityWindows
            .Concat(ToUnavailabilityWindows(availability))
            .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ThenBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();

        return problem with { UnavailabilityWindows = windows };
    }
}
