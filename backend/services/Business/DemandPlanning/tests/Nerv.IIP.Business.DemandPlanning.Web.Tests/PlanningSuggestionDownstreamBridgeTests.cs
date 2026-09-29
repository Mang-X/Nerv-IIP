using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class PlanningSuggestionDownstreamBridgeTests
{
    [Theory]
    [InlineData("reschedule-in", "erp:purchase-order:PO-001:10", "/api/business/v1/erp/purchase-orders/PO-001/lines/10/reschedule", "promisedDate", "BusinessErp", "PurchaseOrderLine", "PO-001:10")]
    [InlineData("reschedule-out", "mes:work-order:WO-001", "/api/business/v1/mes/work-orders/WO-001/due-utc", "dueUtc", "BusinessMes", "WorkOrder", "WO-001")]
    [InlineData("cancel", "erp:purchase-order:PO-001:10", "/api/business/v1/erp/purchase-orders/PO-001/lines/10/cancel", "reason", "BusinessErp", "PurchaseOrderLine", "PO-001:10")]
    [InlineData("cancel", "mes:work-order:WO-001", "/api/business/v1/mes/work-orders/WO-001/cancel", "reason", "BusinessMes", "WorkOrder", "WO-001")]
    public async Task Scheduled_receipt_acceptance_writes_the_source_document_before_returning_its_reference(
        string type, string source, string expectedPath, string expectedField, string expectedService, string expectedType, string expectedId)
    {
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new MrpRunId(Guid.CreateVersion7()),
            type, "SKU-001", "pcs", "SITE-01", 2m, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29), "scheduled-receipt");
        suggestion.AddPeggingLink("scheduled-receipt", source, "SKU-001", null, 2m, null, null, null);
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(expectedPath, request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-internal-token"), request.Headers.Authorization);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var writeValue = document.RootElement.GetProperty(expectedField);
            if (expectedField == "promisedDate")
                Assert.Equal("2026-09-30", writeValue.GetString());
            else if (expectedField == "dueUtc")
                Assert.Equal(DateTimeOffset.Parse("2026-09-30T00:00:00Z"), writeValue.GetDateTimeOffset());
            else
                Assert.False(string.IsNullOrWhiteSpace(writeValue.GetString()));
            return JsonResponse("""{"success":true,"data":"accepted"}""");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://downstream.test") };
        var bridge = new HttpScheduledReceiptSuggestionDownstreamBridge(client, new TestHttpClientFactory(client), new TestInternalServiceTokenProvider());

        var reference = await bridge.CreateDownstreamAsync(suggestion,
            new PlanningSuggestionDownstreamRequest("ScheduledReceipt", "ScheduledReceipt", null, "accept-001"), CancellationToken.None);

        Assert.Equal(expectedService, reference.DownstreamService);
        Assert.Equal(expectedType, reference.DownstreamDocumentType);
        Assert.Equal(expectedId, reference.DownstreamDocumentId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Failed_scheduled_receipt_write_does_not_return_an_accepted_reference()
    {
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new MrpRunId(Guid.CreateVersion7()),
            "cancel", "SKU-001", "pcs", "SITE-01", 2m, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29), "scheduled-receipt");
        suggestion.AddPeggingLink("scheduled-receipt", "mes:work-order:WO-001", "SKU-001", null, 2m, null, null, null);
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://downstream.test") };
        var bridge = new HttpScheduledReceiptSuggestionDownstreamBridge(client, new TestHttpClientFactory(client), new TestInternalServiceTokenProvider());

        await Assert.ThrowsAsync<KnownException>(() => bridge.CreateDownstreamAsync(suggestion,
            new PlanningSuggestionDownstreamRequest("ScheduledReceipt", "ScheduledReceipt", null, "accept-001"), CancellationToken.None));
    }

    [Fact]
    public async Task Business_failure_envelope_does_not_accept_a_scheduled_receipt()
    {
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new MrpRunId(Guid.CreateVersion7()),
            "cancel", "SKU-001", "pcs", "SITE-01", 2m, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29), "scheduled-receipt");
        suggestion.AddPeggingLink("scheduled-receipt", "mes:work-order:WO-001", "SKU-001", null, 2m, null, null, null);
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse("""{"success":false,"message":"cannot cancel"}""")));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://downstream.test") };
        var bridge = new HttpScheduledReceiptSuggestionDownstreamBridge(client, new TestHttpClientFactory(client), new TestInternalServiceTokenProvider());

        await Assert.ThrowsAsync<KnownException>(() => bridge.CreateDownstreamAsync(suggestion,
            new PlanningSuggestionDownstreamRequest("ScheduledReceipt", "ScheduledReceipt", null, "accept-001"), CancellationToken.None));
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

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
            Assert.Equal(JsonValueKind.Null, root.GetProperty("workCenterId").ValueKind);
            Assert.Equal("DemandPlanning", root.GetProperty("sourceSystem").GetString());
            Assert.Equal("PlanningSuggestion", root.GetProperty("sourceDocumentType").GetString());
            Assert.Equal(suggestion.Id.ToString(), root.GetProperty("sourceDocumentId").GetString());
            Assert.Equal("DEMAND-001", root.GetProperty("sourceDemandReference").GetString());
            Assert.Equal(
                ["DEMAND-001"],
                root.GetProperty("sourceDemandReferences").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray());
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
        var bridge = new HttpMesPlanningSuggestionDownstreamBridge(
            httpClient,
            NullLogger<HttpMesPlanningSuggestionDownstreamBridge>.Instance,
            new TestInternalServiceTokenProvider());

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
    public async Task Http_mes_bridge_carries_every_demand_reference_of_a_batched_suggestion_and_excludes_scheduled_receipts()
    {
        // #1286：合批建议 peg 到多张销售订单；桥接必须完整携带 demand 类型需求源引用（去重），
        // 排除 scheduled-receipt 引用，主引用取第一个 demand 引用。
        var suggestion = NewWorkOrderSuggestion();
        suggestion.AddPeggingLink("demand", "SO-20260730-000005", "SKU-FG-1000", null, 120m, "PV-001", "MBOM-001", "ROUTING-001");
        suggestion.AddPeggingLink("demand", "DEMAND-001", "SKU-FG-1000", null, 5m, "PV-001", "MBOM-001", "ROUTING-001");
        suggestion.AddPeggingLink("scheduled-receipt", "erp:purchase-order:PO-0001", "SKU-FG-1000", null, 10m, null, null, null);
        var handler = new StubHttpMessageHandler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("DEMAND-001", root.GetProperty("sourceDemandReference").GetString());
            Assert.Equal(
                ["DEMAND-001", "SO-20260730-000005"],
                root.GetProperty("sourceDemandReferences").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray());

            return JsonResponse("""
                {
                  "status": "accepted",
                  "referenceId": "WO-002",
                  "acceptedAtUtc": "2026-07-30T00:00:00Z"
                }
                """);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var bridge = new HttpMesPlanningSuggestionDownstreamBridge(
            httpClient,
            NullLogger<HttpMesPlanningSuggestionDownstreamBridge>.Instance,
            new TestInternalServiceTokenProvider());

        var reference = await bridge.CreateDownstreamAsync(
            suggestion,
            new PlanningSuggestionDownstreamRequest("BusinessMes", "WorkOrder", null, "idem-002"),
            CancellationToken.None);

        Assert.Equal("WO-002", reference.DownstreamDocumentId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_mes_bridge_uses_safe_chinese_message_without_downstream_diagnostic()
    {
        var suggestion = NewWorkOrderSuggestion();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            ReasonPhrase = "Conflict",
            Content = new StringContent("""{"message":"production work order already exists"}""")
        });
        var logger = new ListLogger<HttpMesPlanningSuggestionDownstreamBridge>();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var bridge = new HttpMesPlanningSuggestionDownstreamBridge(
            httpClient,
            logger,
            new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            bridge.CreateDownstreamAsync(
                suggestion,
                new PlanningSuggestionDownstreamRequest("BusinessMes", "WorkOrder", null, "idem-001"),
                CancellationToken.None));

        Assert.Equal("MES 下游创建工单失败，请稍后重试。", exception.Message);
        Assert.DoesNotContain("HTTP 409", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("production work order already exists", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("HTTP 409 Conflict", StringComparison.Ordinal)
                && entry.Message.Contains("production work order already exists", StringComparison.Ordinal));
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
        var bridge = new HttpErpPlanningSuggestionDownstreamBridge(
            httpClient,
            NullLogger<HttpErpPlanningSuggestionDownstreamBridge>.Instance,
            new TestInternalServiceTokenProvider());

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
    public async Task Http_erp_bridge_uses_safe_chinese_message_without_downstream_diagnostic()
    {
        var suggestion = NewPurchaseSuggestion();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            ReasonPhrase = "Conflict",
            Content = new StringContent("""{"message":"purchase source is blocked"}""")
        });
        var logger = new ListLogger<HttpErpPlanningSuggestionDownstreamBridge>();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var bridge = new HttpErpPlanningSuggestionDownstreamBridge(
            httpClient,
            logger,
            new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            bridge.CreateDownstreamAsync(
                suggestion,
                new PlanningSuggestionDownstreamRequest("BusinessErp", "PurchaseRequisition", null, "idem-erp-001"),
                CancellationToken.None));

        Assert.Equal("ERP 下游创建采购申请失败，请稍后重试。", exception.Message);
        Assert.DoesNotContain("HTTP 409", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("purchase source is blocked", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("HTTP 409 Conflict", StringComparison.Ordinal)
                && entry.Message.Contains("purchase source is blocked", StringComparison.Ordinal));
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

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
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
