using System.Net;
using System.Text;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessOeeAggregateCapabilityTests
{
    private static readonly DateTimeOffset WindowStart = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
    private static readonly DateTimeOffset WindowEnd = DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public async Task Query_intersects_an_organization_grant_with_authoritative_device_identity_and_transmits_the_full_contract()
    {
        const string canonicalDeviceId = "018f47f1-40c4-7f6e-aafb-02f943701234";
        var masterData = new RoutingHandler(request => ResourceResponse(request,
            $$"""{"resourceType":"device-asset","code":"DEVICE-01","displayName":"Press","active":true,"snapshotVersion":"v1","siteCode":"SITE-01","workshopCode":"WS-01","lineCode":"LINE-01","workCenterCode":"WC-01","deviceAssetId":"{{canonicalDeviceId}}"}"""));
        var telemetry = new RoutingHandler(request => OeeResponse(request, canonicalDeviceId));
        var capability = Capability(masterData, telemetry);
        var request = new BusinessOeeAggregateRequest(
            "org-001",
            "env-dev",
            BusinessOeeAggregateDimension.Device,
            WindowStart,
            WindowEnd,
            DeviceAssetId: "DEVICE-01",
            WorkCenterId: "WC-01",
            ShiftCode: "SHIFT-A",
            LineCode: "LINE-01",
            WorkshopCode: "WS-01",
            BusinessDate: new DateOnly(2026, 8, 1),
            Skip: 4,
            Take: 25);

        // The exact shift query is an independent MasterData authority read.
        masterData.AddResponse("resourceType=shift", ResourceResponseBody(
            """{"resourceType":"shift","code":"SHIFT-A","displayName":"Shift A","active":true,"snapshotVersion":"v1"}"""));
        masterData.AddResponse("resourceType=work-center", ResourceResponseBody(
            """{"resourceType":"work-center","code":"WC-01","displayName":"WC","active":true,"snapshotVersion":"v1","plantCode":"SITE-01","lineCode":"LINE-01","workshopCode":"WS-01"}"""));
        masterData.AddResponse("resourceType=production-line", ResourceResponseBody(
            """{"resourceType":"production-line","code":"LINE-01","displayName":"Line","active":true,"snapshotVersion":"v1","siteCode":"SITE-01","workshopCode":"WS-01"}"""));
        masterData.AddResponse("resourceType=workshop", ResourceResponseBody(
            """{"resourceType":"workshop","code":"WS-01","displayName":"Workshop","active":true,"snapshotVersion":"v1","siteCode":"SITE-01"}"""));

        var response = await capability.QueryAsync(OrganizationAuthorization(), request, CancellationToken.None);

        Assert.Equal(canonicalDeviceId, Assert.Single(response.Buckets).DeviceAssetId);
        var query = telemetry.LastRequest!.RequestUri!.Query;
        Assert.Contains("dimension=device", query, StringComparison.Ordinal);
        Assert.Contains($"deviceAssetId={canonicalDeviceId}", query, StringComparison.Ordinal);
        Assert.Contains("workCenterId=WC-01", query, StringComparison.Ordinal);
        Assert.Contains("shiftCode=SHIFT-A", query, StringComparison.Ordinal);
        Assert.Contains("lineCode=LINE-01", query, StringComparison.Ordinal);
        Assert.Contains("workshopCode=WS-01", query, StringComparison.Ordinal);
        Assert.Contains("businessDate=2026-08-01", query, StringComparison.Ordinal);
        Assert.Contains("skip=4", query, StringComparison.Ordinal);
        Assert.Contains("take=25", query, StringComparison.Ordinal);
        Assert.Equal("internal-token", telemetry.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Query_projects_a_single_work_center_grant_when_the_request_has_no_spatial_filter()
    {
        var masterData = new RoutingHandler(request => ResourceResponse(request,
            """{"resourceType":"work-center","code":"WC-01","displayName":"WC","active":true,"snapshotVersion":"v1","plantCode":"SITE-01","lineCode":"LINE-01","workshopCode":"WS-01"}"""));
        var telemetry = new RoutingHandler(request => OeeResponse(request));
        var capability = Capability(masterData, telemetry);
        var authorization = ScopedAuthorization("work-center", "WC-01");

        await capability.QueryAsync(
            authorization,
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.WorkCenter,
                WindowStart,
                WindowEnd),
            CancellationToken.None);

        Assert.Contains("workCenterId=WC-01", telemetry.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_rejects_a_device_outside_the_granted_line_before_calling_telemetry()
    {
        var masterData = new RoutingHandler(request => ResourceResponse(request,
            """{"resourceType":"device-asset","code":"DEVICE-02","displayName":"Press","active":true,"snapshotVersion":"v1","siteCode":"SITE-01","workshopCode":"WS-01","lineCode":"LINE-02","workCenterCode":"WC-02","deviceAssetId":"018f47f1-40c4-7f6e-aafb-02f943709999"}"""));
        var telemetry = new RoutingHandler(_ => throw new InvalidOperationException("Telemetry must not be called."));
        var capability = Capability(masterData, telemetry);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => capability.QueryAsync(
            ScopedAuthorization("production-line", "LINE-01"),
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.Device,
                WindowStart,
                WindowEnd,
                DeviceAssetId: "DEVICE-02"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Null(telemetry.LastRequest);
    }

    [Fact]
    public async Task Query_fails_closed_when_master_data_returns_a_failure_envelope()
    {
        var masterData = new RoutingHandler(_ => Json("""{"success":false,"message":"broken","code":200}"""));
        var telemetry = new RoutingHandler(_ => throw new InvalidOperationException("Telemetry must not be called."));
        var capability = Capability(masterData, telemetry);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => capability.QueryAsync(
            OrganizationAuthorization(),
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.Device,
                WindowStart,
                WindowEnd,
                DeviceAssetId: "DEVICE-01"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
        Assert.Null(telemetry.LastRequest);
    }

    [Fact]
    public async Task Query_rejects_an_invalid_window_before_calling_downstream_services()
    {
        var masterData = new RoutingHandler(_ => throw new InvalidOperationException("MasterData must not be called."));
        var telemetry = new RoutingHandler(_ => throw new InvalidOperationException("Telemetry must not be called."));
        var capability = Capability(masterData, telemetry);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => capability.QueryAsync(
            OrganizationAuthorization(),
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.Day,
                WindowEnd,
                WindowStart),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("oee-aggregate-request-invalid", exception.Message);
        Assert.Null(masterData.LastRequest);
        Assert.Null(telemetry.LastRequest);
    }

    [Fact]
    public async Task Query_rejects_an_unknown_scope_grant_before_calling_downstream_services()
    {
        var masterData = new RoutingHandler(_ => throw new InvalidOperationException("MasterData must not be called."));
        var telemetry = new RoutingHandler(_ => throw new InvalidOperationException("Telemetry must not be called."));
        var capability = Capability(masterData, telemetry);
        var authorization = BusinessGatewayAuthorizationResult.Allowed(
            "user-001",
            "user",
            "operator",
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-engineer",
                    "device",
                    "DEVICE-01",
                    [BusinessGatewayPermissions.IiotTelemetryRead]),
            ]);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => capability.QueryAsync(
            authorization,
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.Device,
                WindowStart,
                WindowEnd,
                DeviceAssetId: "DEVICE-01"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("oee-scope-not-authorized", exception.Message);
        Assert.Null(masterData.LastRequest);
        Assert.Null(telemetry.LastRequest);
    }

    [Fact]
    public async Task Industrial_telemetry_adapter_rejects_a_cross_organization_success_response()
    {
        var telemetry = new RoutingHandler(request => OeeResponse(request, organizationId: "org-other"));
        using var http = new HttpClient(telemetry) { BaseAddress = new Uri("https://telemetry.test") };
        var client = new HttpBusinessIndustrialTelemetryClient(http);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.QueryOeeAggregatesAsync(
            "internal-token",
            new BusinessOeeAggregateRequest(
                "org-001",
                "env-dev",
                BusinessOeeAggregateDimension.Line,
                WindowStart,
                WindowEnd),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    private static BusinessOeeAggregateCapability Capability(
        HttpMessageHandler masterDataHandler,
        HttpMessageHandler telemetryHandler)
    {
        var masterDataHttp = new HttpClient(masterDataHandler) { BaseAddress = new Uri("https://master-data.test") };
        var telemetryHttp = new HttpClient(telemetryHandler) { BaseAddress = new Uri("https://telemetry.test") };
        var tokenProvider = new TestInternalServiceTokenProvider("internal-token");
        return new BusinessOeeAggregateCapability(
            new HttpBusinessMasterDataClient(masterDataHttp),
            new HttpBusinessIndustrialTelemetryClient(telemetryHttp),
            tokenProvider);
    }

    private static BusinessGatewayAuthorizationResult OrganizationAuthorization() =>
        BusinessGatewayAuthorizationResult.Allowed(
            "user-001",
            "user",
            "operator",
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-engineer",
                    "organization",
                    "org-001",
                    [BusinessGatewayPermissions.IiotTelemetryRead],
                    OrganizationWide: true),
            ]);

    private static BusinessGatewayAuthorizationResult ScopedAuthorization(string kind, string id) =>
        BusinessGatewayAuthorizationResult.Allowed(
            "user-001",
            "user",
            "operator",
            new AuthorizationDataScope(
                kind == "site" ? [id] : [],
                kind == "workshop" ? [id] : [],
                kind == "production-line" ? [id] : [],
                WorkCenterCodes: kind == "work-center" ? [id] : []),
            [new AuthorizationScopeGrant("role", "role-engineer", kind, id, [BusinessGatewayPermissions.IiotTelemetryRead])]);

    private static HttpResponseMessage ResourceResponse(HttpRequestMessage request, string resource)
    {
        Assert.Equal("internal-token", request.Headers.Authorization!.Parameter);
        return Json(ResourceResponseBody(resource));
    }

    private static string ResourceResponseBody(string resource) =>
        $$"""{"success":true,"data":{"resources":[{{resource}}],"total":1},"message":"","code":0}""";

    private static HttpResponseMessage OeeResponse(
        HttpRequestMessage request,
        string? deviceAssetId = null,
        string organizationId = "org-001")
    {
        var query = request.RequestUri!.Query;
        var dimension = QueryValue(query, "dimension") ?? "device";
        var skip = int.Parse(QueryValue(query, "skip") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var take = int.Parse(QueryValue(query, "take") ?? "100", System.Globalization.CultureInfo.InvariantCulture);
        var device = deviceAssetId ?? QueryValue(query, "deviceAssetId");
        return Json($$"""
            {"success":true,"data":{"organizationId":"{{organizationId}}","environmentId":"env-dev","dimension":"{{dimension}}","windowStartUtc":"2026-08-01T00:00:00+00:00","windowEndUtc":"2026-08-02T00:00:00+00:00","buckets":[{"dimension":"{{dimension}}","dimensionValue":"bucket-1","siteCode":"SITE-01","workshopCode":"WS-01","lineCode":"LINE-01","workCenterId":"WC-01","deviceAssetId":{{(device is null ? "null" : $"\"{device}\"")}},"shiftCode":"SHIFT-A","businessDate":"2026-08-01","bucketStartUtc":"2026-08-01T00:00:00+00:00","bucketEndUtc":"2026-08-02T00:00:00+00:00","deviceCount":1,"stateSampleCount":2,"productionFactCount":3,"availabilityRate":0.9,"performanceRate":0.8,"qualityRate":0.95,"oeeRate":0.684,"goodQuantity":10,"scrapQuantity":1,"reworkQuantity":0,"outputUomCode":"PCS","expectedOutputQuantity":12,"isDegraded":true,"degradedReasons":["runtimeStateCoverageIncomplete"]}],"totalCount":1,"skip":{{skip}},"take":{{take}}},"message":"","code":0}
            """);
    }

    private static string? QueryValue(string query, string key) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == key)
            .Select(pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty)
            .SingleOrDefault();

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> fallback) : HttpMessageHandler
    {
        private readonly Dictionary<string, string> responses = new(StringComparer.Ordinal);

        public HttpRequestMessage? LastRequest { get; private set; }

        public void AddResponse(string queryFragment, string response) => responses.Add(queryFragment, response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var match = responses.FirstOrDefault(item => request.RequestUri!.Query.Contains(item.Key, StringComparison.Ordinal));
            return Task.FromResult(match.Key is null ? fallback(request) : Json(match.Value));
        }
    }
}
