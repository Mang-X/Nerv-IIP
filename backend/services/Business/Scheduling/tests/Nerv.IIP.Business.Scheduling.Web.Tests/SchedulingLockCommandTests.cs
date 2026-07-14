using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingLockCommandTests
{
    [Fact]
    public async Task Assemble_resolves_base_plan_locks_and_explicit_lock_wins_for_the_same_operation()
    {
        await using var db = CreateDbContext();
        var baseProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var baseContract = new FiniteCapacityScheduler().Schedule(
            baseProblem, "plan-base", baseProblem.HorizonStartUtc.AddMinutes(-1));
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            baseProblem.OrganizationId, baseProblem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(baseContract)));
        await db.SaveChangesAsync();
        var baseAssignments = baseContract.Assignments.Take(2).ToArray();
        var explicitAssignment = baseAssignments[0] with
        {
            AssignmentId = "explicit-lock",
            StartUtc = baseAssignments[0].StartUtc.AddMinutes(15),
            EndUtc = baseAssignments[0].EndUtc.AddMinutes(15),
            ExplanationCode = "caller-explicit"
        };
        var explicitLock = new SchedulingLockedAssignmentContract(
            explicitAssignment.AssignmentId, explicitAssignment.OrderId, explicitAssignment.OperationId,
            explicitAssignment.OperationSequence, explicitAssignment.ResourceId, explicitAssignment.WorkCenterId,
            explicitAssignment.StartUtc, explicitAssignment.EndUtc, explicitAssignment.ExplanationCode);
        var request = new AssembleSchedulingProblemRequest(
            "problem-replan", baseProblem.OrganizationId, baseProblem.EnvironmentId,
            baseProblem.HorizonStartUtc, baseProblem.HorizonEndUtc,
            [new SchedulingProblemSourceOrder("WO", "SKU", 1m, baseProblem.HorizonEndUtc, 1, false,
                baseProblem.HorizonStartUtc, "routing")],
            LockedAssignments: [explicitLock], BasePlanId: "plan-base",
            LockedOperationIds: baseAssignments.Select(x => x.OperationId).ToArray());
        var handler = new AssembleSchedulingProblemCommandHandler(new CapturingProducer(baseProblem), db);

        var result = await handler.Handle(new AssembleSchedulingProblemCommand(request), CancellationToken.None);

        var explicitResult = Assert.Single(result.LockedAssignments, x => x.OperationId == explicitLock.OperationId);
        Assert.Equal("caller-explicit", explicitResult.LockReasonCode);
        Assert.Equal(explicitLock.StartUtc, explicitResult.StartUtc);
        var baseResult = Assert.Single(result.LockedAssignments, x => x.OperationId == baseAssignments[1].OperationId);
        Assert.Equal("base-plan-lock", baseResult.LockReasonCode);
        Assert.Equal(baseAssignments[1].ResourceId, baseResult.ResourceId);
        Assert.Equal(baseAssignments[1].StartUtc, baseResult.StartUtc);
        Assert.Equal(baseAssignments[1].EndUtc, baseResult.EndUtc);
    }

    [Fact]
    public async Task Upsert_override_reports_legacy_problem_payload_as_business_error()
    {
        await using var db = CreateDbContext();
        var baseProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var planContract = new FiniteCapacityScheduler().Schedule(
            baseProblem, "plan-legacy", baseProblem.HorizonStartUtc.AddMinutes(-1));
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            baseProblem.OrganizationId, baseProblem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(planContract)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            baseProblem.ProblemId, 1, baseProblem.OrganizationId, baseProblem.EnvironmentId,
            "legacy", "{}", baseProblem.HorizonStartUtc, baseProblem.HorizonEndUtc,
            baseProblem.HorizonStartUtc));
        await db.SaveChangesAsync();
        var assignment = planContract.Assignments.First();
        var handler = new UpsertScheduleOperationOverrideCommandHandler(
            db, TimeProvider.System, new StubContextAccessor());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpsertScheduleOperationOverrideCommand(
                baseProblem.OrganizationId, baseProblem.EnvironmentId, planContract.PlanId,
                assignment.OperationId, assignment.ResourceId, assignment.StartUtc, assignment.EndUtc),
            CancellationToken.None));

        Assert.Contains("unavailable for manual override", exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-lock-commands-{Guid.NewGuid():N}").Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CapturingProducer(SchedulingProblemContract template) : ISchedulingProblemProducer
    {
        public Task<SchedulingProblemContract> AssembleAsync(
            AssembleSchedulingProblemRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(template with
            {
                ProblemId = request.ProblemId,
                LockedAssignments = request.LockedAssignments ?? []
            });
    }

    private sealed class StubContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext() => new("corr", "cause", "user:planner");
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
