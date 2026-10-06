using System.Net;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class SchedulingWorkbenchTests
{
    [Fact]
    public async Task Revision_reserves_excluded_baseline_freeze_capacity()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var excluded = problem.Orders.First();
        var included = problem.Orders.Last();
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(problem, "plan-excluded-freeze-base", problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, []),
            problem.HorizonStartUtc, problem.HorizonEndUtc, problem.HorizonStartUtc));
        await db.SaveChangesAsync();
        var baseline = basePlan.Assignments.First(x => x.OrderId == excluded.OrderId);
        var sender = new CapturingPlanSender(baseline.StartUtc);

        var result = await new CreateSchedulePlanRevisionCommandHandler(db, sender,
            new FreezeTestTimeProvider(baseline.StartUtc),
            new SchedulingFreezeSettings(TimeSpan.FromMinutes(1), new Dictionary<string, TimeSpan>())).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId,
                problem.EnvironmentId, [included.OrderId], []), CancellationToken.None);

        Assert.Contains(sender.LastCommand!.Freeze!.Assignments, x =>
            x.Assignment.OrderId == excluded.OrderId && x.Assignment.OperationId == baseline.OperationId);
        Assert.DoesNotContain(result.Candidate.Assignments, x => x.OrderId == excluded.OrderId);
        Assert.True(result.Candidate.Assignments.First(x => x.OrderId == included.OrderId).StartUtc >= baseline.EndUtc);
    }

    [Fact]
    public async Task Revision_keeps_new_actual_occupancy_distinct_from_baseline_freeze()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(problem, "plan-execution-base", problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, []),
            problem.HorizonStartUtc, problem.HorizonEndUtc, problem.HorizonStartUtc));
        var assignment = basePlan.Assignments.First();
        var actualStart = assignment.StartUtc.AddMinutes(5);
        var execution = OperationExecutionProjection.Create(problem.OrganizationId, problem.EnvironmentId,
            assignment.OrderId, assignment.OperationId, assignment.OperationSequence,
            assignment.WorkCenterId, actualStart, "event-create");
        execution.ApplyStarted(actualStart, "event-start");
        db.OperationExecutionProjections.Add(execution);
        await db.SaveChangesAsync();
        var sender = new CapturingPlanSender(actualStart);

        var result = await new CreateSchedulePlanRevisionCommandHandler(db, sender,
            new FreezeTestTimeProvider(actualStart)).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId,
                problem.EnvironmentId, problem.Orders.Select(x => x.OrderId).ToArray(), []), CancellationToken.None);

        var input = sender.LastCommand!;
        var occupancy = Assert.Single(input.FixedReservations!, x =>
            x.OrderId == assignment.OrderId && x.OperationId == assignment.OperationId);
        Assert.Equal(actualStart, occupancy.StartUtc);
        Assert.DoesNotContain(input.Problem.LockedAssignments, x =>
            x.OrderId == assignment.OrderId && x.OperationId == assignment.OperationId);
        var frozen = Assert.Single(input.Freeze!.Assignments, x =>
            x.Assignment.OrderId == assignment.OrderId && x.Assignment.OperationId == assignment.OperationId);
        Assert.Equal(assignment.ResourceId, frozen.Assignment.ResourceId);
        Assert.Equal(assignment.StartUtc, frozen.Assignment.StartUtc);
        Assert.Equal(SchedulingFreezeReason.Started, (SchedulingFreezeReason)frozen.Reasons);
        Assert.Equal("in-progress", Assert.Single(result.Candidate.Assignments, x =>
            x.OrderId == assignment.OrderId && x.OperationId == assignment.OperationId).ExplanationCode);
    }

    [Fact]
    public async Task Preview_and_create_use_the_same_problem_fingerprint()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var clock = new FreezeTestTimeProvider(problem.HorizonStartUtc);
        var preview = new PreviewSchedulePlanCommandHandler(
            new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), SchedulingEquipmentUnknownModeOption.Default);
        var create = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), clock,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), new OrderUrgencyService(db, clock),
            SchedulingEquipmentUnknownModeOption.Default);

        var freeze = new SchedulingFreezeSnapshot(problem.HorizonStartUtc, TimeSpan.Zero,
            new Dictionary<string, TimeSpan>(), []);
        var previewPlan = await preview.Handle(new PreviewSchedulePlanCommand(problem, Freeze: freeze), CancellationToken.None);
        var createdPlan = await create.Handle(new CreateSchedulePlanCommand(problem, Freeze: freeze), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(createdPlan.ProblemFingerprint, previewPlan.ProblemFingerprint);
        var snapshot = await db.ScheduleProblems.SingleAsync();
        Assert.Equal(freeze.AsOfUtc, SchedulingFrozenOccupancy.ReadFreezeSnapshot(snapshot.ProblemJson)!.AsOfUtc);
        Assert.Equal(createdPlan.PlanId,
            (await create.Handle(new CreateSchedulePlanCommand(problem, Freeze: freeze), CancellationToken.None)).PlanId);
        await Assert.ThrowsAsync<KnownException>(() => create.Handle(
            new CreateSchedulePlanCommand(problem, Freeze: freeze with { AsOfUtc = freeze.AsOfUtc.AddMinutes(1) }),
            CancellationToken.None));
    }

    [Fact]
    public async Task Revision_stable_window_freezes_baseline_and_captures_policy_in_fingerprint()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(problem, "plan-window-base", problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, []),
            problem.HorizonStartUtc, problem.HorizonEndUtc, problem.HorizonStartUtc));
        await db.SaveChangesAsync();
        var baseline = basePlan.Assignments.First();
        var asOf = baseline.StartUtc.AddMinutes(-10);
        var settings = new SchedulingFreezeSettings(TimeSpan.Zero,
            new Dictionary<string, TimeSpan> { [baseline.WorkCenterId] = TimeSpan.FromMinutes(30) });
        var sender = new CapturingPlanSender(asOf);
        var handler = new CreateSchedulePlanRevisionCommandHandler(db, sender,
            new FreezeTestTimeProvider(asOf), settings);

        var result = await handler.Handle(new CreateSchedulePlanRevisionCommand(basePlan.PlanId,
            problem.OrganizationId, problem.EnvironmentId, problem.Orders.Select(x => x.OrderId).ToArray(), []),
            CancellationToken.None);

        var input = Assert.IsType<CreateSchedulePlanCommand>(sender.LastCommand);
        var frozen = Assert.Single(input.Problem.LockedAssignments, x =>
            x.OrderId == baseline.OrderId && x.OperationId == baseline.OperationId);
        Assert.Equal(baseline.AssignmentId, frozen.AssignmentId);
        Assert.Equal(baseline.ResourceId, frozen.ResourceId);
        Assert.Equal(baseline.StartUtc, frozen.StartUtc);
        Assert.Equal(baseline.EndUtc, frozen.EndUtc);
        Assert.Equal(baseline.Segments, frozen.Segments);
        Assert.Equal(asOf, input.Freeze!.AsOfUtc);
        Assert.Equal(TimeSpan.Zero, input.Freeze.DefaultWindow);
        Assert.Equal(TimeSpan.FromMinutes(30), input.Freeze.WorkCenterWindows[baseline.WorkCenterId]);
        Assert.Equal(0, result.Comparison.MovedOperationCount);
        var fingerprint = CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(input.Problem,
            input.FixedReservations ?? [], input.Freeze);
        Assert.NotEqual(fingerprint, CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(input.Problem,
            input.FixedReservations ?? [], input.Freeze with { AsOfUtc = asOf.AddMinutes(1) }));
        Assert.NotEqual(fingerprint, CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(input.Problem,
            input.FixedReservations ?? [], input.Freeze with { DefaultWindow = TimeSpan.FromMinutes(1) }));
    }

    private sealed class FreezeTestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Revision_preserves_baseline_manual_lock_with_segments()
    {
        await using var db = CreateDbContext();
        var template = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = template.Orders.First();
        var operation = order.Operations.First();
        var resource = template.Resources.Single(x => x.ResourceId == operation.PrimaryResourceId);
        var start = template.HorizonStartUtc;
        var segments = new[]
        {
            new ScheduleAssignmentSegmentContract(start, start.AddMinutes(20)),
            new ScheduleAssignmentSegmentContract(start.AddMinutes(30), start.AddMinutes(50))
        };
        var baselineLock = new SchedulingLockedAssignmentContract(
            "baseline-lock", order.OrderId, operation.OperationId, operation.OperationSequence,
            resource.ResourceId, resource.WorkCenterId, start, start.AddMinutes(50),
            "planner-draft-lock", segments);
        var problem = template with
        {
            Orders = [order with { Operations = [operation with { SplitPolicy = ScheduleSplitPolicyContract.Interruptible }] }],
            LockedAssignments = [baselineLock]
        };
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(problem, "plan-manual-base", start),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, []),
            problem.HorizonStartUtc, problem.HorizonEndUtc, start));
        await db.SaveChangesAsync();
        var sender = new CapturingPlanSender(start);

        await new CreateSchedulePlanRevisionCommandHandler(db, sender).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId, problem.EnvironmentId,
                [order.OrderId], []), CancellationToken.None);

        var preserved = Assert.Single(sender.LastCommand!.Problem.LockedAssignments);
        Assert.Equal(baselineLock.AssignmentId, preserved.AssignmentId);
        Assert.Equal(baselineLock.ResourceId, preserved.ResourceId);
        Assert.Equal(baselineLock.StartUtc, preserved.StartUtc);
        Assert.Equal(baselineLock.EndUtc, preserved.EndUtc);
        Assert.Equal(segments, preserved.Segments);
    }

    [Fact]
    public async Task Revision_replays_frozen_snapshot_and_excludes_it_from_moved_count()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var operation = problem.Orders.First().Operations.First();
        var frozen = new FixedWorkCenterReservation(
            problem.Orders.First().OrderId, operation.OperationId, operation.OperationSequence,
            problem.Resources.Single(x => x.ResourceId == operation.PrimaryResourceId).WorkCenterId,
            problem.HorizonStartUtc, problem.HorizonStartUtc.AddHours(1), null);
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().ScheduleWithFixedReservations(problem, "plan-frozen-base", problem.HorizonStartUtc, [frozen]),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, [frozen]),
            problem.HorizonStartUtc, problem.HorizonEndUtc, problem.HorizonStartUtc));
        await db.SaveChangesAsync();
        var sender = new CapturingPlanSender(problem.HorizonStartUtc);
        var handler = new CreateSchedulePlanRevisionCommandHandler(db, sender);

        var result = await handler.Handle(new CreateSchedulePlanRevisionCommand(
            basePlan.PlanId, problem.OrganizationId, problem.EnvironmentId,
            problem.Orders.Select(x => x.OrderId).ToArray(),
            [new SchedulingLockedAssignmentContract(
                "planner-lock", frozen.OrderId, frozen.OperationId, frozen.OperationSequence,
                operation.PrimaryResourceId!, frozen.WorkCenterId,
                frozen.StartUtc.AddMinutes(5), frozen.EndUtc.AddMinutes(5), "planner-draft-lock")]), CancellationToken.None);

        Assert.Equal([frozen], Assert.IsType<CreateSchedulePlanCommand>(sender.LastCommand).FixedReservations);
        Assert.Empty(sender.LastCommand!.Problem.LockedAssignments);
        var fixedAssignment = Assert.Single(result.Candidate.Assignments, x => x.OperationId == operation.OperationId);
        Assert.Equal("in-progress", fixedAssignment.ExplanationCode);
        Assert.Equal(frozen.StartUtc, fixedAssignment.StartUtc);
        Assert.Equal(frozen.EndUtc, fixedAssignment.EndUtc);
        Assert.Equal(0, result.Comparison.MovedOperationCount);
    }

    [Fact]
    public async Task Created_plan_persists_frozen_occupancy_in_snapshot_and_assignment()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var operation = problem.Orders.First().Operations.First();
        var frozen = new FixedWorkCenterReservation(
            problem.Orders.First().OrderId, operation.OperationId, operation.OperationSequence,
            problem.Resources.Single(x => x.ResourceId == operation.PrimaryResourceId).WorkCenterId,
            problem.HorizonStartUtc, problem.HorizonStartUtc.AddHours(1), null);
        var handler = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), TimeProvider.System,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), new OrderUrgencyService(db, TimeProvider.System),
            SchedulingEquipmentUnknownModeOption.Default);

        var plan = await handler.Handle(new CreateSchedulePlanCommand(problem, [frozen]), CancellationToken.None);
        await db.SaveChangesAsync();

        var snapshot = await db.ScheduleProblems.SingleAsync();
        Assert.Equal(plan.ProblemFingerprint, snapshot.ProblemFingerprint);
        Assert.Equal([frozen], SchedulingFrozenOccupancy.ReadSnapshot(snapshot.ProblemJson));
        var assignment = Assert.Single((await db.SchedulePlans.Include(x => x.Assignments).SingleAsync()).Assignments,
            x => x.OperationId == operation.OperationId);
        Assert.Equal(string.Empty, assignment.ResourceId);
        Assert.Equal("in-progress", assignment.ExplanationCode);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSchedulePlanCommand(problem, [frozen with { EndUtc = frozen.EndUtc.AddMinutes(1) }]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Revision_uses_excluded_frozen_order_only_for_capacity()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var excludedOrder = problem.Orders.First();
        var includedOrder = problem.Orders.Last();
        var operation = excludedOrder.Operations.First();
        var frozen = new FixedWorkCenterReservation(
            excludedOrder.OrderId, operation.OperationId, operation.OperationSequence,
            problem.Resources.Single(x => x.ResourceId == operation.PrimaryResourceId).WorkCenterId,
            problem.HorizonStartUtc, problem.HorizonStartUtc.AddHours(1), null);
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().ScheduleWithFixedReservations(problem, "plan-partial-base", problem.HorizonStartUtc, [frozen]),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, [frozen]),
            problem.HorizonStartUtc, problem.HorizonEndUtc, problem.HorizonStartUtc));
        await db.SaveChangesAsync();
        var sender = new CapturingPlanSender(problem.HorizonStartUtc);

        var result = await new CreateSchedulePlanRevisionCommandHandler(db, sender).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId, problem.EnvironmentId,
                [includedOrder.OrderId], []), CancellationToken.None);

        var reservation = Assert.Single(sender.LastCommand!.FixedReservations!);
        Assert.Equal(excludedOrder.OrderId, reservation.OrderId);
        Assert.DoesNotContain(sender.LastCommand.Problem.Orders, x => x.OrderId == reservation.OrderId);
        Assert.All(result.Candidate.Assignments, x => Assert.Equal(includedOrder.OrderId, x.OrderId));
        Assert.Equal(includedOrder.Operations.Count, result.Candidate.Metrics.ScheduledOperationCount);
        var competingOperation = includedOrder.Operations.First();
        Assert.True(Assert.Single(result.Candidate.Assignments,
            x => x.OperationId == competingOperation.OperationId).StartUtc >= frozen.EndUtc);

        var createHandler = new CreateSchedulePlanCommandHandler(
            db, new FiniteCapacityScheduler(), TimeProvider.System,
            new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(db), new OrderUrgencyService(db, TimeProvider.System),
            SchedulingEquipmentUnknownModeOption.Default);
        var persistedCandidate = await createHandler.Handle(sender.LastCommand, CancellationToken.None);
        Assert.All(persistedCandidate.Assignments, x => Assert.Equal(includedOrder.OrderId, x.OrderId));
        Assert.True(Assert.Single(persistedCandidate.Assignments,
            x => x.OperationId == competingOperation.OperationId).StartUtc >= frozen.EndUtc);
    }

    [Fact]
    public async Task Revision_interruptible_segments_respect_excluded_frozen_order_capacity()
    {
        await using var db = CreateDbContext();
        var template = ShockAbsorberSchedulingFixture.CreateProblem();
        var start = template.HorizonStartUtc;
        var excludedOrder = template.Orders.First();
        var excludedOperation = excludedOrder.Operations.First();
        var includedOrder = template.Orders.Last();
        var includedOperation = includedOrder.Operations.First() with
        {
            SplitPolicy = ScheduleSplitPolicyContract.Interruptible,
            DurationMinutes = 150
        };
        var problem = template with
        {
            HorizonEndUtc = start.AddHours(12),
            Orders = [
                excludedOrder with { Operations = [excludedOperation] },
                includedOrder with { Operations = [includedOperation] }
            ],
            Resources = [template.Resources.Single(x => x.ResourceId == includedOperation.PrimaryResourceId)],
            Calendars = [new SchedulingCalendarContract("CAL-DAY", [
                new SchedulingTimeWindowContract(start, start.AddHours(2), "first"),
                new SchedulingTimeWindowContract(start.AddHours(10), start.AddHours(12), "second")])],
            UnavailabilityWindows = [],
            QualityBlocks = []
        };
        var frozen = new FixedWorkCenterReservation(
            excludedOrder.OrderId, excludedOperation.OperationId, excludedOperation.OperationSequence,
            problem.Resources.Single().WorkCenterId, start, start.AddHours(1), null);
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().ScheduleWithFixedReservations(
                problem, "plan-excluded-split-base", start, [frozen]),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId, problem.ContractVersion, problem.OrganizationId, problem.EnvironmentId,
            "fingerprint", SchedulingFrozenOccupancy.SerializeSnapshot(problem, [frozen]),
            problem.HorizonStartUtc, problem.HorizonEndUtc, start));
        await db.SaveChangesAsync();
        var sender = new CapturingPlanSender(start);

        var result = await new CreateSchedulePlanRevisionCommandHandler(db, sender).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId, problem.EnvironmentId,
                [includedOrder.OrderId], []), CancellationToken.None);

        Assert.Equal([frozen], sender.LastCommand!.FixedReservations);
        Assert.DoesNotContain(sender.LastCommand.Problem.Orders, x => x.OrderId == excludedOrder.OrderId);
        var assignment = Assert.Single(result.Candidate.Assignments);
        Assert.Equal(includedOperation.OperationId, assignment.OperationId);
        Assert.Equal([
            new ScheduleAssignmentSegmentContract(start.AddHours(1), start.AddHours(2)),
            new ScheduleAssignmentSegmentContract(start.AddHours(10), start.AddHours(11).AddMinutes(30))
        ], assignment.Segments);
        Assert.Equal(1, result.Candidate.Metrics.ScheduledOperationCount);
    }
}
