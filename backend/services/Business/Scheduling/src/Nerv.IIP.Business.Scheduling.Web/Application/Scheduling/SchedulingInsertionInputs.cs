using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class SchedulingInsertionInputs(
    ApplicationDbContext db, SchedulingWorkbenchPlanAssembler assembler, FiniteCapacityScheduler scheduler,
    ISchedulingEquipmentAvailabilityProvider equipmentAvailabilityProvider,
    ISchedulingMaterialReadinessProvider materialReadinessProvider,
    ISchedulingOperationOverrideOverlay overrideOverlay, SchedulingEquipmentUnknownModeOption equipmentUnknownMode)
{
    internal async Task<SchedulingInsertionCalculationSnapshotContract> LoadAsync(
        SchedulingInsertionPreviewInputContract input, SchedulePlanContract baseline,
        SchedulingProblemContract baselineProblem, SchedulingFreezePolicy policy, CancellationToken ct)
    {
        var priorFreeze = baseline.FreezeContext;
        var baselineKeys = baseline.Assignments.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var baselineAssignments = baseline.Assignments.Concat((priorFreeze?.Assignments.Select(x => x.Assignment) ?? [])
            .Where(x => !baselineKeys.Contains((x.OrderId, x.OperationId)))).ToArray();
        var assembled = await assembler.AssembleAsync(input.OrganizationId, input.EnvironmentId,
            input.HorizonStartUtc, input.HorizonEndUtc,
            input.WorkOrderIds.Select(x => new SchedulingWorkbenchOrderSelection(x, 0, false)).ToArray(), ct);
        var baselineOrderIds = baselineAssignments.Select(x => x.OrderId).Distinct(StringComparer.Ordinal).ToArray();
        var execution = await db.OperationExecutionProjections.AsNoTracking().Where(x =>
            x.OrganizationId == input.OrganizationId && x.EnvironmentId == input.EnvironmentId &&
            baselineOrderIds.Contains(x.WorkOrderId)).ToArrayAsync(ct);
        var manualLocks = ManualLocks(baseline, baselineProblem);
        var frozen = SchedulingFreezeCalculator.Calculate(baselineAssignments,
            execution.Select(x => new SchedulingFreezeExecutionFact(x.WorkOrderId, x.OperationId,
                x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(), manualLocks, policy);
        var fixedKeys = assembled.FixedReservations.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var operationKeys = assembled.Problem.Orders.SelectMany(order =>
            order.Operations.Select(operation => (order.OrderId, operation.OperationId))).ToHashSet();
        var frozenLocks = frozen.Where(x => operationKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)) &&
            !fixedKeys.Contains((x.Assignment.OrderId, x.Assignment.OperationId)))
            .Select(x => new SchedulingLockedAssignmentContract(x.Assignment.AssignmentId,
                x.Assignment.OrderId, x.Assignment.OperationId, x.Assignment.OperationSequence,
                x.Assignment.ResourceId, x.Assignment.WorkCenterId, x.Assignment.StartUtc, x.Assignment.EndUtc,
                SchedulingFrozenOccupancy.BaselineLockReasonCode, x.Assignment.Segments)).ToArray();
        var frozenKeys = frozenLocks.Select(x => (x.OrderId, x.OperationId)).ToHashSet();
        var problem = assembled.Problem with
        {
            LockedAssignments = frozenLocks.Concat(assembled.Problem.LockedAssignments.Where(x =>
                !frozenKeys.Contains((x.OrderId, x.OperationId)) && !fixedKeys.Contains((x.OrderId, x.OperationId)))).ToArray()
        };
        problem = await overrideOverlay.ApplyAsync(problem, ct);
        var availability = await equipmentAvailabilityProvider.QueryAsync(problem, ct);
        var equipmentInput = SchedulingEquipmentAvailabilitySnapshot.Create(availability, policy.AsOfUtc);
        var material = await materialReadinessProvider.QueryAsync(problem, ct);
        problem = SchedulingProblemNormalizer.Normalize(MaterialReadinessSchedulingAdapter.Apply(
            EquipmentAvailabilitySchedulingAdapter.Apply(problem, availability, equipmentUnknownMode.Mode), material))
            with { ProblemId = baseline.ProblemId };
        var locksByKey = problem.LockedAssignments.ToDictionary(x => (x.OrderId, x.OperationId));
        // Frozen baseline wins over active override; ordinary overrides remain the authoritative manual position.
        var calculationBaseline = baseline with
        {
            Assignments = baseline.Assignments.Where(x => x.OrderId != input.WorkOrderId).Select(x =>
                locksByKey.TryGetValue((x.OrderId, x.OperationId), out var locked) &&
                locked.LockReasonCode != SchedulingFrozenOccupancy.BaselineLockReasonCode
                    ? x with { ResourceId = locked.ResourceId, WorkCenterId = locked.WorkCenterId,
                        StartUtc = locked.StartUtc, EndUtc = locked.EndUtc, Segments = locked.Segments,
                        IsLocked = true, ExplanationCode = locked.LockReasonCode } : x).ToArray(),
            UnscheduledOperations = baseline.UnscheduledOperations.Where(x => x.OrderId != input.WorkOrderId).ToArray()
        };
        var freeze = SchedulingFrozenOccupancy.ToContract(SchedulingFreezeSnapshot.From(policy, frozen))!;
        return new SchedulingInsertionCalculationSnapshotContract(problem, baseline, calculationBaseline,
            freeze, execution.Select(x => new SchedulingInsertionExecutionFactContract(x.WorkOrderId, x.OperationId,
                x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(),
            assembled.FixedReservations.Select(x => new SchedulePlanFixedReservationContract(x.OrderId, x.OperationId,
                x.WorkCenterId, x.StartUtc, x.EndUtc, x.ResourceId)).ToArray(), scheduler.MaterialConstraintMode,
            scheduler.QualityConstraintMode, equipmentUnknownMode.Mode, equipmentInput);
    }

    internal static (string, string)[] ManualLocks(SchedulePlanContract baseline, SchedulingProblemContract baselineProblem) =>
        baselineProblem.LockedAssignments
            .Where(x => x.LockReasonCode != SchedulingFrozenOccupancy.BaselineLockReasonCode)
            .Select(x => (x.OrderId, x.OperationId))
            .Concat(baseline.FreezeContext?.Assignments.Where(x =>
                x.Reasons.Contains(SchedulePlanFreezeReasonContract.ManualLock))
                .Select(x => (x.Assignment.OrderId, x.Assignment.OperationId)) ?? [])
            .Distinct().ToArray();

    internal static FixedWorkCenterReservation[] FixedReservations(SchedulingInsertionCalculationSnapshotContract snapshot) =>
        snapshot.FixedReservations.Select(x => new FixedWorkCenterReservation(
            x.OrderId, x.OperationId,
            snapshot.Problem.Orders.Single(order => order.OrderId == x.OrderId).Operations
                .Single(operation => operation.OperationId == x.OperationId).OperationSequence,
            x.WorkCenterId, x.StartUtc, x.EndUtc, x.ResourceId)).ToArray();

    internal static SchedulingFreezeSnapshot FreezeSnapshot(SchedulePlanFreezeContextContract freeze) =>
        new(freeze.AsOfUtc, freeze.DefaultWindowEndUtc - freeze.AsOfUtc,
            freeze.WorkCenterWindows.ToDictionary(x => x.WorkCenterId, x => x.EndUtc - freeze.AsOfUtc, StringComparer.Ordinal),
            freeze.Assignments.Select(x => new SchedulingFrozenAssignmentSnapshot(x.Assignment,
                x.Reasons.Aggregate(0, (reasons, reason) => reasons | (int)reason))).ToArray());

    internal static string ComparableInput(SchedulingInsertionCalculationSnapshotContract snapshot) =>
        ReschedulingImpactAnalyzer.CanonicalJson(new
        {
            snapshot.Problem,
            snapshot.Freeze,
            snapshot.Execution,
            snapshot.FixedReservations,
            snapshot.MaterialMode,
            snapshot.QualityMode,
            snapshot.EquipmentUnknownMode,
            snapshot.EquipmentAvailability
        });
}
