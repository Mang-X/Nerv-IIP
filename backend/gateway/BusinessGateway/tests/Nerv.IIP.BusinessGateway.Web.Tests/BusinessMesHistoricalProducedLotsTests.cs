using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract / Regression: #4504 / #4461 spec r1. HTTP test host + fake IAM/MES, not a real-stack lane.
public sealed class BusinessMesHistoricalProducedLotsTests
{
    [Fact]
    public async Task Traceability_reader_receives_unchanged_history_and_forwards_work_order_and_scope()
    {
        var auth = HistoryReader("organization", "org-history");
        var mes = new MesHandler();
        await using var lease = Lease(auth, mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken("org-history", "env-history"));

        using var response = await client.GetAsync(Url("org-history", "env-history"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, mes.Paths.Count);
        Assert.Equal("/api/business/v1/mes/traceability/work-orders/WO-HISTORY/produced-lots?organizationId=org-history&environmentId=env-history", mes.Paths[1]);
        Assert.DoesNotContain("assignedUserIds", mes.Paths[0]);
        var requirement = Assert.Single(auth.Requirements);
        Assert.Equal(BusinessGatewayPermissions.MesTraceabilityRead, requirement.PermissionCode);
        Assert.True(requirement.IncludePrincipalContext);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Equal("org-history", requirement.OrganizationId);
        Assert.Equal("env-history", requirement.EnvironmentId);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lot = Assert.Single(body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("LOT-HISTORY", lot.GetProperty("producedLotNo").GetString());
        Assert.Equal("REPORT-HISTORY", lot.GetProperty("reportNo").GetString());
        Assert.Equal("OP-10", lot.GetProperty("operationTaskId").GetString());
        Assert.Equal(10m, lot.GetProperty("quantity").GetDecimal());
        Assert.Equal(DateTimeOffset.Parse("2026-10-18T00:00:00Z"), lot.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal("LEGACY-SN", lot.GetProperty("serialNo").GetString());
        Assert.Equal(new[] { "SN-1", "SN-2" }, lot.GetProperty("serialNumbers").EnumerateArray().Select(x => x.GetString()));
        Assert.False(lot.TryGetProperty("remainingQuantity", out _));
    }

    [Theory]
    [InlineData(BusinessGatewayPermissions.MesWorkOrdersRead)]
    [InlineData(BusinessGatewayPermissions.MesReceiptsRead)]
    public async Task Readers_without_traceability_permission_cannot_query_history(string permission)
    {
        var mes = new MesHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(permission), mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken("org-history", "env-history"));
        using var response = await client.GetAsync(Url("org-history", "env-history"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mes.Paths);
    }

    [Theory]
    [InlineData("other-org", "env-history")]
    [InlineData("org-history", "other-env")]
    public async Task Unauthorized_context_is_rejected_before_reading_mes(string organizationId, string environmentId)
    {
        var auth = new FakeBusinessGatewayAuthorizationClient(requirement =>
            requirement.PermissionCode == BusinessGatewayPermissions.MesTraceabilityRead
            && requirement.OrganizationId == "org-history" && requirement.EnvironmentId == "env-history");
        var mes = new MesHandler();
        await using var lease = Lease(auth, mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken(organizationId, environmentId));
        using var response = await client.GetAsync(Url(organizationId, environmentId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mes.Paths);
    }

    [Theory]
    [InlineData("user-admin", true)]
    [InlineData("user-other", false)]
    public async Task Self_reader_reads_only_assigned_work_order_before_querying_history(string assignedUserId, bool allowed)
    {
        var auth = HistoryReader("self", "user-admin");
        var mes = new MesHandler(assignedUserId);
        await using var lease = Lease(auth, mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken("org-history", "env-history"));

        using var response = await client.GetAsync(Url("org-history", "env-history"));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? 2 : 1, mes.Paths.Count);
        Assert.Contains("authorizedAssignedUserIds=user-admin", mes.Paths[0]);
        Assert.Contains("workOrderId=WO-HISTORY", mes.Paths[0]);
        Assert.True(Assert.Single(auth.Requirements).IncludePrincipalContext);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        if (allowed)
        {
            using var body = JsonDocument.Parse(payload);
            var lot = Assert.Single(body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal("LOT-HISTORY", lot.GetProperty("producedLotNo").GetString());
            Assert.Equal(10m, lot.GetProperty("quantity").GetDecimal());
        }
        else
        {
            using var body = JsonDocument.Parse(payload);
            Assert.Equal("work-scope-not-authorized", body.RootElement.GetProperty("message").GetString());
            Assert.DoesNotContain("LOT-HISTORY", payload);
            Assert.DoesNotContain("REPORT-HISTORY", payload);
            Assert.DoesNotContain("OP-10", payload);
            Assert.DoesNotContain("quantity", payload);
            Assert.DoesNotContain(mes.Paths, path => path.Contains("produced-lots", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("user-admin", "team-other", true, true)]
    [InlineData("user-other", "team-review", true, true)]
    [InlineData("user-other", "team-other", true, false)]
    [InlineData("user-other", "team-review", false, false)]
    public async Task Multi_scope_reader_reads_only_work_orders_in_current_permission_grants(
        string assignedUserId, string teamId, bool teamHasTraceabilityRead, bool allowed)
    {
        var auth = new FakeBusinessGatewayAuthorizationClient(
            requirement => requirement.PermissionCode == BusinessGatewayPermissions.MesTraceabilityRead,
            scopeGrants:
            [
                new AuthorizationScopeGrant("membership", "trace-self", "self", "user-admin",
                    [BusinessGatewayPermissions.MesTraceabilityRead]),
                new AuthorizationScopeGrant("role", "trace-team", "team", "team-review",
                    [teamHasTraceabilityRead ? BusinessGatewayPermissions.MesTraceabilityRead : BusinessGatewayPermissions.MesWorkOrdersRead]),
            ]);
        var mes = new MesHandler(assignedUserId, teamId);
        await using var lease = Lease(auth, mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken("org-history", "env-history"));

        using var response = await client.GetAsync(Url("org-history", "env-history"));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? 2 : 1, mes.Paths.Count);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://mes.local" + mes.Paths[0]).Query);
        Assert.Equal("user-admin", query["authorizedAssignedUserIds"]);
        Assert.Equal(teamHasTraceabilityRead ? "team-review" : "", query.GetValueOrDefault("authorizedTeamIds").ToString());
        Assert.False(query.ContainsKey("assignedUserIds"));
        Assert.False(query.ContainsKey("teamIds"));
        Assert.False(query.ContainsKey("authorizedWorkCenterIds"));
        Assert.Equal("WO-HISTORY", query["workOrderId"]);
        if (allowed)
        {
            using var body = JsonDocument.Parse(payload);
            var lot = Assert.Single(body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal("LOT-HISTORY", lot.GetProperty("producedLotNo").GetString());
            Assert.Equal("REPORT-HISTORY", lot.GetProperty("reportNo").GetString());
            Assert.Equal(10m, lot.GetProperty("quantity").GetDecimal());
        }
        else
        {
            using var body = JsonDocument.Parse(payload);
            Assert.Equal("work-scope-not-authorized", body.RootElement.GetProperty("message").GetString());
            Assert.DoesNotContain("LOT-HISTORY", payload);
            Assert.DoesNotContain("REPORT-HISTORY", payload);
            Assert.DoesNotContain("OP-10", payload);
            Assert.DoesNotContain("quantity", payload);
            Assert.DoesNotContain(mes.Paths, path => path.Contains("produced-lots", StringComparison.Ordinal));
        }
    }

    private static FakeBusinessGatewayAuthorizationClient HistoryReader(string kind, string id) =>
        new(requirement => requirement.PermissionCode == BusinessGatewayPermissions.MesTraceabilityRead,
            scopeGrants: [new AuthorizationScopeGrant("role", "trace-reader", kind, id,
                [BusinessGatewayPermissions.MesTraceabilityRead], OrganizationWide: kind == "organization")]);

    private static string Url(string organizationId, string environmentId) =>
        $"/api/business-console/v1/mes/traceability/work-orders/WO-HISTORY/produced-lots?organizationId={organizationId}&environmentId={environmentId}";

    private static BusinessGatewayTestHostLease Lease(IBusinessGatewayAuthorizationClient auth, MesHandler mes) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(new RecordingMasterDataClient
            {
                PrincipalWorkContext = new BusinessMasterDataPrincipalWorkContextResponse(
                    "resolved", null, [], [], [], [], [],
                    [new("self", "user-admin", "当前用户", "worker", []),
                     new("organization", "org-history", "当前组织", "organization", []),
                     new("team", "team-review", "当前班组", "team-member", [])],
                    ["self", "organization", "team"], []),
            });
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(new HttpBusinessMesClient(new HttpClient(mes) { BaseAddress = new Uri("http://mes.local") }));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
        });

    private sealed class MesHandler(string assignedUserId = "user-other", string teamId = "team-other") : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            Paths.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath == "/api/business/v1/mes/work-orders")
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                Assert.Equal("WO-HISTORY", query["workOrderId"]);
                Assert.False(query.ContainsKey("assignedUserIds"));
                Assert.False(query.ContainsKey("teamIds"));
                var visible = true;
                if (query.ContainsKey("authorizedAssignedUserIds") || query.ContainsKey("authorizedTeamIds") || query.ContainsKey("authorizedWorkCenterIds"))
                {
                    visible = query["authorizedAssignedUserIds"].ToString().Split(',').Contains(assignedUserId)
                        || query.GetValueOrDefault("authorizedTeamIds").ToString().Split(',').Contains(teamId);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(visible
                        ? "{\"items\":[{\"workOrderId\":\"WO-HISTORY\"}],\"total\":1}"
                        : "{\"items\":[],\"total\":0}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"items":[{"producedLotNo":"LOT-HISTORY","reportNo":"REPORT-HISTORY","operationTaskId":"OP-10","quantity":10,"createdAtUtc":"2026-10-18T00:00:00Z","serialNo":"LEGACY-SN","serialNumbers":["SN-1","SN-2"]}]}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
