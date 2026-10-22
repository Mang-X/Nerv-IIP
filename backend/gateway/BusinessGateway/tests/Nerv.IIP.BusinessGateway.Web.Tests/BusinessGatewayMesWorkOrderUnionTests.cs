using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayMesWorkOrderUnionTests
{
    private const string Permission = BusinessGatewayPermissions.MesWorkOrdersRead;

    // DomainInvariant：#4256；此 HTTP fixture 只证明可信范围投影，不替代真实 MES/PostgreSQL 验收。
    [Fact]
    public async Task Work_order_http_list_unions_current_permission_grants_and_preserves_business_filters()
    {
        var mes = new RecordingMesClient();
        var masterData = MasterData();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants: Grants());
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.GetAsync(
            "/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&keyword=WO-A&workCenterIds=WC-A,WC-X&skip=2&take=3&authorizedWorkCenterIds=WC-X");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.IsType<BusinessMesWorkOrderListRequest>(mes.LastWorkOrderListRequest);
        Assert.Equal("user-admin", request.AuthorizedAssignedUserIds);
        Assert.Equal("TEAM-A", request.AuthorizedTeamIds);
        Assert.Equal("WC-A,WC-B", request.AuthorizedWorkCenterIds);
        Assert.Equal("WC-A,WC-X", request.WorkCenterIds);
        Assert.Equal("WO-A", request.Keyword);
        Assert.Equal(2, request.Skip);
        Assert.Equal(3, request.Take);
        Assert.Null(request.AssignedUserIds);
        Assert.Null(request.TeamIds);
    }

    [Fact]
    public async Task Work_order_union_does_not_relax_other_scope_consumers_or_explicit_selection()
    {
        var resolver = new PrincipalWorkScopeResolver(MasterData(), new TestInternalServiceTokenProvider("internal-test-token"));
        var authorization = BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "operator",
            "org-001", "env-dev", null, Grants(), []);
        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => resolver.ResolveAsync(
            authorization, "org-001", "env-dev", Permission, null, null, CancellationToken.None));
        var selected = await resolver.ResolveWorkOrderListAsync(authorization, "org-001", "env-dev",
            Permission, "team", "TEAM-A", CancellationToken.None);
        Assert.Equal(["TEAM-A"], selected.TeamIds);
        Assert.Empty(selected.WorkCenterIds);
        Assert.Empty(selected.AssignedUserIds);
        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => resolver.ResolveWorkOrderListAsync(
            authorization, "org-001", "env-dev", Permission, "work-center", "WC-X", CancellationToken.None));
        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => resolver.ResolveWorkOrderListAsync(
            authorization, "org-001", "env-dev", Permission, "team", null, CancellationToken.None));
        var wrongPermission = authorization with { ScopeGrants =
            [new AuthorizationScopeGrant("role", "other", "work-center", "WC-A", [BusinessGatewayPermissions.MesOperationsRead])] };
        await Assert.ThrowsAsync<BusinessServiceProxyException>(() => resolver.ResolveWorkOrderListAsync(
            wrongPermission, "org-001", "env-dev", Permission, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Internal_mes_http_request_carries_all_authorization_dimensions()
    {
        var handler = new UnionRequestHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesClient(http);
        await client.ListWorkOrdersAsync("internal-token", new("org-001", "env-dev",
            WorkCenterIds: "WC-X", AuthorizedAssignedUserIds: "user-admin",
            AuthorizedTeamIds: "TEAM-A", AuthorizedWorkCenterIds: "WC-A,WC-B"), CancellationToken.None);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.RequestUri!.Query);
        Assert.Equal("user-admin", query["authorizedAssignedUserIds"].ToString());
        Assert.Equal("TEAM-A", query["authorizedTeamIds"].ToString());
        Assert.Equal("WC-A,WC-B", query["authorizedWorkCenterIds"].ToString());
        Assert.Equal("WC-X", query["workCenterIds"].ToString());
    }

    private sealed class UnionRequestHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"items\":[],\"total\":0}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static AuthorizationScopeGrant[] Grants() =>
    [
        new("membership", "self", "self", "user-admin", [Permission]),
        new("role", "team", "team", "TEAM-A", [Permission]),
        new("role", "center", "work-center", "WC-A", [Permission]),
        new("role", "workshop", "workshop", "WS-B", [Permission]),
        new("role", "other-permission", "work-center", "WC-X", [BusinessGatewayPermissions.MesOperationsRead]),
    ];

    private static RecordingMasterDataClient MasterData() => new()
    {
        PrincipalWorkContext = new("resolved", null, [],
            [new BusinessMasterDataWorkContextCoveredWorkCenter("WC-B", "工作中心 B", "WS-B", "workshop-covered")],
            [], [], [],
            [
                new("self", "user-admin", "当前人员", "worker-user", []),
                new("team", "TEAM-A", "班组 A", "active-membership", []),
                new("work-center", "WC-A", "工作中心 A", "worker-center", []),
                new("workshop", "WS-B", "车间 B", "worker-workshop", []),
                new("work-center", "WC-B", "工作中心 B", "workshop-covered",
                    [new BusinessMasterDataWorkContextScopeAncestor("workshop", "WS-B")]),
                new("work-center", "WC-X", "工作中心 X", "worker-center", []),
            ], ["self", "team", "work-center", "workshop"], []),
    };
}
