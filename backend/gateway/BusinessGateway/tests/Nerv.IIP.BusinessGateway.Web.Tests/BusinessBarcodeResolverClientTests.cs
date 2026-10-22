using System.Net;
using System.Text;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessBarcodeResolverClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_accepts_raw_and_success_enveloped_contracts(bool enveloped)
    {
        const string raw = """{"status":"resolved","reasonCode":null,"candidates":[{"sourceDocumentType":"work-order","sourceDocumentId":"WO-001","authority":"barcode-label","observedAtUtc":"2026-06-03T01:00:00Z"}],"total":1}""";
        var body = enveloped ? $$"""{"success":true,"data":{{raw}},"message":"","code":0}""" : raw;
        var handler = new StaticResponseHandler(body);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeResolverClient(httpClient);

        var response = await client.ResolveAsync(
            "internal-token",
            new BusinessBarcodeResolveRequest("org-001", "env-dev", "WO001", 20, 10),
            CancellationToken.None);

        Assert.Equal("resolved", response.Status);
        Assert.Equal(1, response.Total);
        Assert.Single(response.Candidates);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/business/v1/barcodes/resolve", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("\"skip\":20", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"take\":10", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"status\":\"\",\"reasonCode\":null,\"candidates\":[],\"total\":0}")]
    [InlineData("{\"status\":\"resolved\",\"reasonCode\":null,\"candidates\":null,\"total\":1}")]
    [InlineData("{\"status\":\"resolved\",\"reasonCode\":null,\"candidates\":[{\"sourceDocumentType\":\"\",\"sourceDocumentId\":\"WO-001\",\"authority\":\"barcode-label\",\"observedAtUtc\":\"2026-06-03T01:00:00Z\"}],\"total\":1}")]
    public async Task Resolve_rejects_malformed_authoritative_contracts(string body)
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(body)) { BaseAddress = new Uri("https://barcode-label.test") };
        var client = new HttpBusinessBarcodeResolverClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ResolveAsync(
            "internal-token",
            new BusinessBarcodeResolveRequest("org-001", "env-dev", "WO001", 0, 20),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    [Fact]
    public async Task Resolve_fails_closed_on_a_success_false_envelope()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler("""{"success":false,"message":"declined","code":409}"""))
        {
            BaseAddress = new Uri("https://barcode-label.test"),
        };
        var client = new HttpBusinessBarcodeResolverClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ResolveAsync(
            "internal-token",
            new BusinessBarcodeResolveRequest("org-001", "env-dev", "WO001", 0, 20),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
