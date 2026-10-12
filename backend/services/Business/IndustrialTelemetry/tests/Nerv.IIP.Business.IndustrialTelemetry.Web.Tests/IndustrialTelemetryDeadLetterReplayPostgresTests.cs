using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryDeadLetterReplayPostgresTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-01T08:00:00Z");
    private const string Route = "/api/business/v1/iiot/dlq";

    // DomainInvariant + Regression: #4353 / parent #3743 r1. Real PostgreSQL ledger/DLQ; Ops is a fixture.
    [RealPostgresFact]
    public async Task Original_attempt_outcomes_recover_without_false_success_or_duplicate_effects_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        var ops = new OpsReceiptFixture();
        await using var factory = CreateFactory(ops);
        using var client = Client(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var id in new[] { "op-completed", "op-failed", "op-rejected", "op-isolated" })
                db.DeviceControlCommands.Add(DeviceControlCommand.Record(id, "org-replay", "env-replay", "host", "instance", "DEV-1",
                    "write-tag", "speed", "80", null, "operator", "adjust speed", "idem-" + id, "correlation", "approval-pending", "pending", At));
            await db.SaveChangesAsync();
        }
        var completed = Completed("op-completed");
        var letter = await Add(factory, DeviceControlCommandCompletedHandlerName, completed);
        await Replay(client, letter, "failed"); // absent external result
        ops.AttemptId = "another-attempt";
        await Replay(client, letter, "failed"); // do not trust a different attempt
        using (var scope = factory.Services.CreateScope())
            Assert.All(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DeviceControlCommands.ToListAsync(), x => Assert.Equal("approval-pending", x.Status));
        ops.AttemptId = "attempt-original";
        await Replay(client, letter, "replayed");
        await Replay(client, letter, "replayed");
        var failed = new OperationTaskFailedIntegrationEvent("evt-failed", "ops.OperationTaskFailed", 1, At, "ops", "corr", "cause",
            "org-replay", "env-replay", "host", "idem-failed",
            new OperationTaskFailedPayload("op-failed", "attempt-original", "instance", "device.control.command", At, "device-timeout"));
        var rejected = new OperationApprovalRejectedIntegrationEvent("evt-rejected", "ops.OperationApprovalRejected", 1, At, "ops", "corr", "cause",
            "org-replay", "env-replay", "supervisor", "idem-rejected",
            new OperationApprovalDecidedPayload("op-rejected", "instance", "device.control.command", "supervisor", "unsafe", At));
        var failedLetter = await Add(factory, "business-industrial-telemetry.device-control-failed", failed);
        var rejectedLetter = await Add(factory, "business-industrial-telemetry.device-control-rejected", rejected);
        var isolated = await Add(factory, DeviceControlCommandCompletedHandlerName, Completed("op-isolated") with { OrganizationId = "other-org" });
        var invalid = await Add(factory, DeviceControlCommandCompletedHandlerName, Completed("op-isolated") with { EventId = "invalid", EventVersion = 99 });
        using var batch = await client.PostAsJsonAsync(Route + "/replay-batch", new { status = "pending" });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        using var json = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(2, items.Count(x => x.GetProperty("succeeded").GetBoolean()));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.DeviceControlCommands.AsNoTracking().ToDictionaryAsync(x => x.OperationTaskId);
            Assert.Equal(4, rows.Count);
            Assert.Equal("completed", rows["op-completed"].Status);
            Assert.Equal("Good", rows["op-completed"].DeviceReceiptCode);
            Assert.Equal("original receipt", rows["op-completed"].DeviceReceiptMessage);
            Assert.Equal(At, rows["op-completed"].FinishedAtUtc);
            Assert.Equal("failed", rows["op-failed"].Status);
            Assert.Equal("device-timeout", rows["op-failed"].FailureCode);
            Assert.Equal("Good", rows["op-failed"].DeviceReceiptCode);
            Assert.Equal("rejected", rows["op-rejected"].Status);
            Assert.Equal("rejected", rows["op-rejected"].ApprovalStatus);
            Assert.Equal("approval-pending", rows["op-isolated"].Status);
            var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            foreach (var success in new[] { letter, failedLetter, rejectedLetter })
            {
                var stored = await store.GetAsync(success.Id, CancellationToken.None);
                Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, stored!.Status);
                Assert.Equal(success.EventJson, stored.EventJson);
            }
            Assert.Equal(IntegrationEventDeadLetterStatus.Failed, (await store.GetAsync(isolated.Id, CancellationToken.None))!.Status);
            Assert.Equal(IntegrationEventDeadLetterStatus.Failed, (await store.GetAsync(invalid.Id, CancellationToken.None))!.Status);
        }
    }

    [RealPostgresFact]
    public async Task Oee_replays_original_report_and_reversal_only_when_source_facts_exist_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory(new OpsReceiptFixture());
        using var client = Client(factory);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        var report = new ProductionReportRecordedIntegrationEvent("evt-report", MesIntegrationEventTypes.ProductionReportRecorded, 1,
            At, MesIntegrationEventSources.BusinessMes, "corr", "cause", "org-replay", "env-replay", "actor", "idem-report",
            new ProductionReportRecordedPayload("RPT-ORIGINAL", "WO", "OP", "WC", "DEV-1", 10m, 2m, 1m, "ea", 5m, At, false, MaterialMovementCount: 0));
        var reversal = report with { EventId = "evt-reversal", IdempotencyKey = "idem-reversal", Payload = report.Payload with
            { ReportNo = "RPT-REVERSAL", IsReversal = true, ReversedReportNo = "RPT-ORIGINAL", GoodQuantity = -10m, ScrapQuantity = -2m, ReworkQuantity = -1m } };
        const string consumer = "business-industrial-telemetry.production-report-oee-projection";
        var reversalLetter = await Add(factory, consumer, reversal);
        await Replay(client, reversalLetter, "failed");
        var reportLetter = await Add(factory, consumer, report);
        await Replay(client, reportLetter, "replayed");
        await Replay(client, reversalLetter, "replayed");
        await Replay(client, reportLetter, "replayed");
        var incomplete = await Add(factory, consumer, report with { EventId = "evt-incomplete", IdempotencyKey = "incomplete",
            Payload = report.Payload with { ReportNo = "RPT-INCOMPLETE", DeviceAssetId = null } });
        await Replay(client, incomplete, "failed"); // consumer returns without creating a projection
        var otherScope = await Add(factory, consumer, reversal with { EventId = "evt-other", EnvironmentId = "other-env" });
        await Replay(client, otherScope, "failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facts = await db.OeeProductionFacts.AsNoTracking().OrderBy(x => x.SourceReportNo).ToListAsync();
            Assert.Equal(2, facts.Count);
            var original = Assert.Single(facts, x => x.SourceReportNo == "RPT-ORIGINAL");
            Assert.Equal(("org-replay", "env-replay", "WC", "DEV-1", 10m, 2m, 1m, At),
                (original.OrganizationId, original.EnvironmentId, original.WorkCenterId, original.DeviceAssetId,
                    original.GoodQuantity, original.ScrapQuantity, original.ReworkQuantity, original.ReportedAtUtc));
            Assert.Equal(-10m, Assert.Single(facts, x => x.SourceReportNo == "RPT-REVERSAL").GoodQuantity);
            var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, (await store.GetAsync(reportLetter.Id, CancellationToken.None))!.Status);
            Assert.Equal(reportLetter.EventJson, (await store.GetAsync(reportLetter.Id, CancellationToken.None))!.EventJson);
        }
    }

    private const string DeviceControlCommandCompletedHandlerName = "business-industrial-telemetry.device-control-completed";
    private static OperationTaskCompletedIntegrationEvent Completed(string id) => new("evt-" + id, "ops.OperationTaskCompleted", 1,
        At, "ops", "corr", "cause", "org-replay", "env-replay", "host", "idem-" + id,
        new Nerv.IIP.Contracts.Ops.OperationTaskCompletedPayload(id, "attempt-original", "instance", "device.control.command", At));

    private static WebApplicationFactory<Program> CreateFactory(OpsReceiptFixture ops) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:PostgreSQL", IndustrialTelemetryPostgresLaneDatabase.ConnectionString);
        builder.UseSetting("InternalService:BearerToken", "test-replay-token");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
            ["Persistence:AutoMigrate"] = "false",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDeviceControlOpsClient>(ops);
            // The production branch uses this same persistent store; Testing otherwise uses the memory store.
            var original = services.Single(x => x.IsKeyedService && x.ServiceType == typeof(IIntegrationEventDeadLetterStore));
            services.Remove(original);
            services.AddKeyedScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>(original.ServiceKey);
        });
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
        return client;
    }

    private static async Task<IntegrationEventDeadLetterMessage> Add<T>(WebApplicationFactory<Program> factory, string consumer, T e)
        where T : IIntegrationEventEnvelope
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var added = await store.AddAsync(IntegrationEventDeadLetterMessage.Create(consumer, e, "original-failure", "original failure"), CancellationToken.None);
        // jsonb canonicalizes JSON on persistence; compare against the stored original envelope.
        return (await store.GetAsync(added.Id, CancellationToken.None))!;
    }

    private static async Task Replay(HttpClient client, IntegrationEventDeadLetterMessage letter, string expected)
    {
        using var response = await client.PostAsync($"{Route}/{letter.Id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private sealed class OpsReceiptFixture : IDeviceControlOpsClient
    {
        public string? AttemptId { get; set; }
        public Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OperationTaskResponse> GetDeviceControlTaskAsync(string operationTaskId, CancellationToken token)
        {
            if (AttemptId is null) throw new HttpRequestException("Original external result unavailable.");
            return Task.FromResult(new OperationTaskResponse(operationTaskId, "org-replay", "env-replay", "instance", "device.control.command",
                "completed", "operator", At, null, AttemptId,
                [new OperationAttemptSummary(AttemptId, "succeeded", At, At, null, "lease", At, At.AddMinutes(5), 1, 300, 3, null,
                    new Dictionary<string, string> { ["deviceReceiptCode"] = "Good", ["deviceReceiptMessage"] = "original receipt" })], []));
        }
    }
}
