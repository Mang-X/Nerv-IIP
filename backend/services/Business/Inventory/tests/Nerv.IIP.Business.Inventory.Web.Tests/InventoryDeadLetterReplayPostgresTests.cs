using NetCorePal.Extensions.DistributedTransactions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

[Collection(InventoryPostgresLaneCollection.Name)]
public sealed class InventoryDeadLetterReplayPostgresTests
{
    private static readonly DateTimeOffset OriginalTime = DateTimeOffset.Parse("2026-10-01T00:00:00Z");

    [InventoryPostgresFact]
    public async Task Movement_http_replay_reads_persisted_stock_and_rejection_without_duplicate_posting()
    {
        await ResetSchemaAsync();
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory);
        var original = Movement() with { Payload = Movement().Payload with { SourceDocumentId = " IN-REPLAY " } };
        var letter = await AddAsync(factory, InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.ConsumerName, original);
        // Location is absent: the consumer publishes a rejection and returns normally.
        await ReplayAsync(client, letter, "failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.StockMovements.ToListAsync());
            db.StockLocations.Add(StockLocation.CreateOrUpdate(null, "org-replay", "env-replay", "LOC-01", "storage", "SITE-01", null, "active"));
            await db.SaveChangesAsync();
        }
        await ReplayAsync(client, letter, "replayed");
        await ReplayAsync(client, letter, "replayed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var movement = Assert.Single(await db.StockMovements.AsNoTracking().ToListAsync());
            Assert.Equal("IN-REPLAY", movement.SourceDocumentId);
            Assert.Equal(original.Payload.IdempotencyKey, movement.IdempotencyKey);
            Assert.Equal(5m, movement.Quantity);
            Assert.Equal(10m, Assert.Single(await db.StockLedgers.AsNoTracking().ToListAsync()).InventoryValue);
            var stored = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(letter.Id, default);
            Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, stored!.Status);
            Assert.Equal(letter.EventJson, stored.EventJson);
        }
        // A conflicting replay must fail even though the original movement already exists.
        var conflict = original with { EventId = "evt-conflict", Payload = original.Payload with { Quantity = 6m } };
        var rejected = await AddAsync(factory, letter.ConsumerName, conflict);
        await ReplayAsync(client, rejected, "failed");
        await AddAsync(factory, letter.ConsumerName, conflict with { EventId = "evt-batch-conflict" });
        await AddAsync(factory, letter.ConsumerName, original with { EventId = "evt-batch-success" });
        var unsupported = await AddAsync(factory, "business-inventory.unregistered", original with { EventId = "evt-unsupported" });
        using var batch = await client.PostAsJsonAsync("/api/inventory/v1/dlq/replay-batch", new { status = "pending" });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        using var json = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "replayed");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "failed");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "noHandler");
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            var unchanged = await store.GetAsync(unsupported.Id, default);
            Assert.Equal(unsupported.Status, unchanged!.Status);
            Assert.Equal(unsupported.EventJson, unchanged.EventJson);
            Assert.Equal(unsupported.FailureCode, unchanged.FailureCode);
            Assert.Equal(unsupported.FailureMessage, unchanged.FailureMessage);
            Assert.Equal(unsupported.ReplayedAtUtc, unchanged.ReplayedAtUtc);
            Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().StockMovements.ToListAsync());
        }
    }

    [InventoryPostgresFact]
    public async Task Reservation_replay_releases_original_scope_and_does_not_hide_missing_ledger()
    {
        await ResetSchemaAsync();
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var organization in new[] { "org-replay", "other-org" })
            {
                var ledger = Ledger(organization, "unrestricted");
                var reservation = StockReservation.Reserve(ledger, "mes", "WO-REPLAY", "10", "reserve-key", 3m, OriginalTime.UtcDateTime.AddYears(1));
                ledger.Reserve(reservation);
                db.StockLedgers.Add(ledger);
                db.StockReservations.Add(reservation);
            }
            await db.SaveChangesAsync();
            // Simulates the original failure condition: reservation exists, its ledger is unavailable.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM inventory.stock_ledgers WHERE organization_id = 'org-replay'");
        }
        var original = new InventoryReservationReleaseRequestedIntegrationEvent("evt-release", InventoryIntegrationEventTypes.InventoryReservationReleaseRequested, 1,
            OriginalTime, InventoryIntegrationEventSources.BusinessWms, "corr", "cause", "org-replay", "env-replay", "system:wms", "release-key",
            new InventoryReservationReleaseRequestedPayload(InventoryIntegrationEventSources.BusinessMes, "WO-REPLAY", ["10"], "cancelled", OriginalTime));
        var letter = await AddAsync(factory, InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations.ConsumerName, original);
        await ReplayAsync(client, letter, "failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.StockReservations.SingleAsync(x => x.OrganizationId == original.OrganizationId);
            var ledger = Ledger("org-replay", "unrestricted");
            ledger.Reserve(reservation);
            db.StockLedgers.Add(ledger);
            await db.SaveChangesAsync();
        }
        await ReplayAsync(client, letter, "replayed");
        await ReplayAsync(client, letter, "replayed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.StockReservations.SingleAsync(x => x.OrganizationId == original.OrganizationId);
            Assert.Equal(0m, reservation.OpenQuantity);
            Assert.Equal(3m, reservation.ReleasedQuantity);
            Assert.Equal(0m, (await db.StockLedgers.SingleAsync(x => x.OrganizationId == original.OrganizationId)).ReservedQuantity);
            Assert.Equal(3m, (await db.StockReservations.SingleAsync(x => x.OrganizationId == "other-org")).OpenQuantity);
        }
    }

    [InventoryPostgresFact]
    public async Task Quality_replay_recovers_two_status_legs_and_preserves_failure_and_scope()
    {
        await ResetSchemaAsync();
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory);
        var original = new InspectionResultIntegrationEvent("evt-quality", QualityIntegrationEventTypes.InspectionPassed, 1,
            OriginalTime, QualityIntegrationEventSources.BusinessQuality, "corr", "cause", "org-replay", "env-replay", "system:quality", "quality-key",
            new InspectionResultPayload("QI-REPLAY", "PLAN", "receiving", "quality", "IN-REPLAY", "SKU-01", 3m, "passed", null, [], OriginalTime,
                new StockReleaseDimensionPayload("kg", "SITE-01", "LOC-01", null, null, "quality", "company", null, "unrestricted")));
        var letter = await AddAsync(factory, QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.ConsumerName, original);
        await ReplayAsync(client, letter, "failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.StockLedgers.Add(Ledger("org-replay", "quality"));
            db.StockLedgers.Add(Ledger("other-org", "quality"));
            await db.SaveChangesAsync();
        }
        await ReplayAsync(client, letter, "replayed");
        await ReplayAsync(client, letter, "replayed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.StockMovements.CountAsync());
            Assert.Equal(7m, (await db.StockLedgers.SingleAsync(x => x.OrganizationId == "org-replay" && x.QualityStatus == "quality")).OnHandQuantity);
            Assert.Equal(3m, (await db.StockLedgers.SingleAsync(x => x.OrganizationId == "org-replay" && x.QualityStatus == "unrestricted")).OnHandQuantity);
            Assert.Equal(10m, (await db.StockLedgers.SingleAsync(x => x.OrganizationId == "other-org")).OnHandQuantity);
        }
        // The original posting trims document identity while retaining the incoming envelope.
        var padded = original with
        {
            EventId = "evt-quality-padded", IdempotencyKey = "quality-padded-key",
            Payload = original.Payload with { SourceDocumentId = " IN-PADDED " },
        };
        var paddedLetter = await AddAsync(factory, letter.ConsumerName, padded);
        await ReplayAsync(client, paddedLetter, "replayed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var legs = await db.StockMovements.Where(x => x.SourceDocumentId == "IN-PADDED").ToListAsync();
            Assert.Equal(2, legs.Count);
            Assert.Equal(0m, legs.Sum(x => x.Quantity));
            Assert.Equal(4, await db.StockMovements.CountAsync());
        }
        // Wrong original consumer must not consume a Quality event merely because one exists in the service.
        var wrong = await AddAsync(factory, InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.ConsumerName, original with { EventId = "evt-wrong-consumer" });
        await ReplayAsync(client, wrong, "noHandler");
    }

    [InventoryPostgresFact]
    public async Task Approval_replay_requires_original_adjustment_and_posts_once()
    {
        await ResetSchemaAsync();
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory);
        var original = new ApprovalCompletedIntegrationEvent("evt-approval", ApprovalIntegrationEventTypes.ApprovalApproved, 1,
            OriginalTime, ApprovalIntegrationEventSources.BusinessApproval, "corr", "cause", "org-replay", "env-replay", "system:approval", "approval-key",
            new ApprovalCompletedPayload("CHAIN", ApprovalResults.Approved, "user", "finance", null, null,
                new ApprovalDocumentReferencePayload(ApprovalSourceServices.Inventory, ApprovalDocumentTypes.StockCountVariance, "COUNT", null)));
        var letter = await AddAsync(factory, ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.ConsumerName, original);
        await ReplayAsync(client, letter, "failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ledger = Ledger("org-replay", "unrestricted");
            var task = StockCountTask.Create("org-replay", "env-replay", "COUNT", "COUNT", ledger.OrganizationId, ledger.EnvironmentId,
                ledger.SkuCode, ledger.UomCode, ledger.SiteCode, ledger.LocationCode, ledger.LotNo, ledger.SerialNo, ledger.QualityStatus, ledger.OwnerType, ledger.OwnerId, ledger.LedgerVersion);
            ledger.FreezeForCount(task.CountTaskCode);
            task.SubmitForApproval(ledger, 7m);
            db.StockLedgers.Add(ledger);
            db.StockCountTasks.Add(task);
            db.StockCountAdjustments.Add(StockCountAdjustment.RecordPendingApproval(task, "count-key", "CHAIN", 6m));
            await db.SaveChangesAsync();
        }
        await ReplayAsync(client, letter, "replayed");
        await ReplayAsync(client, letter, "replayed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("posted", (await db.StockCountAdjustments.SingleAsync()).Status);
            Assert.Equal(7m, (await db.StockLedgers.SingleAsync()).OnHandQuantity);
            Assert.False((await db.StockLedgers.SingleAsync()).IsFrozenForCount);
            Assert.Single(await db.StockMovements.ToListAsync());
        }
    }

    private static StockLedger Ledger(string organization, string qualityStatus)
    {
        var ledger = StockLedger.Create(organization, "env-replay", "SKU-01", "kg", "SITE-01", "LOC-01", null, null, qualityStatus, "company", null);
        ledger.ApplyMovement(StockMovement.Post(organization, "env-replay", "inbound", "fixture", "SEED", null, "seed-key", "SKU-01", "kg", "SITE-01", "LOC-01", null, null, qualityStatus, "company", null, 10m, 2m));
        return ledger;
    }

    private static InventoryMovementRequestedIntegrationEvent Movement() => new("evt-replay", InventoryIntegrationEventTypes.InventoryMovementRequested, 1,
        OriginalTime, InventoryIntegrationEventSources.BusinessWms, "original-correlation", "original-causation", "org-replay", "env-replay", "system:wms", "envelope-key",
        new InventoryMovementRequestedPayload("inbound", "wms", "IN-REPLAY", "10", "movement-key", "SKU-01", "kg", "SITE-01", "LOC-01", null, null, "unrestricted", "company", null, 5m, OriginalTime, UnitCost: 2m));

    private static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(Nerv.IIP.Business.Inventory.Domain.InventoryFacts.Schema);
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<WebApplicationFactory<Program>> CreateFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // This lane proves PostgreSQL business facts; outgoing transport is verified separately.
            builder.ConfigureServices(services => services.AddSingleton<IIntegrationEventPublisher, FixturePublisher>());
            builder.UseSetting("ConnectionStrings:PostgreSQL", connectionString);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = connectionString,
                ["InternalService:BearerToken"] = "inventory-replay-test-token",
                ["Persistence:AutoMigrate"] = "false",
                ["Inventory:Seed:Enabled"] = "false",
                ["Inventory:ReservationExpiration:Enabled"] = "false",
            }));
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var expected = new NpgsqlConnectionStringBuilder(connectionString);
        var actual = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal((expected.Host, expected.Port, expected.Database), (actual.Host, actual.Port, actual.Database));
        await db.Database.MigrateAsync();
        return factory;
    }

    private sealed class FixturePublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "inventory-replay-test-token");
        return client;
    }

    private static async Task<IntegrationEventDeadLetterMessage> AddAsync<T>(WebApplicationFactory<Program> factory, string consumer, T integrationEvent)
        where T : Nerv.IIP.Contracts.IntegrationEvents.IIntegrationEventEnvelope
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var added = await store.AddAsync(IntegrationEventDeadLetterMessage.Create(consumer, integrationEvent, "original-failure", "original failure"), default);
        // PostgreSQL jsonb canonicalizes the initial write; compare the persisted original thereafter.
        return (await store.GetAsync(added.Id, default))!;
    }

    private static async Task ReplayAsync(HttpClient client, IntegrationEventDeadLetterMessage letter, string expected)
    {
        using var response = await client.PostAsync($"/api/inventory/v1/dlq/{letter.Id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("data");
        Assert.True(expected == result.GetProperty("status").GetString(), $"Expected {expected}; actual result: {result}");
        using var detail = await client.GetAsync($"/api/inventory/v1/dlq/{letter.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var stored = detailJson.RootElement.GetProperty("data");
        Assert.Equal(expected == "noHandler" ? "pending" : expected, stored.GetProperty("status").GetString());
        Assert.Equal(letter.EventJson, stored.GetProperty("eventJson").GetString());
    }
}
