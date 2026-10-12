using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed partial class AppHubPostgresProfileTests
{
    [AppHubRealPostgresFact]
    public async Task Operation_result_dead_letters_recover_original_instance_and_inbox_through_http()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_apphub_replay");
        try
        {
            await using var factory = ReplayFactory(database.ConnectionString);
            using var client = ReplayClient(factory);
            var completed = Completed("completed", "target");
            var failed = Failed("failed", "target");
            IntegrationEventDeadLetterMessage completionLetter;
            IntegrationEventDeadLetterMessage failureLetter;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                database.AssertOwns(db.Database.GetConnectionString());
                await db.Database.MigrateAsync();
                // The original delivery failed because the instance had not yet been registered.
                await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() =>
                    scope.ServiceProvider.GetRequiredService<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>()
                        .HandleAsync(completed, CancellationToken.None));
            }
            using (var scope = factory.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                completionLetter = await Add(store, completed);
                failureLetter = await Add(store, failed);
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
                db.ApplicationInstances.AddRange(Instance("org-replay", "env-replay", "target"),
                    Instance("other-org", "env-replay", "target"), Instance("org-replay", "other-env", "target"));
                await db.SaveChangesAsync();
            }
            await Replay(client, completionLetter, "replayed");
            await AssertFacts(completedCount: 1, failedCount: 0);
            await Replay(client, failureLetter, "replayed");
            await Replay(client, completionLetter, "replayed");
            await Replay(client, failureLetter, "replayed");
            await AssertFacts(completedCount: 1, failedCount: 1);

            async Task AssertFacts(int completedCount, int failedCount)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var instances = await db.ApplicationInstances.AsNoTracking().Include(x => x.StateHistory).ToListAsync();
                var target = Assert.Single(instances, x => x.OrganizationId == "org-replay" && x.EnvironmentId == "env-replay");
                Assert.Equal(1 + completedCount + failedCount, target.StateHistory.Count);
                Assert.Equal("running", target.ReportedStatus);
                Assert.Equal("healthy", target.HealthStatus);
                Assert.Equal(completed.IdempotencyKey, target.Metadata["ops.lastCompletedOperationIdempotencyKey"]);
                Assert.Equal(completed.Payload.OperationTaskId, target.Metadata["ops.lastCompletedOperationTaskId"]);
                Assert.Equal(completed.CorrelationId, target.Metadata["ops.lastCompletedOperationCorrelationId"]);
                Assert.Contains(target.StateHistory, x => x.ObservedAtUtc == completed.Payload.FinishedAtUtc && x.Summary.Contains("completed"));
                if (failedCount > 0)
                {
                    Assert.Equal(failed.IdempotencyKey, target.Metadata["ops.lastFailedOperationIdempotencyKey"]);
                    Assert.Equal(failed.Payload.FailureCode, target.Metadata["ops.lastFailedOperationFailureCode"]);
                    Assert.Equal(failed.CorrelationId, target.Metadata["ops.lastFailedOperationCorrelationId"]);
                    Assert.Contains(target.StateHistory, x => x.ObservedAtUtc == failed.Payload.FinishedAtUtc && x.Summary.Contains("failed"));
                }
                foreach (var other in instances.Where(x => x != target))
                {
                    Assert.Single(other.StateHistory);
                    Assert.DoesNotContain(other.Metadata.Keys, x => x.StartsWith("ops.", StringComparison.Ordinal));
                }
                var inbox = await db.ProcessedIntegrationEvents.AsNoTracking().ToListAsync();
                Assert.Equal(completedCount + failedCount, inbox.Count);
                Assert.Contains(inbox, x => x.EventId == completed.EventId && x.IdempotencyKey == completed.IdempotencyKey
                    && x.EventType == completed.EventType && x.ConsumerName == completionLetter.ConsumerName);
                if (failedCount > 0) Assert.Contains(inbox, x => x.EventId == failed.EventId && x.IdempotencyKey == failed.IdempotencyKey && x.EventType == failed.EventType);
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                foreach (var original in failedCount > 0 ? new[] { completionLetter, failureLetter } : [completionLetter])
                {
                    var saved = await store.GetAsync(original.Id, CancellationToken.None);
                    Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, saved!.Status);
                    Assert.Equal(original.EventJson, saved.EventJson);
                }
            }
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [AppHubRealPostgresFact]
    public async Task Operation_result_batch_reports_recovery_rejection_exception_and_unsupported_without_false_success()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_apphub_replay");
        try
        {
            await using var factory = ReplayFactory(database.ConnectionString);
            using var client = ReplayClient(factory);
            IntegrationEventDeadLetterMessage[] letters;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                database.AssertOwns(db.Database.GetConnectionString());
                await db.Database.MigrateAsync();
                db.ApplicationInstances.Add(Instance("org-replay", "env-replay", "target"));
                await db.SaveChangesAsync();
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                letters = [await Add(store, Completed("batch-completed", "target")),
                    await Add(store, Failed("batch-failed", "target")),
                    await Add(store, Completed("missing", "missing")),
                    await Add(store, Completed("invalid", "target") with { EventVersion = 2 }),
                    await Add(store, Completed("unknown", "target"), "apphub.unknown")];
            }
            using var response = await client.PostAsJsonAsync("/internal/apphub/v1/dlq/replay-batch", new { status = "pending" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(5, items.Length);
            Assert.Equal(2, items.Count(x => x.GetProperty("status").GetString() == "replayed"));
            Assert.Equal(2, items.Count(x => x.GetProperty("status").GetString() == "failed"));
            Assert.Equal(1, items.Count(x => x.GetProperty("status").GetString() == "noHandler"));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var target = await db.ApplicationInstances.AsNoTracking().Include(x => x.StateHistory).SingleAsync();
                Assert.Equal(3, target.StateHistory.Count);
                Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
                Assert.False(await db.ProcessedIntegrationEvents.AnyAsync(x => x.IdempotencyKey == "missing" || x.IdempotencyKey == "invalid"));
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                foreach (var (letter, index) in letters.Select((letter, index) => (letter, index)))
                {
                    var saved = await store.GetAsync(letter.Id, CancellationToken.None);
                    Assert.Equal(index < 2 ? IntegrationEventDeadLetterStatus.Replayed : index < 4 ? IntegrationEventDeadLetterStatus.Failed : IntegrationEventDeadLetterStatus.Pending, saved!.Status);
                    Assert.Equal(letter.EventJson, saved.EventJson);
                    if (index == 4) Assert.Equal(letter, saved);
                }
            }
            // Repair only the missing source fact; the same original envelope now recovers.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.ApplicationInstances.Add(Instance("org-replay", "env-replay", "missing"));
                await db.SaveChangesAsync();
            }
            await Replay(client, letters[2], "replayed");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(3, await db.ProcessedIntegrationEvents.CountAsync());
                Assert.Equal(2, (await db.ApplicationInstances.AsNoTracking().Include(x => x.StateHistory).SingleAsync(x => x.InstanceKey == "missing")).StateHistory.Count);
            }
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static WebApplicationFactory<Program> ReplayFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Persistence:Provider", "PostgreSQL");
            builder.UseSetting("ConnectionStrings:AppHubDb", connectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL", ["Persistence:AutoMigrate"] = "false",
                ["ConnectionStrings:AppHubDb"] = connectionString,
                ["InternalService:BearerToken"] = "apphub-replay-test-token",
                ["Messaging:Provider"] = "InMemory", ["Caching:Provider"] = "Memory",
                ["Logging:LogLevel:Default"] = "None",
            }));
        });

    private static HttpClient ReplayClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "apphub-replay-test-token");
        return client;
    }

    private static async Task Replay(HttpClient client, IntegrationEventDeadLetterMessage letter, string expected)
    {
        using var response = await client.PostAsync($"/internal/apphub/v1/dlq/{letter.Id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private static async Task<IntegrationEventDeadLetterMessage> Add(IIntegrationEventDeadLetterStore store,
        IIntegrationEventEnvelope integrationEvent, string consumer = "apphub.refresh-instance-state")
    {
        var letter = await store.AddAsync(IntegrationEventDeadLetterMessage.Create(consumer, integrationEvent, "original-failure", "Original delivery failed."), CancellationToken.None);
        // Compare against the original durable row: PostgreSQL jsonb canonicalizes JSON on insert.
        return (await store.GetAsync(letter.Id, CancellationToken.None))!;
    }

    private static ApplicationInstance Instance(string organization, string environment, string key)
    {
        var instance = new ApplicationInstance(organization, environment, "host", "app", "1", "node", key, "App", new Dictionary<string, string>(), []);
        instance.RecordStateSnapshot(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), "running", "healthy", "initial", new Dictionary<string, string>());
        return instance;
    }

    private static OperationTaskCompletedIntegrationEvent Completed(string key, string instance) => new(
        $"evt-{key}", "ops.OperationTaskCompleted", 1, DateTimeOffset.Parse("2026-10-01T01:00:00Z"), "ops", $"corr-{key}", "original-cause",
        "org-replay", "env-replay", "original-actor", key,
        new OperationTaskCompletedPayload($"op-{key}", "attempt", instance, "lifecycle.restart", DateTimeOffset.Parse("2026-10-01T00:30:00Z")));

    private static OperationTaskFailedIntegrationEvent Failed(string key, string instance) => new(
        $"evt-{key}", "ops.OperationTaskFailed", 1, DateTimeOffset.Parse("2026-10-01T02:00:00Z"), "ops", $"corr-{key}", "original-cause",
        "org-replay", "env-replay", "original-actor", key,
        new OperationTaskFailedPayload($"op-{key}", "attempt", instance, "lifecycle.restart", DateTimeOffset.Parse("2026-10-01T01:30:00Z"), "restart-failed"));
}
