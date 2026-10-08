using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal static class SchedulingDowntimeImpactProjector
{
    public static SchedulingDowntimeImpactResponse Project(SchedulingProblemContract problem, SchedulePlanContract baseline,
        IReadOnlyCollection<SchedulingDowntimeFactContract> facts, EquipmentRuntimeAvailabilityResponse availability, DateTimeOffset asOf)
    {
        var operations = problem.Orders.SelectMany(order => order.Operations.Select(operation =>
            (Key: (order.OrderId, operation.OperationId), Operation: operation))).ToDictionary(x => x.Key, x => x.Operation);
        var current = availability.Items.Where(x => x.StartUtc <= asOf && asOf < x.EndUtc)
            .GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var confirmed = current.Where(x => x.Value.Any(w => w.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Available)
                && x.Value.All(w => w.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Available))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        // MES and maintenance raw facts also block a currently reported Available device.
        confirmed.ExceptWith(facts.Where(x => x.DeviceAssetId is not null && x.StartedAtUtc <= asOf
            && (x.RecoveredAtUtc is null || asOf < x.RecoveredAtUtc)).Select(x => x.DeviceAssetId!));
        var items = facts.Where(x => x.StartedAtUtc < problem.HorizonEndUtc
                && (x.RecoveredAtUtc is null || problem.HorizonStartUtc < x.RecoveredAtUtc)).OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.SourceReferenceId, StringComparer.Ordinal).Select(fact =>
        {
            var affected = baseline.Assignments.Where(assignment =>
                    (fact.DeviceAssetId is not null ? assignment.ResourceId == fact.DeviceAssetId : assignment.WorkCenterId == fact.WorkCenterId)
                    && Overlaps(assignment, fact))
                .DistinctBy(x => (x.OrderId, x.OperationId)).OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal).Select(assignment =>
                {
                    var operation = operations[(assignment.OrderId, assignment.OperationId)];
                    var requiredCodes = new[] { operation.RequiredCapabilityCode }.Concat(operation.RequiredSkillCodes ?? []);
                    var alternatives = problem.Resources.Where(resource => resource.ResourceId != assignment.ResourceId
                            && operation.EligibleResourceIds.Contains(resource.ResourceId, StringComparer.Ordinal)
                            && requiredCodes.All(code => resource.CapabilityCodes.Contains(code, StringComparer.Ordinal))
                            && confirmed.Contains(resource.ResourceId)
                            && !facts.Any(x => x.DeviceAssetId is null && x.WorkCenterId == resource.WorkCenterId
                                && x.StartedAtUtc <= asOf && (x.RecoveredAtUtc is null || asOf < x.RecoveredAtUtc)))
                        .Select(x => x.ResourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                    return new SchedulingDowntimeAffectedOperationContract(assignment.OrderId, assignment.OperationId, alternatives);
                }).ToArray();
            return new SchedulingDowntimeImpactContract(fact, affected, affected.Count(x => x.AvailableAlternativeResourceIds.Count > 0));
        }).ToArray();
        var affectedOperations = items.SelectMany(x => x.AffectedOperations)
            .DistinctBy(x => (x.WorkOrderId, x.OperationId)).OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray();
        return new(baseline.PlanId, baseline.ProblemId, asOf, items, affectedOperations,
            affectedOperations.Count(x => x.AvailableAlternativeResourceIds.Count > 0));
    }

    private static bool Overlaps(ScheduleAssignmentContract assignment, SchedulingDowntimeFactContract fact)
    {
        var end = fact.RecoveredAtUtc ?? DateTimeOffset.MaxValue;
        return assignment.Segments is { Count: > 0 } segments
            ? segments.Any(x => x.StartUtc < end && fact.StartedAtUtc < x.EndUtc)
            : assignment.StartUtc < end && fact.StartedAtUtc < assignment.EndUtc;
    }
}
