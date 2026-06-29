using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Nerv.IIP.Caching;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayAuthorizationClientTests
{
    [Fact]
    public async Task Http_authorization_client_posts_internal_iam_check_contract()
    {
        var handler = new RecordingHandler(_ => AuthorizationResponse(HttpStatusCode.OK, allowed: true));
        var client = CreateClient(handler, new BusinessGatewayAuthorizationOptions
        {
            AuthorizationCacheTtlSeconds = 60,
            AuthorizationCheckPath = "/custom/iam/check",
        });

        var result = await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.MasterDataProductsRead,
                "org-001",
                "env-dev",
                "sku",
                "SKU-001"),
            CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        Assert.Equal("/custom/iam/check", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.Requests.Single().Headers.Authorization!.Scheme);
        Assert.Equal("access-token-001", handler.Requests.Single().Headers.Authorization!.Parameter);

        using var payload = JsonDocument.Parse(handler.RequestBodies.Single());
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, payload.RootElement.GetProperty("permissionCode").GetString());
        Assert.Equal("org-001", payload.RootElement.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", payload.RootElement.GetProperty("environmentId").GetString());
        Assert.Equal("sku", payload.RootElement.GetProperty("resourceType").GetString());
        Assert.Equal("SKU-001", payload.RootElement.GetProperty("resourceId").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden")]
    public async Task Http_authorization_client_maps_iam_unauthorized_and_forbidden_to_denial(
        HttpStatusCode statusCode,
        string expectedReason)
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(statusCode)));

        var result = await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.MasterDataProductsRead,
                "org-001",
                "env-dev",
                null,
                null),
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal(expectedReason, result.DenialReason);
    }

    [Fact]
    public async Task Http_authorization_client_cache_keeps_contexts_separate()
    {
        var handler = new RecordingHandler(_ => AuthorizationResponse(HttpStatusCode.OK, allowed: true));
        var client = CreateClient(handler);

        await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(BusinessGatewayPermissions.MasterDataProductsRead, "org-001", "env-dev", null, null),
            CancellationToken.None);
        await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(BusinessGatewayPermissions.MasterDataProductsRead, "org-001", "env-dev", null, null),
            CancellationToken.None);
        await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(BusinessGatewayPermissions.MasterDataProductsRead, "org-001", "env-prod", null, null),
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Http_authorization_client_cache_key_includes_permission_version()
    {
        var handler = new RecordingHandler(_ => AuthorizationResponse(HttpStatusCode.OK, allowed: true));
        var cache = new RecordingCache();
        var client = CreateClient(
            handler,
            new BusinessGatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 60 },
            cache);
        var requirement = new BusinessGatewayPermissionRequirement(
            BusinessGatewayPermissions.MesWorkOrdersRead,
            "org-001",
            "env-dev",
            null,
            null);

        await client.CheckAsync(BusinessGatewayTestTokens.ValidAccessToken(permissionVersion: 7), requirement, CancellationToken.None);
        await client.CheckAsync(BusinessGatewayTestTokens.ValidAccessToken(permissionVersion: 8), requirement, CancellationToken.None);

        Assert.Collection(
            cache.Keys,
            key => Assert.Contains(":permission-version:7:", key, StringComparison.Ordinal),
            key => Assert.Contains(":permission-version:8:", key, StringComparison.Ordinal));
        Assert.NotEqual(cache.Keys[0], cache.Keys[1]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Accept_language_forwarding_handler_copies_current_request_language()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.AcceptLanguage = "zh-CN, en;q=0.8";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(new AcceptLanguageForwardingHandler(accessor)
        {
            InnerHandler = terminal,
        });

        await httpClient.GetAsync("http://iam.local/internal/iam/v1/authorization/check");

        Assert.Equal(
            "zh-CN, en; q=0.8",
            string.Join(", ", terminal.Requests.Single().Headers.AcceptLanguage.Select(value => value.ToString())));
    }

    private static HttpBusinessGatewayAuthorizationClient CreateClient(
        RecordingHandler handler,
        BusinessGatewayAuthorizationOptions? options = null,
        IAppCache? cache = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://iam.local") },
            cache ?? new MemoryAppCache(),
            Options.Create(options ?? new BusinessGatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 60 }));

    private static HttpResponseMessage AuthorizationResponse(HttpStatusCode statusCode, bool allowed)
    {
        var content = JsonSerializer.Serialize(new
        {
            data = new
            {
                allowed,
                principalId = allowed ? "user-admin" : null,
                principalType = allowed ? "user" : null,
                loginName = allowed ? "admin" : null,
                denialReason = allowed ? null : "forbidden",
            },
            success = true,
            message = string.Empty,
            code = 0,
        });

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }

    private sealed class RecordingCache : IAppCache
    {
        public List<string> Keys { get; } = [];

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
        {
            Keys.Add(key);
            return await factory();
        }

        public void InvalidatePrefix(string prefix)
        {
        }

        public void Clear()
        {
        }
    }
}
