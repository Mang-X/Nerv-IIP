using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;
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
                "SKU-001",
                IncludePrincipalContext: true),
            CancellationToken.None);

        Assert.True(result.IsAllowed);
        var grant = Assert.Single(result.ScopeGrants!);
        Assert.Equal("role-worker", grant.SourceId);
        Assert.Equal("workshop", grant.ScopeKind);
        Assert.Equal("WS-MC", grant.ScopeId);
        var role = Assert.Single(result.Roles!);
        Assert.Equal("role-worker", role.Id);
        Assert.Equal("一线操作工", role.DisplayName);
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
        Assert.True(payload.RootElement.GetProperty("includePrincipalContext").GetBoolean());
    }

    [Fact]
    public async Task Http_authorization_client_accepts_legacy_three_field_data_scope_payload()
    {
        var handler = new RecordingHandler(_ => LegacyDataScopeAuthorizationResponse());
        var client = CreateClient(handler);

        var result = await client.CheckAsync(
            "access-token-001",
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.MasterDataProductsRead,
                "org-001",
                "env-dev",
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.Equal(["WS-A"], result.DataScope!.WorkshopCodes);
        Assert.True(result.DataScope.HasRestrictions);
        Assert.Null(result.DataScope.SelfIds);
        Assert.Null(result.DataScope.TeamCodes);
        Assert.Null(result.DataScope.WorkCenterCodes);
        Assert.Null(result.DataScope.OrganizationIds);
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
    public async Task Http_authorization_client_keeps_read_continuity_from_cache_when_iam_is_temporarily_unavailable()
    {
        var fail = false;
        var handler = new RecordingHandler(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : AuthorizationResponse(HttpStatusCode.OK, allowed: true));
        var client = CreateClient(handler, new BusinessGatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 10 });
        var requirement = new BusinessGatewayPermissionRequirement(
            BusinessGatewayPermissions.MasterDataProductsRead,
            "org-001",
            "env-dev",
            null,
            null);

        var first = await client.CheckAsync(
            "access-token-001",
            requirement,
            BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
            CancellationToken.None);
        fail = true;
        var second = await client.CheckAsync(
            "access-token-001",
            requirement,
            BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
            CancellationToken.None);

        Assert.True(first.IsAllowed);
        Assert.True(second.IsAllowed);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_authorization_client_rejects_realtime_write_check_when_iam_is_unavailable()
    {
        var fail = false;
        var handler = new RecordingHandler(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : AuthorizationResponse(HttpStatusCode.OK, allowed: true));
        var client = CreateClient(handler, new BusinessGatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 10 });
        var requirement = new BusinessGatewayPermissionRequirement(
            BusinessGatewayPermissions.MasterDataProductsManage,
            "org-001",
            "env-dev",
            null,
            null);

        var first = await client.CheckAsync(
            "access-token-001",
            requirement,
            BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
            CancellationToken.None);
        fail = true;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.CheckAsync(
            "access-token-001",
            requirement,
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            CancellationToken.None));

        Assert.True(first.IsAllowed);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
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
        BusinessGatewayAuthorizationOptions? options = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://iam.local") },
            new MemoryAppCache(),
            Options.Create(options ?? new BusinessGatewayAuthorizationOptions { AuthorizationCacheTtlSeconds = 60 }),
            new BusinessGatewayDownstreamHealthState());

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
                scopeGrants = allowed
                    ? new[]
                    {
                        new
                        {
                            sourceKind = "role",
                            sourceId = "role-worker",
                            scopeKind = "workshop",
                            scopeId = "WS-MC",
                            applicablePermissionCodes = new[] { BusinessGatewayPermissions.MasterDataProductsRead },
                            organizationWide = false,
                        },
                    }
                    : null,
                roles = allowed
                    ? new[] { new { id = "role-worker", displayName = "一线操作工" } }
                    : null,
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

    private static HttpResponseMessage LegacyDataScopeAuthorizationResponse()
    {
        const string Content = """
            {
              "data": {
                "allowed": true,
                "principalId": "user-admin",
                "principalType": "user",
                "loginName": "admin",
                "dataScope": {
                  "siteCodes": [],
                  "workshopCodes": ["WS-A"],
                  "productionLineCodes": [],
                  "denyAll": false
                }
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Content, System.Text.Encoding.UTF8, "application/json"),
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
}
