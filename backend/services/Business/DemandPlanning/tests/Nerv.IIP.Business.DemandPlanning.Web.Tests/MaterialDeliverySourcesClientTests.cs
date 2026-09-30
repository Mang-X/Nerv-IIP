using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// PublicContract: 既有 ERP Released PO/PR 读面与 #4094 Sources 契约；这里只验证 HTTP 映射，不声称真实跨服务执行。
public sealed class MaterialDeliverySourcesClientTests
{
    [Fact]
    public async Task Procurement_reads_all_pages_and_retains_PR_to_suggestion_line_sources()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("test-token", request.Headers.Authorization!.Parameter);
            var path = request.RequestUri!.PathAndQuery;
            requests.Add(path);
            if (path.Contains("purchase-requisitions")) return Envelope(new { total = 1, items = new[] { new { requisitionNo = "PR", suggestionId = "S" } } });
            Assert.Contains("status=Released", path);
            var page = path.Contains("skip=0&") ? 0 : 1;
            var items = Enumerable.Range(0, page == 0 ? 100 : 1).Select(i => new
            {
                purchaseOrderNo = $"PO-{page}-{i}", siteCode = "SITE", lines = new object[] {
                    new { lineNo = "10", skuCode = "SKU", uomCode = "EA", openQuantity = 3m, promisedDate = "2026-10-09",
                        sources = new[] { new { purchaseRequisitionNo = "PR", purchaseRequisitionLineNo = "1", quantity = 5m } } },
                    new { lineNo = "20", skuCode = "SKU", uomCode = "EA", openQuantity = 0m, promisedDate = "2026-10-09",
                        sources = Array.Empty<object>() } }
            }).ToArray();
            return Envelope(new { total = 101, items });
        })) { BaseAddress = new("http://erp.test") };
        var client = new HttpMaterialDeliverySourcesClient(new Factory(http), new Token());
        var rows = await client.GetSupplyAsync("org", "env", default);
        Assert.Equal(101, rows.Count);
        Assert.Equal(3, requests.Count);
        Assert.Contains(requests, x => x.Contains("skip=100"));
        var source = Assert.Single(rows.First().Sources);
        Assert.Equal("S", source.SuggestionId);
        Assert.Equal("PR", source.PurchaseRequisitionNo);
        Assert.Equal("1", source.PurchaseRequisitionLineNo);
        Assert.Equal(5, source.Quantity);
        Assert.Equal(3, rows.First().OpenQuantity);
        Assert.Equal(new DateOnly(2026, 10, 9), rows.First().PromisedDate);
    }

    [Fact]
    public async Task Scheduling_posts_only_explicit_plan_and_preserves_owner_bounds()
    {
        Task<string>? bodyTask = null;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("plans/selected/material-delivery-sources", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-token", request.Headers.Authorization!.Parameter);
            bodyTask = request.Content!.ReadAsStringAsync();
            return Envelope(new MaterialDeliverySourcesResponse("selected", []));
        })) { BaseAddress = new("http://scheduling.test") };
        var client = new HttpMaterialDeliverySourcesClient(new Factory(http), new Token());
        var response = await client.GetSchedulingAsync("org", "env", "selected",
            [new("S", "WO", [new("D", DateTimeOffset.UnixEpoch)])], default);
        Assert.Equal("selected", response.PlanId);
        using var payload = JsonDocument.Parse(await bodyTask!);
        Assert.Equal("WO", payload.RootElement.GetProperty("sources")[0].GetProperty("workOrderId").GetString());
    }

    [Fact]
    public async Task Failed_owner_envelope_is_not_a_successful_empty_supply()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = false }) }))
        { BaseAddress = new("http://erp.test") };
        var client = new HttpMaterialDeliverySourcesClient(new Factory(http), new Token());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetSupplyAsync("org", "env", default));
    }

    private static HttpResponseMessage Envelope<T>(T data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Token : IInternalServiceTokenProvider { public string BearerToken => "test-token"; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
