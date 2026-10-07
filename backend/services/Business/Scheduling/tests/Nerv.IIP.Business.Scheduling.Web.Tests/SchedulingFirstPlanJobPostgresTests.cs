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
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobContract>>(SchedulingJson.Options))!.Data;
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
            Assert.Equal(12, completed.Preview.Assignments.Count);
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
        var before = await ReadInsertionBaseline(factory, original);
        using var worker = InsertionWorker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var response = await client.PostAsJsonAsync(InsertionRoute, InsertionInput(original, "order-004"), SchedulingJson.Options);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobContract>>(SchedulingJson.Options))!.Data;
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
        var accepted = (await acceptedResponse.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobContract>>(SchedulingJson.Options))!.Data;
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
            var accepted = (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobContract>>(SchedulingJson.Options))!.Data;
            var failed = await TerminalInsertion(client, accepted.JobId);
            Assert.Equal(SchedulingInsertionPreviewJobStatusContract.Failed, failed.Status);
            Assert.Equal(source.Failure.Message, failed.FailureReason);
            Assert.Null(failed.Preview);
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
    private static async Task<SchedulingInsertionPreviewJobContract> ReadInsertion(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ResponseData<SchedulingInsertionPreviewJobContract>>(
            $"{InsertionRoute}/{id}?organizationId={Input(1).OrganizationId}&environmentId={Input(1).EnvironmentId}", SchedulingJson.Options))!.Data;
    private static ValueTask<SchedulingInsertionPreviewJobContract> TerminalInsertion(HttpClient client, Guid id) =>
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
        TimeProvider? clock = null, SchedulingFreezeSettings? freezeSettings = null) : WebApplicationFactory<Program>
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
                if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
                if (freezeSettings is not null) { services.RemoveAll<SchedulingFreezeSettings>(); services.AddSingleton(freezeSettings); }
                if (interceptor is not null) services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
            });
        }
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
        public async Task<IReadOnlyCollection<SchedulingWorkbenchProblemSourceOrder>> ResolveOrdersAsync(string org, string env,
            DateTimeOffset start, IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections, CancellationToken ct)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            if (Failure is not null) throw Failure;
            return selections.Select(x => new SchedulingWorkbenchProblemSourceOrder(
                new(x.WorkOrderId, "SKU-001", 1, start.AddDays(7), x.Priority, x.IsRush, start, "routing-v1"), [])).ToArray();
        }
        public Task<SchedulingProblemContract> AssembleAsync(AssembleSchedulingProblemRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SchedulingProblemContract> AssembleWorkbenchAsync(AssembleSchedulingWorkbenchProblemRequest request, CancellationToken ct)
        {
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
                Orders = request.Orders.Select(x => order with { OrderId = x.Order.OrderId, Priority = x.Order.Priority,
                    Operations = [operation with { OperationId = $"{x.Order.OrderId}-op", DurationMinutes = 1 }] }).ToArray(),
                AssemblyDependencies = [], LockedAssignments = [], QualityBlocks = []
            });
        }
    }
}
