using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingReplayTests
{
    [Fact]
    public async Task Exact_replay_with_stored_input_plan_id_and_time_matches_the_canonical_digest()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new FiniteCapacityScheduler();
        AddReplayFixture(db, problem, engine, SchedulingPersistenceTestData.CurrentAvailableTrace);
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(SchedulePlanReplayVerificationStatus.Verified, result.Status);
        Assert.NotNull(result.ExpectedDigest);
        Assert.Equal(result.ExpectedDigest, result.ActualDigest);
    }

    [Theory]
    [InlineData(
        "unknown-engine",
        "aps-lite-v1",
        SchedulePlanReplayVerificationStatus.UnknownEngineId)]
    [InlineData(
        "finite-capacity",
        "aps-lite-v0",
        SchedulePlanReplayVerificationStatus.UnknownEngineVersion)]
    public async Task Replay_never_falls_back_to_the_current_engine(
        string persistedEngineId,
        string persistedAlgorithmVersion,
        SchedulePlanReplayVerificationStatus expectedStatus)
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var currentEngine = new RecordingEngine();
        AddReplayFixture(
            db,
            problem,
            new FiniteCapacityScheduler(),
            SchedulingPersistenceTestData.CurrentAvailableTrace with
            {
                EngineId = persistedEngineId,
                EngineVersion = persistedAlgorithmVersion,
            },
            persistedAlgorithmVersion: persistedAlgorithmVersion);
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [currentEngine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(currentEngine.WasCalled);
    }

    [Fact]
    public async Task Replay_reports_effective_input_unavailable_for_legacy_null_input()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new FiniteCapacityScheduler();
        var snapshot = AddReplayFixture(
            db,
            problem,
            engine,
            SchedulingPersistenceTestData.CurrentAvailableTrace);
        db.Entry(snapshot).Property(x => x.EngineInputJson).CurrentValue = null;
        db.Entry(snapshot).Property(x => x.EngineInputFingerprint).CurrentValue = null;
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(SchedulePlanReplayVerificationStatus.EffectiveInputUnavailable, result.Status);
    }

    [Fact]
    public async Task Replay_reports_trace_unavailable_for_legacy_trace()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new FiniteCapacityScheduler();
        AddReplayFixture(db, problem, engine, SchedulePlanExecutionTraceSnapshot.LegacyUnavailable);
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(SchedulePlanReplayVerificationStatus.TraceUnavailable, result.Status);
    }

    [Fact]
    public async Task Replay_reports_unsupported_trace_schema_without_running_the_engine()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new RecordingEngine();
        AddReplayFixture(
            db,
            problem,
            new FiniteCapacityScheduler(),
            SchedulingPersistenceTestData.CurrentAvailableTrace with { TraceSchemaVersion = 99 });
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(SchedulePlanReplayVerificationStatus.UnsupportedTraceSchema, result.Status);
        Assert.False(engine.WasCalled);
    }

    [Fact]
    public async Task Replay_reports_digest_mismatch_for_changed_plan_output()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new FiniteCapacityScheduler();
        AddReplayFixture(
            db,
            problem,
            engine,
            SchedulingPersistenceTestData.CurrentAvailableTrace,
            mutatePersistedPlan: plan => plan with
            {
                Metrics = plan.Metrics with { AssignedMinutes = plan.Metrics.AssignedMinutes + 1 }
            });
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);

        var result = await service.VerifyAsync(
            "plan-replay-001",
            problem.OrganizationId,
            problem.EnvironmentId,
            CancellationToken.None);

        Assert.Equal(SchedulePlanReplayVerificationStatus.DigestMismatch, result.Status);
        Assert.NotEqual(result.ExpectedDigest, result.ActualDigest);
    }

    [Theory]
    [InlineData("null-orders")]
    [InlineData("invalid-horizon")]
    [InlineData("problem-id")]
    [InlineData("organization-id")]
    [InlineData("environment-id")]
    [InlineData("fingerprint")]
    public async Task Replay_rejects_semantically_invalid_or_scope_inconsistent_exact_input_without_calling_engine(
        string invalidCase)
    {
        await using var db = CreateDbContext();
        var problem = SchedulingProblemNormalizer.Normalize(
            ShockAbsorberSchedulingFixture.CreateProblem());
        var engine = new RecordingEngine();
        var snapshot = AddReplayFixture(
            db,
            problem,
            new FiniteCapacityScheduler(),
            SchedulingPersistenceTestData.CurrentAvailableTrace);
        var invalidInput = invalidCase switch
        {
            "null-orders" => problem with { Orders = null! },
            "invalid-horizon" => problem with { HorizonEndUtc = problem.HorizonStartUtc },
            "problem-id" => problem with { ProblemId = "problem-other" },
            "organization-id" => problem with { OrganizationId = "org-other" },
            "environment-id" => problem with { EnvironmentId = "env-other" },
            _ => problem,
        };
        var inputJson = JsonSerializer.Serialize(invalidInput, SchedulingJson.Options);
        var inputFingerprint = invalidCase == "fingerprint"
            ? new string('0', 64)
            : Fingerprint(inputJson);
        db.Entry(snapshot).Property(x => x.EngineInputJson).CurrentValue = inputJson;
        db.Entry(snapshot).Property(x => x.EngineInputFingerprint).CurrentValue = inputFingerprint;
        await db.SaveChangesAsync();
        var service = new SchedulePlanReplayService(db, [engine]);
        SchedulePlanReplayVerificationResult? result = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await service.VerifyAsync(
                "plan-replay-001",
                problem.OrganizationId,
                problem.EnvironmentId,
                CancellationToken.None);
        });

        Assert.Null(exception);
        Assert.Equal(SchedulePlanReplayVerificationStatus.InvalidEffectiveInput, result?.Status);
        Assert.False(engine.WasCalled);
    }

    private static ScheduleProblemSnapshot AddReplayFixture(
        ApplicationDbContext db,
        SchedulingProblemContract problem,
        ISchedulingEngine engine,
        SchedulePlanExecutionTraceSnapshot trace,
        string? persistedAlgorithmVersion = null,
        Func<SchedulePlanContract, SchedulePlanContract>? mutatePersistedPlan = null)
    {
        var normalized = SchedulingProblemNormalizer.Normalize(problem);
        var inputJson = JsonSerializer.Serialize(normalized, SchedulingJson.Options);
        var inputFingerprint = SchedulingPersistenceTestData.UnchangedEffectiveInputFingerprint(inputJson);
        var plan = engine.Schedule(normalized, "plan-replay-001", problem.HorizonStartUtc);
        plan = SchedulePlanContractMapper.WithStatus(plan, SchedulePlanStatusContract.Generated) with
        {
            AlgorithmVersion = persistedAlgorithmVersion ?? plan.AlgorithmVersion,
        };
        plan = mutatePersistedPlan?.Invoke(plan) ?? plan;
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            problem.OrganizationId,
            problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(plan),
            trace));
        var snapshot = new ScheduleProblemSnapshot(
            problem.ProblemId,
            problem.ContractVersion,
            problem.OrganizationId,
            problem.EnvironmentId,
            inputFingerprint,
            inputJson,
            problem.HorizonStartUtc,
            problem.HorizonEndUtc,
            problem.HorizonStartUtc,
            inputFingerprint,
            inputJson);
        db.ScheduleProblems.Add(snapshot);
        return snapshot;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-replay-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static string Fingerprint(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private sealed class RecordingEngine : ISchedulingEngine
    {
        public string EngineId => "finite-capacity";

        public string Version => "aps-lite-v1";

        public bool WasCalled { get; private set; }

        public SchedulePlanContract Schedule(
            SchedulingProblemContract problem,
            string planId,
            DateTimeOffset generatedAtUtc)
        {
            WasCalled = true;
            return new FiniteCapacityScheduler().Schedule(problem, planId, generatedAtUtc);
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
