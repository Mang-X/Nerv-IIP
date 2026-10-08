using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class SchedulingFirstPlanJobPostgresTests
{
    // #4230 / approved #3620 r1: persist the selected result, never solve a different plan or publish implicitly.
    private static async Task VerifyInsertionCandidateSave()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        var start = sample.HorizonStartUtc;
        var template = sample.Orders.First();
        var operation = template.Operations.First();
        var resource = sample.Resources.First();
        var segments = new[] { new ScheduleAssignmentSegmentContract(start.AddMinutes(30), start.AddMinutes(35)),
            new ScheduleAssignmentSegmentContract(start.AddMinutes(40), start.AddMinutes(45)) };
        var problem = sample with
        {
            ProblemId = "save-insertion-baseline", AssemblyDependencies = [], QualityBlocks = [], UnavailabilityWindows = [],
            Orders = Enumerable.Range(1, 3).Select(i => template with
            {
                OrderId = $"order-{i:D3}", IsRush = false, Priority = 0,
                Operations = [operation with { OperationId = $"order-{i:D3}-op", DurationMinutes = 10,
                    IsRush = false, Priority = 0, SplitPolicy = ScheduleSplitPolicyContract.Interruptible,
                    EarliestStartUtc = i == 3 ? start.AddHours(2) : start }]
            }).ToArray(),
            LockedAssignments = [new SchedulingLockedAssignmentContract("manual-lock", "order-001", "order-001-op",
                operation.OperationSequence, resource.ResourceId, resource.WorkCenterId, segments[0].StartUtc,
                segments[1].EndUtc, "planner-lock", segments)]
        };
        var source = new ControlledSource { InsertionProblem = problem };
        var publisher = new ReleaseEventCapture();
        await using var factory = new JobFactory(source, clock: new InsertionClock(start),
            freezeSettings: new SchedulingFreezeSettings(TimeSpan.Zero, new Dictionary<string, TimeSpan>()), publisher: publisher);
        await Migrate(factory);
        using var client = Client(factory);
        SchedulePlanContract original;
        await using (var scope = factory.Services.CreateAsyncScope())
            original = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateSchedulePlanCommand(problem));
        async Task Release(string planId)
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/business/v1/scheduling/plans/{planId}/release?organizationId={problem.OrganizationId}&environmentId={problem.EnvironmentId}",
                new { }, SchedulingJson.Options);
            Assert.True((await response.Content.ReadFromJsonAsync<ResponseData<ReleaseSchedulePlanResponse>>(SchedulingJson.Options))!.Success);
        }
        await Release(original.PlanId);
        AssertReleasedAssignments(original, Assert.Single(publisher.Released));
        await Release(original.PlanId);
        Assert.Single(publisher.Released);
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-004"), SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
            var candidateId = $"insertion-{accepted.JobId:N}";
            var ids = problem.Orders.Select(x => x.OrderId).Append("order-004").ToArray();
            var input = new Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling.CreateSchedulePlanRevisionRequest(
                candidateId, problem.OrganizationId, problem.EnvironmentId, ids, []);
            async Task<ResponseData<SchedulePlanRevisionContract>> Save(Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling.CreateSchedulePlanRevisionRequest request)
            {
                using var saved = await client.PostAsJsonAsync($"/api/business/v1/scheduling/plans/{request.PlanId}/revisions", request, SchedulingJson.Options);
                return (await saved.Content.ReadFromJsonAsync<ResponseData<SchedulePlanRevisionContract>>(SchedulingJson.Options))!;
            }
            Assert.False((await Save(input)).Success); // Running job cannot become a draft.
            source.Release.TrySetResult();
            var completed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Completed, completed.Status);
            var result = completed.Result!;
            Assert.Contains(result.Candidate.Assignments, x => x.OrderId == "order-004");
            Assert.Equal(segments, result.Candidate.Assignments.Single(x => x.OrderId == "order-001").Segments);
            Assert.Equal(JsonSerializer.Serialize(original.Assignments.Single(x => x.OrderId == "order-003"), SchedulingJson.Options),
                JsonSerializer.Serialize(result.Candidate.Assignments.Single(x => x.OrderId == "order-003"), SchedulingJson.Options));
            Assert.False((await Save(input with { IncludedOrderIds = ids[..^1] })).Success);
            Assert.False((await Save(input with { IncludedOrderIds = ids.Append("extra").ToArray() })).Success);
            Assert.False((await Save(input with { LockedAssignments = problem.LockedAssignments })).Success);
            Assert.False((await Save(input with { OrganizationId = "other" })).Success);
            Assert.False((await Save(input with { EnvironmentId = "other" })).Success);
            Assert.False((await Save(input with { PlanId = $"insertion-{Guid.NewGuid():N}" })).Success);
            var saved = await Save(input);
            Assert.True(saved.Success, saved.Message);
            Assert.Equal(candidateId, saved.Data.Candidate.PlanId);
            // The existing HTTP endpoint overlays live execution; it is separate from the saved placement.
            var draft = saved.Data.Candidate with
            {
                Assignments = saved.Data.Candidate.Assignments.Select(x => x with { CurrentExecution = null }).ToArray()
            };
            Assert.Equal(SchedulePlanStatusContract.Generated, draft.Status);
            Assert.Equal(JsonSerializer.Serialize(result.Candidate.Assignments, SchedulingJson.Options), JsonSerializer.Serialize(draft.Assignments, SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(result.Candidate.ResourceLoads.OrderBy(x => x.ResourceId), SchedulingJson.Options), JsonSerializer.Serialize(draft.ResourceLoads.OrderBy(x => x.ResourceId), SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(result.Candidate.Conflicts.OrderBy(x => x.ConflictId), SchedulingJson.Options), JsonSerializer.Serialize(draft.Conflicts.OrderBy(x => x.ConflictId), SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(result.Candidate.UnscheduledOperations, SchedulingJson.Options), JsonSerializer.Serialize(draft.UnscheduledOperations, SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(result.Candidate.FreezeContext, SchedulingJson.Options), JsonSerializer.Serialize(draft.FreezeContext, SchedulingJson.Options));
            Assert.Equal(result.Candidate.ProblemFingerprint, draft.ProblemFingerprint);
            Assert.Equal(result.Candidate.GeneratedAtUtc, draft.GeneratedAtUtc);
            Assert.Equal(result.Candidate.Metrics, draft.Metrics);
            var repeated = await Save(input with { IncludedOrderIds = ids.Reverse().ToArray() });
            Assert.True(repeated.Success, repeated.Message);
            Assert.Equal(draft.PlanId, repeated.Data.Candidate.PlanId);
            Assert.Equal(JsonSerializer.Serialize(result, SchedulingJson.Options),
                JsonSerializer.Serialize((await ReadInsertion(client, accepted.JobId)).Result, SchedulingJson.Options));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var read = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetSchedulePlanDetailQuery(candidateId, problem.OrganizationId, problem.EnvironmentId));
                Assert.Equivalent(draft, read, strict: true);
                var snapshot = await db.ScheduleProblems.SingleAsync(x => x.ProblemId == draft.ProblemId);
                Assert.Equivalent(result.Snapshot.Problem with { ProblemId = draft.ProblemId },
                    JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options), strict: true);
                Assert.Equal(result.Candidate.ProblemFingerprint, snapshot.ProblemFingerprint);
                Assert.Equal(2, await db.SchedulePlans.CountAsync());
                Assert.Equal(2, await db.ScheduleProblems.CountAsync());
            }
            // Baseline remains released and identical until explicit confirmation.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var baseline = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetSchedulePlanDetailQuery(original.PlanId, problem.OrganizationId, problem.EnvironmentId));
                var baselineSnapshot = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScheduleProblems.SingleAsync(x => x.ProblemId == original.ProblemId);
                Assert.Equal(before, JsonSerializer.Serialize(new { Plan = baseline, baselineSnapshot.ProblemJson }, SchedulingJson.Options));
            }
            Assert.Equal(1, source.Calls);
            await Release(candidateId);
            AssertReleasedAssignments(draft, publisher.Released.Last());
            var revoked = Assert.Single(publisher.Revoked);
            Assert.Equal(original.PlanId, revoked.Payload.PlanId);
            Assert.Equal(candidateId, revoked.Payload.SupersededByPlanId);
            Assert.Equal(ExpectedOperations(original), revoked.Payload.AffectedOperations);
            Assert.Equal(1, revoked.Payload.ReleaseRevision);
            Assert.Equal(2, publisher.Released.Last().Payload.ReleaseRevision);
            await Release(candidateId);
            Assert.Equal(2, publisher.Released.Count);
            Assert.Single(publisher.Revoked);
            Assert.Equal(1, source.Calls);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var baseline = await sender.Send(new GetSchedulePlanDetailQuery(original.PlanId, problem.OrganizationId, problem.EnvironmentId));
                var released = await sender.Send(new GetSchedulePlanDetailQuery(candidateId, problem.OrganizationId, problem.EnvironmentId));
                Assert.Equal(SchedulePlanStatusContract.Superseded, baseline.Status);
                Assert.Equal(SchedulePlanStatusContract.Released, released.Status);
                Assert.Equal(JsonSerializer.Serialize(draft.Assignments, SchedulingJson.Options), JsonSerializer.Serialize(released.Assignments, SchedulingJson.Options));
            }
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }
    // Regression / ADR 0014 decision 3 / #4241: new HTTP scopes must publish saved placements.
    private static SchedulePlanAffectedOperationPayload[] ExpectedOperations(SchedulePlanContract plan) =>
        plan.Assignments.OrderBy(x => x.StartUtc).ThenBy(x => x.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationSequence).ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .Select(x => new SchedulePlanAffectedOperationPayload(x.OrderId, x.OperationId, x.OperationSequence,
                x.ResourceId, x.WorkCenterId, x.StartUtc, x.EndUtc, x.StandardOperationCode)).ToArray();

    private static void AssertReleasedAssignments(SchedulePlanContract plan, SchedulePlanReleasedIntegrationEvent released)
    {
        Assert.Equal(plan.PlanId, released.Payload.PlanId);
        Assert.Equal(plan.ProblemFingerprint, released.Payload.ProblemFingerprint);
        Assert.Equal(ExpectedOperations(plan), released.Payload.AffectedOperations);
    }

    private sealed class ReleaseEventCapture : IIntegrationEventPublisher
    {
        public ConcurrentQueue<SchedulePlanReleasedIntegrationEvent> Released { get; } = new();
        public ConcurrentQueue<SchedulePlanRevokedIntegrationEvent> Revoked { get; } = new();
        public Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            if (integrationEvent is SchedulePlanReleasedIntegrationEvent released) Released.Enqueue(released);
            if (integrationEvent is SchedulePlanRevokedIntegrationEvent revoked) Revoked.Enqueue(revoked);
            return Task.CompletedTask;
        }
    }
}
