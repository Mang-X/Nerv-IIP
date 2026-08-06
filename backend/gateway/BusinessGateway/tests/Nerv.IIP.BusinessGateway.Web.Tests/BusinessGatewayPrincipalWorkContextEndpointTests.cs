using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayPrincipalWorkContextEndpointTests
{
    private const string PermissionCode = BusinessGatewayPermissions.MesWorkOrdersRead;

    [Fact]
    public async Task Current_principal_context_uses_realtime_authorization_and_server_principal()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant("role", "role-worker", "team", "TEAM-A", [PermissionCode]),
            ],
            roles: [new AuthorizationRole("role-worker", "一线操作工")]);
        var masterData = new RecordingMasterDataClient { PrincipalWorkContext = Context() };
        await using var lease = LeaseHost(auth, masterData);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            $"/api/business-console/v1/me/work-context?organizationId=org-001&environmentId=env-dev&permissionCode={PermissionCode}&scopeKind=team&scopeId=TEAM-A&userId=forged-user");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Equal(
            new BusinessMasterDataPrincipalWorkContextRequest("org-001", "env-dev", "user-admin"),
            masterData.LastPrincipalWorkContextRequest);
        Assert.Equal("internal-context-token", masterData.LastInternalToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("org-001", data.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", data.GetProperty("environmentId").GetString());
        Assert.Equal(PermissionCode, data.GetProperty("applicablePermissionCode").GetString());
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(data.GetProperty("resolvedAtUtc").GetString()!).Offset);
        Assert.Equal("user-admin", data.GetProperty("principal").GetProperty("id").GetString());
        Assert.Equal("user", data.GetProperty("principal").GetProperty("principalType").GetString());
        Assert.Equal("一线操作工", data.GetProperty("principal").GetProperty("roles")[0].GetProperty("displayName").GetString());
        Assert.Equal("TEAM-A", data.GetProperty("selectedScope").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Malformed_or_conflicting_role_facts_are_dropped_from_the_public_response()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant("role", "role-worker", "team", "TEAM-A", [PermissionCode]),
            ],
            roles:
            [
                null!,
                new AuthorizationRole("", "无标识角色"),
                new AuthorizationRole("role-conflict", "冲突甲"),
                new AuthorizationRole("role-conflict", "冲突乙"),
                new AuthorizationRole("role-valid", "有效角色"),
            ]);
        var masterData = new RecordingMasterDataClient { PrincipalWorkContext = Context() };
        await using var lease = LeaseHost(auth, masterData);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            $"/api/business-console/v1/me/work-context?organizationId=org-001&environmentId=env-dev&permissionCode={PermissionCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var role = Assert.Single(document.RootElement
            .GetProperty("data")
            .GetProperty("principal")
            .GetProperty("roles")
            .EnumerateArray());
        Assert.Equal("role-valid", role.GetProperty("id").GetString());
        Assert.Equal("有效角色", role.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Unknown_permission_is_rejected_before_authorization()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var masterData = new RecordingMasterDataClient { PrincipalWorkContext = Context() };
        await using var lease = LeaseHost(auth, masterData);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/me/work-context?organizationId=org-001&environmentId=env-dev&permissionCode=business.unknown.read");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Null(masterData.LastPrincipalWorkContextRequest);
    }

    [Fact]
    public async Task Unauthorized_scope_selection_is_forbidden()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant("role", "role-worker", "team", "TEAM-A", [PermissionCode]),
            ]);
        var masterData = new RecordingMasterDataClient { PrincipalWorkContext = Context() };
        await using var lease = LeaseHost(auth, masterData);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            $"/api/business-console/v1/me/work-context?organizationId=org-001&environmentId=env-dev&permissionCode={PermissionCode}&scopeKind=work-center&scopeId=WC-A");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deny_all_stops_before_master_data_lookup()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            new AuthorizationDataScope([], [], [], DenyAll: true),
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-org",
                    "organization",
                    "org-001",
                    [PermissionCode],
                    OrganizationWide: true),
            ]);
        var masterData = new RecordingMasterDataClient { PrincipalWorkContext = Context() };
        await using var lease = LeaseHost(auth, masterData);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            $"/api/business-console/v1/me/work-context?organizationId=org-001&environmentId=env-dev&permissionCode={PermissionCode}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(masterData.LastPrincipalWorkContextRequest);
    }

    [Fact]
    public async Task Master_data_client_escapes_principal_and_forwards_scope()
    {
        var handler = new RecordingHandler();
        var client = new HttpBusinessMasterDataClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://master-data.local"),
        });

        await client.GetPrincipalWorkContextAsync(
            "internal-token",
            new BusinessMasterDataPrincipalWorkContextRequest("org-001", "env-dev", "user/001"),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "/api/business/v1/master-data/principals/user%2F001/work-context?organizationId=org-001&environmentId=env-dev",
            request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Master_data_transport_failure_is_mapped_to_a_stable_service_unavailable_error()
    {
        var client = new HttpBusinessMasterDataClient(new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://master-data.local"),
        });

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.GetPrincipalWorkContextAsync(
                "internal-token",
                new BusinessMasterDataPrincipalWorkContextRequest("org-001", "env-dev", "user-admin"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("downstream-unavailable", exception.Message);
    }

    private static BusinessMasterDataPrincipalWorkContextResponse Context() =>
        new(
            "ready-with-gaps",
            new BusinessMasterDataWorkContextWorker(
                "worker-id",
                "user-admin",
                "EMP-001",
                "操作工",
                null,
                null,
                "机加操作工",
                "active"),
            [new BusinessMasterDataWorkContextTeam("TEAM-A", "甲班", false, "WS-A", "SHIFT-A")],
            [],
            [],
            [],
            [],
            [
                new BusinessMasterDataWorkContextCandidateScope(
                    "team",
                    "TEAM-A",
                    "甲班",
                    "active-membership",
                    [new BusinessMasterDataWorkContextScopeAncestor("workshop", "WS-A")]),
                new BusinessMasterDataWorkContextCandidateScope(
                    "work-center",
                    "WC-A",
                    "加工中心",
                    "workshop-covered",
                    [new BusinessMasterDataWorkContextScopeAncestor("workshop", "WS-A")]),
            ],
            ["team", "work-center"],
            ["position-master-not-modeled"]);

    private static BusinessGatewayTestHostLease LeaseHost(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessMasterDataClient masterData) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-context-token"));
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            const string Payload = """
                {
                  "data": {
                    "resolutionStatus": "worker-not-mapped",
                    "worker": null,
                    "teams": [],
                    "coveredWorkCenters": [],
                    "workshops": [],
                    "shifts": [],
                    "sites": [],
                    "candidateScopes": [],
                    "candidateScopeKinds": [],
                    "issues": ["worker-not-mapped"]
                  },
                  "success": true,
                  "message": "",
                  "code": 0
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
