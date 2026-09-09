using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Contracts.Inventory;
using NetCorePal.Extensions.DistributedTransactions;
using MediatR;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Messaging.CAP;
using Microsoft.Extensions.Logging.Abstractions;
using InventoryDb = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using ErpDb = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using WmsDb = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

// NERV-2121/2122 DomainInvariant/PublicContract/ProviderBehavior：真实 Gateway→ERP→WMS HTTP 与 PostgreSQL。
// IAM 权限检查为允许桩；不证明真实 IAM 授权。
// #3268：Inventory 使用真实 PostgreSQL/命令/事件转换，事件由测试直接调用消费者交付；不证明 Redis/CAP 或外部进程 FullChain。
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class PurchaseReceiptPostingRoutePostgresAcceptanceTests
{
    private const string Token = "receipt-route-http-test-token";
    private const string Organization = "org-route";
    private const string EnvironmentId = "env-route";

    [ReceiptRoutePostgresFact]
    public async Task Public_receipt_routes_are_exclusive_and_replay_preserves_identity_on_postgres()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync("erp", "wms", "inventory");
        var events = new RecordingPublisher();
        await using var erp = new WebApplicationFactory<RecordPurchaseReceiptEndpoint>()
            .WithWebHostBuilder(builder => Configure(builder, events));
        using var erpClient = Client(erp);
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "receipt-route-test" };
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = signingKey.KeyId });
        await using var gateway = new WebApplicationFactory<RecordBusinessConsoleErpPurchaseReceiptEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                foreach (var service in new[] { "Iam", "MasterData", "Inventory", "Quality", "ProductEngineering",
                    "DemandPlanning", "Erp", "Wms", "Approval", "BarcodeLabel", "Notification", "FileStorage",
                    "Mes", "Scheduling", "IndustrialTelemetry", "Maintenance", "AppHub" })
                    builder.UseSetting($"{service}:BaseUrl", $"http://{service.ToLowerInvariant()}.test");
                builder.UseSetting("InternalService:BearerToken", Token);
                builder.UseSetting("Iam:Jwt:JwksJson", JsonSerializer.Serialize(new { keys = new[] { publicKey } }));
                builder.UseSetting("Iam:Jwt:Issuer", "receipt-route-test");
                builder.UseSetting("Iam:Jwt:Audience", "receipt-route-test");
                builder.UseSetting("Security:Cors:AllowedOrigins", "http://receipt-route.test");
                builder.ConfigureTestServices(services =>
                {
                    // FastEndpoints 在同进程宿主间共享序列化选项；保留 ERP 的强类型 ID wire converter。
                    services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
                        options.SerializerOptions.AddNetCorePalJsonConverters());
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(RecordBusinessConsoleErpPurchaseReceiptEndpoint).Assembly];
                        options.DisableAutoDiscovery = true;
                        options.IncludeAbstractValidators = true;
                    });
                    services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                    services.AddSingleton<IBusinessGatewayAuthorizationClient>(new AllowedAuthorization());
                    services.AddHttpClient<IBusinessErpClient, HttpBusinessErpClient>(client => client.BaseAddress = erpClient.BaseAddress)
                        .ConfigurePrimaryHttpMessageHandler(() => erp.Server.CreateHandler());
                });
            });
        using var gatewayClient = gateway.CreateClient();
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("receipt-route-test", "receipt-route-test",
                [new Claim("sub", "receipt-user"), new Claim("organizationId", Organization), new Claim("environmentId", EnvironmentId)],
                now.AddMinutes(-1), now.AddMinutes(10), new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256))));
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
            var payload = new Dictionary<string, object?>
            {
                ["organizationId"] = Organization, ["environmentId"] = EnvironmentId,
                ["purchaseReceiptNo"] = $"RCV-{route}", ["purchaseOrderNo"] = $"PO-{route}",
                ["idempotencyKey"] = $"receipt-{route}",
                ["lines"] = new[] { new { purchaseOrderLineNo = "1", receivedQuantity = 10m, qualityStatus = "unrestricted", lotNo = "LOT-ROUTE" } },
            };
            if (route == "wms") payload["inventoryPostingRoute"] = route;
            using var first = await gatewayClient.PostAsJsonAsync("/api/business-console/v1/erp/procurement/purchase-receipts", payload);
            var firstData = await SuccessfulData(first);
            using var replay = await gatewayClient.PostAsJsonAsync("/api/business-console/v1/erp/procurement/purchase-receipts", payload);
            Assert.Equal(firstData.GetRawText(), (await SuccessfulData(replay)).GetRawText());
            using var source = await erpClient.GetAsync($"/api/business/v1/erp/purchase-receipts/RCV-{route}/source-document?organizationId={Organization}&environmentId={EnvironmentId}");
            var sourceData = await SuccessfulData(source);
            Assert.Equal(route, sourceData.GetProperty("inventoryPostingRoute").GetString());
            Assert.Equal("CNY", sourceData.GetProperty("currencyCode").GetString());
            Assert.Equal(1m, sourceData.GetProperty("exchangeRate").GetDecimal());
            Assert.Equal(2m, sourceData.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
            Assert.Equal(2m, sourceData.GetProperty("lines")[0].GetProperty("estimatedUnitCost").GetDecimal());
        }

        var directMovement = Assert.Single(events.Published.OfType<InventoryMovementRequestedIntegrationEvent>());
        Assert.Equal("RCV-direct", directMovement.Payload.SourceDocumentId);
        Assert.Equal(10m, directMovement.Payload.Quantity);
        Assert.Equal(2m, directMovement.Payload.UnitCost);

        var wmsEvents = new RecordingPublisher();
        await using var wms = new WebApplicationFactory<CompleteInboundOrderEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                Configure(builder, wmsEvents);
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
            await db.GetService<IMigrator>().MigrateAsync("20260729205928_CompleteWmsWorkPoolExecutionBoundary");
            // 真实旧 schema 中已成立的无价请求：升级只加可空列，不补价或产生新库存事件。
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO wms.inventory_movement_requests
                    (id, organization_id, environment_id, movement_type, source_document_id, source_document_line_id,
                     idempotency_key, sku_code, uom_code, site_code, location_code, quality_status, owner_type,
                     quantity, status, inventory_movement_id, created_at_utc, posted_at_utc)
                VALUES ('00000000-0000-0000-0000-000000002131', 'org-route', 'env-route', 'inbound', 'IN-LEGACY', '1',
                    'legacy-complete', 'SKU-01', 'ea', 'SITE-01', 'RECEIVING', 'unrestricted', 'company',
                    8, 'Posted', 'LEGACY-MOVEMENT', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z')
                """);
            await db.Database.MigrateAsync();
            var legacy = await db.InventoryMovementRequests.SingleAsync();
            Assert.Null(legacy.UnitCost);
            Assert.Equal("LEGACY-MOVEMENT", legacy.InventoryMovementId);
            Assert.Equal(8m, legacy.Quantity);
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
                    stagingLocationCode = "RECEIVING", qualityStatus = "unrestricted", ownerType = "company", lotNo = "LOT-ROUTE" } },
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
        Assert.Single(events.Published.OfType<InventoryMovementRequestedIntegrationEvent>());
        var wmsMovement = Assert.Single(wmsEvents.Published.OfType<InventoryMovementRequestedIntegrationEvent>());
        Assert.Equal(2m, wmsMovement.Payload.UnitCost);
        await using (var read = wms.Services.CreateAsyncScope())
            Assert.Equal(2m, (await read.ServiceProvider.GetRequiredService<WmsDb>().InventoryMovementRequests.SingleAsync(x => x.SourceDocumentId == "IN-wms")).UnitCost);

        await AssertPostedValueAndConsumptionAsync(directMovement, erp, consume: false);
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync("inventory");
        await AssertPostedValueAndConsumptionAsync(wmsMovement, erp, consume: true);
    }

    private static async Task AssertPostedValueAndConsumptionAsync(
        InventoryMovementRequestedIntegrationEvent receipt,
        WebApplicationFactory<RecordPurchaseReceiptEndpoint> erp,
        bool consume)
    {
        var published = new RecordingPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(PostStockMovementCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddIntegrationEvents(typeof(StockMovementPostedIntegrationEventConverter));
        services.AddInventoryPostgreSqlPersistence(AcceptancePostgresLaneDatabase.ConnectionString);
        services.AddInMemoryDistributedLock();
        services.AddSingleton<IInventoryIntegrationEventContextAccessor, ReceiptInventoryContext>();
        services.AddSingleton<IIntegrationEventPublisher>(published);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var consumer = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender, new InMemoryIntegrationEventDeadLetterStore(), published);
        await consumer.HandleAsync(receipt, CancellationToken.None);
        await consumer.HandleAsync(receipt, CancellationToken.None);
        db.ChangeTracker.Clear();
        var ledger = Assert.Single(await db.StockLedgers.ToArrayAsync());
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(20m, ledger.InventoryValue);
        Assert.Single(await db.StockMovements.ToArrayAsync());
        if (!consume) return;

        var payload = receipt.Payload;
        var transfer = new PostStockMovementCommand(Organization, EnvironmentId, "transfer", "wms",
            "ISSUE-ROUTE", "1", "issue-route", payload.SkuCode, payload.UomCode, payload.SiteCode,
            payload.LocationCode, payload.LotNo, null, "unrestricted", payload.OwnerType, payload.OwnerId, -4m,
            TransferInSiteCode: payload.SiteCode, TransferInLocationCode: "LINE-SIDE", TransferInQuantity: 4m);
        await sender.Send(transfer);
        await sender.Send(transfer);
        var consumption = ProductionReportMaterialConsumption.Record(Organization, EnvironmentId, "REPORT-ROUTE", "WO-ROUTE",
            "OP-10", payload.SkuCode, "LOT-ROUTE", payload.UomCode, 3m, "ISSUE-ROUTE",
            payload.SiteCode, "LINE-SIDE", payload.OwnerType, payload.OwnerId);
        var requested = new ProductionMaterialConsumedIntegrationEventConverter().Convert(
            Assert.Single(consumption.GetDomainEvents().OfType<ProductionMaterialConsumedDomainEvent>()));
        await consumer.HandleAsync(requested, CancellationToken.None);
        await consumer.HandleAsync(requested, CancellationToken.None);
        var posted = Assert.Single(published.Published.OfType<StockMovementPostedIntegrationEvent>(),
            x => x.Payload.SourceDocumentId == "REPORT-ROUTE");
        Assert.Equal(-3m, posted.Payload.Quantity);
        Assert.Equal(2m, posted.Payload.UnitCost);
        Assert.Equal(-6m, posted.Payload.MovementAmount);
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.StockMovements.CountAsync());
        Assert.Equal(7m, await db.StockLedgers.SumAsync(x => x.OnHandQuantity));
        Assert.Equal(14m, await db.StockLedgers.SumAsync(x => x.InventoryValue));
        Assert.Equal(20m, await db.StockLedgers.SumAsync(x => x.InventoryValue) - posted.Payload.MovementAmount);

        await using var erpScope = erp.Services.CreateAsyncScope();
        var erpDb = erpScope.ServiceProvider.GetRequiredService<ErpDb>();
        var cost = WorkOrderCost.Open(Organization, EnvironmentId, "WO-ROUTE", "FG-ROUTE");
        cost.RecordLabor("REPORT-ROUTE", "WC-ROUTE", 1m, 1m, "CNY", false, posted.OccurredAtUtc);
        erpDb.WorkOrderCosts.Add(cost);
        await erpDb.SaveChangesAsync();
        var costConsumer = new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(
            erpDb, new InMemoryIntegrationEventDeadLetterStore(), erpDb);
        await costConsumer.HandleAsync(posted, CancellationToken.None);
        await costConsumer.HandleAsync(posted, CancellationToken.None);
        erpDb.ChangeTracker.Clear();
        var persistedCost = await erpDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(6m, persistedCost.MaterialCost);
        Assert.Single(persistedCost.Details, x => x.Type == WorkOrderCostDetailType.Material);
    }

    private sealed class ReceiptInventoryContext : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext() => new("receipt-valuation", "receipt-valuation", "system:test");
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

    private sealed class AllowedAuthorization : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(string bearerToken,
            BusinessGatewayPermissionRequirement requirement, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessGatewayAuthorizationResult.Allowed("receipt-user", "user", "receipt-user",
                requirement.OrganizationId, requirement.EnvironmentId));
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
