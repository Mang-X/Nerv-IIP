using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class SchedulingFirstPlanJobPostgresTests
{
    private const string Route = "/api/business/v1/scheduling/workbench/first-plan-jobs";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [SchedulingPostgresFact]
    public async Task Acceptance_commits_before_blocked_500_order_calculation_and_completed_plan_is_readable()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new ControlledSource();
        await using var factory = new JobFactory(source);
        await Migrate(factory);
        using var client = Client(factory);
        using var worker = Worker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var input = Input(500);
            // If HTTP does the computation, this times out while the controlled source remains blocked.
            var accepted = await TestTimeout.RunAsync("first-plan acceptance before assembly completes",
                async ct => await Accept(client, input, ct), Budget);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Created, accepted.Status);
            Assert.Null(accepted.PlanId);
            await TestTimeout.RunAsync("worker entered independent assembly scope", async ct =>
                await source.Entered.Task.WaitAsync(ct), Budget);
            var running = await Read(client, accepted.JobId);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Running, running.Status);
            Assert.NotNull(running.StartedAtUtc);
            Assert.Equal(JsonSerializer.Serialize(input, SchedulingJson.Options), JsonSerializer.Serialize(running.Input, SchedulingJson.Options));
            using var wrongOrg = await client.GetAsync($"{Route}/{accepted.JobId}?organizationId=other&environmentId={input.EnvironmentId}");
            Assert.Contains("未找到", await wrongOrg.Content.ReadAsStringAsync());
            using var wrongEnv = await client.GetAsync($"{Route}/{accepted.JobId}?organizationId={input.OrganizationId}&environmentId=other");
            Assert.Contains("未找到", await wrongEnv.Content.ReadAsStringAsync());
            client.Dispose(); // request/client lifetime ends while computation is still blocked.
            source.Release.TrySetResult();
            using var reopened = Client(factory);
            var completed = await Terminal(reopened, accepted.JobId, SchedulingFirstPlanJobStatusContract.Completed, factory.FailureLog);
            Assert.NotNull(completed.FinishedAtUtc);
            Assert.Null(completed.FailureReason);
            Assert.NotNull(completed.PlanId);
            var result = await reopened.GetFromJsonAsync<ResponseData<SchedulePlanContract>>(
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
        using var client = Client(factory);
        using var worker = Worker(factory);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var accepted = await Accept(client, Input(1));
            await TestTimeout.RunAsync("failure source entered", async ct => await source.Entered.Task.WaitAsync(ct), Budget);
            Assert.Equal(SchedulingFirstPlanJobStatusContract.Running, (await Read(client, accepted.JobId)).Status);
            source.Release.TrySetResult();
            var failed = await Terminal(client, accepted.JobId, SchedulingFirstPlanJobStatusContract.Failed, factory.FailureLog);
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
    private static async Task<SchedulingFirstPlanJobContract> Accept(HttpClient client, SchedulingFirstPlanInputContract input, CancellationToken ct = default)
    {
        using var response = await client.PostAsJsonAsync(Route, input, SchedulingJson.Options, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResponseData<SchedulingFirstPlanJobContract>>(SchedulingJson.Options, ct))!.Data;
    }
    private static async Task<SchedulingFirstPlanJobContract> Read(HttpClient client, Guid id)
    {
        var scope = Input(1);
        return (await client.GetFromJsonAsync<ResponseData<SchedulingFirstPlanJobContract>>(
            $"{Route}/{id}?organizationId={scope.OrganizationId}&environmentId={scope.EnvironmentId}", SchedulingJson.Options))!.Data;
    }
    private static async Task<SchedulingFirstPlanJobContract> Terminal(HttpClient client, Guid id, SchedulingFirstPlanJobStatusContract expected, JobFailureLogger log)
    {
        var result = await Eventually.WaitAsync("first-plan worker terminal commit", async _ => await Read(client, id),
            x => x.Status is SchedulingFirstPlanJobStatusContract.Completed or SchedulingFirstPlanJobStatusContract.Failed,
            x => $"{x.Status}: {x.FailureReason}", new EventuallyOptions(Budget, TimeSpan.FromMilliseconds(50), []));
        Assert.True(result.Status == expected, $"Expected {expected}, observed {result.Status}: {result.FailureReason}; {log.Failure}");
        return result;
    }
    private sealed class JobFactory(ControlledSource source, SaveChangesInterceptor? interceptor = null) : WebApplicationFactory<Program>
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
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.RemoveAll<ISchedulingWorkbenchSourceProvider>();
                services.RemoveAll<ISchedulingProblemProducer>();
                services.AddSingleton<ISchedulingWorkbenchSourceProvider>(source);
                services.AddSingleton<ISchedulingProblemProducer>(source);
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
            var sample = ShockAbsorberSchedulingFixture.CreateProblem();
            var order = sample.Orders.First();
            var operation = order.Operations.First();
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
