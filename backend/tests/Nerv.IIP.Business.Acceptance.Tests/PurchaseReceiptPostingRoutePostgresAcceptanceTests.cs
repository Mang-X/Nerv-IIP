using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Contracts.Inventory;
using NetCorePal.Extensions.DistributedTransactions;
using ErpDb = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using WmsDb = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

// NERV-2121 DomainInvariant/PublicContract/ProviderBehavior：真实 HTTP 端点与 PostgreSQL。
// publisher 仅记录库存意图，不证明 Redis/CAP 或外部进程 FullChain。
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class PurchaseReceiptPostingRoutePostgresAcceptanceTests
{
    private const string Token = "receipt-route-http-test-token";
    private const string Organization = "org-route";
    private const string EnvironmentId = "env-route";

    [ReceiptRoutePostgresFact]
    public async Task Public_receipt_routes_are_exclusive_and_replay_preserves_identity_on_postgres()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync("erp", "wms");
        var events = new RecordingPublisher();
        await using var erp = new WebApplicationFactory<RecordPurchaseReceiptEndpoint>()
            .WithWebHostBuilder(builder => Configure(builder, events));
        using var erpClient = Client(erp);
        await using (var scope = erp.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDb>();
            AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var suffix in new[] { "direct", "wms" })
            {
                var order = PurchaseOrder.Create(Organization, EnvironmentId, $"PO-{suffix}", "SUP-01", "SITE-01",
                    [new PurchaseOrderLineDraft("1", "SKU-01", "ea", 10m, 2m, new DateOnly(2026, 9, 1))]);
                order.MarkApprovalRequested($"approval-{suffix}");
                order.ReleaseAfterApproval($"approval-{suffix}");
                db.PurchaseOrders.Add(order);
            }
            await db.SaveChangesAsync();
        }

        foreach (var route in new[] { "direct", "wms" })
        {
            var payload = new
            {
                organizationId = Organization, environmentId = EnvironmentId,
                purchaseReceiptNo = $"RCV-{route}", purchaseOrderNo = $"PO-{route}",
                inventoryPostingRoute = route, idempotencyKey = $"receipt-{route}",
                lines = new[] { new { purchaseOrderLineNo = "1", receivedQuantity = 10m, qualityStatus = "unrestricted", locationCode = "RECEIVING" } },
            };
            using var first = await erpClient.PostAsJsonAsync("/api/business/v1/erp/purchase-receipts", payload);
            var firstData = await SuccessfulData(first);
            using var replay = await erpClient.PostAsJsonAsync("/api/business/v1/erp/purchase-receipts", payload);
            Assert.Equal(firstData.GetRawText(), (await SuccessfulData(replay)).GetRawText());
        }

        var directMovement = Assert.Single(events.Published.OfType<InventoryMovementRequestedIntegrationEvent>());
        Assert.Equal("RCV-direct", directMovement.Payload.SourceDocumentId);
        Assert.Equal(10m, directMovement.Payload.Quantity);

        await using var wms = new WebApplicationFactory<CompleteInboundOrderEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                Configure(builder, new RecordingPublisher());
                builder.ConfigureTestServices(services =>
                {
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(CompleteInboundOrderEndpoint).Assembly];
                        options.DisableAutoDiscovery = true;
                        options.IncludeAbstractValidators = true;
                    });
                    services.AddHttpClient<IWmsPurchaseReceiptPostingRouteClient, HttpWmsPurchaseReceiptPostingRouteClient>(client =>
                            client.BaseAddress = erpClient.BaseAddress)
                        .ConfigurePrimaryHttpMessageHandler(() => erp.Server.CreateHandler());
                });
            });
        using var wmsClient = Client(wms);
        await using (var scope = wms.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
            AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var (org, env) in new[] { (Organization, EnvironmentId), ("org-other", EnvironmentId), (Organization, "env-other") })
                await WmsTrustedCompletionAcceptanceFixture.SeedAsync(db, org, env, "SITE-01");
        }

        foreach (var (suffix, source, org, env, allowed) in new[]
        {
            ("direct", "RCV-direct", Organization, EnvironmentId, false),
            ("missing", "RCV-missing", Organization, EnvironmentId, false),
            ("org", "RCV-wms", "org-other", EnvironmentId, false),
            ("env", "RCV-wms", Organization, "env-other", false),
            ("wms", "RCV-wms", Organization, EnvironmentId, true),
        })
        {
            using var created = await wmsClient.PostAsJsonAsync("/api/business/v1/wms/inbound-orders", new
            {
                organizationId = org, environmentId = env, inboundOrderNo = $"IN-{suffix}",
                sourceDocumentType = "purchase-receipt", sourceDocumentId = source, siteCode = "SITE-01",
                lines = new[] { new { lineNo = "1", skuCode = "SKU-01", uomCode = "ea", receivedQuantity = 10m,
                    stagingLocationCode = "RECEIVING", qualityStatus = "unrestricted", ownerType = "company" } },
            });
            var id = (await SuccessfulData(created)).GetProperty("inboundOrderId").GetString();
            long version;
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                var inbound = await db.InboundOrders.SingleAsync(x => x.InboundOrderNo == $"IN-{suffix}");
                inbound.AssignWorkPool(WmsTrustedCompletionAcceptanceFixture.PoolCode,
                    WmsTrustedCompletionAcceptanceFixture.ActorPrincipalId, inbound.Version);
                version = inbound.Version;
                await db.SaveChangesAsync();
            }
            var complete = new
            {
                organizationId = org, environmentId = env, idempotencyKey = $"complete-{suffix}",
                actorPrincipalId = WmsTrustedCompletionAcceptanceFixture.ActorPrincipalId,
                authorizedSiteCodes = new[] { "SITE-01" }, scopeKind = "self",
                scopeId = WmsTrustedCompletionAcceptanceFixture.ActorPrincipalId, expectedVersion = version,
            };
            using var completed = await wmsClient.PostAsJsonAsync($"/api/business/v1/wms/inbound-orders/{id}/complete", complete);
            if (allowed)
            {
                var first = await SuccessfulData(completed);
                using var replay = await wmsClient.PostAsJsonAsync($"/api/business/v1/wms/inbound-orders/{id}/complete", complete);
                Assert.Equal(first.GetRawText(), (await SuccessfulData(replay)).GetRawText());
            }
            else
            {
                using var rejected = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
                Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
            }
            await using var read = wms.Services.CreateAsyncScope();
            var readDb = read.ServiceProvider.GetRequiredService<WmsDb>();
            Assert.Equal(allowed ? 1 : 0, await readDb.InventoryMovementRequests.CountAsync(x => x.SourceDocumentId == $"IN-{suffix}"));
            if (!allowed)
                Assert.Equal(InboundOrderStatus.Open, (await readDb.InboundOrders.SingleAsync(x => x.InboundOrderNo == $"IN-{suffix}")).Status);
        }
        await using var finalScope = erp.Services.CreateAsyncScope();
        Assert.Equal(2, await finalScope.ServiceProvider.GetRequiredService<ErpDb>().PurchaseReceipts.CountAsync());
    }

    private static void Configure(IWebHostBuilder builder, RecordingPublisher publisher)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:PostgreSQL", AcceptancePostgresLaneDatabase.ConnectionString);
        builder.UseSetting("InternalService:BearerToken", Token);
        builder.UseSetting("Persistence:AutoMigrate", "false");
        builder.UseSetting("Erp:BaseUrl", "http://erp.test");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(publisher);
        });
    }

    private static HttpClient Client<T>(WebApplicationFactory<T> factory) where T : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private static async Task<JsonElement> SuccessfulData(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        return json.RootElement.GetProperty("data").Clone();
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];
        Task IIntegrationEventPublisher.PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}

public sealed class ReceiptRoutePostgresFactAttribute : FactAttribute
{
    public ReceiptRoutePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the ERP and WMS receipt-route HTTP acceptance test.";
    }
}
