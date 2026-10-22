using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleReportingDimensionWireTests
{
    [Fact]
    public async Task Public_resource_endpoint_preserves_site_shift_and_device_asset_reporting_dimensions()
    {
        var downstream = new ReportingDimensionHandler();
        var masterData = new HttpBusinessMasterDataClient(new HttpClient(downstream)
        {
            BaseAddress = new Uri("http://master-data.local"),
        });
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services =>
            {
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(masterData);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new TestInternalServiceTokenProvider("reporting-dimension-token"));
            });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var site = await GetSingleResourceAsync(client, "site");
        Assert.Equal("Asia/Shanghai", site.RootElement.GetProperty("timezone").GetString());

        using var shift = await GetSingleResourceAsync(client, "shift");
        Assert.Equal("20:00:00", shift.RootElement.GetProperty("startsAt").GetString());
        Assert.Equal("04:00:00", shift.RootElement.GetProperty("endsAt").GetString());
        Assert.True(shift.RootElement.GetProperty("crossesMidnight").GetBoolean());
        Assert.Equal(420, shift.RootElement.GetProperty("paidMinutes").GetInt32());
        Assert.Equal(60, shift.RootElement.GetProperty("breakMinutes").GetInt32());

        using var deviceAsset = await GetSingleResourceAsync(client, "device-asset");
        Assert.Equal("SITE-001", deviceAsset.RootElement.GetProperty("siteCode").GetString());
        Assert.Equal("WS-001", deviceAsset.RootElement.GetProperty("workshopCode").GetString());
        Assert.Equal("LINE-001", deviceAsset.RootElement.GetProperty("lineCode").GetString());
        Assert.Equal("WC-001", deviceAsset.RootElement.GetProperty("workCenterCode").GetString());

        Assert.Equal(["site", "shift", "device-asset"], downstream.ResourceTypes);
    }

    private static async Task<JsonDocument> GetSingleResourceAsync(HttpClient client, string resourceType)
    {
        using var response = await client.GetAsync(
            "/api/business-console/v1/master-data/resources" +
            $"?organizationId=org-001&environmentId=env-dev&resourceType={resourceType}&skip=0&take=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = Assert.Single(document.RootElement
            .GetProperty("data")
            .GetProperty("resources")
            .EnumerateArray());
        return JsonDocument.Parse(resource.GetRawText());
    }

    private sealed class ReportingDimensionHandler : HttpMessageHandler
    {
        public List<string> ResourceTypes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            var resourceType = query.Contains("resourceType=device-asset", StringComparison.Ordinal)
                ? "device-asset"
                : query.Contains("resourceType=shift", StringComparison.Ordinal)
                    ? "shift"
                    : query.Contains("resourceType=site", StringComparison.Ordinal)
                        ? "site"
                        : throw new InvalidOperationException($"Unexpected resource query: {query}");
            ResourceTypes.Add(resourceType);

            var resource = resourceType switch
            {
                "site" => """
                    {"resourceType":"site","code":"SITE-001","displayName":"上海工厂","active":true,"snapshotVersion":"v1","timezone":"Asia/Shanghai"}
                    """,
                "shift" => """
                    {"resourceType":"shift","code":"SHIFT-NIGHT","displayName":"夜班","active":true,"snapshotVersion":"v1","startsAt":"20:00:00","endsAt":"04:00:00","crossesMidnight":true,"paidMinutes":420,"breakMinutes":60}
                    """,
                _ => """
                    {"resourceType":"device-asset","code":"DEV-001","displayName":"灌装机","active":true,"snapshotVersion":"v1","siteCode":"SITE-001","workshopCode":"WS-001","lineCode":"LINE-001","workCenterCode":"WC-001"}
                    """,
            };
            var payload = $"{{\"data\":{{\"resources\":[{resource}],\"total\":1}},\"success\":true,\"message\":\"\",\"code\":0}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
