using System.Net;
using System.Text;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductionReportOeeDimensionSnapshotProviderTests
{
    [Fact]
    public async Task Code_alias_resolves_to_canonical_device_and_event_time_dimensions()
    {
        var canonicalDeviceId = Guid.Parse("019c9c62-9987-7af2-8fa2-3fd936098265");
        var handler = new QueueHttpMessageHandler(
            Json("""
                {"data":{"resources":[{"resourceType":"device-asset","code":"DEV-CNC-01","displayName":"CNC 01","active":true,"snapshotVersion":"v1","deviceAssetId":"019c9c62-9987-7af2-8fa2-3fd936098265","workCenterCode":"WC-MACH","siteCode":"SITE-SH","workshopCode":"WS-MACH","lineCode":"LINE-CNC"}],"total":1,"truncated":false}}
                """),
            Json("""
                {"data":{"resources":[{"resourceType":"site","code":"SITE-SH","displayName":"Shanghai","active":true,"snapshotVersion":"v1","timezone":"Asia/Shanghai"}],"total":1,"truncated":false}}
                """),
            Json("""
                {"data":{"resources":[{"resourceType":"shift","code":"EARLY","displayName":"Early","active":true,"snapshotVersion":"v1","startsAt":"08:00:00","endsAt":"16:00:00","crossesMidnight":false,"paidMinutes":450,"breakMinutes":30}],"total":1,"truncated":false}}
                """));
        var provider = new HttpProductionReportOeeDimensionSnapshotProvider(
            new MesMasterDataHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://master-data") }),
            new TestInternalServiceTokenProvider());

        var snapshot = await provider.CaptureAsync(
            new ProductionReportOeeDimensionSnapshotRequest(
                "org-001", "env-dev", "DEV-CNC-01", "WC-LEGACY", "EARLY"),
            CancellationToken.None);

        Assert.Equal("resolved", snapshot.ResolutionStatus);
        Assert.Null(snapshot.DegradedReason);
        Assert.Equal(canonicalDeviceId.ToString(), snapshot.DeviceAssetId);
        Assert.Equal("WC-MACH", snapshot.WorkCenterId);
        Assert.Equal("SITE-SH", snapshot.SiteCode);
        Assert.Equal("WS-MACH", snapshot.WorkshopCode);
        Assert.Equal("LINE-CNC", snapshot.LineCode);
        Assert.Equal("Asia/Shanghai", snapshot.SiteTimezone);
        Assert.Equal("EARLY", snapshot.ShiftCode);
        Assert.Equal(new TimeOnly(8, 0), snapshot.ShiftStartsAt);
        Assert.Equal(new TimeOnly(16, 0), snapshot.ShiftEndsAt);
        Assert.False(snapshot.ShiftCrossesMidnight);
        Assert.Equal(450, snapshot.ShiftPaidMinutes);
        Assert.Equal(30, snapshot.ShiftBreakMinutes);
        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Contains("resourceType=device-asset", uri.Query, StringComparison.Ordinal),
            uri => Assert.Contains("resourceType=site", uri.Query, StringComparison.Ordinal),
            uri => Assert.Contains("resourceType=shift", uri.Query, StringComparison.Ordinal));
        Assert.Contains("deviceAssetId=DEV-CNC-01", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("siteCode=SITE-SH", handler.RequestUris[1].Query, StringComparison.Ordinal);
        Assert.Contains("shiftCode=EARLY", handler.RequestUris[2].Query, StringComparison.Ordinal);
        Assert.All(handler.AuthorizationHeaders, header => Assert.Equal("Bearer internal-token", header));
    }

    [Fact]
    public async Task Canonical_guid_is_accepted_by_the_same_exact_lookup()
    {
        const string canonicalDeviceId = "019c9c62-9987-7af2-8fa2-3fd936098265";
        var handler = new QueueHttpMessageHandler(
            Device(canonicalDeviceId, "WC-MACH"),
            Site(),
            Shift());
        var provider = CreateProvider(handler);

        var snapshot = await provider.CaptureAsync(
            new ProductionReportOeeDimensionSnapshotRequest(
                "org-001", "env-dev", canonicalDeviceId, "WC-LEGACY", "EARLY"),
            CancellationToken.None);

        Assert.Equal("resolved", snapshot.ResolutionStatus);
        Assert.Equal(canonicalDeviceId, snapshot.DeviceAssetId);
        Assert.Contains($"deviceAssetId={canonicalDeviceId}", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DeviceResolutionFailures))]
    public async Task Device_resolution_failures_preserve_raw_identity_without_splicing_current_hierarchy(
        HttpResponseMessage response,
        string expectedReason)
    {
        var provider = CreateProvider(new QueueHttpMessageHandler(response));

        var snapshot = await provider.CaptureAsync(
            new ProductionReportOeeDimensionSnapshotRequest(
                "org-001", "env-dev", "DEV-RAW", "WC-LEGACY", "EARLY"),
            CancellationToken.None);

        Assert.Equal("degraded", snapshot.ResolutionStatus);
        Assert.Equal(expectedReason, snapshot.DegradedReason);
        Assert.Equal("DEV-RAW", snapshot.DeviceAssetId);
        Assert.Equal("WC-LEGACY", snapshot.WorkCenterId);
        Assert.Null(snapshot.SiteCode);
        Assert.Null(snapshot.WorkshopCode);
        Assert.Null(snapshot.LineCode);
        Assert.Null(snapshot.SiteTimezone);
        Assert.Null(snapshot.ShiftCode);
    }

    public static TheoryData<HttpResponseMessage, string> DeviceResolutionFailures => new()
    {
        {
            Json("""{"data":{"resources":[],"total":0,"truncated":false}}"""),
            "not-found"
        },
        {
            Json("""{"success":false,"message":"设备引用无法唯一确定。"}"""),
            "ambiguous"
        },
        {
            Json("""{"data":{"resources":[],"total":501,"truncated":true,"limit":500}}"""),
            "truncated"
        },
        {
            Json("{"),
            "master-data-invalid-response"
        },
        {
            Json("""{"data":{"total":1}}"""),
            "master-data-invalid-response"
        },
        {
            Json("""{"data":{"resources":[null],"total":1}}"""),
            "master-data-invalid-response"
        },
        {
            Json("""{"data":{"resources":[],"total":1}}"""),
            "master-data-invalid-response"
        },
        {
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "master-data-http-503"
        },
        {
            Device("019c9c62-9987-7af2-8fa2-3fd936098265", null),
            "device-work-center-missing"
        },
    };

    [Theory]
    [MemberData(nameof(IncompleteResolvedDimensions))]
    public async Task Incomplete_site_or_shift_response_is_degraded_without_partial_current_dimensions(
        HttpResponseMessage site,
        HttpResponseMessage? shift,
        string expectedReason)
    {
        var responses = shift is null
            ? new[] { Device("019c9c62-9987-7af2-8fa2-3fd936098265", "WC-MACH"), site }
            : new[] { Device("019c9c62-9987-7af2-8fa2-3fd936098265", "WC-MACH"), site, shift };
        var provider = CreateProvider(new QueueHttpMessageHandler(responses));

        var snapshot = await provider.CaptureAsync(
            new ProductionReportOeeDimensionSnapshotRequest(
                "org-001", "env-dev", "DEV-RAW", "WC-LEGACY", shift is null ? null : "EARLY"),
            CancellationToken.None);

        Assert.Equal("degraded", snapshot.ResolutionStatus);
        Assert.Equal(expectedReason, snapshot.DegradedReason);
        Assert.Equal("DEV-RAW", snapshot.DeviceAssetId);
        Assert.Equal("WC-LEGACY", snapshot.WorkCenterId);
        Assert.Null(snapshot.SiteCode);
        Assert.Null(snapshot.SiteTimezone);
        Assert.Null(snapshot.ShiftCode);
    }

    public static TheoryData<HttpResponseMessage, HttpResponseMessage?, string> IncompleteResolvedDimensions => new()
    {
        {
            Json("""{"data":{"resources":[{"resourceType":"site","code":"SITE-SH","displayName":"Shanghai","active":true,"snapshotVersion":"v1"}],"total":1,"truncated":false}}"""),
            null,
            "site-timezone-missing"
        },
        {
            Site(),
            Json("""{"data":{"resources":[{"resourceType":"shift","code":"EARLY","displayName":"Early","active":true,"snapshotVersion":"v1","startsAt":"08:00:00","crossesMidnight":false,"paidMinutes":450,"breakMinutes":30}],"total":1,"truncated":false}}"""),
            "shift-definition-invalid"
        },
    };

    private static HttpProductionReportOeeDimensionSnapshotProvider CreateProvider(HttpMessageHandler handler) =>
        new(
            new MesMasterDataHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://master-data") }),
            new TestInternalServiceTokenProvider());

    private static HttpResponseMessage Device(string deviceAssetId, string? workCenterCode) =>
        Json(
            "{\"data\":{\"resources\":[{\"resourceType\":\"device-asset\",\"code\":\"DEV-CNC-01\",\"displayName\":\"CNC 01\",\"active\":true,\"snapshotVersion\":\"v1\",\"deviceAssetId\":\"" +
            deviceAssetId +
            "\",\"workCenterCode\":" +
            JsonValue(workCenterCode) +
            ",\"siteCode\":\"SITE-SH\",\"workshopCode\":\"WS-MACH\",\"lineCode\":\"LINE-CNC\"}],\"total\":1,\"truncated\":false}}");

    private static HttpResponseMessage Site() =>
        Json("""{"data":{"resources":[{"resourceType":"site","code":"SITE-SH","displayName":"Shanghai","active":true,"snapshotVersion":"v1","timezone":"Asia/Shanghai"}],"total":1,"truncated":false}}""");

    private static HttpResponseMessage Shift() =>
        Json("""{"data":{"resources":[{"resourceType":"shift","code":"EARLY","displayName":"Early","active":true,"snapshotVersion":"v1","startsAt":"08:00:00","endsAt":"16:00:00","crossesMidnight":false,"paidMinutes":450,"breakMinutes":30}],"total":1,"truncated":false}}""");

    private static string JsonValue(string? value) => value is null ? "null" : $"\"{value}\"";

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<Uri> RequestUris { get; } = [];
        public List<string?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed record TestInternalServiceTokenProvider : Nerv.IIP.ServiceAuth.IInternalServiceTokenProvider
    {
        public string BearerToken => "internal-token";
    }
}
