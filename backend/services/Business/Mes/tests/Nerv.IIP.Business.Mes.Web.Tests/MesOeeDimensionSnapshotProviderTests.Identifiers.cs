using System.Net;
using System.Text;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed partial class MesOeeDimensionSnapshotProviderTests
{
    private const string CanonicalDeviceId = "7f50f991-8788-4e52-a204-0383e13282bc";

    [Fact]
    public async Task MasterData_provider_uses_exact_canonical_filters_and_resolves_public_device_id()
    {
        var handler = new CanonicalIdentifierHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));

        var snapshot = await provider.CaptureAsync(
            new MesOeeDimensionSnapshotRequest(
                "org-001",
                "env-dev",
                "WC-TASK",
                CanonicalDeviceId,
                "SHIFT-NIGHT"),
            CancellationToken.None);

        Assert.Equal("WC-CANONICAL", snapshot.WorkCenterCode);
        Assert.Equal(CanonicalDeviceId, snapshot.DeviceAssetId);
        Assert.Equal("SITE-CANONICAL", snapshot.SiteCode);
        Assert.Equal("WS-CANONICAL", snapshot.WorkshopCode);
        Assert.Equal("LINE-CANONICAL", snapshot.LineCode);
        Assert.Equal("Asia/Shanghai", snapshot.SiteTimezone);
        Assert.Equal(new TimeOnly(20, 0), snapshot.ShiftStartsAt);

        var deviceRequest = Assert.Single(
            handler.RequestUris,
            uri => uri.Query.Contains("resourceType=device-asset", StringComparison.Ordinal));
        Assert.Contains($"deviceAssetId={CanonicalDeviceId}", deviceRequest.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("keyword=", deviceRequest.Query, StringComparison.Ordinal);
        var shiftRequest = Assert.Single(
            handler.RequestUris,
            uri => uri.Query.Contains("resourceType=shift", StringComparison.Ordinal));
        Assert.Contains("shiftCode=SHIFT-NIGHT", shiftRequest.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MasterData_provider_degrades_noncanonical_missing_ambiguous_and_truncated_identifiers()
    {
        var modelOnly = await CaptureIdentifierAsync(
            "MODEL-ONLY",
            "SHIFT-NAME",
            CanonicalIdentifierHandler.ModelOnly);
        Assert.Null(modelOnly.SiteCode);
        Assert.Null(modelOnly.ShiftStartsAt);

        var missing = await CaptureIdentifierAsync(
            "DEV-MISSING",
            "SHIFT-MISSING",
            CanonicalIdentifierHandler.Missing);
        Assert.Null(missing.SiteCode);
        Assert.Null(missing.ShiftStartsAt);

        var ambiguous = await CaptureIdentifierAsync(
            CanonicalDeviceId,
            "SHIFT-AMBIGUOUS",
            CanonicalIdentifierHandler.Ambiguous);
        Assert.Null(ambiguous.SiteCode);
        Assert.Null(ambiguous.ShiftStartsAt);

        var truncated = await CaptureIdentifierAsync(
            CanonicalDeviceId,
            "SHIFT-NIGHT",
            CanonicalIdentifierHandler.Truncated);
        Assert.Null(truncated.SiteCode);
        Assert.Null(truncated.ShiftStartsAt);
    }

    private static async Task<MesOeeDimensionSnapshot> CaptureIdentifierAsync(
        string deviceAssetId,
        string shiftCode,
        string scenario)
    {
        using var httpClient = new HttpClient(new CanonicalIdentifierHandler(scenario))
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));
        return await provider.CaptureAsync(
            new MesOeeDimensionSnapshotRequest(
                "org-001",
                "env-dev",
                "WC-TASK",
                deviceAssetId,
                shiftCode),
            CancellationToken.None);
    }

    private sealed class CanonicalIdentifierHandler(string scenario = CanonicalIdentifierHandler.Canonical)
        : HttpMessageHandler
    {
        internal const string Canonical = "canonical";
        internal const string ModelOnly = "model-only";
        internal const string Missing = "missing";
        internal const string Ambiguous = "ambiguous";
        internal const string Truncated = "truncated";

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            RequestUris.Add(uri);
            var resourceType = uri.Query.Contains("resourceType=device-asset", StringComparison.Ordinal)
                ? "device-asset"
                : uri.Query.Contains("resourceType=site", StringComparison.Ordinal)
                    ? "site"
                    : "shift";
            var data = (scenario, resourceType) switch
            {
                (Missing, "device-asset" or "shift") => Envelope("", total: 0),
                (ModelOnly, "device-asset") => Envelope(Device("DEV-CODE", CanonicalDeviceId, "MODEL-ONLY"), total: 1),
                (ModelOnly, "shift") => Envelope(Shift("SHIFT-CODE", "SHIFT-NAME"), total: 1),
                (Ambiguous, "device-asset") => Envelope(
                    Device("DEV-CODE", CanonicalDeviceId, "MODEL-A") + "," +
                    Device(CanonicalDeviceId, "2e151fcf-1b08-4db1-a2a1-6c1d7827a777", "MODEL-B"),
                    total: 2),
                (Ambiguous, "shift") => Envelope(
                    Shift("SHIFT-AMBIGUOUS", "Night A") + "," + Shift("SHIFT-AMBIGUOUS", "Night B"),
                    total: 2),
                (Truncated, "device-asset") => Envelope(Device("DEV-CODE", CanonicalDeviceId, "MODEL-A"), 5001, truncated: true),
                (Truncated, "shift") => Envelope(Shift("SHIFT-NIGHT", "Night"), 5001, truncated: true),
                (_, "device-asset") => Envelope(Device("DEV-CODE", CanonicalDeviceId, "MODEL-A"), total: 1),
                (_, "site") => Envelope(
                    "{\"resourceType\":\"site\",\"code\":\"SITE-CANONICAL\",\"displayName\":\"Site\",\"active\":true,\"snapshotVersion\":\"v1\",\"timezone\":\"Asia/Shanghai\"}",
                    total: 1),
                _ => Envelope(Shift("SHIFT-NIGHT", "Night"), total: 1),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(data, Encoding.UTF8, "application/json"),
            });
        }

        private static string Device(string code, string deviceAssetId, string model) =>
            $"{{\"resourceType\":\"device-asset\",\"code\":\"{code}\",\"displayName\":\"{model}\",\"active\":true,\"snapshotVersion\":\"v1\",\"deviceAssetId\":\"{deviceAssetId}\",\"siteCode\":\"SITE-CANONICAL\",\"workshopCode\":\"WS-CANONICAL\",\"lineCode\":\"LINE-CANONICAL\",\"workCenterCode\":\"WC-CANONICAL\"}}";

        private static string Shift(string code, string name) =>
            $"{{\"resourceType\":\"shift\",\"code\":\"{code}\",\"displayName\":\"{name}\",\"active\":true,\"snapshotVersion\":\"v1\",\"startsAt\":\"20:00:00\",\"endsAt\":\"04:00:00\",\"crossesMidnight\":true,\"paidMinutes\":450,\"breakMinutes\":30}}";

        private static string Envelope(string resources, int total, bool truncated = false) =>
            $"{{\"data\":{{\"resources\":[{resources}],\"total\":{total},\"truncated\":{truncated.ToString().ToLowerInvariant()},\"limit\":5000}},\"success\":true,\"message\":\"\",\"code\":0}}";
    }
}
