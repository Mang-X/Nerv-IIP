using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessBarcodeLabelLifecycleClientTests
{
    [Theory]
    [InlineData("dispatch", 0, "printer-01", null, "/api/business/v1/barcodes/print-batches/batch%20%2F%201/dispatch", "printBatchId", "printerId")]
    [InlineData("reprint", 7, "printer-02", null, "/api/business/v1/barcodes/print-batches/batch%20%2F%201/items/7/reprint", "printBatchId", "sequenceNo", "printerId")]
    [InlineData("void", 7, null, "标签损坏", "/api/business/v1/barcodes/print-batches/batch%20%2F%201/items/7/void", "printBatchId", "sequenceNo", "reason")]
    public async Task Lifecycle_client_posts_the_current_downstream_path_and_exact_body(
        string action,
        int sequenceNo,
        string? printerId,
        string? reason,
        string expectedPath,
        params string[] expectedBodyProperties)
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = await CloneAsync(request);
            return action == "reprint"
                ? JsonResponse("""{"data":{"printBatchId":"batch / 1","status":"printed","printJobId":"job-001","failureReason":null}}""")
                : JsonResponse("""{"data":{"printBatchId":"batch / 1"}}""");
        });
        var client = new HttpBusinessBarcodeLabelClient(new HttpClient(handler) { BaseAddress = new Uri("http://barcode") });

        switch (action)
        {
            case "dispatch":
                await client.DispatchPrintBatchAsync("internal-token", new("batch / 1", "org-001", "env-dev", printerId!), CancellationToken.None);
                break;
            case "reprint":
                await client.ReprintLabelAsync("internal-token", new("batch / 1", sequenceNo, "org-001", "env-dev", printerId!), CancellationToken.None);
                break;
            default:
                await client.VoidLabelAsync("internal-token", new("batch / 1", sequenceNo, "org-001", "env-dev", reason!), CancellationToken.None);
                break;
        }

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(expectedPath, captured.RequestUri!.AbsolutePath);
        Assert.Equal("?organizationId=org-001&environmentId=env-dev", captured.RequestUri.Query);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", captured.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync());
        Assert.Equal(expectedBodyProperties.Order(), body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("batch / 1", body.RootElement.GetProperty("printBatchId").GetString());
        if (sequenceNo > 0)
        {
            Assert.Equal(sequenceNo, body.RootElement.GetProperty("sequenceNo").GetInt32());
        }

        if (printerId is not null)
        {
            Assert.Equal(printerId, body.RootElement.GetProperty("printerId").GetString());
        }

        if (reason is not null)
        {
            Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            clone.Content = new StringContent(await request.Content.ReadAsStringAsync(), Encoding.UTF8, "application/json");
        }

        return clone;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
