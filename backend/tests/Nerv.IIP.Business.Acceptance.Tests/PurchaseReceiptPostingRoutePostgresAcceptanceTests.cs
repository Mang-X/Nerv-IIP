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
using ErpDb = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using WmsDb = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

// NERV-2121/2122 DomainInvariant/PublicContract/ProviderBehavior：真实 Gateway→ERP→WMS HTTP 与 PostgreSQL。
// IAM 权限检查为允许桩；不证明真实 IAM 授权。
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
                ["lines"] = new[] { new { purchaseOrderLineNo = "1", receivedQuantity = 10m, qualityStatus = "unrestricted" } },
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
