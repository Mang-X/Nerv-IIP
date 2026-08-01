using System.Net;
using System.Text.Json;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialSupplyLocationResolverTests
{
    [Fact]
    public async Task Resolver_does_not_use_mes_work_order_lot_as_inventory_source_lot()
    {
        using var handler = new AvailabilityHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("lotNo=", StringComparison.Ordinal))
            {
                return AvailabilityResponse(0m);
            }

            return AvailabilityResponse(577m, "LOT-OPENING-RM-SPR-02");
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") };
        var resolver = new InventoryMesMaterialSupplyLocationResolver(
            new MesMaterialSupplyLocationOptions
            {
                SiteCode = "SITE-001",
                SourceLocationCodes = ["WH-WB-RM-01"],
                LineSideLocationCode = "WH-WB-LINE-01",
            },
            new MesInventoryHttpClient(httpClient));

        var result = await resolver.ResolveAsync(
            new MesMaterialSupplyLocationRequest(
                "org-001",
                "env-dev",
                "RM-SPR-02",
                "pcs",
                "LOT-RM-SPR-02-WO-2026-03395",
                577m),
            CancellationToken.None);

        Assert.Equal("WH-WB-RM-01", result.SourceLocationCode);
        var allocation = Assert.Single(result.SourceAllocations);
        Assert.Equal("LOT-OPENING-RM-SPR-02", allocation.SourceLotNo);
        var query = Assert.Single(handler.Requests).Query;
        Assert.DoesNotContain("lotNo=", query, StringComparison.Ordinal);
        Assert.Contains("organizationId=org-001", query, StringComparison.Ordinal);
        Assert.Contains("environmentId=env-dev", query, StringComparison.Ordinal);
        Assert.Contains("siteCode=SITE-001", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_allocates_deterministically_across_locations_when_no_single_location_is_enough()
    {
        using var handler = new AvailabilityHandler(request =>
        {
            var quantity = request.RequestUri!.Query.Contains("WH-WB-RM-01", StringComparison.Ordinal)
                ? 300m
                : 300m;
            return AvailabilityResponse(quantity);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") };
        var resolver = new InventoryMesMaterialSupplyLocationResolver(
            new MesMaterialSupplyLocationOptions
            {
                SiteCode = "SITE-001",
                SourceLocationCodes = ["WH-WB-SF-01", "WH-WB-RM-01"],
                LineSideLocationCode = "WH-WB-LINE-01",
            },
            new MesInventoryHttpClient(httpClient));

        var result = await resolver.ResolveAsync(
            new MesMaterialSupplyLocationRequest("org-001", "env-dev", "RM-SPR-02", "pcs", "LOT-WO", 500m),
            CancellationToken.None);

        Assert.Collection(
            result.SourceAllocations,
            first =>
            {
                Assert.Equal("WH-WB-RM-01", first.SourceLocationCode);
                Assert.Equal(300m, first.Quantity);
            },
            second =>
            {
                Assert.Equal("WH-WB-SF-01", second.SourceLocationCode);
                Assert.Equal(200m, second.Quantity);
            });
    }

    [Fact]
    public async Task Resolver_rejects_total_shortage_with_required_and_available_totals()
    {
        using var handler = new AvailabilityHandler(_ => AvailabilityResponse(300m));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") };
        var resolver = new InventoryMesMaterialSupplyLocationResolver(
            new MesMaterialSupplyLocationOptions
            {
                SiteCode = "SITE-001",
                SourceLocationCodes = ["WH-WB-RM-01", "WH-WB-SF-01"],
                LineSideLocationCode = "WH-WB-LINE-01",
            },
            new MesInventoryHttpClient(httpClient));

        var exception = await Assert.ThrowsAsync<KnownException>(() => resolver.ResolveAsync(
            new MesMaterialSupplyLocationRequest("org-001", "env-dev", "RM-SPR-02", "pcs", "LOT-WO", 650m),
            CancellationToken.None));

        Assert.Contains("需求650", exception.Message, StringComparison.Ordinal);
        Assert.Contains("合计可用600", exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage AvailabilityResponse(decimal quantity, string? lotNo = null)
    {
        object[] items = lotNo is null
            ? []
            : [new { lotNo, availableQuantity = quantity, movementAllowed = true }];
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                data = new
                {
                    availableQuantity = quantity,
                    items,
                },
                success = true,
                message = string.Empty,
                code = 200,
            }), System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class AvailabilityHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Handle(request));

        private HttpResponseMessage Handle(HttpRequestMessage request)
        {
            Requests.Add(request.RequestUri!);
            return responseFactory(request);
        }
    }
}
