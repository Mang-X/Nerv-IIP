using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class SchedulingCandidateService(ApplicationDbContext db, ISender sender,
    SchedulingWorkbenchPlanAssembler assembler, ISchedulingEquipmentAvailabilityProvider equipment,
    ISchedulingMaterialReadinessProvider materials, ISchedulingOperationOverrideOverlay overrides,
    SchedulingFreezeSettings freezeSettings, SchedulingEquipmentUnknownModeOption equipmentMode,
    IConfiguration configuration, TimeProvider clock)
{
    internal sealed record Input(ReschedulingCandidateInput Candidate,
        IReadOnlyCollection<FixedWorkCenterReservation> Reservations);

    internal async Task<Input> ReadAsync(string org, string env, string planId, DateTimeOffset asOf, CancellationToken ct)
    {
        var baseline = await sender.Send(new GetSchedulePlanDetailQuery(planId, org, env), ct);
        if (baseline.Status is SchedulePlanStatusContract.Superseded or SchedulePlanStatusContract.Revoked)
            throw new KnownException("基线方案已被取代或撤销，请选择当前方案后重预览。");
        var snapshot = await db.ScheduleProblems.AsNoTracking().SingleAsync(x => x.ProblemId == baseline.ProblemId
            && x.OrganizationId == org && x.EnvironmentId == env, ct);
        var original = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        var priorFreeze = SchedulingFrozenOccupancy.ReadFreezeSnapshot(snapshot.ProblemJson);
        var orderIds = original.Orders.Select(x => x.OrderId).ToArray();
        var execution = await db.OperationExecutionProjections.AsNoTracking().Where(x => x.OrganizationId == org
            && x.EnvironmentId == env && orderIds.Contains(x.WorkOrderId)).ToArrayAsync(ct);
        var completedKeys = execution.Where(x => x.ActualCompletedAtUtc is not null)
            .Select(x => (x.WorkOrderId, x.OperationId)).ToHashSet();
        var completedOrders = original.Orders.Where(o => o.Operations.All(op => completedKeys.Contains((o.OrderId, op.OperationId)))).ToArray();
        var completedIds = completedOrders.Select(x => x.OrderId).ToHashSet(StringComparer.Ordinal);
        var selections = original.Orders.Where(x => !completedIds.Contains(x.OrderId))
            .Select(x => new SchedulingWorkbenchOrderSelection(x.OrderId, x.Priority, x.IsRush)).ToArray();
        // Completed MES orders no longer belong to the schedulable source. Their frozen baseline is still part of this candidate.
        var assembled = selections.Length == 0
            ? (Problem: original with { Orders = Array.Empty<SchedulingOrderContract>() }, Reservations: (IReadOnlyCollection<FixedWorkCenterReservation>)[])
            : await AssembleCurrentAsync(selections);
        var problem = await overrides.ApplyAsync(assembled.Problem with { ProblemId = original.ProblemId,
            Orders = assembled.Problem.Orders.Concat(completedOrders).ToArray() }, ct);
        problem = MaterialReadinessSchedulingAdapter.Apply(problem, await materials.QueryAsync(problem, ct));
        var availability = SchedulingEquipmentAvailabilitySnapshot.Create(await equipment.QueryAsync(problem, ct), asOf);

        async Task<(SchedulingProblemContract Problem, IReadOnlyCollection<FixedWorkCenterReservation> Reservations)> AssembleCurrentAsync(
            SchedulingWorkbenchOrderSelection[] orders)
        {
            var result = await assembler.AssembleAsync(org, env, original.HorizonStartUtc, original.HorizonEndUtc, orders, ct);
            return (result.Problem, result.FixedReservations);
        }
        var manualLocks = original.LockedAssignments.Where(x => x.LockReasonCode != SchedulingFrozenOccupancy.BaselineLockReasonCode)
            .Select(x => (x.OrderId, x.OperationId)).Concat(priorFreeze?.Assignments.Where(x =>
                ((SchedulingFreezeReason)x.Reasons).HasFlag(SchedulingFreezeReason.ManualLock))
                .Select(x => (x.Assignment.OrderId, x.Assignment.OperationId)) ?? [])
            .Concat(problem.LockedAssignments.Select(x => (x.OrderId, x.OperationId))).Distinct().ToArray();
        var deviations = new List<SchedulingDeviation>();
        foreach (var source in availability.Windows.Where(x => x.Window.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unavailable))
        {
            var window = source.Window;
            deviations.Add(new SchedulingResourceUnavailableDeviation(window.SourceReferenceId,
                ReschedulingImpactAnalyzer.CanonicalJson(window), window.StartUtc, window.ReasonCode,
                window.DeviceAssetId, window.StartUtc, window.EndUtc));
        }
        var invalidations = await db.SchedulePlanInvalidations.AsNoTracking().Where(x => x.OrganizationId == org
            && x.EnvironmentId == env && x.PlanId == planId).ToArrayAsync(ct);
        foreach (var row in invalidations)
        {
            var affected = baseline.Assignments.Where(x =>
                (row.AffectedOperationId is null || row.AffectedOperationId == x.OperationId) &&
                (row.AffectedWorkOrderId is null || row.AffectedWorkOrderId == x.OrderId) &&
                (row.AffectedResourceId is null || row.AffectedResourceId == x.ResourceId || row.AffectedResourceId == x.WorkCenterId) &&
                (row.AffectedSkuCode is null || original.Orders.Any(o => o.OrderId == x.OrderId && o.SkuCode == row.AffectedSkuCode)));
            deviations.AddRange(affected.Select(x => new SchedulingOperationDeviation(row.SourceEventId,
                row.RecordedAtUtc.ToString("O"), row.OccurredAtUtc, row.ReasonCode, x.OrderId, x.OperationId)));
        }
        return new(new(problem, baseline, deviations,
            execution.Select(x => new SchedulingFreezeExecutionFact(x.WorkOrderId, x.OperationId, x.ActualStartedAtUtc, x.ActualCompletedAtUtc)).ToArray(),
            manualLocks, freezeSettings.At(asOf), availability,
            SchedulingMaterialConstraintModeResolver.Resolve(configuration[SchedulingMaterialConstraintModeResolver.ConfigurationKey]),
            SchedulingQualityConstraintModeResolver.Resolve(configuration[SchedulingQualityConstraintModeResolver.ConfigurationKey]), equipmentMode.Mode), assembled.Reservations);
    }

    internal SchedulingCandidateContract Generate(Input input)
    {
        var candidate = RightShiftCandidateGenerator.Generate(input.Candidate);
        return SchedulingCandidateProjector.Project(input.Candidate, candidate);
    }

    public async Task<SchedulingCandidateSetContract> PreviewAsync(SchedulingCandidatePreviewRequestContract request, CancellationToken ct)
    {
        var asOf = clock.GetUtcNow();
        var input = await ReadAsync(request.OrganizationId, request.EnvironmentId, request.BaselinePlanId, asOf, ct);
        var candidate = Generate(input);
        return new(1, request.BaselinePlanId, asOf, candidate.InputFingerprint, [candidate]);
    }

    public async Task<SchedulingCandidateSelectionContract> SelectAsync(SchedulingCandidateSelectRequestContract request, string userId, CancellationToken ct)
    {
        if (request.Strategy != SchedulingReschedulingStrategyContract.RightShift)
            throw new KnownException("候选策略不可用，请重预览后重新选择。");
        var input = await ReadAsync(request.OrganizationId, request.EnvironmentId, request.BaselinePlanId, request.AsOfUtc, ct);
        var context = ReschedulingCandidateContext.Create(input.Candidate);
        if (context.Fingerprint != request.InputFingerprint)
            throw new KnownException("排程输入已变化，请重预览后重新选择候选。");
        // Verify current time-sensitive facts too, without moving the explicit calculation time of the selected candidate.
        var now = clock.GetUtcNow();
        var currentFrozen = SchedulingFreezeCalculator.Calculate(input.Candidate.Baseline.Assignments, input.Candidate.Execution,
            input.Candidate.ManualLocks, freezeSettings.At(now));
        if (!currentFrozen.Select(x => (ReschedulingCandidateContext.Key(x.Assignment), x.Reasons))
                .SequenceEqual(context.Impact.FrozenAssignments.Select(x => (ReschedulingCandidateContext.Key(x.Assignment), x.Reasons)))
            || input.Candidate.EquipmentAvailability!.Windows.Any(x => x.Window.ExpectedRestoreAtUtc is { } restore
                && restore > request.AsOfUtc && restore <= now))
            throw new KnownException("冻结或设备恢复依据已变化，请重预览后重新选择候选。");
        // Stateless verification rebuilds the identical local result, never calls the full-plan creation/solver chain.
        var candidate = Generate(input);
        var freeze = SchedulingFreezeSnapshot.From(input.Candidate.Policy, context.Impact.FrozenAssignments);
        var problem = context.Problem with { ProblemId = $"candidate-{Guid.CreateVersion7():N}" };
        var plan = SchedulePlanValidationContextProjector.Attach(SchedulePlanContractMapper.WithStatus(candidate.Plan with
        {
            PlanId = $"plan-{Guid.CreateVersion7():N}", ProblemId = problem.ProblemId
        }, SchedulePlanStatusContract.Generated), problem, input.Reservations);
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(problem.ProblemId, problem.ContractVersion,
            problem.OrganizationId, problem.EnvironmentId, plan.ProblemFingerprint,
            SchedulingFrozenOccupancy.SerializeSnapshot(problem, input.Reservations, freeze, input.Candidate.EquipmentAvailability),
            problem.HorizonStartUtc, problem.HorizonEndUtc, plan.GeneratedAtUtc));
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(plan)));
        var state = new SchedulingWorkingDraftStateContract(1,
            problem.Orders.Select(x => new SchedulingWorkingDraftOrderContract(x.OrderId, x.Priority, x.IsRush, true)).ToArray(),
            plan.Assignments.Select(x => new SchedulingWorkingDraftTaskContract(x.AssignmentId, x.OrderId, x.OperationId,
                x.ResourceId, x.WorkCenterId, x.StartUtc, x.EndUtc, x.IsLocked, x.Segments?.ToArray())).ToArray(),
            plan.UnscheduledOperations.Select(x => new SchedulingWorkingDraftPendingOperationContract($"unscheduled:{x.OrderId}:{x.OperationId}",
                x.OrderId, x.OperationId, SchedulingWorkingDraftPendingSource.Unscheduled, x.Message, false, ReasonCode: x.ReasonCode.ToString())).ToArray());
        db.ScheduleWorkingDrafts.Add(new Domain.AggregatesModel.ScheduleWorkingDraftAggregate.ScheduleWorkingDraft(problem.OrganizationId,
            problem.EnvironmentId, plan.PlanId, userId, JsonSerializer.Serialize(state, SchedulingJson.Options), now));
        return new(plan, new(plan.PlanId, now, state), CreateSchedulePlanRevisionCommandHandler.Compare(input.Candidate.Baseline, plan));
    }
}
