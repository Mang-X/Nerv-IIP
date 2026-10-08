extern alias Gateway;
using GatewayAuth = Gateway::Nerv.IIP.BusinessGateway.Web.Application.Auth;
using GatewayServices = Gateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class SchedulingFirstPlanJobPostgresTests
{
    private const string GatewayRoute = "/api/business-console/v1/scheduling/workbench/first-plan-jobs";
    private const string Route = "/api/business/v1/scheduling/workbench/first-plan-jobs";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [SchedulingPostgresFact]
    public async Task Acceptance_commits_before_blocked_500_order_calculation_and_completed_plan_is_readable()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource();
        await using var factory = new JobFactory(source);
        await Migrate(factory);
        using var serviceClient = Client(factory);
        await using var gateway = new GatewayJobFactory(serviceClient);
        using var client = GatewayClient(gateway);
        using var worker = Worker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var input = Input(500);
            // If HTTP does the computation, this times out while the controlled source remains blocked.
            var accepted = await TestTimeout.RunAsync("first-plan acceptance before assembly completes",
                async ct => await Accept(client, input, ct, GatewayRoute), Budget);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Created, accepted.Status);
            Assert.Null(accepted.PlanId);
            await TestTimeout.RunAsync("worker entered independent assembly scope", async ct =>
                await source.Entered.Task.WaitAsync(ct), Budget);
            var running = await Read(client, accepted.JobId, GatewayRoute);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Running, running.Status);
            Assert.NotNull(running.StartedAtUtc);
            Assert.Equal(JsonSerializer.Serialize(input, SchedulingJson.Options), JsonSerializer.Serialize(running.Input, SchedulingJson.Options));
            using var wrongOrg = await serviceClient.GetAsync($"{Route}/{accepted.JobId}?organizationId=other&environmentId={input.EnvironmentId}");
            Assert.Contains("未找到", await wrongOrg.Content.ReadAsStringAsync());
            using var wrongEnv = await serviceClient.GetAsync($"{Route}/{accepted.JobId}?organizationId={input.OrganizationId}&environmentId=other");
            Assert.Contains("未找到", await wrongEnv.Content.ReadAsStringAsync());
            client.Dispose(); // request/client lifetime ends while computation is still blocked.
            source.Release.TrySetResult();
            using var reopened = GatewayClient(gateway);
            var completed = await Terminal(reopened, accepted.JobId, SchedulingFirstPlanJobStatusContract.Completed, factory.FailureLog, GatewayRoute);
            Assert.NotNull(completed.FinishedAtUtc);
            Assert.Null(completed.FailureReason);
            Assert.NotNull(completed.PlanId);
            var result = await serviceClient.GetFromJsonAsync<ResponseData<SchedulePlanContract>>(
                $"/api/business/v1/scheduling/plans/{completed.PlanId}?organizationId={input.OrganizationId}&environmentId={input.EnvironmentId}", SchedulingJson.Options);
            Assert.True(result!.Success);
            Assert.Equal(500, result.Data.Assignments.Count);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshot = await db.ScheduleProblems.SingleAsync(x => x.ProblemId == result.Data.ProblemId);
            var persistedInput = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
            Assert.Equal(input.Orders.Select(x => x.WorkOrderId).Order(), persistedInput.Orders.Select(x => x.OrderId).Order());
            Assert.Equal(input.HorizonStartUtc, persistedInput.HorizonStartUtc);
            Assert.Equal(input.HorizonEndUtc, persistedInput.HorizonEndUtc);
            Assert.Single(await db.SchedulePlans.ToArrayAsync());
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    [SchedulingPostgresFact]
    public async Task Real_calculation_failure_commits_failed_reason_without_plan_or_retry()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource { Failure = new KnownException("工艺路线不可用，请重新选择工单。") };
        await using var factory = new JobFactory(source);
        await Migrate(factory);
        using var serviceClient = Client(factory);
        await using var gateway = new GatewayJobFactory(serviceClient);
        using var client = GatewayClient(gateway);
        using var worker = Worker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var accepted = await Accept(client, Input(1), route: GatewayRoute);
            await TestTimeout.RunAsync("failure source entered", async ct => await source.Entered.Task.WaitAsync(ct), Budget);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Running, (await Read(client, accepted.JobId, GatewayRoute)).Status);
            source.Release.TrySetResult();
            var failed = await Terminal(client, accepted.JobId, SchedulingFirstPlanJobStatusContract.Failed, factory.FailureLog, GatewayRoute);
            Assert.Equal(source.Failure.Message, failed.FailureReason);
            Assert.Null(failed.PlanId);
            Assert.NotNull(failed.FinishedAtUtc);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.SchedulePlans.ToArrayAsync());
            Assert.Empty(await db.ScheduleProblems.ToArrayAsync());
            Assert.Equal(1, source.Calls);
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    [SchedulingPostgresFact]
    public async Task Completion_write_failure_rolls_back_plan_and_input_before_committing_failed()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource();
        source.Release.TrySetResult();
        await using var factory = new JobFactory(source, new CompletionWriteFailure());
        await Migrate(factory);
        using var client = Client(factory);
        using var worker = Worker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var accepted = await Accept(client, Input(1));
            var failed = await Terminal(client, accepted.JobId, SchedulingFirstPlanJobStatusContract.Failed, factory.FailureLog);
            Assert.True(failed.FailureReason == "模拟首版终态写入失败", factory.FailureLog.Failure?.ToString());
            Assert.Null(failed.PlanId);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.SchedulePlans.ToArrayAsync());
            Assert.Empty(await db.ScheduleProblems.ToArrayAsync());
            Assert.Equal(1, source.Calls);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [SchedulingPostgresFact]
    public async Task Acceptance_validates_async_capacity_and_scope_without_expanding_sync_preview_or_revision()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = new JobFactory(new ControlledSource());
        await Migrate(factory);
        using var client = factory.CreateClient();
        using var anonymous = await client.PostAsJsonAsync(Route, Input(1), SchedulingJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        foreach (var input in new[] { Input(0), Input(501), Input(1) with { OrganizationId = "" }, Input(1) with { EnvironmentId = "" },
            Input(1) with { HorizonEndUtc = Input(1).HorizonStartUtc }, Input(2) with { Orders = [new("same", 1, false), new(" same ", 1, false)] } })
        {
            using var rejected = await client.PostAsJsonAsync(Route, input, SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        var accepted = await Accept(client, Input(1));
        Assert.Equal(SchedulingFirstPlanJobStatusContract.Created, (await Read(client, accepted.JobId)).Status);
        var sync = new PreviewSchedulingWorkbenchPlanCommand(Input(11).OrganizationId, Input(11).EnvironmentId,
            Input(11).HorizonStartUtc, Input(11).HorizonEndUtc,
            Input(11).Orders.Select(x => new SchedulingWorkbenchOrderSelection(x.WorkOrderId, x.Priority, x.IsRush)).ToArray());
        Assert.False((await new PreviewSchedulingWorkbenchPlanCommandValidator().ValidateAsync(sync)).IsValid);
        Assert.False((await new CreateSchedulePlanRevisionCommandValidator().ValidateAsync(new CreateSchedulePlanRevisionCommand("base", sync.OrganizationId,
            sync.EnvironmentId, sync.Orders.Select(x => x.WorkOrderId).ToArray(), []))).IsValid);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScheduleFirstPlanJobs.ToArrayAsync());
    }

    [SchedulingPostgresFact]
    public async Task Insertion_preview_preserves_all_snapshot_orders_and_original_plan_without_saving_candidate()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource();
        await using var factory = new JobFactory(source);
        await Migrate(factory);
        using var client = Client(factory);
        var original = await SeedInsertionPlan(factory, 11);
        Assert.DoesNotContain(original.Assignments, x => x.OrderId == "order-011");
        Assert.Contains(original.UnscheduledOperations, x => x.OrderId == "order-011");
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var input = InsertionInput(original, "order-012");
            using var response = await TestTimeout.RunAsync("insertion acceptance before source completes",
                async ct => await client.PostAsJsonAsync(InsertionRoute, input, SchedulingJson.Options, ct), Budget);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Created, accepted.Status);
            Assert.Null(accepted.Preview);
            Assert.Equal(12, accepted.Input.WorkOrderIds.Count);
            Assert.Contains("order-011", accepted.Input.WorkOrderIds);
            Assert.Equal(Input(1).HorizonStartUtc, accepted.Input.HorizonStartUtc);
            Assert.Equal(Input(1).HorizonEndUtc, accepted.Input.HorizonEndUtc);
            await TestTimeout.RunAsync("insertion worker source entered", async ct => await source.Entered.Task.WaitAsync(ct), Budget);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Running, (await ReadInsertion(client, accepted.JobId)).Status);
            foreach (var scope in new[] { "organizationId=other&environmentId=" + input.EnvironmentId,
                "organizationId=" + input.OrganizationId + "&environmentId=other" })
            {
                using var missing = await client.GetAsync($"{InsertionRoute}/{accepted.JobId}?{scope}");
                Assert.Contains("未找到", await missing.Content.ReadAsStringAsync());
            }
            source.Release.TrySetResult();
            var completed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Completed, completed.Status);
            Assert.NotNull(completed.FinishedAtUtc);
            Assert.Null(completed.FailureReason);
            Assert.Equal(SchedulePlanStatusContract.Preview, completed.Preview!.Status);
            // #3620 spec r1：局部插入不补排基线原来未排的独立订单。
            Assert.Contains(completed.Preview.UnscheduledOperations, x => x.OrderId == "order-011");
            Assert.Equal(11, completed.Preview.Assignments.Count);
            foreach (var assignment in original.Assignments)
                Assert.Equal(assignment, completed.Preview.Assignments.Single(x => x.OrderId == assignment.OrderId));
            var result = Assert.IsType<SchedulingInsertionPreviewResultContract>(completed.Result);
            Assert.Equal(original.PlanId, result.BaselinePlanId);
            Assert.Equal(completed.Preview.PlanId, result.CandidatePlanId);
            Assert.Equal(result.InputFingerprint, completed.Preview.ProblemFingerprint);
            Assert.Equal(SchedulingInsertionOrderStatusContract.Unscheduled, result.Orders.Single(x => x.OrderId == "order-011").Status);
            Assert.Null(result.Orders.Single(x => x.OrderId == "order-011").DelayDays);
            Assert.Equal(SchedulingInsertionOrderStatusContract.New, result.Orders.Single(x => x.OrderId == "order-012").Status);
            Assert.Equal(1, result.Kpis.UnscheduledOperationCount.Candidate);
            Assert.Equal(12, result.Snapshot.Problem.Orders.Count);
            Assert.Equal(original.PlanId, accepted.AcceptedBaseline!.Baseline.PlanId);
            Assert.Equal(JsonSerializer.Serialize(result, SchedulingJson.Options),
                JsonSerializer.Serialize((await ReadInsertion(client, accepted.JobId)).Result, SchedulingJson.Options));
            Assert.Equal(before, await ReadInsertionBaseline(factory, original));
            await using var scopeAfter = factory.Services.CreateAsyncScope();
            var db = scopeAfter.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.SchedulePlans.ToArrayAsync());
            Assert.Single(await db.ScheduleProblems.ToArrayAsync());
            Assert.Empty(await db.ScheduleFirstPlanJobs.ToArrayAsync());
            Assert.Equal(1, source.Calls);
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
        // DomainInvariant: ADR 0032 §3 / #4208. Same registered HTTP → worker → PostgreSQL seam.
        foreach (var window in new[] { TimeSpan.FromMinutes(20), TimeSpan.Zero })
            await VerifyInsertionFreeze(window);
        await VerifyExistingRushInsertion();
        await VerifyEquipmentSourceSnapshot();
        await VerifyCandidateSelection();
        await VerifyCandidateSelection(SchedulingReschedulingStrategyContract.RightShift);
        await VerifyCandidateSelection(SchedulingReschedulingStrategyContract.ResourceTransfer);
        await VerifyInsertionCandidateSave();
    }

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
        await using var factory = new JobFactory(source, clock: new InsertionClock(start),
            freezeSettings: new SchedulingFreezeSettings(TimeSpan.Zero, new Dictionary<string, TimeSpan>()));
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

    private static async Task VerifyEquipmentSourceSnapshot()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        var source = new ControlledSource();
        source.Release.TrySetResult();
        var equipment = new VersionedEquipment();
        await using var factory = new JobFactory(source, clock: new InsertionClock(sample.HorizonStartUtc), equipment: equipment);
        await Migrate(factory);
        using var client = Client(factory);
        var original = await SeedInsertionPlan(factory, 2);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            string? firstProblem = null;
            foreach (var version in new[] { "v1", "v2" })
            {
                equipment.Version = version;
                using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-003"), SchedulingJson.Options);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
                var completed = await TerminalInsertion(client, accepted.JobId);
                Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Completed, completed.Status);
                var readback = (await ReadInsertion(client, accepted.JobId)).Result!;
                using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(readback.Snapshot, SchedulingJson.Options));
                // #4186 / ADR 0032：同实际窗但不同恢复来源版本必须从持久化结果区分。
                Assert.True(snapshot.RootElement.TryGetProperty("equipmentAvailability", out var retained));
                Assert.Equal(sample.HorizonStartUtc, retained.GetProperty("asOfUtc").GetDateTimeOffset());
                Assert.Equal(1, retained.GetProperty("contractVersion").GetInt32());
                var item = Assert.Single(retained.GetProperty("windows").EnumerateArray());
                var window = item.GetProperty("window");
                Assert.Equal(version, window.GetProperty("restorePredictionSourceVersion").GetString());
                Assert.Equal("device-mttr", window.GetProperty("restorePredictionSource").GetString());
                Assert.Equal(sample.HorizonStartUtc.AddMinutes(-1), window.GetProperty("expectedRestoreAtUtc").GetDateTimeOffset());
                Assert.Equal("source-maintenance", window.GetProperty("sourceReferenceId").GetString());
                Assert.True(item.GetProperty("restorePredictionExpired").GetBoolean());
                Assert.Equal(new[] { "substitute-a", "substitute-z" }, window.GetProperty("substituteDeviceAssetIds").EnumerateArray().Select(x => x.GetString()));
                var problem = JsonSerializer.Serialize(readback.Snapshot.Problem, SchedulingJson.Options);
                if (firstProblem is null) firstProblem = problem;
                else Assert.Equal(firstProblem, problem);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private static async Task VerifyExistingRushInsertion()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource { RushOrderId = "order-003" };
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        await using var factory = new JobFactory(source, clock: new InsertionClock(sample.HorizonStartUtc));
        await Migrate(factory);
        using var client = Client(factory);
        var template = sample.Orders.First();
        var problem = sample with
        {
            ProblemId = "existing-rush-baseline", AssemblyDependencies = [], LockedAssignments = [], QualityBlocks = [],
            Orders = Enumerable.Range(1, 3).Select(i => template with { OrderId = $"order-{i:D3}", Priority = 0, IsRush = false,
                Operations = [template.Operations.First() with { OperationId = $"order-{i:D3}-op", DurationMinutes = 1, Priority = 0, IsRush = false }] }).ToArray()
        };
        SchedulePlanContract original;
        await using (var scope = factory.Services.CreateAsyncScope())
            original = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateSchedulePlanCommand(problem));
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-003"), SchedulingJson.Options);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
            source.Release.TrySetResult();
            var completed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Completed, completed.Status);
            var result = completed.Result!;
            Assert.Equal(3, result.Candidate.Assignments.Count);
            Assert.Equal(sample.HorizonStartUtc.AddMinutes(1), result.PromiseUtc);
            Assert.False(result.Orders.Single(x => x.OrderId == "order-003").IsNew);
            Assert.Equal(sample.HorizonStartUtc, result.Candidate.Assignments.Single(x => x.OrderId == "order-003").StartUtc);
            Assert.True(result.Snapshot.Problem.Orders.Single(x => x.OrderId == "order-003").IsRush);
            Assert.Contains(result.Orders, x => x.Status == SchedulingInsertionOrderStatusContract.Delayed);
            Assert.Contains(result.Operations, x => x.OrderId == "order-001" && x.Paths.Count > 0);
            Assert.Equal(before, await ReadInsertionBaseline(factory, original));
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    private static async Task VerifyCandidateSelection(SchedulingReschedulingStrategyContract? selectedStrategy = null)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        var start = sample.HorizonStartUtc;
        var resource = sample.Resources.First();
        var order = sample.Orders.First();
        var operation = order.Operations.First();
        var segments = new[] { new ScheduleAssignmentSegmentContract(start.AddHours(2), start.AddHours(2).AddMinutes(5)),
            new ScheduleAssignmentSegmentContract(start.AddHours(2).AddMinutes(10), start.AddHours(2).AddMinutes(15)) };
        var problem = sample with { ProblemId = "candidate-baseline", AssemblyDependencies = [], QualityBlocks = [],
            UnavailabilityWindows = [], LockedAssignments = [new("manual", "order-002", "order-002-op", 1,
                resource.ResourceId, resource.WorkCenterId, segments[0].StartUtc, segments[1].EndUtc, "planner-lock", segments)],
            Orders = Enumerable.Range(1, 3).Select(i => order with { OrderId = $"order-{i:D3}",
                Operations = [operation with { OperationId = $"order-{i:D3}-op", OperationSequence = 1, DurationMinutes = 10,
                    EligibleResourceIds = [resource.ResourceId], PrimaryResourceId = resource.ResourceId,
                    DueUtc = start.AddMinutes(5), EarliestStartUtc = start,
                    SplitPolicy = ScheduleSplitPolicyContract.Interruptible }] }).ToArray() };
        var first = problem.Orders.First();
        var firstOperation = first.Operations.Single();
        var independentResource = sample.Resources.Skip(1).First();
        problem = problem with { Orders = problem.Orders.Select(x => x.OrderId == "order-001"
            ? x with { Operations = [firstOperation, firstOperation with { OperationId = "order-001-op20", OperationSequence = 2,
                PredecessorOperationIds = [firstOperation.OperationId] }] }
            : x.OrderId == "order-003" ? x with { Operations = [x.Operations.Single() with {
                RequiredCapabilityCode = independentResource.CapabilityCodes.First(), EligibleResourceIds = [independentResource.ResourceId],
                PrimaryResourceId = independentResource.ResourceId }] } : x).ToArray() };
        const string substituteId = "DEV-WELD-02";
        if (selectedStrategy is not null)
            problem = problem with {
                Resources = [.. problem.Resources, resource with { ResourceId = substituteId }],
                Orders = problem.Orders.Select(x => x.OrderId == "order-001" ? x with {
                    Operations = x.Operations.Select(op => op with {
                        EligibleResourceIds = [resource.ResourceId, substituteId], SetupMinutes = 5
                    }).ToArray() } : x).ToArray() };
        var source = new ControlledSource { CandidateProblem = problem };
        source.Release.TrySetResult();
        var equipment = new CandidateEquipment { SubstituteId = selectedStrategy is null ? null : substituteId };
        await using var factory = new JobFactory(source, clock: new InsertionClock(start), equipment: equipment);
        await Migrate(factory);
        using var service = Client(factory);
        await using var gateway = new GatewayJobFactory(service);
        using var client = GatewayClient(gateway);
        SchedulePlanContract baseline;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            baseline = await sender.Send(new CreateSchedulePlanCommand(problem));
            await sender.Send(new ReleaseSchedulePlanCommand(baseline.PlanId, problem.OrganizationId, problem.EnvironmentId));
            var independent = baseline.Assignments.Single(x => x.OrderId == "order-003");
            var actual = OperationExecutionProjection.Create(problem.OrganizationId, problem.EnvironmentId,
                independent.OrderId, independent.OperationId, independent.OperationSequence, independent.WorkCenterId, independent.StartUtc, "created");
            actual.ApplyStarted(independent.StartUtc, "started");
            actual.ApplyCompleted(independent.EndUtc, "completed");
            var seedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedDb.OperationExecutionProjections.Add(actual);
            await seedDb.SaveChangesAsync();
        }
        equipment.EndUtc = start.AddMinutes(30);
        const string previewRoute = "/api/business-console/v1/scheduling/workbench/candidates/preview";
        const string selectRoute = "/api/business-console/v1/scheduling/workbench/candidates/select";
        var request = new SchedulingCandidatePreviewRequestContract(problem.OrganizationId, problem.EnvironmentId, baseline.PlanId);
        var sourceReads = source.Calls;
        using var previewResponse = await client.PostAsJsonAsync(previewRoute, request, SchedulingJson.Options);
        Assert.Equal(sourceReads + 1, source.Calls);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingCandidateSetContract>>(SchedulingJson.Options))!.Data;
        Assert.Equal(selectedStrategy is null ? 1 : 2, preview.Candidates.Count);
        Assert.All(preview.Candidates, x => Assert.Equal(preview.InputFingerprint, x.InputFingerprint));
        var candidate = Assert.Single(preview.Candidates, x => x.Strategy == (selectedStrategy ?? SchedulingReschedulingStrategyContract.RightShift));
        if (selectedStrategy is not null)
        {
            var right = Assert.Single(preview.Candidates, x => x.Strategy == SchedulingReschedulingStrategyContract.RightShift);
            var transfer = Assert.Single(preview.Candidates, x => x.Strategy == SchedulingReschedulingStrategyContract.ResourceTransfer);
            Assert.Equal(right.Kpis.BaselineOnTimeDenominator, transfer.Kpis.BaselineOnTimeDenominator);
            Assert.Equal(right.Kpis.BaselineResourceUtilization, transfer.Kpis.BaselineResourceUtilization);
            Assert.All(right.Plan.Assignments, x => Assert.Equal(baseline.Assignments.Single(b => b.OrderId == x.OrderId && b.OperationId == x.OperationId).ResourceId, x.ResourceId));
            Assert.NotEmpty(transfer.Transfers);
            var directTransfer = Assert.Single(transfer.Transfers, x => x.OperationId == "order-001-op");
            Assert.Equal(resource.ResourceId, directTransfer.OriginalResourceId);
            Assert.Equal(substituteId, directTransfer.ResourceId);
            // The idle substitute has no preceding assignment, so the existing algorithm charges no first setup.
            Assert.Equal(0, directTransfer.SetupMinutes);
            Assert.Equal("CMMS-20261008-001", Assert.Single(directTransfer.DeviceSources).SourceReference);
            Assert.All(transfer.Transfers, x => Assert.Contains(x.ResourceId,
                problem.Orders.Single(o => o.OrderId == x.OrderId).Operations.Single(op => op.OperationId == x.OperationId).EligibleResourceIds));
        }
        Assert.Equal(SchedulePlanStatusContract.Preview, candidate.Plan.Status);
        Assert.NotEmpty(candidate.Movements);
        Assert.All(candidate.Movements, x => { Assert.NotEmpty(x.Reasons); Assert.NotEmpty(x.Paths); });
        Assert.Equal(2, candidate.Kpis.TotalLockedCount);
        Assert.Equal(2, candidate.Kpis.PreservedLockedCount);
        Assert.Equal(segments, Assert.Single(candidate.Plan.Assignments, x => x.OrderId == "order-002").Segments);
        Assert.Equal(2, candidate.Kpis.BaselineOnTimeDenominator);
        Assert.Equal(2, candidate.Kpis.CandidateOnTimeDenominator);
        Assert.Equal(3, candidate.Kpis.BaselineLateOrderCount);
        Assert.Equal(3, candidate.Kpis.CandidateLateOrderCount);
        var select = new SchedulingCandidateSelectRequestContract(request.OrganizationId, request.EnvironmentId,
            baseline.PlanId, preview.AsOfUtc, preview.InputFingerprint, candidate.Strategy);
        using var selectedResponse = await client.PostAsJsonAsync(selectRoute, select, SchedulingJson.Options);
        Assert.Equal(HttpStatusCode.OK, selectedResponse.StatusCode);
        var selected = (await selectedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingCandidateSelectionContract>>(SchedulingJson.Options))!.Data;
        Assert.Equal(SchedulePlanStatusContract.Generated, selected.Plan.Status);
        Assert.Equal(JsonSerializer.Serialize(candidate.Plan.Assignments, SchedulingJson.Options),
            JsonSerializer.Serialize(selected.Plan.Assignments, SchedulingJson.Options));
        using var reopened = GatewayClient(gateway);
        var restored = await reopened.GetFromJsonAsync<ResponseData<IReadOnlyList<SchedulingWorkingDraftContract>>>(
            $"/api/business-console/v1/scheduling/working-drafts?organizationId={request.OrganizationId}&environmentId={request.EnvironmentId}&planId={selected.Plan.PlanId}", SchedulingJson.Options);
        Assert.True(restored!.Success);
        Assert.Equal(selected.Plan.PlanId, Assert.Single(restored.Data).PlanId);
        Assert.Equal(JsonSerializer.Serialize(selected.WorkingDraft.State, SchedulingJson.Options),
            JsonSerializer.Serialize(Assert.Single(restored.Data).State, SchedulingJson.Options));
        var readPlan = await reopened.GetFromJsonAsync<ResponseData<SchedulePlanContract>>(
            $"/api/business-console/v1/scheduling/plans/{selected.Plan.PlanId}?organizationId={request.OrganizationId}&environmentId={request.EnvironmentId}", SchedulingJson.Options);
        Assert.True(readPlan!.Success);
        Assert.Equal(JsonSerializer.Serialize(selected.Plan.Assignments, SchedulingJson.Options),
            JsonSerializer.Serialize(readPlan.Data.Assignments.Select(x => x with { CurrentExecution = null }), SchedulingJson.Options));
        // A saved candidate remains a usable baseline: derived freeze flags must not become manual locks.
        using var secondPreviewResponse = await client.PostAsJsonAsync(previewRoute,
            request with { BaselinePlanId = selected.Plan.PlanId }, SchedulingJson.Options);
        var secondPreview = (await secondPreviewResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingCandidateSetContract>>(SchedulingJson.Options))!.Data;
        using var secondSelectedResponse = await client.PostAsJsonAsync(selectRoute,
            select with { BaselinePlanId = selected.Plan.PlanId, AsOfUtc = secondPreview.AsOfUtc,
                InputFingerprint = secondPreview.InputFingerprint, Strategy = SchedulingReschedulingStrategyContract.RightShift }, SchedulingJson.Options);
        var secondSelected = (await secondSelectedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingCandidateSelectionContract>>(SchedulingJson.Options))!;
        Assert.True(secondSelected.Success, secondSelected.Message);
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(secondPreview.Candidates, x => x.Strategy == SchedulingReschedulingStrategyContract.RightShift).Plan.Assignments, SchedulingJson.Options),
            JsonSerializer.Serialize(secondSelected.Data.Plan.Assignments, SchedulingJson.Options));
        var completedFreeze = Assert.Single(secondSelected.Data.Plan.FreezeContext!.Assignments, x => x.Assignment.OrderId == "order-003");
        Assert.Contains(SchedulePlanFreezeReasonContract.Completed, completedFreeze.Reasons);
        Assert.DoesNotContain(SchedulePlanFreezeReasonContract.ManualLock, completedFreeze.Reasons);
        equipment.EndUtc = start.AddMinutes(45);
        using var staleResponse = await client.PostAsJsonAsync(selectRoute, select, SchedulingJson.Options);
        var stale = await staleResponse.Content.ReadFromJsonAsync<ResponseData>(SchedulingJson.Options);
        Assert.False(stale!.Success);
        Assert.Contains("重预览", stale.Message);
        using var refreshedResponse = await client.PostAsJsonAsync(previewRoute, request, SchedulingJson.Options);
        var refreshed = (await refreshedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingCandidateSetContract>>(SchedulingJson.Options))!.Data;
        Assert.NotEqual(preview.InputFingerprint, refreshed.InputFingerprint);
        Assert.Equal(preview.Candidates.Count, refreshed.Candidates.Count);
        Assert.All(refreshed.Candidates, x => Assert.Equal(refreshed.InputFingerprint, x.InputFingerprint));
        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(3, await db.SchedulePlans.CountAsync());
        Assert.Single(await db.OperationExecutionProjections.ToArrayAsync());
        Assert.Equal(Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate.SchedulePlanLifecycleStatus.Generated,
            (await db.SchedulePlans.SingleAsync(x => x.PlanId == selected.Plan.PlanId)).Status);
        Assert.Equal(Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate.SchedulePlanLifecycleStatus.Released,
            (await db.SchedulePlans.SingleAsync(x => x.PlanId == baseline.PlanId)).Status);
        Assert.Equal(2, candidate.Kpis.MovedOperationCount);
        var independentBefore = baseline.Assignments.Single(x => x.OrderId == "order-003");
        var independentAfter = candidate.Plan.Assignments.Single(x => x.OrderId == "order-003");
        Assert.Equal((independentBefore.ResourceId, independentBefore.StartUtc, independentBefore.EndUtc),
            (independentAfter.ResourceId, independentAfter.StartUtc, independentAfter.EndUtc));
        Assert.Equal(0, candidate.Kpis.UnscheduledCountChange);
        Assert.Equal(candidate.Kpis.CandidateOnTimeRate - candidate.Kpis.BaselineOnTimeRate, candidate.Kpis.OnTimeRateChange);
        // Four 10-minute operations (manual segments total 10); four resources x two 480-minute shifts,
        // minus one 30-minute equipment window. Both sides use 40 / 3810, rounded to four decimals.
        if (selectedStrategy is null)
        {
            Assert.Equal(0.0105m, candidate.Kpis.BaselineResourceUtilization);
            Assert.Equal(0.0105m, candidate.Kpis.CandidateResourceUtilization);
            Assert.Equal(0m, candidate.Kpis.ResourceUtilizationChange);
        }
        // Selection still uses the original release/supersede chain, with no automatic release before this explicit action.
        using var released = await reopened.PostAsJsonAsync($"/api/business-console/v1/scheduling/plans/{selected.Plan.PlanId}/release",
            new { request.OrganizationId, request.EnvironmentId }, SchedulingJson.Options);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.True((await released.Content.ReadFromJsonAsync<ResponseData<JsonElement>>(SchedulingJson.Options))!.Success);
    }

    private sealed class CandidateEquipment : ISchedulingEquipmentAvailabilityProvider
    {
        public DateTimeOffset? EndUtc { get; set; }
        public string? SubstituteId { get; init; }
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(SchedulingProblemContract problem, CancellationToken ct) =>
            Task.FromResult(new EquipmentRuntimeAvailabilityResponse(1, problem.OrganizationId, problem.EnvironmentId,
                problem.HorizonStartUtc, problem.HorizonEndUtc, EndUtc is { } end
                    ? [new(problem.Resources.First().ResourceId, problem.Resources.First().WorkCenterId,
                        EquipmentRuntimeAvailabilityStatus.Unavailable, "equipment.downtime", EquipmentRuntimeSeverity.Blocked,
                        problem.HorizonStartUtc, end, EquipmentRuntimeSourceType.MaintenanceWindow, "CMMS-20261008-001", "equipment.downtime", SubstituteId is { } id ? [id] : [])] : []));
    }

    private static async Task VerifyInsertionFreeze(TimeSpan window)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        var start = sample.HorizonStartUtc;
        var template = sample.Orders.First();
        var operation = template.Operations.First();
        var resource = sample.Resources.First();
        var segments = new[] { new ScheduleAssignmentSegmentContract(start.AddMinutes(20), start.AddMinutes(25)),
            new ScheduleAssignmentSegmentContract(start.AddMinutes(30), start.AddMinutes(35)) };
        var problem = sample with
        {
            ProblemId = "insertion-freeze-baseline", AssemblyDependencies = [], QualityBlocks = [], UnavailabilityWindows = [],
            Orders = Enumerable.Range(1, 3).Select(i => template with
            {
                OrderId = $"order-{i:D3}", IsRush = false, Priority = 0,
                Operations = [operation with { OperationId = $"order-{i:D3}-op", DurationMinutes = 10,
                    IsRush = false, Priority = 0, SplitPolicy = ScheduleSplitPolicyContract.Interruptible,
                    EarliestStartUtc = i == 3 ? start.AddMinutes(60) : start }]
            }).ToArray(),
            LockedAssignments = [new SchedulingLockedAssignmentContract("manual-lock", "order-001", "order-001-op",
                operation.OperationSequence, resource.ResourceId, resource.WorkCenterId, segments[0].StartUtc,
                segments[1].EndUtc, "planner-lock", segments)]
        };
        var source = new ControlledSource { InsertionProblem = problem };
        var clock = new InsertionClock(start);
        await using var factory = new JobFactory(source, clock: clock,
            freezeSettings: new SchedulingFreezeSettings(window / 2,
                new Dictionary<string, TimeSpan> { [resource.WorkCenterId] = window }));
        await Migrate(factory);
        using var client = Client(factory);
        SchedulePlanContract original;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            original = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateSchedulePlanCommand(problem));
            var execution = OperationExecutionProjection.Create(problem.OrganizationId, problem.EnvironmentId,
                "order-003", "order-003-op", operation.OperationSequence, resource.WorkCenterId,
                start.AddMinutes(60), "created");
            execution.ApplyStarted(start.AddMinutes(60), "started");
            if (window > TimeSpan.Zero) execution.ApplyCompleted(start.AddMinutes(70), "completed");
            else execution.ApplyPaused(start.AddMinutes(65), "paused");
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OperationExecutionProjections.Add(execution);
            await db.SaveChangesAsync();
        }
        foreach (var orderId in new[] { "order-001", "order-002" })
        {
            using var moved = await client.PutAsJsonAsync(
                $"/api/business/v1/scheduling/plans/{original.PlanId}/operations/{orderId}-op/override",
                new { problem.OrganizationId, problem.EnvironmentId, resource.ResourceId,
                    StartUtc = start.AddHours(2), EndUtc = start.AddHours(2).AddMinutes(10) }, SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            Assert.True((await moved.Content.ReadFromJsonAsync<ResponseData<JsonElement>>(SchedulingJson.Options))!.Success);
        }
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-004"), SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
            await TestTimeout.RunAsync("freeze insertion source entered", async ct => await source.Entered.Task.WaitAsync(ct), Budget);
            source.Release.TrySetResult();
            var completed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Completed, completed.Status);
            var preview = completed.Preview!;
            Assert.Equal(SchedulePlanStatusContract.Preview, preview.Status);
            var manual = Assert.Single(preview.Assignments, x => x.OrderId == "order-001");
            var baselineManual = Assert.Single(original.Assignments, x => x.OrderId == "order-001");
            Assert.Equal((baselineManual.ResourceId, baselineManual.StartUtc, baselineManual.EndUtc),
                (manual.ResourceId, manual.StartUtc, manual.EndUtc));
            Assert.Equal(segments, manual.Segments);
            var stable = Assert.Single(preview.Assignments, x => x.OrderId == "order-002");
            var baselineStable = Assert.Single(original.Assignments, x => x.OrderId == "order-002");
            if (window > TimeSpan.Zero)
            {
                Assert.Equal(baselineStable.Segments, stable.Segments);
                Assert.Equal((baselineStable.ResourceId, baselineStable.StartUtc, baselineStable.EndUtc),
                    (stable.ResourceId, stable.StartUtc, stable.EndUtc));
            }
            else Assert.NotEqual(baselineStable.StartUtc, stable.StartUtc);
            var actual = Assert.Single(preview.Assignments, x => x.OrderId == "order-003");
            Assert.Equal(start.AddMinutes(60), actual.StartUtc);
            Assert.Equal(window > TimeSpan.Zero ? start.AddMinutes(70) : problem.HorizonEndUtc, actual.EndUtc);
            var inserted = Assert.Single(preview.Assignments, x => x.OrderId == "order-004");
            Assert.True(inserted.EndUtc <= manual.StartUtc || inserted.StartUtc >= manual.EndUtc);
            Assert.Contains(preview.Conflicts, x => x.OrderId == manual.OrderId &&
                x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment);
            Assert.NotNull(preview.FreezeContext);
            Assert.Null(completed.Result!.PromiseUtc);
            Assert.Contains(SchedulingInsertionFailureContract.BlockingConflict, completed.Result.Failures);
            Assert.Contains(completed.Result.Operations, x => x.OrderId == manual.OrderId && x.ReasonCodes.Contains("frozen-conflict"));
            Assert.Single(completed.Result.Snapshot.Execution);
            Assert.Single(completed.Result.Snapshot.FixedReservations);
            Assert.Equal(start, preview.FreezeContext.AsOfUtc);
            Assert.Equal(start + window / 2, preview.FreezeContext.DefaultWindowEndUtc);
            Assert.Contains(preview.FreezeContext.WorkCenterWindows, x => x.WorkCenterId == resource.WorkCenterId &&
                x.EndUtc == start + window);
            Assert.Contains(preview.FreezeContext.Assignments, x => x.Assignment.OrderId == manual.OrderId &&
                x.Reasons.Contains(SchedulePlanFreezeReasonContract.ManualLock));
            Assert.Contains(preview.FreezeContext.Assignments, x => x.Assignment.OrderId == actual.OrderId &&
                x.Reasons.Contains(window > TimeSpan.Zero ? SchedulePlanFreezeReasonContract.Completed : SchedulePlanFreezeReasonContract.Started));
            var manualFreeze = Assert.Single(preview.FreezeContext.Assignments, x => x.Assignment.OrderId == manual.OrderId);
            Assert.DoesNotContain(SchedulePlanFreezeReasonContract.StableWindow, manualFreeze.Reasons);
            if (window == TimeSpan.Zero)
                Assert.DoesNotContain(preview.FreezeContext.Assignments, x => x.Reasons.Contains(SchedulePlanFreezeReasonContract.StableWindow));
            clock.Now = start.AddDays(1);
            Assert.Equal(JsonSerializer.Serialize(preview, SchedulingJson.Options),
                JsonSerializer.Serialize((await ReadInsertion(client, accepted.JobId)).Preview, SchedulingJson.Options));
            Assert.Equal(before, await ReadInsertionBaseline(factory, original));
            await using var after = factory.Services.CreateAsyncScope();
            Assert.Single(await after.ServiceProvider.GetRequiredService<ApplicationDbContext>().SchedulePlans.ToArrayAsync());
            Assert.Single(await after.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScheduleProblems.ToArrayAsync());
            // #4230: a conflicting candidate is still saved faithfully; the existing release gate rejects it.
            using var savedResponse = await client.PostAsJsonAsync(
                $"/api/business/v1/scheduling/plans/{completed.Result!.CandidatePlanId}/revisions",
                new { problem.OrganizationId, problem.EnvironmentId,
                    IncludedOrderIds = completed.Result.Snapshot.Problem.Orders.Select(x => x.OrderId).ToArray(),
                    LockedAssignments = Array.Empty<SchedulingLockedAssignmentContract>() }, SchedulingJson.Options);
            var saved = (await savedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulePlanRevisionContract>>(SchedulingJson.Options))!;
            Assert.True(saved.Success, saved.Message);
            var persisted = await after.ServiceProvider.GetRequiredService<ISender>().Send(new GetSchedulePlanDetailQuery(
                saved.Data.Candidate.PlanId, problem.OrganizationId, problem.EnvironmentId));
            Assert.Equal(JsonSerializer.Serialize(preview.Assignments, SchedulingJson.Options), JsonSerializer.Serialize(persisted.Assignments, SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(preview.Conflicts.OrderBy(x => x.ConflictId), SchedulingJson.Options),
                JsonSerializer.Serialize(persisted.Conflicts.OrderBy(x => x.ConflictId), SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(preview.UnscheduledOperations, SchedulingJson.Options), JsonSerializer.Serialize(persisted.UnscheduledOperations, SchedulingJson.Options));
            Assert.Equal(JsonSerializer.Serialize(preview.FreezeContext, SchedulingJson.Options), JsonSerializer.Serialize(persisted.FreezeContext, SchedulingJson.Options));
            using var release = await client.PostAsJsonAsync(
                $"/api/business/v1/scheduling/plans/{persisted.PlanId}/release?organizationId={problem.OrganizationId}&environmentId={problem.EnvironmentId}",
                new { }, SchedulingJson.Options);
            Assert.False((await release.Content.ReadFromJsonAsync<ResponseData<ReleaseSchedulePlanResponse>>(SchedulingJson.Options))!.Success);
        }
        finally { source.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    private sealed class InsertionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [SchedulingPostgresFact]
    public async Task Insertion_preview_accepts_500_deduplicates_and_rejects_501_missing_snapshot_or_wrong_scope()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = new JobFactory(new ControlledSource());
        await Migrate(factory);
        using var client = Client(factory);
        var original = await SeedInsertionPlan(factory, 500);
        using var acceptedResponse = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, " order-500 "), SchedulingJson.Options);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = (await acceptedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
        Assert.Equal(500, accepted.Input.WorkOrderIds.Count);
        Assert.Equal(500, accepted.Input.WorkOrderIds.Distinct().Count());
        foreach (var input in new[] { InsertionInput(original, "new-order"),
            InsertionInput(original, "order-001") with { OrganizationId = "other" },
            InsertionInput(original, "order-001") with { EnvironmentId = "other" } })
        {
            using var rejected = await client.PostAsJsonAsync(InsertionRoute, input, SchedulingJson.Options);
            Assert.Contains(input.WorkOrderId == "new-order" ? "500" : "未找到", await rejected.Content.ReadAsStringAsync());
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ScheduleProblems.Remove(await db.ScheduleProblems.SingleAsync());
            await db.SaveChangesAsync();
        }
        using var historical = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-001"), SchedulingJson.Options);
        Assert.Contains("缺少完整排程问题快照", await historical.Content.ReadAsStringAsync());
        await using var after = factory.Services.CreateAsyncScope();
        Assert.Single(await after.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScheduleInsertionPreviewJobs.ToArrayAsync());
    }

    [SchedulingPostgresFact]
    public async Task Insertion_preview_failure_is_readable_and_does_not_change_saved_plans()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource { Failure = new KnownException("工艺路线不可用，请重新选择工单。") };
        source.Release.TrySetResult();
        await using var factory = new JobFactory(source);
        await Migrate(factory);
        using var client = Client(factory);
        var original = await SeedInsertionPlan(factory, 11);
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-012"), SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(SchedulingJson.Options))!.Data;
            var failed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Failed, failed.Status);
            Assert.Equal(source.Failure.Message, failed.FailureReason);
            Assert.Null(failed.Preview);
            Assert.Null(failed.Result);
            Assert.NotNull(failed.FinishedAtUtc);
            Assert.Equal(before, await ReadInsertionBaseline(factory, original));
            Assert.Equal(1, source.Calls);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private const string InsertionRoute = "/api/business/v1/scheduling/workbench/insertion-preview-jobs";
    private static SchedulingInsertionPreviewRequestContract InsertionInput(SchedulePlanContract plan, string orderId) =>
        new(Input(1).OrganizationId, Input(1).EnvironmentId, plan.PlanId, orderId);
    private static async Task<string> ReadInsertionBaseline(JobFactory factory, SchedulePlanContract original)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var plan = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetSchedulePlanDetailQuery(
            original.PlanId, Input(1).OrganizationId, Input(1).EnvironmentId));
        var snapshot = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScheduleProblems.AsNoTracking().SingleAsync();
        return JsonSerializer.Serialize(new { Plan = plan, snapshot.ProblemJson }, SchedulingJson.Options);
    }
    private static ScheduleInsertionPreviewJobWorker InsertionWorker(JobFactory factory) => new(
        factory.Services.GetRequiredService<ScheduleInsertionPreviewJobQueue>(), factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ScheduleInsertionPreviewJobWorker>.Instance);
    private static async Task<SchedulingInsertionPreviewJobDetailContract> ReadInsertion(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobDetailContract>>(
            $"{InsertionRoute}/{id}?organizationId={Input(1).OrganizationId}&environmentId={Input(1).EnvironmentId}", SchedulingJson.Options))!.Data;
    private static ValueTask<SchedulingInsertionPreviewJobDetailContract> TerminalInsertion(HttpClient client, Guid id) =>
        Eventually.WaitAsync("insertion preview terminal commit", async _ => await ReadInsertion(client, id),
            x => x.Status is SchedulingInsertionPreviewJobStatusContract.Completed or SchedulingInsertionPreviewJobStatusContract.Failed,
            x => $"{x.Status}: {x.FailureReason}", new EventuallyOptions(Budget, TimeSpan.FromMilliseconds(50), []));
    private static async Task<SchedulePlanContract> SeedInsertionPlan(JobFactory factory, int count)
    {
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = sample.Orders.First();
        var operation = order.Operations.First();
        var problem = sample with
        {
            ProblemId = "original-insertion-plan", AssemblyDependencies = [], LockedAssignments = [], QualityBlocks = [],
            Orders = Enumerable.Range(1, count).Select(i => order with
            {
                OrderId = $"order-{i:D3}", Operations = [operation with { OperationId = $"order-{i:D3}-op",
                    DurationMinutes = i == count ? 100000 : 1 }]
            }).ToArray()
        };
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateSchedulePlanCommand(problem));
    }

    private static SchedulingFirstPlanInputContract Input(int count)
    {
        var sample = ShockAbsorberSchedulingFixture.CreateProblem();
        return new(sample.OrganizationId, sample.EnvironmentId, sample.HorizonStartUtc, sample.HorizonEndUtc,
            Enumerable.Range(1, count).Select(i => new SchedulingFirstPlanOrderContract($"order-{i:D3}", i % 5, false)).ToArray());
    }
    private static async Task Migrate(JobFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }
    private static HttpClient Client(JobFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        return client;
    }
    private static ScheduleFirstPlanJobWorker Worker(JobFactory factory) => new(factory.Services.GetRequiredService<ScheduleFirstPlanJobQueue>(),
        factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.FailureLog);
    private static async Task<SchedulingFirstPlanJobContract> Accept(HttpClient client, SchedulingFirstPlanInputContract input, CancellationToken ct = default, string route = Route)
    {
        using var response = await client.PostAsJsonAsync(route, input, SchedulingJson.Options, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingFirstPlanJobContract>>(SchedulingJson.Options, ct))!.Data;
    }
    private static async Task<SchedulingFirstPlanJobContract> Read(HttpClient client, Guid id, string route = Route)
    {
        var scope = Input(1);
        return (await client.GetFromJsonAsync<ResponseData<SchedulingFirstPlanJobContract>>(
            $"{route}/{id}?organizationId={scope.OrganizationId}&environmentId={scope.EnvironmentId}", SchedulingJson.Options))!.Data;
    }
    private static async Task<SchedulingFirstPlanJobContract> Terminal(HttpClient client, Guid id, SchedulingFirstPlanJobStatusContract expected, JobFailureLogger log, string route = Route)
    {
        var result = await Eventually.WaitAsync("first-plan worker terminal commit", async _ => await Read(client, id, route),
            x => x.Status is SchedulingFirstPlanJobStatusContract.Completed or SchedulingFirstPlanJobStatusContract.Failed,
            x => $"{x.Status}: {x.FailureReason}", new EventuallyOptions(Budget, TimeSpan.FromMilliseconds(50), []));
        Assert.True(result.Status == expected, $"Expected {expected}, observed {result.Status}: {result.FailureReason}; {log.Failure}");
        return result;
    }
    private static HttpClient GatewayClient(GatewayJobFactory factory)
    {
        var client = factory.CreateClient();
        var input = Input(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            BusinessGatewayTestTokens.ValidAccessToken(input.OrganizationId, input.EnvironmentId));
        return client;
    }

    // JWT verification and the Gateway transport run normally; only IAM is a local permission fixture.
    private sealed class GatewayJobFactory(HttpClient scheduling) : WebApplicationFactory<Gateway::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<GatewayAuth.IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<GatewayAuth.IBusinessGatewayAuthorizationClient, JobGatewayAuthorization>();
                services.RemoveAll<GatewayServices.IBusinessSchedulingClient>();
                services.AddSingleton<GatewayServices.IBusinessSchedulingClient>(_ =>
                    new GatewayServices.HttpBusinessSchedulingClient(scheduling));
            });
        }
    }
    private sealed class JobGatewayAuthorization : GatewayAuth.IBusinessGatewayAuthorizationClient
    {
        public Task<GatewayAuth.BusinessGatewayAuthorizationResult> CheckAsync(string bearerToken,
            GatewayAuth.BusinessGatewayPermissionRequirement requirement, CancellationToken cancellationToken) =>
            Task.FromResult(GatewayAuth.BusinessGatewayAuthorizationResult.Allowed("planner", "user", "planner",
                requirement.OrganizationId, requirement.EnvironmentId));
    }

    private sealed class JobFactory(ControlledSource source, SaveChangesInterceptor? interceptor = null,
        TimeProvider? clock = null, SchedulingFreezeSettings? freezeSettings = null,
        ISchedulingEquipmentAvailabilityProvider? equipment = null) : WebApplicationFactory<Program>
    {
        public JobFailureLogger FailureLog { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.UseSetting("ConnectionStrings:PostgreSQL", SchedulingPostgresLaneDatabase.ConnectionString);
            builder.UseSetting("Persistence:AutoMigrate", "false");
            foreach (var service in new[] { "MasterData", "ProductEngineering", "Mes", "IndustrialTelemetry", "Maintenance" })
                builder.UseSetting($"{service}:BaseUrl", "http://localhost");
            builder.ConfigureTestServices(services =>
            {
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(Program).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.IncludeAbstractValidators = true;
                });
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.RemoveAll<ISchedulingWorkbenchSourceProvider>();
                services.RemoveAll<ISchedulingProblemProducer>();
                services.AddSingleton<ISchedulingWorkbenchSourceProvider>(source);
                services.AddSingleton<ISchedulingProblemProducer>(source);
                if (equipment is not null) { services.RemoveAll<ISchedulingEquipmentAvailabilityProvider>(); services.AddSingleton(equipment); }
                if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
                if (freezeSettings is not null) { services.RemoveAll<SchedulingFreezeSettings>(); services.AddSingleton(freezeSettings); }
                if (interceptor is not null) services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
            });
        }
    }
    private sealed class VersionedEquipment : ISchedulingEquipmentAvailabilityProvider
    {
        public string Version { get; set; } = "v1";
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(SchedulingProblemContract problem, CancellationToken ct) =>
            Task.FromResult(new EquipmentRuntimeAvailabilityResponse(1, problem.OrganizationId, problem.EnvironmentId,
                problem.HorizonStartUtc, problem.HorizonEndUtc,
                [new(problem.Resources.First().ResourceId, null, EquipmentRuntimeAvailabilityStatus.Unavailable,
                    EquipmentRuntimeReasonCodes.Downtime, EquipmentRuntimeSeverity.Blocked,
                    problem.HorizonEndUtc.AddMinutes(-1), problem.HorizonEndUtc,
                    EquipmentRuntimeSourceType.Downtime, "source-maintenance", "equipment.downtime", ["substitute-z", "substitute-a"],
                    ExpectedRestoreAtUtc: problem.HorizonStartUtc.AddMinutes(-1), RestorePredictionSource: "device-mttr",
                    RestorePredictionSourceVersion: Version)]));
    }
    // This provider lane proves Scheduling persistence/HTTP/worker behavior; CAP transport has its own lane.
    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class JobFailureLogger : ILogger<ScheduleFirstPlanJobWorker>
    {
        public Exception? Failure { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Failure = exception;
    }

    private sealed class CompletionWriteFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ScheduleFirstPlanJob>().Any(x => x.Entity.Status == ScheduleFirstPlanJobStatus.Completed))
                throw new KnownException("模拟首版终态写入失败");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ControlledSource : ISchedulingWorkbenchSourceProvider, ISchedulingProblemProducer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public KnownException? Failure { get; init; }
        public int Calls { get; private set; }
        public SchedulingProblemContract? InsertionProblem { get; init; }
        public string? RushOrderId { get; init; }
        public SchedulingProblemContract? CandidateProblem { get; init; }
        public async Task<IReadOnlyCollection<SchedulingWorkbenchProblemSourceOrder>> ResolveOrdersAsync(string org, string env,
            DateTimeOffset start, IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections, CancellationToken ct)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            if (Failure is not null) throw Failure;
            return selections.Select(x => new SchedulingWorkbenchProblemSourceOrder(
                new(x.WorkOrderId, "SKU-001", 1, start.AddDays(7), x.Priority, x.WorkOrderId == RushOrderId || x.IsRush, start, "routing-v1"), [])).ToArray();
        }
        public Task<SchedulingProblemContract> AssembleAsync(AssembleSchedulingProblemRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SchedulingProblemContract> AssembleWorkbenchAsync(AssembleSchedulingWorkbenchProblemRequest request, CancellationToken ct)
        {
            if (CandidateProblem is not null) return Task.FromResult(CandidateProblem with { ProblemId = request.ProblemId,
                Orders = CandidateProblem.Orders.Where(o => request.Orders.Any(x => x.Order.OrderId == o.OrderId)).ToArray() });
            var sample = InsertionProblem ?? ShockAbsorberSchedulingFixture.CreateProblem();
            var order = sample.Orders.First();
            var operation = order.Operations.First();
            if (InsertionProblem is not null)
                return Task.FromResult(sample with
                {
                    ProblemId = request.ProblemId,
                    Orders = sample.Orders.Append(order with { OrderId = "order-004", Priority = 1000,
                        Operations = [operation with { OperationId = "order-004-op", Priority = 1000 }] }).ToArray(),
                    LockedAssignments = [],
                    UnavailabilityWindows = [new SchedulingUnavailabilityWindowContract(sample.Resources.First().ResourceId,
                        null, sample.HorizonStartUtc.AddMinutes(20), sample.HorizonStartUtc.AddMinutes(25), "maintenance")]
                });
            return Task.FromResult(sample with
            {
                ProblemId = request.ProblemId, OrganizationId = request.OrganizationId, EnvironmentId = request.EnvironmentId,
                HorizonStartUtc = request.HorizonStartUtc, HorizonEndUtc = request.HorizonEndUtc,
                Orders = request.Orders.Select(x => order with { OrderId = x.Order.OrderId, Priority = x.Order.Priority, IsRush = x.Order.IsRush,
                    Operations = [operation with { OperationId = $"{x.Order.OrderId}-op", DurationMinutes = 1, IsRush = x.Order.IsRush }] }).ToArray(),
                AssemblyDependencies = [], LockedAssignments = [], QualityBlocks = []
            });
        }
    }
}
