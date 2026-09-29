using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public static class SchedulePlanMaterialShortageSummary
{
    public static IReadOnlyCollection<SchedulePlanMaterialShortageSummaryContract> Project(
        IEnumerable<SchedulePlanMaterialRiskContract> risks)
    {
        return risks
            .SelectMany(risk => risk.Shortages.Select(shortage => (risk, shortage)))
            .Where(x => x.shortage.ShortageQuantity > 0)
            .GroupBy(x => (x.shortage.MaterialId, x.shortage.MaterialLotId, x.shortage.UomCode))
            .OrderBy(x => x.Key.MaterialId, StringComparer.Ordinal)
            .ThenBy(x => x.Key.MaterialLotId, StringComparer.Ordinal)
            .ThenBy(x => x.Key.UomCode, StringComparer.Ordinal)
            .Select(group => new SchedulePlanMaterialShortageSummaryContract(
                group.Key.MaterialId,
                group.Key.MaterialLotId,
                group.Key.UomCode,
                group.GroupBy(x => x.risk.OrderId, StringComparer.Ordinal)
                    .Sum(order => order.Max(x => x.shortage.ShortageQuantity)),
                group.Select(x => new SchedulePlanMaterialAffectedOperationContract(
                        x.risk.OrderId, x.risk.OperationId))
                    .Distinct()
                    .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }
}
