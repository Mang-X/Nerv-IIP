using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessBarcodeLabelLifecycleClientTests
{
    [Fact]
    public async Task Detail_by_idempotency_key_uses_the_exact_key_and_authorized_scope()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatch":{"printBatchId":"batch-001","labelTemplateId":"template-001","sourceDocumentType":"work-order","sourceDocumentId":"WO-001","idempotencyKey":"intent:Case/A","reportIntentKey":"intent:Case/A","reportIntentFingerprint":"opaque:fingerprint-a","requestedQuantity":1,"status":"reserved","printerId":null,"printJobId":null,"failureReason":null,"productionReportId":null,"productionReportNo":null,"items":[]}},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.GetPrintBatchByIdempotencyKeyAsync(
            "internal-token",
            new BusinessConsoleBarcodePrintBatchByIdempotencyKeyRequest(
                "org-001",
                "env-dev",
                "intent:Case/A"),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("batch-001", response.PrintBatch.PrintBatchId);
        Assert.Equal("opaque:fingerprint-a", response.PrintBatch.ReportIntentFingerprint);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/v2/barcodes/print-batches/by-idempotency-key?organizationId=org-001&environmentId=env-dev&idempotencyKey=intent%3ACase%2FA",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Detail_by_idempotency_key_returns_no_batch_for_the_scoped_not_found_result()
    {
        var handler = new RecordingResponseHandler(
            """{"success":false,"data":null,"message":"未找到打印批次。","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.GetPrintBatchByIdempotencyKeyAsync(
            "internal-token",
            new BusinessConsoleBarcodePrintBatchByIdempotencyKeyRequest(
                "org-001",
                "env-dev",
                "intent-unknown"),
            CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task Detail_uses_the_authorized_scope_on_the_downstream_v2_route()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatch":{"printBatchId":"batch-001","labelTemplateId":"template-001","sourceDocumentType":"work-order","sourceDocumentId":"WO-001","idempotencyKey":"intent-001","reportIntentFingerprint":null,"requestedQuantity":1,"status":"reserved","items":[]}},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        _ = await client.GetPrintBatchAsync(
            "internal-token",
            new BusinessConsoleBarcodePrintBatchRequest("org-001", "env-dev", "batch-001"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/v2/barcodes/print-batches/batch-001?organizationId=org-001&environmentId=env-dev",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Detail_reads_the_report_intent_fingerprint_from_the_downstream_v2_response()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatch":{"printBatchId":"batch-001","labelTemplateId":"template-001","sourceDocumentType":"work-order","sourceDocumentId":"WO-001","idempotencyKey":"intent-001","reportIntentKey":"intent-001","reportIntentFingerprint":"  opaque:Report-Intent/A  ","requestedQuantity":1,"status":"reserved","printerId":null,"printJobId":null,"failureReason":null,"productionReportId":null,"productionReportNo":null,"items":[]}},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.GetPrintBatchAsync(
            "internal-token",
            new BusinessConsoleBarcodePrintBatchRequest("org-001", "env-dev", "batch-001"),
            CancellationToken.None);

        Assert.Equal("  opaque:Report-Intent/A  ", response.PrintBatch.ReportIntentFingerprint);
    }

    [Fact]
    public async Task Create_forwards_the_optional_report_intent_fingerprint_unchanged()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001"},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        _ = await client.CreatePrintBatchAsync(
            "internal-token",
            new BusinessConsoleCreateBarcodePrintBatchRequest(
                "org-001",
                "env-dev",
                "rule-001",
                "template-001",
                "work-order",
                "WO-001",
                "intent-001",
                "{}",
                1,
                "  opaque:Report-Intent/A  "),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/business/v1/barcodes/print-batches", handler.LastRequest.RequestUri!.PathAndQuery);
        using var body = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal(
            "  opaque:Report-Intent/A  ",
            body.RootElement.GetProperty("reportIntentFingerprint").GetString());
    }

    [Fact]
    public async Task Activate_uses_scoped_internal_route_and_mes_report_identity()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001"},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.ActivatePrintBatchAsync(
            "internal-token",
            new BusinessConsoleActivateBarcodePrintBatchRequest(
                "batch-001",
                "org-001",
                "env-dev",
                new BusinessConsoleActivateBarcodePrintBatchBody("report-001", "PR-001")),
            CancellationToken.None);

        Assert.Equal("batch-001", response.PrintBatchId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/internal/v1/barcodes/print-batches/batch-001/activate?organizationId=org-001&environmentId=env-dev",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization!.Parameter);
        using var body = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        Assert.Equal("report-001", body.RootElement.GetProperty("productionReportId").GetString());
        Assert.Equal("PR-001", body.RootElement.GetProperty("productionReportNo").GetString());
    }

    [Fact]
    public async Task Dispatch_uses_scoped_internal_route_query_and_unchanged_body()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001"},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.DispatchPrintBatchAsync(
            "internal-token",
            new BusinessConsoleDispatchBarcodePrintBatchRequest(
                "batch-001",
                "org-001",
                "env-dev",
                new BusinessConsoleDispatchBarcodePrintBatchBody("batch-001", "printer-01")),
            CancellationToken.None);

        Assert.Equal("batch-001", response.PrintBatchId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/internal/v1/barcodes/print-batches/batch-001/dispatch?organizationId=org-001&environmentId=env-dev",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        Assert.Equal("batch-001", body.RootElement.GetProperty("printBatchId").GetString());
        Assert.Equal("printer-01", body.RootElement.GetProperty("printerId").GetString());
        Assert.False(body.RootElement.TryGetProperty("organizationId", out _));
        Assert.False(body.RootElement.TryGetProperty("environmentId", out _));
    }

    [Fact]
    public async Task Reprint_uses_scoped_internal_route_query_and_unchanged_body()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001","status":"reprinted","printJobId":"job-001","failureReason":null},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.ReprintLabelAsync(
            "internal-token",
            new BusinessConsoleReprintBarcodeLabelRequest(
                "batch-001",
                7,
                "org-001",
                "env-dev",
                new BusinessConsoleReprintBarcodeLabelBody("batch-001", 7, "printer-01")),
            CancellationToken.None);

        Assert.Equal("reprinted", response.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/internal/v1/barcodes/print-batches/batch-001/items/7/reprint?organizationId=org-001&environmentId=env-dev",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.Equal("batch-001", body.RootElement.GetProperty("printBatchId").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("sequenceNo").GetInt32());
        Assert.Equal("printer-01", body.RootElement.GetProperty("printerId").GetString());
        Assert.False(body.RootElement.TryGetProperty("organizationId", out _));
        Assert.False(body.RootElement.TryGetProperty("environmentId", out _));
    }

    [Fact]
    public async Task Void_uses_scoped_internal_route_query_and_unchanged_body()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001"},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.VoidLabelAsync(
            "internal-token",
            new BusinessConsoleVoidBarcodeLabelRequest(
                "batch-001",
                7,
                "org-001",
                "env-dev",
                new BusinessConsoleVoidBarcodeLabelBody("batch-001", 7, "damaged")),
            CancellationToken.None);

        Assert.Equal("batch-001", response.PrintBatchId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "/api/business/internal/v1/barcodes/print-batches/batch-001/items/7/void?organizationId=org-001&environmentId=env-dev",
            handler.LastRequest.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.Equal("batch-001", body.RootElement.GetProperty("printBatchId").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("sequenceNo").GetInt32());
        Assert.Equal("damaged", body.RootElement.GetProperty("reason").GetString());
        Assert.False(body.RootElement.TryGetProperty("organizationId", out _));
        Assert.False(body.RootElement.TryGetProperty("environmentId", out _));
    }

    private sealed class RecordingResponseHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
