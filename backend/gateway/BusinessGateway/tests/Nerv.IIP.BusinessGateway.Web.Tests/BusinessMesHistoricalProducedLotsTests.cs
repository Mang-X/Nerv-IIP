using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #4499 / #4461 spec r1. HTTP test host + fake IAM/MES, not a real-stack lane.
public sealed class BusinessMesHistoricalProducedLotsTests
{
    [Fact]
    public async Task Traceability_reader_receives_unchanged_history_and_forwards_work_order_and_scope()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesTraceabilityRead);
        var mes = new MesHandler();
        await using var lease = Lease(auth, mes);
        using var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken("org-history", "env-history"));

        using var response = await client.GetAsync(Url("org-history", "env-history"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/business/v1/mes/traceability/work-orders/WO-HISTORY/produced-lots?organizationId=org-history&environmentId=env-history", Assert.Single(mes.Paths));
        var requirement = Assert.Single(auth.Requirements);
        Assert.Equal(BusinessGatewayPermissions.MesTraceabilityRead, requirement.PermissionCode);
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

    private static string Url(string organizationId, string environmentId) =>
        $"/api/business-console/v1/mes/traceability/work-orders/WO-HISTORY/produced-lots?organizationId={organizationId}&environmentId={environmentId}";

    private static BusinessGatewayTestHostLease Lease(IBusinessGatewayAuthorizationClient auth, MesHandler mes) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(new HttpBusinessMesClient(new HttpClient(mes) { BaseAddress = new Uri("http://mes.local") }));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
        });

    private sealed class MesHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"items":[{"producedLotNo":"LOT-HISTORY","reportNo":"REPORT-HISTORY","operationTaskId":"OP-10","quantity":10,"createdAtUtc":"2026-10-18T00:00:00Z","serialNo":"LEGACY-SN","serialNumbers":["SN-1","SN-2"]}]}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
