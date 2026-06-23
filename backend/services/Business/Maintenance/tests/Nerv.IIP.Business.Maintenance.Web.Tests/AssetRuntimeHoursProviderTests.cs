using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class AssetRuntimeHoursProviderTests
{
    [Fact]
    public async Task Http_runtime_provider_does_not_query_fallback_when_oee_has_samples()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(99m, HasRuntimeSamples: false));
        var provider = CreateProvider(fallback, """
            {
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "deviceAssetId": "DEV-CNC-01",
                "windowStartUtc": "2026-06-08T00:00:00Z",
                "windowEndUtc": "2026-06-08T04:00:00Z",
                "stateSampleCount": 2,
                "availabilityRate": 0.25,
                "performanceRate": 1,
                "qualityRate": 1,
                "oeeRate": 0.25,
                "performanceRateEstimated": true,
                "qualityRateEstimated": true
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """);

        var result = await provider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T04:00:00Z"),
            CancellationToken.None);

        Assert.Equal(1m, result.RuntimeHours);
        Assert.True(result.HasRuntimeSamples);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task Http_runtime_provider_queries_fallback_when_oee_has_no_samples()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(3m, HasRuntimeSamples: false));
        var provider = CreateProvider(fallback, """
            {
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "deviceAssetId": "DEV-CNC-01",
                "windowStartUtc": "2026-06-08T00:00:00Z",
                "windowEndUtc": "2026-06-08T04:00:00Z",
                "stateSampleCount": 0,
                "availabilityRate": 0,
                "performanceRate": 0,
                "qualityRate": 0,
                "oeeRate": 0,
                "performanceRateEstimated": true,
                "qualityRateEstimated": true
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """);

        var result = await provider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T04:00:00Z"),
            CancellationToken.None);

        Assert.Equal(3m, result.RuntimeHours);
        Assert.False(result.HasRuntimeSamples);
        Assert.Equal(1, fallback.CallCount);
    }

    private static HttpIndustrialTelemetryAssetRuntimeHoursProvider CreateProvider(
        CountingFallbackProvider fallback,
        string responseJson)
    {
        var httpClient = new HttpClient(new JsonResponseHandler(responseJson))
        {
            BaseAddress = new Uri("https://industrial-telemetry.local"),
        };
        return new HttpIndustrialTelemetryAssetRuntimeHoursProvider(
            new FixedHttpClientFactory(httpClient),
            tokenProvider: null,
            fallback,
            NullLogger<HttpIndustrialTelemetryAssetRuntimeHoursProvider>.Instance);
    }

    private sealed class CountingFallbackProvider(AssetRuntimeHoursResult result) : IAssetRuntimeHoursFallbackProvider
    {
        public int CallCount { get; private set; }

        public Task<AssetRuntimeHoursResult> CalculateFallbackAsync(
            string organizationId,
            string environmentId,
            string deviceAssetId,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken)
        {
            _ = organizationId;
            _ = environmentId;
            _ = deviceAssetId;
            _ = windowStartUtc;
            _ = windowEndUtc;
            _ = cancellationToken;
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpIndustrialTelemetryAssetRuntimeHoursProvider.ClientName, name);
            return client;
        }
    }

    private sealed class JsonResponseHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
