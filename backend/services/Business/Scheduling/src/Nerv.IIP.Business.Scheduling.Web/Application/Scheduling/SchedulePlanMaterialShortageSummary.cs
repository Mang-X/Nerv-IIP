using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public static class SchedulePlanMaterialShortageSummary
{
    public static IReadOnlyCollection<SchedulePlanMaterialShortageSummaryContract> Project(
        IEnumerable<SchedulePlanMaterialRiskContract> risks) =>
        ProjectOccurrences(risks.SelectMany(risk => risk.Shortages.Select(shortage =>
            new ShortageOccurrence(risk.OrderId, risk.OperationId, shortage))));

    public static IReadOnlyCollection<SchedulePlanMaterialShortageSummaryContract> Project(
        SchedulingProblemContract problem) =>
        ProjectOccurrences(problem.Orders.SelectMany(order => order.Operations.SelectMany(operation =>
            problem.MaterialReadiness
                .Where(readiness => !readiness.IsReady && AppliesTo(readiness, order, operation))
                .SelectMany(readiness => readiness.Shortages ?? [])
                .Select(shortage => new ShortageOccurrence(order.OrderId, operation.OperationId, shortage)))));

    private static IReadOnlyCollection<SchedulePlanMaterialShortageSummaryContract> ProjectOccurrences(
        IEnumerable<ShortageOccurrence> occurrences)
    {
        return occurrences
            .Where(x => x.Shortage.ShortageQuantity > 0)
            .GroupBy(x => (x.Shortage.MaterialId, x.Shortage.MaterialLotId, x.Shortage.UomCode))
            .OrderBy(x => x.Key.MaterialId, StringComparer.Ordinal)
            .ThenBy(x => x.Key.MaterialLotId, StringComparer.Ordinal)
            .ThenBy(x => x.Key.UomCode, StringComparer.Ordinal)
            .Select(group => new SchedulePlanMaterialShortageSummaryContract(
                group.Key.MaterialId,
                group.Key.MaterialLotId,
                group.Key.UomCode,
                group.GroupBy(x => x.OrderId, StringComparer.Ordinal)
                    .Sum(order => order.Max(x => x.Shortage.ShortageQuantity)),
                group.Select(x => new SchedulePlanMaterialAffectedOperationContract(
                        x.OrderId, x.OperationId))
                    .Distinct()
                    .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static bool AppliesTo(
        SchedulingMaterialReadinessContract readiness,
        SchedulingOrderContract order,
        SchedulingOperationContract operation) => readiness.ScopeType.ToLowerInvariant() switch
    {
        "operation" => string.Equals(readiness.ScopeId, operation.OperationId, StringComparison.Ordinal),
        "order" => string.Equals(readiness.ScopeId, order.OrderId, StringComparison.Ordinal),
        "sku" => string.Equals(readiness.ScopeId, order.SkuCode, StringComparison.Ordinal),
        "resource" => operation.EligibleResourceIds.Contains(readiness.ScopeId, StringComparer.Ordinal)
            || string.Equals(readiness.ScopeId, operation.PrimaryResourceId, StringComparison.Ordinal),
        _ => false
    };

    private sealed record ShortageOccurrence(
        string OrderId, string OperationId, SchedulingMaterialShortageContract Shortage);
}
