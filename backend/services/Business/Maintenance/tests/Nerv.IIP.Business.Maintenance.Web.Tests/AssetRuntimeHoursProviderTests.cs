using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class AssetRuntimeHoursProviderTests
{
    [Fact]
    public async Task Http_runtime_provider_reads_industrial_telemetry_runtime_hours_endpoint()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(99m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false));
        var handler = new JsonResponseHandler("""
            {
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "deviceAssetId": "DEV-CNC-01",
                "windowStartUtc": "2026-06-08T00:00:00Z",
                "windowEndUtc": "2026-06-08T06:00:00Z",
                "stateSampleCount": 3,
                "totalRuntimeHours": 2.5,
                "totalLoadingHours": 3,
                "hasRuntimeSamples": true,
                "daily": [
                  {
                    "businessDate": "2026-06-08",
                    "runtimeHours": 2.5,
                    "loadingHours": 3,
                    "stateSampleCount": 3
                  }
                ]
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """);
        var provider = CreateProvider(fallback, handler);

        var result = await provider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T06:00:00Z"),
            CancellationToken.None);

        Assert.Equal(2.5m, result.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Oee, result.RuntimeSource);
        Assert.True(result.HasRuntimeSamples);
        Assert.Equal(0, fallback.CallCount);
        Assert.Contains("/api/business/v1/iiot/runtime-hours?", handler.LastRequestUri);
    }

    [Fact]
    public async Task Http_runtime_provider_does_not_query_fallback_when_oee_has_samples()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(99m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false));
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
        Assert.Equal(AssetRuntimeSources.Oee, result.RuntimeSource);
        Assert.True(result.HasRuntimeSamples);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task Http_runtime_provider_multiplies_oee_availability_by_loading_hours()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(99m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false));
        var provider = CreateProvider(fallback, """
            {
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "deviceAssetId": "DEV-CNC-01",
                "windowStartUtc": "2026-06-08T00:00:00Z",
                "windowEndUtc": "2026-06-08T06:00:00Z",
                "stateSampleCount": 3,
                "availabilityRate": 0.5,
                "loadingRate": 0.5,
                "performanceRate": 1,
                "qualityRate": 1,
                "oeeRate": 0.5,
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
            DateTimeOffset.Parse("2026-06-08T06:00:00Z"),
            CancellationToken.None);

        Assert.Equal(1.5m, result.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Oee, result.RuntimeSource);
        Assert.True(result.HasRuntimeSamples);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task Runtime_source_fields_make_oee_and_fallback_denominator_difference_explicit()
    {
        var oeeProvider = CreateProvider(
            new CountingFallbackProvider(new AssetRuntimeHoursResult(3m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)),
            """
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
        var fallbackProvider = CreateProvider(
            new CountingFallbackProvider(new AssetRuntimeHoursResult(3m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)),
            """
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

        var oeeResult = await oeeProvider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T04:00:00Z"),
            CancellationToken.None);
        var fallbackResult = await fallbackProvider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T04:00:00Z"),
            CancellationToken.None);

        Assert.Equal(1m, oeeResult.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Oee, oeeResult.RuntimeSource);
        Assert.True(oeeResult.HasRuntimeSamples);
        Assert.Equal(3m, fallbackResult.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Fallback, fallbackResult.RuntimeSource);
        Assert.False(fallbackResult.HasRuntimeSamples);
    }

    [Fact]
    public async Task Http_runtime_provider_queries_fallback_when_oee_has_no_samples()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(3m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false));
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
        Assert.Equal(AssetRuntimeSources.Fallback, result.RuntimeSource);
        Assert.False(result.HasRuntimeSamples);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task Http_runtime_provider_queries_fallback_when_oee_response_json_is_invalid()
    {
        var fallback = new CountingFallbackProvider(new AssetRuntimeHoursResult(3m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false));
        var provider = CreateProvider(fallback, """{ "data": """);

        var result = await provider.CalculateAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-08T04:00:00Z"),
            CancellationToken.None);

        Assert.Equal(3m, result.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Fallback, result.RuntimeSource);
        Assert.False(result.HasRuntimeSamples);
        Assert.Equal(1, fallback.CallCount);
    }

    private static HttpIndustrialTelemetryAssetRuntimeHoursProvider CreateProvider(
        CountingFallbackProvider fallback,
        string responseJson)
    {
        return CreateProvider(fallback, new JsonResponseHandler(responseJson));
    }

    private static HttpIndustrialTelemetryAssetRuntimeHoursProvider CreateProvider(
        CountingFallbackProvider fallback,
        JsonResponseHandler responseHandler)
    {
        var httpClient = new HttpClient(responseHandler)
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
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequestUri = request.RequestUri?.PathAndQuery;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
