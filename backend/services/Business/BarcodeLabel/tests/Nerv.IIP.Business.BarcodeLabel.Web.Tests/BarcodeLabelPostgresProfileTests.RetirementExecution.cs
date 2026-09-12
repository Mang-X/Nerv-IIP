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
