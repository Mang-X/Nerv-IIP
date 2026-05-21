using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayAuthorizationClientTests
{
    [Fact]
    public async Task CheckAsync_reuses_cached_result_for_same_token_and_requirement()
    {
        var handler = new CountingAuthorizationHandler();
        var client = CreateClient(handler);
        var requirement = new GatewayPermissionRequirement(
            "iam.users.read",
            "org-001",
            "env-dev",
            null,
            null);

        var first = await client.CheckAsync("token-v7", requirement, CancellationToken.None);
        var second = await client.CheckAsync("token-v7", requirement, CancellationToken.None);

        Assert.True(first.IsAllowed);
        Assert.True(second.IsAllowed);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckAsync_cache_key_varies_by_token_and_requirement()
    {
        var handler = new CountingAuthorizationHandler();
        var client = CreateClient(handler);
        var requirement = new GatewayPermissionRequirement(
            "iam.users.read",
            "org-001",
            "env-dev",
            null,
            null);

        await client.CheckAsync("token-v7", requirement, CancellationToken.None);
        await client.CheckAsync("token-v8", requirement, CancellationToken.None);
        await client.CheckAsync("token-v8", requirement with { PermissionCode = "iam.users.manage" }, CancellationToken.None);

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task CheckAsync_uses_configured_cache_ttl()
    {
        var handler = new CountingAuthorizationHandler();
        var cache = new RecordingCache();
        var client = CreateClient(
            handler,
            cache,
            Options.Create(new GatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 12 }));
        var requirement = new GatewayPermissionRequirement(
            "iam.users.read",
            "org-001",
            "env-dev",
            null,
            null);

        await client.CheckAsync("token-v7", requirement, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(12), cache.LastTtl);
    }

    private static HttpGatewayAuthorizationClient CreateClient(CountingAuthorizationHandler handler)
    {
        return CreateClient(
            handler,
            new MemoryAppCache(),
            Options.Create(new GatewayAuthorizationOptions()));
    }

    private static HttpGatewayAuthorizationClient CreateClient(
        CountingAuthorizationHandler handler,
        IAppCache cache,
        IOptions<GatewayAuthorizationOptions> options)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://iam.local")
        };
        return new HttpGatewayAuthorizationClient(httpClient, cache, options);
    }

    private sealed class CountingAuthorizationHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ResponseDataEnvelope<AuthorizationCheckResponse>(
                    new AuthorizationCheckResponse(true, "user-admin", "user", "admin", null),
                    true,
                    "OK",
                    0))
            };
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingCache : IAppCache
    {
        public TimeSpan? LastTtl { get; private set; }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
        {
            LastTtl = ttl;
            return await factory();
        }

        public void InvalidatePrefix(string prefix)
        {
        }

        public void Clear()
        {
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
