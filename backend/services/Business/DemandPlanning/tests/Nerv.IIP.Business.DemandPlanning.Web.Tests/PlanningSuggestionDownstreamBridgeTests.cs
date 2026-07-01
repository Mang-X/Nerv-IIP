using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class PlanningSuggestionDownstreamBridgeTests
{
    [Fact]
    public async Task Http_mes_bridge_posts_expected_work_order_contract_and_returns_reference()
    {
        var suggestion = NewWorkOrderSuggestion();
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/business/v1/mes/production-plans/" + suggestion.Id + "/work-orders", request.RequestUri?.PathAndQuery);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-internal-token"), request.Headers.Authorization);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
            Assert.Equal("env-dev", root.GetProperty("environmentId").GetString());
            Assert.Equal(suggestion.Id.ToString(), root.GetProperty("productionPlanId").GetString());
            Assert.Equal("SKU-FG-1000", root.GetProperty("skuId").GetString());
            Assert.Equal("PV-001", root.GetProperty("productionVersionId").GetString());
            Assert.Equal("DemandPlanning", root.GetProperty("sourceSystem").GetString());
            Assert.Equal("PlanningSuggestion", root.GetProperty("sourceDocumentType").GetString());
            Assert.Equal(suggestion.Id.ToString(), root.GetProperty("sourceDocumentId").GetString());
            Assert.Equal("DEMAND-001", root.GetProperty("sourceDemandReference").GetString());
            Assert.Equal("idem-001", root.GetProperty("idempotencyKey").GetString());

            return JsonResponse("""
                {
                  "status": "accepted",
                  "referenceId": "WO-001",
                  "acceptedAtUtc": "2026-06-24T00:00:00Z"
                }
                """);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var bridge = new HttpMesPlanningSuggestionDownstreamBridge(httpClient, new TestInternalServiceTokenProvider());

        var reference = await bridge.CreateDownstreamAsync(
            suggestion,
            new PlanningSuggestionDownstreamRequest("BusinessMes", "WorkOrder", null, "idem-001"),
            CancellationToken.None);

        Assert.Equal("BusinessMes", reference.DownstreamService);
        Assert.Equal("WorkOrder", reference.DownstreamDocumentType);
        Assert.Equal("WO-001", reference.DownstreamDocumentId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_mes_bridge_wraps_non_success_response_as_known_exception_with_diagnostic()
    {
        var suggestion = NewWorkOrderSuggestion();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            ReasonPhrase = "Conflict",
            Content = new StringContent("""{"message":"production work order already exists"}""")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var bridge = new HttpMesPlanningSuggestionDownstreamBridge(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            bridge.CreateDownstreamAsync(
                suggestion,
                new PlanningSuggestionDownstreamRequest("BusinessMes", "WorkOrder", null, "idem-001"),
                CancellationToken.None));

        Assert.Contains("HTTP 409 Conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production work order already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_erp_bridge_posts_expected_purchase_requisition_contract_and_returns_reference()
    {
        var suggestion = NewPurchaseSuggestion();
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/business/v1/erp/purchase-requisitions/from-suggestion", request.RequestUri?.PathAndQuery);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-internal-token"), request.Headers.Authorization);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("org-001", root.GetProperty("organizationId").GetString());
            Assert.Equal("env-dev", root.GetProperty("environmentId").GetString());
            Assert.Equal(suggestion.Id.ToString(), root.GetProperty("suggestionId").GetString());
            Assert.Equal("SKU-RM-1000", root.GetProperty("skuCode").GetString());
            Assert.Equal("kg", root.GetProperty("uomCode").GetString());
            Assert.Equal("SITE-01", root.GetProperty("siteCode").GetString());
            Assert.Equal(12.5m, root.GetProperty("quantity").GetDecimal());
            Assert.Equal("2026-06-03", root.GetProperty("requiredDate").GetString());
            Assert.Equal("idem-erp-001", root.GetProperty("idempotencyKey").GetString());

            return JsonResponse("""
                {
                  "data": {
                    "purchaseRequisitionId": "01978d7a-6a44-7c96-a921-45215a8aaab3",
                    "requisitionNo": "PR-20260603-001"
                  }
                }
                """);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var bridge = new HttpErpPlanningSuggestionDownstreamBridge(httpClient, new TestInternalServiceTokenProvider());

        var reference = await bridge.CreateDownstreamAsync(
            suggestion,
            new PlanningSuggestionDownstreamRequest("BusinessErp", "PurchaseRequisition", null, "idem-erp-001"),
            CancellationToken.None);

        Assert.Equal("BusinessErp", reference.DownstreamService);
        Assert.Equal("PurchaseRequisition", reference.DownstreamDocumentType);
        Assert.Equal("PR-20260603-001", reference.DownstreamDocumentId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_erp_bridge_wraps_non_success_response_as_known_exception_with_diagnostic()
    {
        var suggestion = NewPurchaseSuggestion();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            ReasonPhrase = "Conflict",
            Content = new StringContent("""{"message":"purchase source is blocked"}""")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var bridge = new HttpErpPlanningSuggestionDownstreamBridge(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            bridge.CreateDownstreamAsync(
                suggestion,
                new PlanningSuggestionDownstreamRequest("BusinessErp", "PurchaseRequisition", null, "idem-erp-001"),
                CancellationToken.None));

        Assert.Contains("HTTP 409 Conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("purchase source is blocked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanningSuggestion NewWorkOrderSuggestion()
    {
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new MrpRunId(Guid.CreateVersion7()),
            "planned-work-order",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            10m,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 5, 27),
            "MRP-001");
        SetSuggestionId(suggestion);
        suggestion.AddPeggingLink("demand", "DEMAND-001", "SKU-FG-1000", null, 10m, "PV-001", "MBOM-001", "ROUTING-001");
        return suggestion;
    }

    private static PlanningSuggestion NewPurchaseSuggestion()
    {
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new MrpRunId(Guid.CreateVersion7()),
            "planned-purchase",
            "SKU-RM-1000",
            "kg",
            "SITE-01",
            12.5m,
            new DateOnly(2026, 6, 3),
            new DateOnly(2026, 5, 28),
            "MRP-001");
        SetSuggestionId(suggestion);
        suggestion.AddPeggingLink("demand", "DEMAND-002", "SKU-FG-1000", null, 12.5m, null, "MBOM-001", null);
        return suggestion;
    }

    private static void SetSuggestionId(PlanningSuggestion suggestion)
    {
        var idProperty = typeof(PlanningSuggestion).GetProperty(nameof(PlanningSuggestion.Id))
            ?? throw new InvalidOperationException("PlanningSuggestion.Id property was not found.");
        idProperty.SetValue(suggestion, new PlanningSuggestionId(Guid.CreateVersion7()));
    }

    private sealed class TestInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this(request => Task.FromResult(send(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await send(request);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }
}
