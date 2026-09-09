using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.ServiceAuth;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class PurchaseReceiptPostingRouteTests
{
    [Fact]
    public async Task Legacy_unvalued_request_replays_without_backfill_or_new_event()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"receipt-legacy-{Guid.NewGuid():N}").Options, new NoopMediator());
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-LEGACY", "purchase-receipt", "RCV-01", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 8m, "RECEIVING", null, null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        var command = new CompleteInboundOrderCommand(inbound.Id, "legacy-complete").TrustedFor(db, inbound);
        db.InventoryMovementRequests.AddRange(inbound.Complete("legacy-complete", inbound.Version));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await new CompleteInboundOrderCommandHandler(db, new UnavailableSource()).Handle(command, CancellationToken.None);
        var request = Assert.Single(await db.InventoryMovementRequests.ToArrayAsync());
        Assert.Null(request.UnitCost);
        Assert.Empty(request.GetDomainEvents());
    }

    [Fact]
    public async Task Multiple_receipt_lines_use_their_own_frozen_cost_regardless_of_source_order()
    {
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-MULTI", "purchase-receipt", "RCV-01", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 8m, "RECEIVING", null, null, "qualified", "company", null),
             new InboundOrderLineDraft("2", "SKU-02", "kg", 3m, "RECEIVING", null, null, "qualified", "company", null)]);
        using var http = new HttpClient(new SourceResponse("""
            {"success":true,"data":{"purchaseReceiptNo":"RCV-01","inventoryPostingRoute":"wms","lines":[
              {"lineNo":"2","skuCode":"SKU-02","uomCode":"kg","receivedQuantity":3,"estimatedUnitCost":2.5},
              {"lineNo":"1","skuCode":"SKU-01","uomCode":"ea","receivedQuantity":8,"estimatedUnitCost":1.4}]}}
            """)) { BaseAddress = new Uri("http://erp.test") };
        var costs = await new HttpWmsPurchaseReceiptPostingRouteClient(http, new Token()).GetUnitCostsAsync(
            "org-route", "env-route", "RCV-01", inbound.Lines, CancellationToken.None);
        var requests = inbound.Complete("multi-complete", inbound.Version, unitCostsByLine: costs);
        Assert.Equal(1.4m, requests.Single(x => x.SourceDocumentLineId == "1").UnitCost);
        Assert.Equal(2.5m, requests.Single(x => x.SourceDocumentLineId == "2").UnitCost);
    }

    [Theory]
    [InlineData("RCV-other", "1", "SKU-01", "ea", 8, 1.4)]
    [InlineData("RCV-01", "other", "SKU-01", "ea", 8, 1.4)]
    [InlineData("RCV-01", "1", "SKU-other", "ea", 8, 1.4)]
    [InlineData("RCV-01", "1", "SKU-01", "kg", 8, 1.4)]
    [InlineData("RCV-01", "1", "SKU-01", "ea", 7, 1.4)]
    [InlineData("RCV-01", "1", "SKU-01", "ea", 8, null)]
    public async Task Unmatched_or_unvalued_source_does_not_complete_receipt(
        string receiptNo, string lineNo, string skuCode, string uomCode, int quantity, double? cost)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"receipt-invalid-{Guid.NewGuid():N}").Options, new NoopMediator());
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-INVALID", "purchase-receipt", "RCV-01", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 8m, "RECEIVING", null, null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new SourceResponse(JsonSerializer.Serialize(new
        {
            success = true,
            data = new { purchaseReceiptNo = receiptNo, inventoryPostingRoute = "wms", currencyCode = "CNY", exchangeRate = 1,
                lines = new[] { new { lineNo, skuCode, uomCode, receivedQuantity = quantity, unitPrice = 99m, estimatedUnitCost = cost } } },
        }))) { BaseAddress = new Uri("http://erp.test") };
        var command = new CompleteInboundOrderCommand(inbound.Id, "invalid-complete").TrustedFor(db, inbound);
        await Assert.ThrowsAsync<KnownException>(() => new CompleteInboundOrderCommandHandler(db,
            new HttpWmsPurchaseReceiptPostingRouteClient(http, new Token())).Handle(command, CancellationToken.None));
        Assert.Equal(InboundOrderStatus.Open, inbound.Status);
        Assert.Empty(db.InventoryMovementRequests.Local);
    }

    // #3268 Regression：冻结来源成本必须进入持久请求产生的库存事件，重读请求后仍是首次值。
    [Fact]
    public async Task Receipt_frozen_cost_survives_request_reload_and_replay()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"receipt-cost-{Guid.NewGuid():N}").Options, new NoopMediator());
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-COST",
            "purchase-receipt", "RCV-01", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 8m, "RECEIVING", null, null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        var command = new CompleteInboundOrderCommand(inbound.Id, "cost-complete").TrustedFor(db, inbound);
        using var http = new HttpClient(new SourceResponse("""
            {"success":true,"data":{"purchaseReceiptNo":"RCV-01","status":"recorded","inventoryPostingRoute":"wms","currencyCode":"CNY","exchangeRate":1,"lines":[{"lineNo":"1","skuCode":"SKU-01","uomCode":"ea","receivedQuantity":8,"lotNo":null,"status":"qualified","unitPrice":1.4,"estimatedUnitCost":1.4}]}}
            """)) { BaseAddress = new Uri("http://erp.test") };
        var first = await new CompleteInboundOrderCommandHandler(db,
            new HttpWmsPurchaseReceiptPostingRouteClient(http, new Token())).Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var replay = await new CompleteInboundOrderCommandHandler(db, new UnavailableSource()).Handle(command, CancellationToken.None);
        Assert.Equal(first.RequestId, replay.RequestId);
        var movement = Assert.Single(await db.InventoryMovementRequests.ToArrayAsync());
        var payload = new Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters.InventoryMovementRequestCreatedIntegrationEventConverter()
            .Convert(new Nerv.IIP.Business.Wms.Domain.DomainEvents.InventoryMovementRequestCreatedDomainEvent(movement)).Payload;
        Assert.Equal(8m, payload.Quantity);
        Assert.Equal(1.4m, payload.UnitCost);
        Assert.Equal(11.2m, payload.Quantity * payload.UnitCost);

        movement.MarkFailed("POSTING_REJECTED", "rejected");
        (await db.InboundOrders.SingleAsync()).MarkInventoryPostingFailed();
        await db.SaveChangesAsync();
        var retry = await new RetryInboundInventoryPostingCommandHandler(db).Handle(
            new RetryInboundInventoryPostingCommand(inbound.Id, "cost-retry"), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var retried = await db.InventoryMovementRequests.SingleAsync(x => x.Id == retry.RequestId);
        Assert.Equal(1.4m, retried.UnitCost);
    }

    // NERV-2121: 没有 ERP 来源事实的采购收货不能生成第二条库存写入路径。
    [Theory]
    [InlineData(null)]
    [InlineData(PurchaseReceiptInventoryPostingRoute.Direct)]
    public async Task Missing_or_direct_purchase_receipt_cannot_create_inventory_request(PurchaseReceiptInventoryPostingRoute? route)
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"receipt-route-{Guid.NewGuid():N}").Options,
            new NoopMediator());
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-ROUTE",
            "purchase-receipt", "RCV-MISSING", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 10m, "RECEIVING", null, null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        var command = new CompleteInboundOrderCommand(inbound.Id, "route-complete")
            .TrustedFor(db, inbound);

        await Assert.ThrowsAsync<KnownException>(() =>
            new CompleteInboundOrderCommandHandler(db, new WmsReceiptRouteFixture(route)).Handle(command, CancellationToken.None));

        Assert.Empty(db.InventoryMovementRequests.Local);
        Assert.Equal(InboundOrderStatus.Open, inbound.Status);
    }

    [Fact]
    public async Task Wms_receipt_completes_once_and_existing_execution_replays_without_source_lookup()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"receipt-route-{Guid.NewGuid():N}").Options,
            new NoopMediator());
        var inbound = InboundOrder.Create("org-route", "env-route", "IN-ROUTE",
            "purchase-receipt", "RCV-WMS", "SITE-01",
            [new InboundOrderLineDraft("1", "SKU-01", "ea", 10m, "RECEIVING", null, null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        var command = new CompleteInboundOrderCommand(inbound.Id, "route-complete")
            .TrustedFor(db, inbound);
        var first = await new CompleteInboundOrderCommandHandler(db, new WmsReceiptRouteFixture()).Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replay = await new CompleteInboundOrderCommandHandler(db, new UnavailableSource()).Handle(command, CancellationToken.None);

        Assert.Equal(first.RequestId, replay.RequestId);
        var movement = Assert.Single(await db.InventoryMovementRequests.ToArrayAsync());
        Assert.Equal(10m, movement.Quantity);
        Assert.Equal("IN-ROUTE", movement.SourceDocumentId);
    }

    private sealed class UnavailableSource : Nerv.IIP.Business.Wms.Web.Application.Inventory.IWmsPurchaseReceiptPostingRouteClient
    {
        public Task<IReadOnlyDictionary<string, decimal>> GetUnitCostsAsync(string organizationId, string environmentId, string receiptNo,
            IReadOnlyCollection<InboundOrderLine> lines, CancellationToken cancellationToken) =>
            throw new HttpRequestException("ERP unavailable");
    }

    [Theory]
    [InlineData("{\"success\":true,\"data\":{\"inventoryPostingRoute\":\"direct\"}}")]
    [InlineData("{\"success\":true,\"data\":null}")]
    public async Task Source_http_rejects_missing_or_direct_receipt(string body)
    {
        using var client = new HttpClient(new SourceResponse(body)) { BaseAddress = new Uri("http://erp.test") };
        await Assert.ThrowsAsync<KnownException>(() => new HttpWmsPurchaseReceiptPostingRouteClient(client, new Token())
            .GetUnitCostsAsync("org-route", "env-route", "RCV-01", [], CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"success\":false,\"data\":{\"inventoryPostingRoute\":\"wms\"}}", 200, typeof(HttpRequestException))]
    [InlineData("{\"success\":true,\"data\":{\"inventoryPostingRoute\":\"wms\"}}", 503, typeof(HttpRequestException))]
    [InlineData("{\"success\":true,\"data\":{}}", 200, typeof(KeyNotFoundException))]
    [InlineData("{\"success\":true,\"data\":{\"inventoryPostingRoute\":\"unknown\"}}", 200, typeof(System.Text.Json.JsonException))]
    public async Task Source_http_never_accepts_failed_or_missing_route(string body, int status, Type exceptionType)
    {
        using var client = new HttpClient(new SourceResponse(body, (HttpStatusCode)status)) { BaseAddress = new Uri("http://erp.test") };
        await Assert.ThrowsAsync(exceptionType, () => new HttpWmsPurchaseReceiptPostingRouteClient(client, new Token())
            .GetUnitCostsAsync("org-route", "env-route", "RCV-01", [], CancellationToken.None));
    }

    private sealed class Token : IInternalServiceTokenProvider
    {
        public string BearerToken => "route-test-token";
    }

    private sealed class SourceResponse(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/business/v1/erp/purchase-receipts/RCV-01/source-document?organizationId=org-route&environmentId=env-route", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer route-test-token", request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
