using System.Net;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class MaterialDeliveryMesSourceProviderTests
{
    [Fact]
    public async Task Public_mes_facts_page_all_signed_reports_and_preserve_exact_identity_tenant_and_start()
    {
        var requested = new List<string>();
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        using var client = new HttpClient(new FakeHttp(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("token", request.Headers.Authorization.Parameter);
            var path = request.RequestUri!.PathAndQuery;
            requested.Add(path);
            Assert.Contains("organizationId=org", path);
            Assert.Contains("environmentId=env", path);
            object data;
            if (path.Contains("production-reports"))
            {
                Assert.Contains("workOrderId=wo", path);
                data = new { total = 101, items = new[] { new { operationTaskId = "op", goodQuantity = path.Contains("skip=100") ? -1m : 5m } } };
            }
            else if (path.StartsWith("/api/business/v1/mes/work-orders/wo?", StringComparison.Ordinal))
                data = new { sourcePlanReference = new { sourceSystem = "DemandPlanning", sourceDocumentType = "PlanningSuggestion", sourceDocumentId = "s" },
                    operationTasks = new[] { new { operationTaskId = "op", startedAtUtc = start } } };
            else
            {
                Assert.Contains("workOrderId=wo", path);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        total = 1,
                        items = new[] { new { workOrderId = "wo", productionVersionId = "pv", quantity = 10m,
                            operationTasks = new[] { new { operationTaskId = "op", operationSequence = 2, status = "started", earliestStartUtc = start.AddHours(-1) } } } }
                    }))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data })) };
        })) { BaseAddress = new Uri("http://mes") };
        var source = await new HttpMaterialDeliveryMesSourceProvider(client, new Token()).GetAsync("org", "env", "wo", default);
        Assert.Equal("wo", source!.WorkOrderId);
        Assert.Equal("pv", source.ProductionVersionId);
        Assert.Equal(10m, source.Quantity);
        Assert.Equal("s", source!.SuggestionId);
        var operation = Assert.Single(source.Operations);
        Assert.Equal(4m, operation.NetGoodQuantity);
        Assert.Equal(start, operation.StartedAtUtc);
        Assert.Equal("op", operation.OperationId);
        Assert.Equal(4, requested.Count);
    }

    [Fact]
    public async Task Empty_mes_work_order_list_returns_not_found_without_fetching_detail()
    {
        var requested = new List<string>();
        using var client = new HttpClient(new FakeHttp(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            requested.Add(path);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { total = 0, items = Array.Empty<object>() }))
            };
        })) { BaseAddress = new Uri("http://mes") };

        var source = await new HttpMaterialDeliveryMesSourceProvider(client, new Token()).GetAsync("org", "env", "wo", default);

        Assert.Null(source);
        Assert.Single(requested);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handler(request));
    }
    private sealed class Token : IInternalServiceTokenProvider { public string BearerToken => "token"; }
}
