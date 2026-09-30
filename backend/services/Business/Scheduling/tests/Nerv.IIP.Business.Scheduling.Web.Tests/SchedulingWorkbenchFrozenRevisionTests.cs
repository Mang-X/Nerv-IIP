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
