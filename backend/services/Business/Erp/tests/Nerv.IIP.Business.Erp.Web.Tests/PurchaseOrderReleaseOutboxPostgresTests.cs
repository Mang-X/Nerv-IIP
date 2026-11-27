using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Wms;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Erp;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class PurchaseOrderReleaseOutboxPostgresTests
{
    private const string Topic = "nerv-iip.development.business-erp.procurement.purchase-order-released.v2";
    private const string CancellationTopic = "nerv-iip.development.business-erp.procurement.purchase-order-cancelled.v1";
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
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreatePurchaseOrderCommand(
                "org-001", "env-tenant", "PO-001", "SUP-001", "SITE-01",
                [
                    new("20", "SKU-001", "kg", 2.75m, 4m, new DateOnly(2026, 12, 2)),
                    new("10", "SKU-001", "kg", 3.5m, 2m, new DateOnly(2026, 12, 1)),
                ]));
        }

        await ApplyApprovalAsync(factory);
        await ApplyApprovalAsync(factory);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        foreach (var route in new[] { "direct", "wms" })
        {
            using var response = await client.PostAsJsonAsync("/api/business/v1/erp/purchase-receipts", new
            {
                organizationId = "org-001",
                environmentId = "env-tenant",
                purchaseOrderNo = "PO-001",
                purchaseReceiptNo = "RCV-NEW",
                idempotencyKey = "new-erp-receipt",
                inventoryPostingRoute = route,
                lines = new[] { new { purchaseOrderLineNo = "10", receivedQuantity = 1m,
                    qualityStatus = "unrestricted", locationCode = "RAW-A-01" } },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(body);
            Assert.False(body.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("该采购订单由 WMS 收货，请在 WMS 完成收货。", body.RootElement.GetProperty("message").GetString());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.PurchaseOrders.Include(x => x.Lines).SingleAsync();
            Assert.Equal(PurchaseOrderStatus.Released, order.Status);
            Assert.Equal(PurchaseReceiptInventoryPostingRoute.Wms, order.ReceivingAuthority);
            Assert.All(order.Lines, line => Assert.Equal(0m, line.ReceivedQuantity));
            Assert.Empty(await db.PurchaseReceipts.ToListAsync());
            Assert.Empty(await db.CodeIdempotencyKeys.ToListAsync());
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
            Assert.Equal("wms", payload.GetProperty("ReceivingAuthority").GetString());
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

    [PurchaseOrderReleasePostgresFact]
    public async Task Wms_owned_cancellation_commits_one_event_across_late_retry()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await ErpPostgresLaneDatabase.ResetCapSchemaAsync();
        var wms = new TrackingWmsCancellationClient();
        await using var factory = CreateFactory(wms);
        var before = await SeedReleasedOrderAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-po-cancel");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:buyer");
        foreach (var reason in new[] { "supplier withdrew", "retry reason" })
        {
            using var response = await client.PostAsJsonAsync(
                "/api/business/v1/erp/purchase-orders/PO-CANCEL/cancel",
                new { organizationId = "org-001", environmentId = "env-tenant", purchaseOrderNo = "PO-CANCEL", reason });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(body);
            Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        }

        Assert.Equal(0, wms.Calls);
        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.PurchaseOrders.Include(x => x.ChangeHistory).SingleAsync();
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);
        Assert.Equal(before + 1, order.Version);
        Assert.Single(order.ChangeHistory);
        var message = Assert.Single(await ReadEventsAsync(db, CancellationTopic));
        Assert.Equal("erp.PurchaseOrderCancelled", message.GetProperty("EventType").GetString());
        Assert.Equal(1, message.GetProperty("EventVersion").GetInt32());
        Assert.Equal("business-erp", message.GetProperty("SourceService").GetString());
        Assert.Equal("org-001", message.GetProperty("OrganizationId").GetString());
        Assert.Equal("env-tenant", message.GetProperty("EnvironmentId").GetString());
        Assert.Equal("erp:purchase-order-cancelled:v1:org-001:env-tenant:PO-CANCEL", message.GetProperty("IdempotencyKey").GetString());
        Assert.StartsWith("evt-", message.GetProperty("EventId").GetString());
        Assert.Equal(TimeSpan.Zero, message.GetProperty("OccurredAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal("corr-po-cancel", message.GetProperty("CorrelationId").GetString());
        Assert.StartsWith("command:cancel-purchase-order:", message.GetProperty("CausationId").GetString());
        Assert.Equal("user:buyer", message.GetProperty("Actor").GetString());
        var payload = message.GetProperty("Payload");
        Assert.NotNull(message.Deserialize<PurchaseOrderCancelledIntegrationEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(order.Id.ToString(), payload.GetProperty("PurchaseOrderId").GetString());
        Assert.Equal("PO-CANCEL", payload.GetProperty("PurchaseOrderNo").GetString());
        Assert.Equal("SITE-01", payload.GetProperty("SiteCode").GetString());
        Assert.Equal(before + 1, payload.GetProperty("PurchaseOrderVersion").GetInt32());
        Assert.Equal("supplier withdrew", payload.GetProperty("Reason").GetString());
    }

    [PurchaseOrderReleasePostgresFact]
    public async Task Accepted_wms_receipt_wins_against_stale_cancellation_without_publishing()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await ErpPostgresLaneDatabase.ResetCapSchemaAsync();
        var wms = new TrackingWmsCancellationClient();
        await using var factory = CreateFactory(wms);
        var version = await SeedReleasedOrderAsync(factory);
        var batchId = Guid.NewGuid();

        await using (var stale = factory.Services.CreateAsyncScope())
        {
            var staleDb = stale.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staleOrder = await staleDb.PurchaseOrders.Include(x => x.Lines).SingleAsync();
            Assert.Equal(version, staleOrder.Version);
            await using var winner = factory.Services.CreateAsyncScope();
            using var context = winner.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>()
                .BeginScope("command:accept-wms-purchase-receipt");
            await winner.ServiceProvider.GetRequiredService<ISender>().Send(new AcceptWmsPurchaseReceiptCommand(
                "org-001", "env-tenant", batchId, "PO-CANCEL", "SITE-01", 1m, [new("10", 1m, "RAW-A-01")]));

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CancelAsync(stale.ServiceProvider));
        }

        await using var retry = factory.Services.CreateAsyncScope();
        var rejection = await Assert.ThrowsAsync<KnownException>(() => CancelAsync(retry.ServiceProvider));
        Assert.Equal("Purchase orders with received quantity cannot be cancelled.", rejection.Message);
        var db = retry.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.PurchaseOrders.Include(x => x.Lines).Include(x => x.ChangeHistory).SingleAsync();
        Assert.Equal(PurchaseOrderStatus.Released, order.Status);
        Assert.Equal(1m, Assert.Single(order.Lines).ReceivedQuantity);
        Assert.Empty(order.ChangeHistory);
        Assert.Equal(version + 1, order.Version);
        var result = await retry.ServiceProvider.GetRequiredService<ISender>().Send(
            new GetWmsPurchaseReceiptAcceptanceQuery("org-001", "env-tenant", batchId));
        Assert.NotNull(result);
        Assert.Equal(result.PurchaseReceiptNo, (await db.PurchaseReceipts.SingleAsync()).PurchaseReceiptNo);
        Assert.Empty(await ReadEventsAsync(db, CancellationTopic));
        Assert.Equal(0, wms.Calls);
    }

    [PurchaseOrderReleasePostgresFact]
    public async Task Cancellation_outbox_failure_rolls_back_order_and_can_retry()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await ErpPostgresLaneDatabase.ResetCapSchemaAsync();
        var wms = new TrackingWmsCancellationClient();
        await using var factory = CreateFactory(wms);
        var version = await SeedReleasedOrderAsync(factory);
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION cap.reject_po_cancellation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Name" = 'nerv-iip.development.business-erp.procurement.purchase-order-cancelled.v1' THEN
                        RAISE EXCEPTION 'injected purchase order cancellation outbox failure';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER reject_po_cancellation BEFORE INSERT ON cap.published
                FOR EACH ROW EXECUTE FUNCTION cap.reject_po_cancellation();
                """);
        }

        await using (var failed = factory.Services.CreateAsyncScope())
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => CancelAsync(failed.ServiceProvider));
            Assert.Contains("injected purchase order cancellation outbox failure", failure.ToString(), StringComparison.Ordinal);
        }

        await using (var read = factory.Services.CreateAsyncScope())
        {
            var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.PurchaseOrders.Include(x => x.ChangeHistory).SingleAsync();
            Assert.Equal(PurchaseOrderStatus.Released, order.Status);
            Assert.Equal(version, order.Version);
            Assert.Empty(order.ChangeHistory);
            Assert.Empty(await ReadEventsAsync(db, CancellationTopic));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_po_cancellation ON cap.published");
        }

        await using (var write = factory.Services.CreateAsyncScope())
        {
            await CancelAsync(write.ServiceProvider);
        }

        await using var verify = factory.Services.CreateAsyncScope();
        var verifiedDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(PurchaseOrderStatus.Cancelled, (await verifiedDb.PurchaseOrders.SingleAsync()).Status);
        Assert.Single(await ReadEventsAsync(verifiedDb, CancellationTopic));
        Assert.Equal(0, wms.Calls);
    }

    private static async Task CancelAsync(IServiceProvider services)
    {
        using var context = services.GetRequiredService<IErpIntegrationEventContextAccessor>()
            .BeginScope("command:cancel", "corr-cancel", "user:buyer");
        await services.GetRequiredService<ISender>().Send(
            new CancelPurchaseOrderCommand("org-001", "env-tenant", "PO-CANCEL", "supplier withdrew"));
    }

    private static async Task<int> SeedReleasedOrderAsync(WebApplicationFactory<Program> factory)
    {
        await using var setup = factory.Services.CreateAsyncScope();
        var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var order = PurchaseOrder.Create("org-001", "env-tenant", "PO-CANCEL", "SUP-001", "SITE-01", "CNY",
            [new("10", "SKU-001", "kg", 5m, 4m, new DateOnly(2026, 12, 1))],
            PurchaseReceiptInventoryPostingRoute.Wms);
        order.MarkApprovalRequested("chain-cancel");
        order.ReleaseAfterApproval("chain-cancel");
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Version;
    }

    private sealed class TrackingWmsCancellationClient : IWmsInboundCancellationClient
    {
        public int Calls { get; private set; }

        public Task CancelOpenInboundOrdersForPurchaseOrderAsync(
            string organizationId, string environmentId, string purchaseOrderNo, string reason, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
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

    internal static WebApplicationFactory<Program> CreateFactory() => CreateFactory(null);

    private static WebApplicationFactory<Program> CreateFactory(TrackingWmsCancellationClient? wms)
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
            ["PurchaseOrderReceiving:WmsScopes:0:OrganizationId"] = "org-001",
            ["PurchaseOrderReceiving:WmsScopes:0:EnvironmentId"] = "env-tenant",
            ["PurchaseOrderReceiving:WmsScopes:0:SiteCode"] = "SITE-01",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPurchaseOrderApprovalClient>();
                services.AddSingleton<IPurchaseOrderApprovalClient, TestApprovalClient>();
                if (wms is not null)
                {
                    services.RemoveAll<IWmsInboundCancellationClient>();
                    services.AddSingleton<IWmsInboundCancellationClient>(wms);
                }
            });
        });
    }

    private sealed class TestApprovalClient : IPurchaseOrderApprovalClient
    {
        public Task<PurchaseOrderApprovalResult> StartApprovalAsync(
            PurchaseOrderApprovalRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseOrderApprovalResult("chain-001"));
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
