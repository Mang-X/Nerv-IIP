using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class ErpDeadLetterReplayPostgresTests
{
    [ErpCostPostgresFact]
    public async Task Original_rate_time_recovers_cost_through_single_and_batch_http_without_duplicate_cost()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:PostgreSQL", ErpPostgresLaneDatabase.ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = ErpPostgresLaneDatabase.ConnectionString,
                ["InternalService:BearerToken"] = "test-replay-token",
                ["Persistence:AutoMigrate"] = "false",
            }));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
        var original = new ProductionReportRecordedIntegrationEvent("evt-replay-original", MesIntegrationEventTypes.ProductionReportRecorded, 1,
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), MesIntegrationEventSources.BusinessMes, "original-correlation", "original-causation",
            "org-replay", "env-replay", "original-actor", "original-idempotency",
            new ProductionReportRecordedPayload("RPT-REPLAY", "WO-REPLAY", "OP-REPLAY", "WC-REPLAY", null,
                10m, 0m, 0m, "ea", 5m, DateTimeOffset.Parse("2026-09-01T00:00:00Z"), false, MaterialMovementCount: 0));
        IntegrationEventDeadLetterMessage deadLetter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            await ActivatorUtilities.GetServiceOrCreateInstance<ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost>(scope.ServiceProvider)
                .HandleAsync(original, CancellationToken.None);
            deadLetter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                .ListAsync(ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost.ConsumerName, null, CancellationToken.None));
            Assert.Equal("missing-work-center-cost-rate", deadLetter.FailureCode);
            Assert.Equal(deadLetter.Id, (await db.Set<IntegrationEventDeadLetter>().SingleAsync()).Id);
            Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
            Assert.Empty(await db.WorkOrderCosts.ToListAsync());
        }
        await ReplaySingle("failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
            Assert.Empty(await db.WorkOrderCosts.ToListAsync());
            var failed = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(deadLetter.Id, CancellationToken.None);
            Assert.Equal(IntegrationEventDeadLetterStatus.Failed, failed!.Status);
            Assert.Equal(deadLetter.EventJson, failed.EventJson);
            // A present-day rate is insufficient: pricing belongs to the original report time.
            db.WorkCenterCostRates.Add(Rate("org-replay", "env-replay", DateTimeOffset.Parse("2026-10-01T00:00:00Z"), 999m, 1));
            db.WorkCenterCostRates.Add(Rate("other-org", "env-replay", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 999m, 1));
            db.WorkCenterCostRates.Add(Rate("org-replay", "other-env", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 999m, 1));
            await db.SaveChangesAsync();
        }
        await ReplaySingle("failed");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WorkCenterCostRates.Add(Rate("org-replay", "env-replay", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 50m, 2));
            await db.SaveChangesAsync();
        }
        await ReplaySingle("replayed");
        await ReplaySingle("replayed");
        using var batch = await client.PostAsJsonAsync("/api/business/v1/erp/dlq/replay-batch", new { consumerName = deadLetter.ConsumerName, status = "pending" });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        using var batchJson = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
        var batchItems = batchJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, batchItems.Length);
        Assert.All(batchItems, result => Assert.True(result.GetProperty("succeeded").GetBoolean()));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cost = Assert.Single(await db.WorkOrderCosts.Include(x => x.Details).ToListAsync());
            Assert.Equal("org-replay", cost.OrganizationId);
            Assert.Equal("env-replay", cost.EnvironmentId);
            Assert.Equal(100m, cost.LaborCost);
            Assert.Single(cost.Details);
            Assert.Single(await db.OperationLaborReportSnapshots.ToListAsync());
            var inbox = Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
            Assert.Equal(original.EventId, inbox.EventId);
            Assert.Equal(original.IdempotencyKey, inbox.IdempotencyKey);
            var replayed = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(deadLetter.Id, CancellationToken.None);
            Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, replayed!.Status);
            Assert.Equal(deadLetter.EventJson, replayed.EventJson);
        }

        // The same CLR contract has several production consumers: only the original cancellation consumer runs.
        var cancellation = new WmsIntegrationEvent("evt-wms-cancel-replay", WmsIntegrationEventTypes.OutboundOrderCancelled, 1,
            original.OccurredAtUtc, WmsIntegrationEventSources.BusinessWms, "original-correlation", "original-causation",
            "org-replay", "env-replay", "original-actor", "wms-cancel-replay",
            new WmsIntegrationPayload("DO-MISSING", null, null, null, null, null, null, "cancelled", null, null));
        IntegrationEventDeadLetterMessage cancellationDeadLetter;
        using (var scope = factory.Services.CreateScope())
        {
            cancellationDeadLetter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection.ConsumerName,
                    cancellation, "original", "original failure"), CancellationToken.None);
        }
        using var cancellationResponse = await client.PostAsync($"/api/business/v1/erp/dlq/{cancellationDeadLetter.Id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, cancellationResponse.StatusCode);
        using var cancellationJson = JsonDocument.Parse(await cancellationResponse.Content.ReadAsStringAsync());
        var cancellationResult = cancellationJson.RootElement.GetProperty("data");
        Assert.Equal("failed", cancellationResult.GetProperty("status").GetString());
        Assert.Contains("cancellation reason in DiagnosticMessage", cancellationResult.GetProperty("message").GetString(), StringComparison.Ordinal);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
            Assert.Empty(await db.PurchaseReceipts.ToListAsync());
            Assert.Empty(await db.AccountReceivables.ToListAsync());
        }

        async Task ReplaySingle(string expected)
        {
            using var response = await client.PostAsync($"/api/business/v1/erp/dlq/{deadLetter.Id}/replay", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
        }
    }

    private static WorkCenterCostRate Rate(string organization, string environment, DateTimeOffset from, decimal amount, int revision) =>
        WorkCenterCostRate.Define(organization, environment, "WC-REPLAY", amount, "CNY", from, null, revision,
            "system:test", "dead-letter recovery", from);
}
