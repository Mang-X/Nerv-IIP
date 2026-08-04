using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// The shared host profiles raise <c>Security:RateLimit:PermitLimit</c>, because the gateway
/// partitions its limiter by principal name and every test signs in as the same principal. These
/// tests own the rate limiter instead, on a dedicated host with a deliberately tiny budget, so the
/// limiter stays covered rather than merely configured away.
/// </summary>
public sealed class BusinessGatewayRateLimitTests
{
    private const string SkusRoute =
        "/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev";

    [Fact]
    public async Task Business_console_requests_are_rejected_once_the_principal_permit_window_is_exhausted()
    {
        await using var factory = CreateRateLimitedFactory(permitLimit: 2);
        using var client = Authenticated(BusinessGatewayTestHost.CreateGatedClient(factory));

        var first = await client.GetAsync(SkusRoute);
        var second = await client.GetAsync(SkusRoute);
        var third = await client.GetAsync(SkusRoute);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Cors_preflight_does_not_consume_the_principal_permit_window()
    {
        await using var factory = CreateRateLimitedFactory(permitLimit: 1);
        using var client = Authenticated(BusinessGatewayTestHost.CreateGatedClient(factory));

        using var preflight = new HttpRequestMessage(HttpMethod.Options, SkusRoute);
        preflight.Headers.TryAddWithoutValidation("Origin", "http://localhost:5105");
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        var preflightResponse = await client.SendAsync(preflight);

        var afterPreflight = await client.GetAsync(SkusRoute);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, preflightResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterPreflight.StatusCode);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateRateLimitedFactory(int permitLimit) =>
        BusinessGatewayTestHost.CreateDedicatedFactory(
            BusinessGatewayTestHostProfile.ServiceBaseUrls,
            builder =>
            {
                builder.UseSetting(
                    "Security:RateLimit:PermitLimit",
                    permitLimit.ToString(CultureInfo.InvariantCulture));
                builder.UseSetting("Security:RateLimit:WindowSeconds", "600");
            },
            services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(
                    FakeBusinessGatewayAuthorizationClient.Allowed());
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(new RecordingMasterDataClient());
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new RateLimitInternalServiceTokenProvider("internal-rate-limit-token"));
            });

    private static HttpClient Authenticated(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        return client;
    }

    private sealed record RateLimitInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}
