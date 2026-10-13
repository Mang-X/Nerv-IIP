using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesTraceabilityDisplayNameTests
{
    public static TheoryData<string, bool> RoutesAndQuality => new()
    {
        { "work-orders/WO-1", false }, { "work-orders/WO-1", true },
        { "batches/SN-1", false }, { "batches/SN-1", true },
        { "material-lots/LOT-1", false }, { "material-lots/LOT-1", true },
    };

    [Theory]
    [MemberData(nameof(RoutesAndQuality))]
    public async Task Authorized_graph_resolves_only_operators_and_preserves_identity_edges_and_quality_layer(string route, bool quality)
    {
        var mes = new MesHandler();
        var iam = new IamHandler();
        var permissions = quality
            ? new[] { BusinessGatewayPermissions.MesTraceabilityRead, BusinessGatewayPermissions.MesQualityRead }
            : [BusinessGatewayPermissions.MesTraceabilityRead];
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(permissions), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(route));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var nodes = data.GetProperty("nodes").EnumerateArray().ToArray();
        var operators = nodes.Where(node => node.GetProperty("nodeType").GetString() == "Operator").ToArray();
        Assert.Equal(new[] { "user-admin", "user-admin", "user-empty", "user-missing" }, operators.Select(node => node.GetProperty("nodeId").GetString()));
        Assert.Equal(new[] { "李工", "李工", "姓名不可用", "姓名不可用" }, operators.Select(node => node.GetProperty("displayName").GetString()));
        Assert.All(operators, node => Assert.Equal("Reported", node.GetProperty("status").GetString()));
        var order = Assert.Single(nodes, node => node.GetProperty("nodeType").GetString() == "WorkOrder");
        Assert.Equal("user-other", order.GetProperty("nodeId").GetString());
        Assert.Equal("订单一", order.GetProperty("displayName").GetString());
        Assert.Equal("Released", order.GetProperty("status").GetString());
        Assert.Equal(quality, nodes.Any(node => node.GetProperty("nodeType").GetString() == "InspectionResult"));
        var edges = data.GetProperty("edges").EnumerateArray().ToArray();
        var reportedBy = Assert.Single(edges, edge => edge.GetProperty("relationType").GetString() == "reported-by");
        Assert.Equal("user-other", reportedBy.GetProperty("fromNodeId").GetString());
        Assert.Equal("user-admin", reportedBy.GetProperty("toNodeId").GetString());
        Assert.Equal(quality, edges.Any(edge => edge.GetProperty("relationType").GetString() == "inspected-as"));
        var query = Assert.Single(iam.Queries);
        Assert.Contains("organizationId=org-001", query);
        Assert.Contains("environmentId=env-dev", query);
        Assert.Contains("includeDisabled=true", query);
        Assert.Equal(1, query.Split("userIds=user-admin").Length - 1);
        Assert.Contains("userIds=user-empty", query);
        Assert.Contains("userIds=user-missing", query);
        Assert.DoesNotContain("user-other", query);
        Assert.Contains("pageSize=3", query);
        Assert.Equal(1, mes.Calls);
    }

    [Theory]
    [InlineData("work-orders/WO-1")]
    [InlineData("batches/SN-1")]
    [InlineData("material-lots/LOT-1")]
    public async Task Without_traceability_permission_neither_graph_nor_names_are_read(string route)
    {
        var mes = new MesHandler();
        var iam = new IamHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesQualityRead), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(route));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, mes.Calls);
        Assert.Empty(iam.Queries);
    }

    private static string Url(string route) => $"/api/business-console/v1/mes/traceability/{route}?organizationId=org-001&environmentId=env-dev";

    private static BusinessGatewayTestHostLease Lease(IBusinessGatewayAuthorizationClient auth, MesHandler mes, IamHandler iam) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(new HttpBusinessMesClient(new HttpClient(mes) { BaseAddress = new Uri("http://mes.local") }));
            services.RemoveAll<IBusinessIamAccountDirectoryClient>();
            services.AddSingleton<IBusinessIamAccountDirectoryClient>(new HttpBusinessIamAccountDirectoryClient(new HttpClient(iam) { BaseAddress = new Uri("http://iam.local") }));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
        });

    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { success = true, data, code = 0, message = "" }), Encoding.UTF8, "application/json"),
    };

    private sealed class MesHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(new BusinessConsoleMesTraceabilityResponse(
                [new("user-admin", "Operator", "user-admin", "Reported"),
                 new("user-admin", "Operator", "user-admin", "Reported"),
                 new("user-empty", "Operator", "login-empty", "Reported"),
                 new("user-missing", "Operator", "login-missing", "Reported"),
                 new("user-other", "WorkOrder", "订单一", "Released"),
                 new("DEF-1", "InspectionResult", "SCRAP", "ScrapAccepted")],
                [new("user-other", "user-admin", "reported-by"), new("user-other", "DEF-1", "inspected-as")])));
        }
    }

    private sealed class IamHandler : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/internal/iam/v1/member-accounts", request.RequestUri!.AbsolutePath);
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            Queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
            return Task.FromResult(Json(new
            {
                pageIndex = 1, pageSize = 3, totalCount = 2,
                items = new[]
                {
                    new { userId = "user-admin", loginName = "admin", displayName = " 李工 ", enabled = false },
                    new { userId = "user-empty", loginName = "empty", displayName = "   ", enabled = true },
                },
            }));
        }
    }
}
