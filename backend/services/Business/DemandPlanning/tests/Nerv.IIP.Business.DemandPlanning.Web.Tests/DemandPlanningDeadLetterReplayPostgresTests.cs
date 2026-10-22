using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class DemandPlanningDeadLetterReplayPostgresTests
{
    // DomainInvariant / Regression: #4352 and approved #3743 r1, original lifecycle projection and inbox.
    [DemandPlanningRealPostgresFact]
    public async Task Sales_lifecycle_replays_restore_persisted_demands_and_reject_invalid_sales_facts_on_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_dp_replay");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:PostgreSQL", database.ConnectionString);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = database.ConnectionString,
                    ["InternalService:BearerToken"] = "test-replay-token",
                    ["Persistence:AutoMigrate"] = "false",
                }));
                // This proof owns migrations; daily MRP is unrelated and must not race an empty schema.
                builder.ConfigureTestServices(services => services.Remove(services.Single(x =>
                    x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(DailyMrpScheduler))));
            });
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

            var released = Event("released", 1, 8m);
            var changed = Event("changed", 2, 10m);
            var delivered = Event("delivered", 3, 10m, 4m);
            var fulfilled = Event("delivered", 4, 10m, 10m);
            var cancelled = Event("cancelled", 5, 10m);
            var releaseRow = await Add(released);
            await Replay(releaseRow, "replayed");
            await ReadDemand(8m, "active", 1, 1);
            foreach (var (fact, quantity, status, version) in new[]
            {
                (changed, 10m, "active", 2), (delivered, 6m, "active", 3),
                (fulfilled, 0m, "fulfilled", 4), (cancelled, 0m, "cancelled", 5),
            })
            {
                var row = await Add(fact);
                await Replay(row, "replayed");
                await Replay(row, "replayed");
                await ReadDemand(quantity, status, version, version);
            }
            await Replay(releaseRow, "replayed");
            await ReadDemand(0m, "cancelled", 5, 5);

            // Original consumer rejects this fact by recording a dead letter and returning normally.
            var rejected = (SalesOrderChangedIntegrationEvent)Event("changed", 6, 99m);
            rejected = rejected with { Payload = rejected.Payload with { SiteCode = "UNSPECIFIED" } };
            var rejectedRow = await Add(rejected);
            await Replay(rejectedRow, "failed");
            await ReadDemand(0m, "cancelled", 5, 5);
            // Rejection must still win even when a durable inbox already exists for that identity.
            var alreadyProcessed = ((SalesOrderReleasedIntegrationEvent)released) with
            { Payload = ((SalesOrderReleasedIntegrationEvent)released).Payload with { SiteCode = "UNSPECIFIED" } };
            var rejectedDuplicate = await Add(alreadyProcessed);
            await Replay(rejectedDuplicate, "failed");
            await ReadDemand(0m, "cancelled", 5, 5);

            var otherScope = (SalesOrderReleasedIntegrationEvent)Event("released", 1, 3m);
            otherScope = otherScope with { EventId = "other-scope", IdempotencyKey = "other-scope", OrganizationId = "other-org", EnvironmentId = "other-env" };
            var otherRow = await Add(otherScope);
            var unknownRow = await Add(Event("released", 7, 20m), "undeclared-demand-consumer");
            using var batch = await client.PostAsJsonAsync($"{Route}/replay-batch", new { status = "pending" });
            Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
            using var batchJson = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
            var items = batchJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(items, x => x.GetProperty("id").GetGuid() == otherRow.Id && x.GetProperty("status").GetString() == "replayed");
            Assert.Contains(items, x => x.GetProperty("id").GetGuid() == unknownRow.Id && x.GetProperty("status").GetString() == "noHandler");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(2, await db.DemandSources.CountAsync());
                var other = await db.DemandSources.AsNoTracking().SingleAsync(x => x.OrganizationId == "other-org");
                Assert.Equal("other-env", other.EnvironmentId);
                Assert.Equal(3m, other.Quantity);
                Assert.Equal(unknownRow, await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(unknownRow.Id, default));
            }
            await ReadDemand(0m, "cancelled", 5, 5);

            async Task<IntegrationEventDeadLetterMessage> Add(IIntegrationEventEnvelope fact, string? consumer = null)
            {
                using var scope = factory.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                var row = await store.AddAsync(
                    IntegrationEventDeadLetterMessage.Create(consumer ?? SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource.ConsumerName,
                        fact, "original-consumption-failed", "Original delivery failed before projection completed."), default);
                // PostgreSQL jsonb normalizes whitespace/order on initial persistence. Compare durable bytes.
                return (await store.GetAsync(row.Id, default))!;
            }

            async Task Replay(IntegrationEventDeadLetterMessage row, string expected)
            {
                using var response = await client.PostAsync($"{Route}/{row.Id}/replay", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
                using var scope = factory.Services.CreateScope();
                var saved = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(row.Id, default);
                Assert.Equal(expected == "replayed" ? IntegrationEventDeadLetterStatus.Replayed : IntegrationEventDeadLetterStatus.Failed, saved!.Status);
                Assert.Equal(row.EventJson, saved.EventJson);
                if (expected == "failed") Assert.Contains("invalid-sales-order-demand-payload", saved.FailureMessage, StringComparison.Ordinal);
            }

            async Task ReadDemand(decimal quantity, string status, int version, int inboxCount)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var demand = Assert.Single(await db.DemandSources.AsNoTracking().Where(x => x.OrganizationId == "org-replay" && x.EnvironmentId == "env-replay").ToListAsync());
                Assert.Equal(quantity, demand.Quantity);
                Assert.Equal(status, demand.SourceStatus);
                Assert.Equal(version, demand.SourceVersion);
                Assert.Equal("order-replay", demand.SourceDocumentId);
                Assert.Equal("10", demand.SourceLineReference);
                var projection = await db.SalesOrderDemandProjections.AsNoTracking().SingleAsync(x => x.OrganizationId == "org-replay");
                Assert.Equal(version, projection.OrderVersion);
                Assert.Equal(inboxCount, await db.ProcessedIntegrationEvents.CountAsync(x => x.IdempotencyKey.StartsWith("replay-")));
            }
        }
        finally { await database.DropAsync(); }
    }

    private static string Route => IntegrationEventDeadLetterServices.DemandPlanning.RoutePrefix + IntegrationEventDeadLetterRoutes.Collection;

    private static IIntegrationEventEnvelope Event(string kind, int version, decimal quantity, decimal delivered = 0m)
    {
        var payload = new SalesOrderLifecyclePayload("order-replay", "SO-REPLAY", "CUST-REPLAY", "SITE-REPLAY", version,
            kind == "cancelled" ? "cancelled" : "released",
            [new SalesOrderLineSnapshot("10", "SKU-REPLAY", quantity, "EA", new DateOnly(2026, 11, 1), kind == "cancelled", delivered)]);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        return kind switch
        {
            "released" => new SalesOrderReleasedIntegrationEvent($"replay-{version}", ErpIntegrationEventTypes.SalesOrderReleased, 1, at,
                ErpIntegrationEventSources.BusinessErp, "original-correlation", "original-causation", "org-replay", "env-replay", "original-actor", $"replay-{version}", payload),
            "changed" => new SalesOrderChangedIntegrationEvent($"replay-{version}", ErpIntegrationEventTypes.SalesOrderChanged, 1, at,
                ErpIntegrationEventSources.BusinessErp, "original-correlation", "original-causation", "org-replay", "env-replay", "original-actor", $"replay-{version}", payload),
            "cancelled" => new SalesOrderCancelledIntegrationEvent($"replay-{version}", ErpIntegrationEventTypes.SalesOrderCancelled, 1, at,
                ErpIntegrationEventSources.BusinessErp, "original-correlation", "original-causation", "org-replay", "env-replay", "original-actor", $"replay-{version}", payload),
            "delivered" => new SalesOrderDeliveryRegisteredIntegrationEvent($"replay-{version}", ErpIntegrationEventTypes.SalesOrderDeliveryRegistered, 1, at,
                ErpIntegrationEventSources.BusinessErp, "original-correlation", "original-causation", "org-replay", "env-replay", "original-actor", $"replay-{version}", payload),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
