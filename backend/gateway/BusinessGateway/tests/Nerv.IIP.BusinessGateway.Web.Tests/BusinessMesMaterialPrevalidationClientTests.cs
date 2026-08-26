using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesMaterialPrevalidationClientTests
{
    [Fact]
    public async Task Client_posts_exact_strong_identifier_contract_with_internal_bearer()
    {
        var handler = new RecordingHandler();
        var client = new HttpBusinessMesMaterialPrevalidationClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://mes"),
        });

        var response = await client.PrevalidateAsync(
            "internal-token",
            new BusinessConsoleMesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None);

        Assert.Equal("accepted", response.Decision);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/business/v1/mes/material-scan-prevalidation", handler.Path);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("internal-token", handler.AuthorizationParameter);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("MIR-001", body.RootElement.GetProperty("materialIssueRequestId").GetString());
        Assert.Equal("WO-001", body.RootElement.GetProperty("workOrderId").GetString());
        Assert.Equal("OP-10", body.RootElement.GetProperty("operationTaskId").GetString());
        Assert.False(body.RootElement.TryGetProperty("inventoryBatchId", out _));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Path { get; private set; } = string.Empty;
        public string AuthorizationScheme { get; private set; } = string.Empty;
        public string AuthorizationParameter { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath ?? string.Empty;
            AuthorizationScheme = request.Headers.Authorization?.Scheme ?? string.Empty;
            AuthorizationParameter = request.Headers.Authorization?.Parameter ?? string.Empty;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BusinessConsoleMesMaterialScanPrevalidationResponse(
                    "accepted", "material-scan-accepted", "MIR-001", "WO-001", "OP-10",
                    "MAT-001", "LOT-001", "primary", DateTimeOffset.Parse("2026-08-26T08:00:00Z"))),
            };
        }
    }
}
