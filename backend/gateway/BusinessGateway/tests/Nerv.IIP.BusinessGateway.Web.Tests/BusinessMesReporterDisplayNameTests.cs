using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessMesReporterDisplayNameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reporting_reader_sees_Iam_display_name_without_worker_or_account_management(bool detail)
    {
        var mes = new MesHandler(detail);
        var iam = new IamHandler();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesReportingRead);
        await using var lease = Lease(auth, mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(Url(detail));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var reports = detail ? new[] { data.GetProperty("report") } : data.GetProperty("items").EnumerateArray().ToArray();
        Assert.All(reports, report =>
        {
            Assert.Equal("user-admin", report.GetProperty("reportedBy").GetString());
            Assert.Equal("王建国", report.GetProperty("reportedByDisplayName").GetString());
            Assert.Equal(12, report.GetProperty("goodQuantity").GetDecimal());
            Assert.False(report.TryGetProperty("loginName", out _));
            Assert.False(report.TryGetProperty("enabled", out _));
        });
        var query = Assert.Single(iam.Queries);
        Assert.Contains("organizationId=org-001", query);
        Assert.Contains("environmentId=env-dev", query);
        Assert.Contains("includeDisabled=true", query);
        Assert.Equal(1, query.Split("userIds=user-admin").Length - 1);
        Assert.DoesNotContain("user-other", query);
        Assert.Contains("pageSize=1", query);
        Assert.Equal(1, mes.Calls);
        Assert.Equal(BusinessGatewayPermissions.MesReportingRead, auth.LastRequirement!.PermissionCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_reporting_permission_reads_neither_Mes_nor_Iam(bool detail)
    {
        var mes = new MesHandler(detail);
        var iam = new IamHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesOperationsRead), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(detail));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, mes.Calls);
        Assert.Empty(iam.Queries);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("王建国", false)]
    public async Task Missing_member_or_display_name_does_not_substitute_login_name(string? displayName, bool memberExists)
    {
        var mes = new MesHandler(true);
        var iam = new IamHandler(displayName, memberExists);
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesReportingRead), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("data").GetProperty("report");
        Assert.Equal("user-admin", report.GetProperty("reportedBy").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("reportedByDisplayName").ValueKind);
    }

    [Fact]
    public async Task Empty_report_page_does_not_scan_member_accounts()
    {
        var mes = new MesHandler(false, []);
        var iam = new IamHandler();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesReportingRead), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(iam.Queries);
    }

    [Fact]
    public async Task Large_report_page_resolves_all_references_within_Iam_batch_limit()
    {
        var ids = Enumerable.Range(1, 101).Select(index => $"user-{index:D3}").ToArray();
        var mes = new MesHandler(false, ids);
        var iam = new IamHandler(namesForRequestedIds: true);
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesReportingRead), mes, iam);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var response = await client.GetAsync(Url(false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, iam.Queries.Count);
        var requestedIds = iam.Queries.SelectMany(query => query.TrimStart('?').Split('&')
            .Where(part => part.StartsWith("userIds=", StringComparison.Ordinal)).Select(part => part[8..])).ToArray();
        Assert.Equal(ids.Order(), requestedIds.Order());
        Assert.Contains("pageSize=100", iam.Queries[0]);
        Assert.Contains("pageSize=1", iam.Queries[1]);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var reports = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(ids.Length, reports.Length);
        Assert.Equal(ids, reports.Select(report => report.GetProperty("reportedBy").GetString()));
        Assert.Equal(ids.Select(id => $"姓名-{id}"), reports.Select(report => report.GetProperty("reportedByDisplayName").GetString()));
    }

    private static string Url(bool detail) => "/api/business-console/v1/mes/production-reports" +
        (detail ? "/PRPT-000001" : "") + "?organizationId=org-001&environmentId=env-dev";

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

    private sealed class MesHandler(bool detail, string[]? reporterIds = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            var report = new { productionReportId = "pr-1", reportNo = "PRPT-000001", workOrderId = "WO-1", operationTaskId = "OP-1", goodQuantity = 12, scrapQuantity = 0, reworkQuantity = 0, reportedAtUtc = "2026-10-13T01:00:00Z", reportedBy = "user-admin" };
            return Task.FromResult(detail
                ? Json(new { report, consumedMaterialLots = Array.Empty<object>(), laborAllocations = Array.Empty<object>() })
                : Json(new { items = (reporterIds ?? ["user-admin", "user-admin"]).Select(id => new
                {
                    report.productionReportId, report.reportNo, report.workOrderId, report.operationTaskId,
                    report.goodQuantity, report.scrapQuantity, report.reworkQuantity, report.reportedAtUtc, reportedBy = id,
                }).ToArray(), total = reporterIds?.Length ?? 2 }));
        }
    }

    private sealed class IamHandler(string? displayName = "王建国", bool memberExists = true, bool namesForRequestedIds = false) : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/internal/iam/v1/member-accounts", request.RequestUri!.AbsolutePath);
            Assert.Equal("internal-token", request.Headers.Authorization?.Parameter);
            Queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
            if (namesForRequestedIds)
            {
                var ids = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query)["userIds"].ToArray();
                return Task.FromResult(Json(new
                {
                    pageIndex = 1, pageSize = ids.Length, totalCount = ids.Length,
                    items = ids.Select(id => new { userId = id, loginName = $"login-{id}", displayName = $"姓名-{id}", enabled = true }),
                }));
            }
            var account = new { userId = "user-admin", loginName = "admin", displayName, enabled = false };
            return Task.FromResult(Json(new { pageIndex = 1, pageSize = 1, totalCount = memberExists ? 1 : 0, items = memberExists ? new[] { account } : [] }));
        }
    }
}
