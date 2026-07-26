using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class DefaultSchedulingConstraintProvider(
    ISchedulingOperationOverrideOverlay overrideOverlay,
    ISchedulingEquipmentAvailabilityProvider equipmentAvailabilityProvider,
    ISchedulingMaterialReadinessProvider materialReadinessProvider)
    : ISchedulingConstraintProvider
{
    public const string OperationOverrideSourceId = "operation-overrides";
    public const string EquipmentSourceId = "equipment-runtime-availability";
    public const string MaterialSourceId = "material-readiness";
    private const string SourceVersion = "v1";
    private const string AppliedSummaryCode = "facts-applied";
    private const string NoDataSummaryCode = "no-data";
    private const string DegradedSummaryCode = "source-unavailable";

    public async Task<SchedulingConstraintProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var baseProblem = await overrideOverlay.ApplyAsync(problem, cancellationToken);
        var availability = await equipmentAvailabilityProvider.QueryAsync(baseProblem, cancellationToken);
        var materialReadiness = await materialReadinessProvider.QueryAsync(baseProblem, cancellationToken);
        var effectiveProblem = MaterialReadinessSchedulingAdapter.Apply(
            EquipmentAvailabilitySchedulingAdapter.Apply(baseProblem, availability),
            materialReadiness);

        return new SchedulingConstraintProviderResult(
            baseProblem,
            effectiveProblem,
            [
                SummarizeOperationOverrides(baseProblem.LockedAssignments),
                SummarizeEquipment(availability),
                SummarizeMaterial(materialReadiness)
            ]);
    }

    private static SchedulingProviderSummary SummarizeOperationOverrides(
        IReadOnlyCollection<SchedulingLockedAssignmentContract> lockedAssignments)
    {
        return CreateSummary(
            OperationOverrideSourceId,
            lockedAssignments.Count == 0
                ? SchedulingProviderOutcome.NoData
                : SchedulingProviderOutcome.Applied,
            lockedAssignments.Count,
            SchedulingFactsFingerprint.Create(lockedAssignments));
    }

    private static SchedulingProviderSummary SummarizeEquipment(
        EquipmentRuntimeAvailabilityResponse availability)
    {
        var outcome = availability.Items.Count == 0
            ? SchedulingProviderOutcome.NoData
            : availability.Items.Any(x => string.Equals(
                x.ReasonCode,
                HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode,
                StringComparison.Ordinal))
                ? SchedulingProviderOutcome.Degraded
                : SchedulingProviderOutcome.Applied;
        return CreateSummary(
            EquipmentSourceId,
            outcome,
            availability.Items.Count,
            SchedulingFactsFingerprint.Create(
                availability.Items,
                item => item with
                {
                    SubstituteDeviceAssetIds = item.SubstituteDeviceAssetIds
                        .Order(StringComparer.Ordinal)
                        .ToArray()
                }));
    }

    private static SchedulingProviderSummary SummarizeMaterial(
        IReadOnlyCollection<SchedulingMaterialReadinessContract> materialReadiness)
    {
        var outcome = materialReadiness.Count == 0
            ? SchedulingProviderOutcome.NoData
            : materialReadiness.Any(x => x.ReasonCodes.Contains(
                HttpSchedulingMaterialReadinessProvider.SourceUnavailableReasonCode,
                StringComparer.Ordinal))
                ? SchedulingProviderOutcome.Degraded
                : SchedulingProviderOutcome.Applied;
        return CreateSummary(
            MaterialSourceId,
            outcome,
            materialReadiness.Count,
            SchedulingFactsFingerprint.Create(
                materialReadiness,
                item => item with
                {
                    ReasonCodes = item.ReasonCodes
                        .Order(StringComparer.Ordinal)
                        .ToArray()
                }));
    }

    private static SchedulingProviderSummary CreateSummary(
        string sourceId,
        SchedulingProviderOutcome outcome,
        int factCount,
        string factsFingerprint)
    {
        var summaryCode = outcome switch
        {
            SchedulingProviderOutcome.Applied => AppliedSummaryCode,
            SchedulingProviderOutcome.NoData => NoDataSummaryCode,
            SchedulingProviderOutcome.Degraded => DegradedSummaryCode,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
        return new SchedulingProviderSummary(
            sourceId,
            SourceVersion,
            outcome,
            factCount,
            factsFingerprint,
            [summaryCode]);
    }
}
