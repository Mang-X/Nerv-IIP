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

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class PurchaseReceiptPostingRouteTests
{
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
        public Task<PurchaseReceiptInventoryPostingRoute?> GetAsync(string organizationId, string environmentId, string receiptNo, CancellationToken cancellationToken) =>
            throw new HttpRequestException("ERP unavailable");
    }

    [Theory]
    [InlineData("{\"success\":true,\"data\":{\"inventoryPostingRoute\":\"wms\"}}", PurchaseReceiptInventoryPostingRoute.Wms)]
    [InlineData("{\"success\":true,\"data\":{\"inventoryPostingRoute\":\"direct\"}}", PurchaseReceiptInventoryPostingRoute.Direct)]
    [InlineData("{\"success\":true,\"data\":null}", null)]
    public async Task Source_http_reads_scoped_frozen_route(string body, PurchaseReceiptInventoryPostingRoute? expected)
    {
        using var client = new HttpClient(new SourceResponse(body)) { BaseAddress = new Uri("http://erp.test") };
        var result = await new HttpWmsPurchaseReceiptPostingRouteClient(client, new Token())
            .GetAsync("org-route", "env-route", "RCV-01", CancellationToken.None);
        Assert.Equal(expected, result);
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
            .GetAsync("org-route", "env-route", "RCV-01", CancellationToken.None));
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
