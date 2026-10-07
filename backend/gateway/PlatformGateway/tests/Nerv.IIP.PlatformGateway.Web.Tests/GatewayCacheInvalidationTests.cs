using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
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
        Assert.Empty(cache.InvalidatedTags);
        Assert.Equal(0, cache.ClearCount);
    }

    [Fact]
    public async Task Invalidate_gateway_cache_allows_internal_service_token_and_invalidates_gateway_tag()
    {
        var cache = new RecordingCache();
        await using var factory = CreateFactory(cache);
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate");
        request.Headers.Authorization = new("Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["gateway"], cache.InvalidatedTags);
        Assert.Equal(0, cache.ClearCount);
    }

    // #4174 PublicContract: explicit scope, trustworthy caller grants, no global fallback.
    [Fact]
    public async Task Scoped_invalidation_requires_configured_caller_and_preserves_legacy_entry()
    {
        var cache = new RecordingCache();
        await using var factory = CreateFactory(cache);
        using var client = factory.CreateClient();
        using var request = ScopeRequest("local-internal-service-token", new { organizationId = "org-001", environmentId = "env-dev" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(cache.InvalidatedTags);
        using var legacy = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate");
        legacy.Headers.Authorization = new("Bearer", InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        using var accepted = await client.SendAsync(legacy);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(["gateway"], cache.InvalidatedTags);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-identity", HttpStatusCode.Unauthorized)]
    [InlineData("local-internal-service-token", HttpStatusCode.Unauthorized)]
    [InlineData("no-permission-token", HttpStatusCode.Forbidden)]
    [InlineData("wrong-scope-token", HttpStatusCode.Forbidden)]
    public async Task Scoped_invalidation_rejects_untrusted_or_ungranted_callers_without_side_effects(string? token, HttpStatusCode expected)
    {
        var cache = new RecordingCache();
        await using var factory = CreateScopedFactory(cache);
        using var client = factory.CreateClient();
        using var request = ScopeRequest(token, new { organizationId = "org-001", environmentId = "env-dev" });
        // Untrusted headers must not expand the profile's scope.
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(cache.InvalidatedTags);
        Assert.Equal(0, cache.ClearCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"organizationId\":\"org-001\"}")]
    [InlineData("{\"environmentId\":\"env-dev\"}")]
    [InlineData("{\"organizationId\":\"\",\"environmentId\":\"env-dev\"}")]
    [InlineData("{\"organizationId\":null,\"environmentId\":\"env-dev\"}")]
    [InlineData("{\"organizationId\":\" org-001\",\"environmentId\":\"env-dev\"}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev \"}")]
    public async Task Scoped_invalidation_rejects_missing_empty_or_noncanonical_scope_without_side_effects(string body)
    {
        var cache = new RecordingCache();
        await using var factory = CreateScopedFactory(cache);
        using var client = factory.CreateClient();
        using var request = ScopeRequest("scoped-cache-token", new { });
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(400, document.RootElement.GetProperty("code").GetInt32());
        Assert.Empty(cache.InvalidatedTags);
        Assert.Equal(0, cache.ClearCount);
    }

    [Fact]
    public async Task Scoped_invalidation_removes_one_composite_tag()
    {
        var cache = new RecordingCache();
        await using var factory = CreateScopedFactory(cache);
        using var client = factory.CreateClient();
        using var request = ScopeRequest("scoped-cache-token", new { organizationId = "org-001", environmentId = "env-dev" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["gateway:scope:org-001:env-dev"], cache.InvalidatedTags);
        Assert.Equal(0, cache.ClearCount);
    }

    private static HttpRequestMessage ScopeRequest(string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/gateway/cache/invalidate-scope") { Content = JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static WebApplicationFactory<Program> CreateScopedFactory(RecordingCache cache) =>
        CreateFactory(cache).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddGatewayCacheInvalidationAuthorization(new ConfigurationBuilder()
                .AddInMemoryCollection(ScopedProfiles()).Build())));

    internal static Dictionary<string, string?> ScopedProfiles()
    {
        var values = new Dictionary<string, string?>();
        foreach (var (index, token, organization, permission) in new[]
        {
            (0, "scoped-cache-token", "org-001", "internal.gateway-cache.invalidate"),
            (1, "no-permission-token", "org-001", "apphub.instances.read"),
            (2, "wrong-scope-token", "org-002", "internal.gateway-cache.invalidate")
        })
        {
            var prefix = $"Gateway:CacheInvalidation:ScopedCallers:Profiles:{index}:";
            values[prefix + "Name"] = $"caller-{index}";
            values[prefix + "BearerToken"] = token;
            values[prefix + "Subject"] = $"cache-caller-{index}";
            values[prefix + "OrganizationId"] = organization;
            values[prefix + "EnvironmentId"] = "env-dev";
            values[prefix + "Permissions:0"] = permission;
        }
        return values;
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingCache cache) =>
        PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAppCache>();
                services.AddSingleton<IAppCache>(cache);
            }));

    private sealed class RecordingCache : IAppCache
    {
        public List<string> InvalidatedTags { get; } = [];
        public int ClearCount { get; private set; }

        public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags) => factory();

        public void RemoveByTag(string tag) => InvalidatedTags.Add(tag);

        public void Clear() => ClearCount++;
    }
}
