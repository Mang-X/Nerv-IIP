using System.Net;
using System.Net.Http.Json;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.Sdk.Core.Tests;

public sealed class PlatformApiClientTests
{
    [Fact]
    public void CreateRequestAddsPlatformContextHeaders()
    {
        var context = new PlatformRequestContext(
            "org-001",
            "prod",
            "corr-123",
            IdempotencyKey: "idem-456",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");

        using var request = PlatformApiClient.CreateRequest(HttpMethod.Post, "/api/example", context);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/example", request.RequestUri?.OriginalString);
        Assert.Equal("org-001", GetHeader(request, "X-Organization-Id"));
        Assert.Equal("prod", GetHeader(request, "X-Environment-Id"));
        Assert.Equal("corr-123", GetHeader(request, "X-Correlation-Id"));
        Assert.Equal("idem-456", GetHeader(request, "X-Idempotency-Key"));
        Assert.Equal("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00", GetHeader(request, "traceparent"));
    }

    [Fact]
    public void CreateRequestOmitsNullablePlatformHeadersWhenAbsent()
    {
        var context = new PlatformRequestContext("org-001", "prod", "corr-123");

        using var request = PlatformApiClient.CreateRequest(HttpMethod.Get, "/api/example", context);

        Assert.False(request.Headers.Contains("X-Idempotency-Key"));
        Assert.False(request.Headers.Contains("traceparent"));
    }

    [Theory]
    [InlineData("", "prod", "corr-123")]
    [InlineData("org-001", "", "corr-123")]
    [InlineData("org-001", "prod", "")]
    public void CreateRequestRejectsMissingRequiredPlatformContextHeaders(
        string organizationId,
        string environmentId,
        string correlationId)
    {
        var context = new PlatformRequestContext(organizationId, environmentId, correlationId);

        Assert.Throws<ArgumentException>(() => PlatformApiClient.CreateRequest(HttpMethod.Get, "/api/example", context));
    }

    [Fact]
    public void CreateRequestRejectsInvalidHeaderValues()
    {
        var context = new PlatformRequestContext("org-001", "prod\r\nX-Injected: true", "corr-123");

        Assert.Throws<FormatException>(() => PlatformApiClient.CreateRequest(HttpMethod.Get, "/api/example", context));
    }

    [Fact]
    public async Task ReadResponseDataAsyncReturnsDataFromResponseDataEnvelope()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResponseDataEnvelope<SampleResponse>(
                new SampleResponse("task-001", 42),
                Success: true,
                Message: "OK",
                Code: 200))
        };

        var result = await PlatformApiClient.ReadResponseDataAsync<SampleResponse>(response);

        Assert.Equal(new SampleResponse("task-001", 42), result);
    }

    private static string GetHeader(HttpRequestMessage request, string name)
    {
        return Assert.Single(request.Headers.GetValues(name));
    }

    private sealed record SampleResponse(string Id, int Revision);

    private sealed record ResponseDataEnvelope<T>(T Data, bool Success, string Message, int Code);
}
