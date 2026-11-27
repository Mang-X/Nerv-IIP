using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class PurchaseOrderReleaseOutboxPostgresTests
{
    private const string Topic = "nerv-iip.development.business-erp.procurement.purchase-order-released.v2";
    private const string LegacyTopic = "ErpIntegrationEvent`1";

    [PurchaseOrderReleasePostgresFact]
    public async Task Approval_commits_both_release_versions_once_and_outbox_failure_rolls_back()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await ErpPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            var order = PurchaseOrder.Create("org-001", "env-tenant", "PO-001", "SUP-001", "SITE-01",
                [
                    new PurchaseOrderLineDraft("20", "SKU-001", "kg", 2.75m, 4m, new DateOnly(2026, 12, 2)),
                    new PurchaseOrderLineDraft("10", "SKU-001", "kg", 3.5m, 2m, new DateOnly(2026, 12, 1)),
                ]);
            order.MarkApprovalRequested("chain-001");
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
        }

        await ApplyApprovalAsync(factory);
        await ApplyApprovalAsync(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(PurchaseOrderStatus.Released, (await db.PurchaseOrders.SingleAsync()).Status);
            Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
            var snapshot = Assert.Single(await ReadEventsAsync(db, Topic));
            var legacy = Assert.Single(await ReadEventsAsync(db, LegacyTopic));
            Assert.Equal(1, legacy.GetProperty("EventVersion").GetInt32());
            Assert.Equal("erp.PurchaseOrderReleased", legacy.GetProperty("EventType").GetString());
            Assert.Equal("erp:purchase-order-released:org-001:env-tenant:PO-001", legacy.GetProperty("IdempotencyKey").GetString());
            var legacyPayload = legacy.GetProperty("Payload");
            Assert.Equal(18m, legacyPayload.GetProperty("TotalAmount").GetDecimal());
            Assert.False(legacyPayload.TryGetProperty("Lines", out _));

            Assert.Equal("erp.PurchaseOrderReleased", snapshot.GetProperty("EventType").GetString());
            Assert.Equal(2, snapshot.GetProperty("EventVersion").GetInt32());
            Assert.Equal("business-erp", snapshot.GetProperty("SourceService").GetString());
            Assert.Equal("org-001", snapshot.GetProperty("OrganizationId").GetString());
            Assert.Equal("env-tenant", snapshot.GetProperty("EnvironmentId").GetString());
            Assert.Equal("erp:purchase-order-released:v2:org-001:env-tenant:PO-001", snapshot.GetProperty("IdempotencyKey").GetString());
            Assert.Equal("corr-001", snapshot.GetProperty("CorrelationId").GetString());
            Assert.Equal("evt-approval-001", snapshot.GetProperty("CausationId").GetString());
            Assert.Equal("user:buyer", snapshot.GetProperty("Actor").GetString());
            Assert.NotEqual(legacy.GetProperty("EventId").GetString(), snapshot.GetProperty("EventId").GetString());
            Assert.Equal(TimeSpan.Zero, snapshot.GetProperty("OccurredAtUtc").GetDateTimeOffset().Offset);
            var decoded = snapshot.Deserialize<PurchaseOrderReleasedV2IntegrationEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(decoded);
            Assert.Equal(2, decoded.Payload.Lines.Count);
            var payload = snapshot.GetProperty("Payload");
            Assert.Equal(legacyPayload.GetProperty("PurchaseOrderId").GetString(), payload.GetProperty("PurchaseOrderId").GetString());
            Assert.Equal("PO-001", payload.GetProperty("PurchaseOrderNo").GetString());
            Assert.Equal("SUP-001", payload.GetProperty("SupplierCode").GetString());
            Assert.Equal("SITE-01", payload.GetProperty("SiteCode").GetString());
            Assert.Collection(payload.GetProperty("Lines").EnumerateArray(),
                line => AssertLine(line, "10", 3.5m, "2026-12-01"),
                line => AssertLine(line, "20", 2.75m, "2026-12-02"));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = PurchaseOrder.Create("org-001", "env-tenant", "PO-002", "SUP-001", "SITE-01",
                [new PurchaseOrderLineDraft("10", "SKU-001", "kg", 2m, 4m, new DateOnly(2026, 12, 2))]);
            order.MarkApprovalRequested("chain-002");
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION cap.reject_po_release_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Name" = 'nerv-iip.development.business-erp.procurement.purchase-order-released.v2' THEN
                        RAISE EXCEPTION 'injected purchase order release outbox failure';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER reject_po_release_snapshot BEFORE INSERT ON cap.published
                FOR EACH ROW EXECUTE FUNCTION cap.reject_po_release_snapshot();
                """);
        }

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => ApplyApprovalAsync(factory, "002"));
        Assert.Contains("injected purchase order release outbox failure", failure.ToString(), StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(PurchaseOrderStatus.PendingApproval,
                (await db.PurchaseOrders.SingleAsync(order => order.PurchaseOrderNo == "PO-002")).Status);
            Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
            Assert.Single(await ReadEventsAsync(db, LegacyTopic));
            Assert.Single(await ReadEventsAsync(db, Topic));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_po_release_snapshot ON cap.published");
        }

        await ApplyApprovalAsync(factory, "002");
        await using var verification = factory.Services.CreateAsyncScope();
        var verifiedDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(PurchaseOrderStatus.Released,
            (await verifiedDb.PurchaseOrders.SingleAsync(order => order.PurchaseOrderNo == "PO-002")).Status);
        Assert.Equal(2, await verifiedDb.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, (await ReadEventsAsync(verifiedDb, LegacyTopic)).Count);
        Assert.Equal(2, (await ReadEventsAsync(verifiedDb, Topic)).Count);
    }

    private static void AssertLine(JsonElement line, string lineNo, decimal quantity, string promisedDate)
    {
        Assert.Equal(lineNo, line.GetProperty("LineNo").GetString());
        Assert.Equal("SKU-001", line.GetProperty("SkuCode").GetString());
        Assert.Equal("kg", line.GetProperty("UomCode").GetString());
        Assert.Equal(quantity, line.GetProperty("OrderedQuantity").GetDecimal());
        Assert.Equal(promisedDate, line.GetProperty("PromisedDate").GetString());
        Assert.False(line.TryGetProperty("ReceivedQuantity", out _));
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadEventsAsync(ApplicationDbContext db, string topic)
    {
        var content = await db.Database.SqlQuery<string>(
            $"""SELECT "Content" AS "Value" FROM cap.published WHERE "Name" = {topic}""").ToListAsync();
        return content.Select(value =>
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.GetProperty("Value").Clone();
        }).ToArray();
    }

    private static async Task ApplyApprovalAsync(WebApplicationFactory<Program> factory, string suffix = "001")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder>(scope.ServiceProvider);
        await handler.HandleCapAsync(new ApprovalCompletedIntegrationEvent(
            $"evt-approval-{suffix}", ApprovalIntegrationEventTypes.ApprovalApproved, 1,
            new DateTimeOffset(2026, 11, 27, 8, 0, 0, TimeSpan.Zero),
            ApprovalIntegrationEventSources.BusinessApproval, $"corr-{suffix}", $"cause-{suffix}",
            "org-001", "env-tenant", "user:buyer", $"approval:chain-{suffix}",
            new ApprovalCompletedPayload($"chain-{suffix}", ApprovalResults.Approved, "user", "buyer", null, null,
                new ApprovalDocumentReferencePayload(ApprovalSourceServices.BusinessErp, ApprovalDocumentTypes.PurchaseOrder,
                    $"PO-{suffix}", null))),
            CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = ErpPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "InMemory",
            ["InternalService:BearerToken"] = "test-internal-token",
            ["Approval:BaseUrl"] = "https://approval.test",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["Wms:BaseUrl"] = "https://wms.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PurchaseOrderReleasePostgresFactAttribute : FactAttribute
{
    public PurchaseOrderReleasePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP purchase order release outbox acceptance test.";
        }
    }
}
