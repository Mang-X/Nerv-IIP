using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Caching;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayCacheInvalidationTests
{
    [Fact]
    public async Task Invalidate_gateway_cache_rejects_anonymous_requests_without_clearing_cache()
    {
        var cache = new RecordingCache();
        await using var factory = CreateFactory(cache);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/internal/gateway/cache/invalidate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(cache.InvalidatedPrefixes);
        Assert.Equal(0, cache.ClearCount);
    }

    [Fact]
    public async Task Invalidate_gateway_cache_allows_internal_service_token_and_invalidates_gateway_prefix()
    {
        var cache = new RecordingCache();
        await using var factory = CreateFactory(cache);
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate");
        request.Headers.Authorization = new("Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["gateway:"], cache.InvalidatedPrefixes);
        Assert.Equal(0, cache.ClearCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingCache cache) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAppCache>();
                services.AddSingleton<IAppCache>(cache);
            }));

    private sealed class RecordingCache : IAppCache
    {
        public List<string> InvalidatedPrefixes { get; } = [];
        public int ClearCount { get; private set; }

        public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl) => factory();

        public void InvalidatePrefix(string prefix) => InvalidatedPrefixes.Add(prefix);

        public void Clear() => ClearCount++;
    }
}
