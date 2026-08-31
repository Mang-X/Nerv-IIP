using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessBarcodeLabelLifecycleClientTests
{
    [Fact]
    public async Task Dispatch_uses_scoped_internal_route_query_and_unchanged_body()
    {
        var handler = new RecordingResponseHandler("""{"success":true,"data":{"printBatchId":"batch-001"},"message":"","code":0}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeLabelClient(httpClient);

        var response = await client.DispatchPrintBatchAsync(
            "internal-token",
            new BusinessConsoleDispatchBarcodePrintBatchRequest(
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
