using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// Regression / DomainInvariant：#4320；HTTP fixture 证明授权入口，不替代真实页面与 MES 持久化验收。
public sealed class BusinessGatewayMesDowntimeOrganizationScopeTests
{
    private const string Permission = BusinessGatewayPermissions.MesDowntimeManage;

    [Fact]
    public async Task Organization_grant_allows_downtime_without_worker_work_center_assignments()
    {
        var (response, mes) = await RecordAsync(
            [OrganizationGrant()], "organization", "org-001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, mes.RecordDowntimeCallCount);
        Assert.Equal("WC-A", mes.LastRecordDowntimeRequest!.WorkCenterId);
        Assert.Equal("org-001", mes.LastRecordDowntimeRequest.OrganizationId);
        Assert.Equal("env-dev", mes.LastRecordDowntimeRequest.EnvironmentId);
    }

    [Theory]
    [InlineData("no-grant")]
    [InlineData("wrong-permission")]
    [InlineData("not-organization-wide")]
    [InlineData("wrong-grant-organization")]
    [InlineData("ungranted-selection")]
    [InlineData("wrong-authorized-organization")]
    [InlineData("wrong-authorized-environment")]
    public async Task Organization_selection_cannot_replace_the_current_permission_and_tenant_authorization(string scenario)
    {
        var grant = OrganizationGrant();
        AuthorizationScopeGrant[] grants = scenario switch
        {
            "no-grant" => [],
            "wrong-permission" => [grant with { ApplicablePermissionCodes = [BusinessGatewayPermissions.MesDowntimeRead] }],
            "not-organization-wide" => [grant with { OrganizationWide = false }],
            "wrong-grant-organization" => [grant with { ScopeId = "org-other" }],
            _ => [grant],
        };
        var (response, mes) = await RecordAsync(grants, "organization",
            scenario == "ungranted-selection" ? "org-other" : "org-001",
            authorizedOrganization: scenario == "wrong-authorized-organization" ? "org-other" : "org-001",
            authorizedEnvironment: scenario == "wrong-authorized-environment" ? "env-other" : "env-dev");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, mes.RecordDowntimeCallCount);
    }

    [Fact]
    public async Task Work_center_grant_still_rejects_another_target()
    {
        var (response, mes) = await RecordAsync(
            [new("role", "operator", "work-center", "WC-A", [Permission])], "work-center", "WC-A", "WC-B");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, mes.RecordDowntimeCallCount);
    }

    private static AuthorizationScopeGrant OrganizationGrant() =>
        new("role", "operator", "organization", "org-001", [Permission], OrganizationWide: true);

    private static async Task<(HttpResponseMessage Response, RecordingMesClient Mes)> RecordAsync(
        AuthorizationScopeGrant[] grants, string? scopeKind, string? scopeId, string workCenter = "WC-A",
        string authorizedOrganization = "org-001", string authorizedEnvironment = "env-dev")
    {
        var authorization = BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "operator",
            authorizedOrganization, authorizedEnvironment, scopeGrants: grants);
        var auth = new FakeBusinessGatewayAuthorizationClient(_ => true, allowedResult: authorization);
        var mes = new RecordingMesClient();
        var masterData = new RecordingMasterDataClient
        {
            PrincipalWorkContext = new("unmapped", null, [], [], [], [], [],
                [new("work-center", "WC-A", "工作中心 A", "assignment", [])], [], []),
        };
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.PostAsJsonAsync("/api/business-console/v2/mes/downtime-events", new
        {
            organizationId = "org-001", environmentId = "env-dev", workOrderId = "WO-DOWNTIME",
            workCenterId = workCenter, reasonCode = "DT-MECH", startedAtUtc = "2026-10-11T12:30:00Z",
            idempotencyKey = "issue-4320-downtime", scopeKind, scopeId,
        });
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        return (response, mes);
    }
}
