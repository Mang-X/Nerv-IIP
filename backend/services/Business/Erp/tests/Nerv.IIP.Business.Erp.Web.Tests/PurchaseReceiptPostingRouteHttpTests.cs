using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class PurchaseReceiptPostingRouteHttpTests
{
    [Fact]
    public async Task OpenApi_publishes_two_supported_route_values()
    {
        await using var factory = CreateFactory(new CapturingSender());
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var route = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith(nameof(PurchaseReceiptInventoryPostingRoute), StringComparison.Ordinal)).Value;
        Assert.Equal("string", route.GetProperty("type").GetString());
        Assert.Equal(new[] { "direct", "wms" }, route.GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData(null, PurchaseReceiptInventoryPostingRoute.Direct, "direct")]
    [InlineData("direct", PurchaseReceiptInventoryPostingRoute.Direct, "direct")]
    [InlineData("wms", PurchaseReceiptInventoryPostingRoute.Wms, "wms")]
    public async Task Http_binds_frozen_route_and_serializes_source_document(string? route, PurchaseReceiptInventoryPostingRoute expected, string wireValue)
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-receipt-token");
        var payload = Payload();
        if (route is not null) payload["inventoryPostingRoute"] = route;
        using var response = await client.PostAsJsonAsync("/api/business/v1/erp/purchase-receipts", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, Assert.Single(sender.Commands).InventoryPostingRoute);
        using var source = await client.GetAsync("/api/business/v1/erp/purchase-receipts/RCV-HTTP/source-document?organizationId=org-route&environmentId=env-route");
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        using var json = JsonDocument.Parse(await source.Content.ReadAsStringAsync());
        Assert.Equal(wireValue, json.RootElement.GetProperty("data").GetProperty("inventoryPostingRoute").GetString());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(1)]
    [InlineData(null)]
    public async Task Http_rejects_unsupported_route_before_command(object? route)
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-receipt-token");
        var payload = Payload();
        payload["inventoryPostingRoute"] = route;
        using var response = await client.PostAsJsonAsync("/api/business/v1/erp/purchase-receipts", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(sender.Commands);
    }

    private static Dictionary<string, object?> Payload() => new()
    {
        ["organizationId"] = "org-route", ["environmentId"] = "env-route",
        ["purchaseReceiptNo"] = "RCV-HTTP", ["purchaseOrderNo"] = "PO-HTTP",
        ["lines"] = new[] { new { purchaseOrderLineNo = "10", receivedQuantity = 2m, qualityStatus = "unrestricted" } },
    };

    private static WebApplicationFactory<Program> CreateFactory(CapturingSender sender) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                ["InternalService:BearerToken"] = "test-receipt-token",
                ["Persistence:AutoMigrate"] = "false",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });

    private sealed class CapturingSender : ISender
    {
        public List<RecordPurchaseReceiptCommand> Commands { get; } = [];
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response;
            if (request is RecordPurchaseReceiptCommand command)
            {
                Commands.Add(command);
                response = new PurchaseReceiptId(Guid.Parse("00000000-0000-0000-0000-000000002120"));
            }
            else if (request is GetPurchaseReceiptSourceDocumentQuery query)
                response = new PurchaseReceiptSourceDocumentResponse(query.PurchaseReceiptNo, "recorded", [], Commands.Single().InventoryPostingRoute);
            else throw new NotSupportedException();
            return Task.FromResult((TResponse)response);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
