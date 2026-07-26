using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class DefaultSchedulingConstraintProvider(
    ISchedulingEquipmentAvailabilityProvider equipmentAvailabilityProvider,
    ISchedulingMaterialReadinessProvider materialReadinessProvider)
    : ISchedulingConstraintProvider
{
    public const string EquipmentSourceId = "equipment-runtime-availability";
    public const string MaterialSourceId = "material-readiness";
    private const int MaxSummaryReasonCodes = 16;

    public async Task<SchedulingConstraintProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var availability = await equipmentAvailabilityProvider.QueryAsync(problem, cancellationToken);
        var materialReadiness = await materialReadinessProvider.QueryAsync(problem, cancellationToken);
        var effectiveProblem = MaterialReadinessSchedulingAdapter.Apply(
            EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability),
            materialReadiness);

        return new SchedulingConstraintProviderResult(
            problem,
            effectiveProblem,
            [
                SummarizeEquipment(availability),
                SummarizeMaterial(materialReadiness)
            ]);
    }

    private static SchedulingProviderSummary SummarizeEquipment(
        EquipmentRuntimeAvailabilityResponse availability)
    {
        var reasonCodes = NormalizeReasonCodes(availability.Items.Select(x => x.ReasonCode));
        return new SchedulingProviderSummary(
            EquipmentSourceId,
            availability.Items.Count == 0
                ? SchedulingProviderOutcome.NoData
                : reasonCodes.Contains(
                    HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode,
                    StringComparer.Ordinal)
                    ? SchedulingProviderOutcome.Degraded
                    : SchedulingProviderOutcome.Applied,
            availability.Items.Count,
            reasonCodes);
    }

    private static SchedulingProviderSummary SummarizeMaterial(
        IReadOnlyCollection<SchedulingMaterialReadinessContract> materialReadiness)
    {
        var reasonCodes = NormalizeReasonCodes(materialReadiness.SelectMany(x => x.ReasonCodes));
        return new SchedulingProviderSummary(
            MaterialSourceId,
            materialReadiness.Count == 0
                ? SchedulingProviderOutcome.NoData
                : reasonCodes.Contains(
                    HttpSchedulingMaterialReadinessProvider.SourceUnavailableReasonCode,
                    StringComparer.Ordinal)
                    ? SchedulingProviderOutcome.Degraded
                    : SchedulingProviderOutcome.Applied,
            materialReadiness.Count,
            reasonCodes);
    }

    private static IReadOnlyCollection<string> NormalizeReasonCodes(IEnumerable<string> reasonCodes)
    {
        return reasonCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaxSummaryReasonCodes)
            .ToArray();
    }
}
