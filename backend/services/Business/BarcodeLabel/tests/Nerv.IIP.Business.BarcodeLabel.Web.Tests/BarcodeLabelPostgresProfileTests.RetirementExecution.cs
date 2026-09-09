using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Retirement;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed partial class BarcodeLabelPostgresProfileTests
{
    private static readonly DateTimeOffset ExecutionEpoch = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly TemplateAssetRetirementExecutorOptions ExecutionOptions = new(
        "synthetic-retirement-execution-key-3045"u8.ToArray(), "business-barcode-label", "file-storage", 2592000, 300, 300);

    // Oracle: #3045 / #3028, one durable decision, frozen inputs, seven-day recovery boundary.
    [RealPostgresFact]
    public async Task Retirement_executor_recovers_lost_response_after_restart_with_frozen_inputs_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var remote = new RetirementTransport(decision, loseResponses: true);
        await RunRetirementHostAsync(clock, remote, "pending");
        var first = await ReadExecutionDecisionAsync();
        Assert.Equal(ExecutionEpoch, first.FirstSentAtUtc);
        Assert.Equal(ExecutionEpoch.AddDays(7), first.RecoveryUntilUtc);
        Assert.Equal("pending", first.Status);
        Assert.Equal(1, remote.Acceptances);

        // Fresh context/executor after the original short proof has expired; the remote receipt is retained.
        clock.Advance(TimeSpan.FromMinutes(11));
        remote.LoseResponses = false;
        await RunRetirementHostAsync(clock, remote, "quota-released",
            ExecutionOptions with { ClientWindowSeconds = 1, LeaseSeconds = 1 });
        var completed = await ReadExecutionDecisionAsync();
        Assert.Equal("quota-released", completed.Status);
        Assert.Equal(2592000, completed.ClientWindowSeconds);
        Assert.Equal(300, completed.ExecutorLeaseSeconds);
        Assert.Equal(2592000, completed.ReplayHorizonSeconds);
        Assert.Equal(ExecutionEpoch, completed.QuotaReleasedAtUtc);
        Assert.Equal(2, remote.Requests.Count);
        Assert.NotEqual(remote.Requests[0].Signature, remote.Requests[1].Signature);
        Assert.Equal(1, remote.Acceptances);
        Assert.False(await ExecuteRetirementAsync(clock, remote));
        Assert.Equal(2, remote.Requests.Count);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_concurrent_delivery_and_abandoned_lease_recover_once_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new RetirementTransport(decision) { BeforeResponse = async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }};
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = ExecuteRetirementAsync(clock, remote, ct: timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.False(await ExecuteRetirementAsync(clock, remote, ct: timeout.Token));
            Assert.Single(remote.Requests);
        }
        finally { release.TrySetResult(); }
        await first;
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);

        await ResetAndMigrateSchemaAsync();
        decision = await SeedExecutionDecisionAsync();
        await using (var abandoned = CreatePostgresDbContext(LaneConnectionString))
            Assert.NotNull(await new TemplateAssetRetirementExecutionStore(abandoned, clock).ClaimAsync(2592000, 300, 300, timeout.Token));
        var restartedRemote = new RetirementTransport(decision);
        Assert.False(await ExecuteRetirementAsync(clock, restartedRemote));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await ExecuteRetirementAsync(clock, restartedRemote));
        Assert.Single(restartedRemote.Requests);
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_deadline_unknown_wins_both_inflight_orderings_on_postgres()
    {
        foreach (var afterBoundary in new[] { TimeSpan.Zero, TimeSpan.FromTicks(10) })
        foreach (var expiryFirst in new[] { false, true })
        {
            await ResetAndMigrateSchemaAsync();
            var decision = await SeedExecutionDecisionAsync();
            var clock = new FakeTimeProvider(ExecutionEpoch);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var remote = new RetirementTransport(decision) { BeforeResponse = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }};
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var inFlight = ExecuteRetirementAsync(clock, remote, ct: timeout.Token);
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                clock.Advance(TimeSpan.FromDays(7) + afterBoundary);
                if (expiryFirst) Assert.False(await ExecuteRetirementAsync(clock, remote, ct: timeout.Token));
            }
            finally { release.TrySetResult(); }
            await inFlight;
            await AssertPermanentUnknownAsync(clock, remote);
        }
    }

    [RealPostgresFact]
    public async Task Retirement_result_waiting_for_row_lock_samples_deadline_after_lock_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        await using var claimant = CreatePostgresDbContext(LaneConnectionString);
        var claim = await new TemplateAssetRetirementExecutionStore(claimant, clock).ClaimAsync(2592000, 300, 300, default);
        Assert.NotNull(claim);
        await using var holder = CreatePostgresDbContext(LaneConnectionString);
        await using var transaction = await holder.Database.BeginTransactionAsync();
        _ = await holder.TemplateAssetRetirementDecisions.FromSqlInterpolated(
            $"SELECT * FROM barcode.template_asset_retirement_decisions WHERE id = {claim.Id.Id} FOR UPDATE").SingleAsync();
        await using var completing = CreatePostgresDbContext(LaneConnectionString);
        await completing.Database.OpenConnectionAsync();
        var completingPid = ((Npgsql.NpgsqlConnection)completing.Database.GetDbConnection()).ProcessID;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var completion = new TemplateAssetRetirementExecutionStore(completing, clock)
            .CompleteAsync(claim, ExecutionEpoch, 2592000, timeout.Token);
        try
        {
            await Eventually.WaitAsync("retirement result waits for owned row", async ct =>
            {
                await using var command = holder.Database.GetDbConnection().CreateCommand();
                command.Transaction = holder.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = $"SELECT cardinality(pg_blocking_pids({completingPid}))";
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            }, count => count > 0, count => $"blockingProcesses={count}",
                new EventuallyOptions(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(20), [LaneConnectionString]));
            clock.Advance(TimeSpan.FromDays(7));
        }
        finally { await transaction.CommitAsync(); }
        await completion;
        Assert.Equal("execution-outcome-unknown", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_result_before_deadline_commits_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var remote = new RetirementTransport(decision) { BeforeResponse = _ =>
        {
            clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromTicks(10));
            return Task.CompletedTask;
        }};
        await ExecuteRetirementAsync(clock, remote);
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await ExecuteRetirementAsync(clock, remote));
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_unknown_preserves_both_remote_outcome_counterexamples_on_postgres()
    {
        foreach (var accepted in new[] { false, true })
        {
            await ResetAndMigrateSchemaAsync();
            var decision = await SeedExecutionDecisionAsync();
            var clock = new FakeTimeProvider(ExecutionEpoch);
            var remote = new RetirementTransport(decision, loseResponses: true) { Accept = accepted };
            await ExecuteRetirementAsync(clock, remote);
            // Model either remote physical completion or no acceptance at all; both are unobservable locally.
            remote.PhysicallyCompleted = accepted;
            clock.Advance(TimeSpan.FromDays(7));
            Assert.False(await ExecuteRetirementAsync(clock, remote));
            await AssertPermanentUnknownAsync(clock, remote);
            Assert.Equal(accepted ? 1 : 0, remote.Acceptances);
            Assert.Equal(accepted, remote.PhysicallyCompleted);
        }
    }

    private static async Task AssertPermanentUnknownAsync(FakeTimeProvider clock, RetirementTransport remote)
    {
        var requests = remote.Requests.Count;
        var signatures = remote.Signatures;
        var acceptances = remote.Acceptances;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            Assert.False(await ExecuteRetirementAsync(clock, remote));
        }
        var held = await ReadExecutionDecisionAsync();
        Assert.Equal("execution-outcome-unknown", held.Status);
        Assert.Null(held.QuotaReleasedAtUtc);
        Assert.Null(held.ReplayHorizonSeconds);
        Assert.Null(held.ExecutionLeaseId);
        Assert.Equal(requests, remote.Requests.Count);
        Assert.Equal(signatures, remote.Signatures);
        Assert.Equal(acceptances, remote.Acceptances);
    }

    private static async Task<TemplateAssetRetirementDecision> SeedExecutionDecisionAsync()
    {
        await using var db = CreatePostgresDbContext(LaneConnectionString);
        var decision = TemplateAssetRetirementDecision.Create("org-retirement", "env-retirement",
            new LabelTemplateId(Guid.NewGuid()), "模板甲", "retirement-file", $"sha256:{new string('c', 64)}",
            "retirement-execution", "user-retirement", TemplateAssetRetirementDecision.RequiredPermission, "obsolete", "retirement-test");
        db.Add(decision);
        await db.SaveChangesAsync();
        return decision;
    }

    private static async Task<TemplateAssetRetirementDecision> ReadExecutionDecisionAsync()
    {
        await using var db = CreatePostgresDbContext(LaneConnectionString);
        return await db.TemplateAssetRetirementDecisions.AsNoTracking().SingleAsync();
    }

    private static async Task<bool> ExecuteRetirementAsync(FakeTimeProvider clock, RetirementTransport remote,
        TemplateAssetRetirementExecutorOptions? options = null, CancellationToken ct = default)
    {
        await using var db = CreatePostgresDbContext(LaneConnectionString);
        using var http = new HttpClient(remote, disposeHandler: false) { BaseAddress = new Uri("http://retirement.test") };
        var settings = options ?? ExecutionOptions;
        return await new TemplateAssetRetirementExecutor(new(db, clock), new CountingRetirementSigner(new(settings, clock), remote), new(http), settings, clock)
            .ExecuteNextAsync(ct);
    }

    private static async Task RunRetirementHostAsync(FakeTimeProvider clock, RetirementTransport remote,
        string expectedStatus, TemplateAssetRetirementExecutorOptions? options = null)
    {
        var settings = options ?? ExecutionOptions;
        using var http = new HttpClient(remote, disposeHandler: false) { BaseAddress = new Uri("http://retirement.test") };
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(settings);
            services.AddSingleton<ITemplateAssetRetirementSigner>(new CountingRetirementSigner(new(settings, clock), remote));
            services.AddSingleton(new TemplateAssetRetirementClient(http));
            services.AddScoped(_ => CreatePostgresDbContext(LaneConnectionString));
            services.AddScoped<TemplateAssetRetirementExecutionStore>();
            services.AddScoped<TemplateAssetRetirementExecutor>();
            services.AddHostedService<TemplateAssetRetirementWorker>();
        }).Build();
        await host.StartAsync();
        try
        {
            await Eventually.WaitAsync("retirement worker persists outcome", async _ =>
            {
                var decision = await ReadExecutionDecisionAsync();
                return (decision.Status, decision.ExecutionLeaseId, decision.FirstSentAtUtc);
            }, state => state.Status == expectedStatus && state.FirstSentAtUtc is not null && state.ExecutionLeaseId is null,
                state => $"status={state.Status}; leased={state.ExecutionLeaseId is not null}",
                new EventuallyOptions(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(20), [LaneConnectionString]));
        }
        finally { await host.StopAsync(); }
    }

    private sealed class CountingRetirementSigner(TemplateAssetRetirementSigner signer, RetirementTransport remote)
        : ITemplateAssetRetirementSigner
    {
        public RetireTemplateAssetRequest Sign(TemplateAssetRetirementDecision decision)
        {
            remote.Signatures++;
            return signer.Sign(decision);
        }
    }

    private sealed class RetirementTransport(TemplateAssetRetirementDecision decision, bool loseResponses = false) : HttpMessageHandler
    {
        public bool LoseResponses { get; set; } = loseResponses;
        public bool Accept { get; init; } = true;
        public bool PhysicallyCompleted { get; set; }
        public int Acceptances { get; private set; }
        public int Signatures { get; set; }
        public List<RetireTemplateAssetRequest> Requests { get; } = [];
        public Func<CancellationToken, Task>? BeforeResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((await request.Content!.ReadFromJsonAsync<RetireTemplateAssetRequest>(ct))!);
            if (Accept && Acceptances == 0) Acceptances++;
            if (BeforeResponse is not null) await BeforeResponse(ct);
            if (LoseResponses) throw new HttpRequestException("Synthetic lost response.");
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RetireTemplateAssetResponse(
                decision.Id.Id.ToString("D"), decision.TemplateFileId, "physical-hold", ExecutionEpoch, 2592000)) };
        }
    }
}
