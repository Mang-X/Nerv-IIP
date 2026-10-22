using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// Regression / DomainInvariant：#4443；HTTP fixture 验证主体边界，数据库聚合另由 MES provider 测试证明。
public sealed class BusinessGatewayMesDowntimeScopeTests
{
    [Fact]
    public async Task Explicit_outside_work_center_is_rejected_even_when_client_claims_authorized_centers()
    {
        var (response, mes, _) = await RequestAsync("WC-B", recovery: false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(mes.LastDowntimeEventListRequest);
    }

    [Fact]
    public async Task Omitted_work_center_forwards_only_canonical_authorized_union_before_paging()
    {
        var (response, mes, auth) = await RequestAsync(null, recovery: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "WC-A", "WC-C" }, mes.LastDowntimeAuthorizedWorkCenterIds);
        Assert.Equal(1, mes.LastDowntimeEventListRequest!.Skip);
        Assert.Equal(1, mes.LastDowntimeEventListRequest.Take);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.True(auth.LastRequirement!.IncludePrincipalContext);
    }

    [Fact]
    public async Task Recovery_forwards_event_identity_and_trusted_scope_ignoring_client_work_center_claim()
    {
        var (response, mes, auth) = await RequestAsync(null, recovery: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DT-OUTSIDE", mes.LastRecoveryEventId);
        Assert.Equal(new[] { "WC-A", "WC-C" }, mes.LastRecoveryAuthorizedWorkCenterIds);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Organization_grant_preserves_unrestricted_internal_scope_without_worker_assignment(bool recovery)
    {
        var (response, mes, _) = await RequestAsync(null, recovery, organizationWide: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (recovery)
        {
            Assert.Equal("DT-OUTSIDE", mes.LastRecoveryEventId);
            Assert.Null(mes.LastRecoveryAuthorizedWorkCenterIds);
        }
        else
        {
            Assert.NotNull(mes.LastDowntimeEventListRequest);
            Assert.Null(mes.LastDowntimeAuthorizedWorkCenterIds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_scope_grants_cannot_be_replaced_with_client_authorization_fields(bool recovery)
    {
        var (response, mes, _) = await RequestAsync(null, recovery, noGrant: true);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(mes.LastDowntimeEventListRequest);
        Assert.Null(mes.LastRecoveryEventId);
    }

    private static async Task<(HttpResponseMessage Response, RecordingMesClient Mes, FakeBusinessGatewayAuthorizationClient Auth)>
        RequestAsync(string? workCenter, bool recovery, bool organizationWide = false, bool noGrant = false)
    {
        var permission = recovery ? BusinessGatewayPermissions.MesDowntimeManage : BusinessGatewayPermissions.MesDowntimeRead;
        AuthorizationScopeGrant[] grants = noGrant ? [] : organizationWide
            ? [new("role", "leader", "organization", "org-001", [permission], OrganizationWide: true)]
            : [new("membership", "member-leader", "work-center", "WC-A", [permission]),
               new("membership", "member-leader", "work-center", "WC-C", [permission])];
        var auth = new FakeBusinessGatewayAuthorizationClient(_ => true,
            allowedResult: BusinessGatewayAuthorizationResult.Allowed("user-leader", "user", "leader",
                "org-001", "env-dev", scopeGrants: grants));
        var mes = new RecordingMesClient();
        var masterData = new RecordingMasterDataClient
        {
            PrincipalWorkContext = new("mapped", null, [], [], [], [], [],
                organizationWide ? [] : [new("work-center", "WC-A", "工作中心 A", "assignment", []),
                    new("work-center", "WC-B", "工作中心 B", "assignment", []),
                    new("work-center", "WC-C", "工作中心 C", "assignment", [])], [], []),
        };
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(new RecordingMaintenanceFacadeClient());
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = recovery
            ? await client.PostAsJsonAsync("/api/business-console/v1/mes/downtime-events/DT-OUTSIDE/recover?organizationId=org-001&environmentId=env-dev", new
            {
                recoveredAtUtc = "2026-10-16T12:00:00Z", workCenterId = "WC-A",
                authorizedWorkCenterIds = new[] { "WC-B" },
            })
            : await client.GetAsync("/api/business-console/v1/mes/downtime-events?organizationId=org-001&environmentId=env-dev&skip=1&take=1&authorizedWorkCenterIds=WC-B&restrictToWorkCenters=false"
                + (workCenter is null ? "" : "&workCenterId=" + workCenter));
        return (response, mes, auth);
    }
}
