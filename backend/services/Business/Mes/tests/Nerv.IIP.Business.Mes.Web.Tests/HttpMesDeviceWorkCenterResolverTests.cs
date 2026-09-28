using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Planning;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3878：设备归属的工作中心直接向 MasterData 查询。三类结果必须分开：
/// 查到 → 工作中心编码；主数据确实没有（空列表 / 多条 / 无工作中心）→ <c>null</c>；
/// 主数据不可达或应答异常 → 抛出供消息系统重试，绝不能被当成「没有归属」。
/// </summary>
public sealed class HttpMesDeviceWorkCenterResolverTests
{
    [Fact]
    public async Task Returns_the_current_masterdata_work_center_of_the_device()
    {
        var handler = new QueueHttpMessageHandler(Json(Resources(Device("DEV-CNC-01", "WC-ROD-01"))));

        var workCenter = await CreateResolver(handler).ResolveAsync("org-001", "env-dev", "DEV-CNC-01", CancellationToken.None);

        Assert.Equal("WC-ROD-01", workCenter);
        var uri = Assert.Single(handler.RequestUris);
        Assert.Equal("/api/business/v1/master-data/resources", uri.AbsolutePath);
        Assert.Contains("organizationId=org-001", uri.Query, StringComparison.Ordinal);
        Assert.Contains("environmentId=env-dev", uri.Query, StringComparison.Ordinal);
        Assert.Contains("resourceType=device-asset", uri.Query, StringComparison.Ordinal);
        Assert.Contains("deviceAssetId=DEV-CNC-01", uri.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer internal-token", Assert.Single(handler.AuthorizationHeaders));
    }

    public static TheoryData<string, string> UnresolvedResponses => new()
    {
        { "not-found", Resources() },
        { "ambiguous", Resources(Device("DEV-CNC-01", "WC-ROD-01"), Device("DEV-CNC-01", "WC-ROD-02")) },
        { "work-center-missing", Resources(Device("DEV-CNC-01", null)) },
        { "work-center-blank", Resources(Device("DEV-CNC-01", "  ")) },
    };

    [Theory]
    [MemberData(nameof(UnresolvedResponses))]
    public async Task Returns_null_when_masterdata_has_no_unique_work_center(string scenario, string body)
    {
        _ = scenario;
        var handler = new QueueHttpMessageHandler(Json(body));

        Assert.Null(await CreateResolver(handler).ResolveAsync("org-001", "env-dev", "DEV-CNC-01", CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_when_masterdata_rejects_an_ambiguous_device_reference()
    {
        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"success":false,"message":"主数据设备引用 'DEV-CNC-01' 对应多条记录，无法唯一确定。"}""", Encoding.UTF8, "application/json"),
        });

        Assert.Null(await CreateResolver(handler).ResolveAsync("org-001", "env-dev", "DEV-CNC-01", CancellationToken.None));
    }

    public static TheoryData<string> OutageScenarios => new() { "http-500", "http-401", "truncated", "unreadable", "transport" };

    [Theory]
    [MemberData(nameof(OutageScenarios))]
    public async Task Throws_for_retry_when_masterdata_is_unavailable(string scenario)
    {
        HttpMessageHandler handler = scenario switch
        {
            "http-500" => new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }),
            "http-401" => new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(string.Empty) }),
            "truncated" => new QueueHttpMessageHandler(Json("""{"data":{"resources":[],"total":0,"truncated":true}}""")),
            "unreadable" => new QueueHttpMessageHandler(Json("not-json")),
            _ => new ThrowingHttpMessageHandler(),
        };

        await Assert.ThrowsAsync<MesMasterDataUnavailableException>(
            () => CreateResolver(handler).ResolveAsync("org-001", "env-dev", "DEV-CNC-01", CancellationToken.None));
    }

    private static HttpMesDeviceWorkCenterResolver CreateResolver(HttpMessageHandler handler) =>
        new(
            new MesMasterDataHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://master-data") }),
            new TestInternalServiceTokenProvider(),
            NullLogger<HttpMesDeviceWorkCenterResolver>.Instance);

    private static string Device(string code, string? workCenterCode) =>
        "{\"resourceType\":\"device-asset\",\"code\":\"" + code +
        "\",\"displayName\":\"CNC\",\"active\":true,\"snapshotVersion\":\"v1\",\"deviceAssetId\":\"019c9c62-9987-7af2-8fa2-3fd936098265\",\"workCenterCode\":" +
        (workCenterCode is null ? "null" : "\"" + workCenterCode + "\"") + "}";

    private static string Resources(params string[] items) =>
        "{\"data\":{\"resources\":[" + string.Join(",", items) + "],\"total\":" + items.Length + ",\"truncated\":false}}";

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

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed record TestInternalServiceTokenProvider : Nerv.IIP.ServiceAuth.IInternalServiceTokenProvider
    {
        public string BearerToken => "internal-token";
    }
}
