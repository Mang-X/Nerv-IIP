using System.Net;
using System.Text;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #2780 首件门禁对 #2779 读契约的取值消费：只有 not-required 与 decided+passed 放行，其余一律拒。
/// 每个取值都是线上承重值，按 wire 字符串钉桩。
/// </summary>
public sealed class HttpMesFirstArticleGateTests
{
    [Theory]
    [InlineData("not-required", null)]
    [InlineData("decided", "passed")]
    // 任务尚未开出：开单的唯一触发点就是本次报工的事件，这一次就是「首件那一件」（拍板决策 2）。
    [InlineData("not-opened", null)]
    public async Task Confirmed_or_triggering_first_article_allows_the_report(string status, string? result)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, Envelope(status, result)));
        var gate = CreateGate(handler);

        await gate.EnsureBatchReportAllowedAsync("org/a", "env dev", "WO+1", "OP 10", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/api/business/v1/quality/first-article-confirmation?organizationId=org%2Fa&environmentId=env%20dev&workOrderId=WO%2B1&operationId=OP%2010",
            request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer test-internal-token", request.Headers.Authorization?.ToString());
    }

    [Theory]
    [InlineData("pending", null, "首件尚未判定")]
    // Quality 还不掌握该工序事实：它靠工单发布事实到达恢复、不靠报工恢复，拒掉它不会锁死任何东西。
    [InlineData("not-synchronized", null, "工单发布事实尚未同步")]
    [InlineData("decided", "rejected", "首件判定不合格")]
    // 让步放行是对已产出那批件的处置结论，不解锁后续批量生产（#2780 决策 1）。
    [InlineData("decided", "conditional-release", "让步放行")]
    public async Task Unconfirmed_first_article_is_a_business_rejection(
        string status,
        string? result,
        string expectedMessage)
    {
        var gate = CreateGate(new RecordingHandler(_ => Json(HttpStatusCode.OK, Envelope(status, result))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FIRST_ARTICLE_SOURCE_UNAVAILABLE", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown-status", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"workOrderId\":\"WO-001\",\"operationId\":\"OP-10\",\"status\":\"released\",\"result\":null,\"attemptNumber\":null}}")]
    [InlineData("missing-status", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"workOrderId\":\"WO-001\",\"operationId\":\"OP-10\",\"result\":\"passed\"}}")]
    [InlineData("decided-without-result", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"workOrderId\":\"WO-001\",\"operationId\":\"OP-10\",\"status\":\"decided\",\"result\":null}}")]
    [InlineData("decided-unknown-result", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"workOrderId\":\"WO-001\",\"operationId\":\"OP-10\",\"status\":\"decided\",\"result\":\"waived\"}}")]
    [InlineData("missing-data", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":null}")]
    [InlineData("unsuccessful-envelope", "{\"success\":false,\"message\":\"failed\",\"code\":200,\"data\":{\"status\":\"not-required\"}}")]
    [InlineData("malformed-json", "{not-json")]
    public async Task Unreadable_confirmation_fails_closed_as_source_unavailable(string _, string responseBody)
    {
        var gate = CreateGate(new RecordingHandler(_ => Json(HttpStatusCode.OK, responseBody)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", CancellationToken.None));

        Assert.StartsWith("FIRST_ARTICLE_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Non_success_status_fails_closed_as_source_unavailable(HttpStatusCode statusCode)
    {
        // 响应体故意是一份「放行」信封：只有状态码这一道判据能拒绝它，删掉它就会放行。
        var gate = CreateGate(new RecordingHandler(_ => Json(statusCode, Envelope("not-required", null))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", CancellationToken.None));

        Assert.StartsWith("FIRST_ARTICLE_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_failure_fails_closed_as_source_unavailable()
    {
        var gate = CreateGate(new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", CancellationToken.None));

        Assert.StartsWith("FIRST_ARTICLE_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_timeout_fails_closed_as_source_unavailable()
    {
        var gate = CreateGate(new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("request timeout"))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", CancellationToken.None));

        Assert.StartsWith("FIRST_ARTICLE_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_reclassification()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("不可达。");
        });
        var gate = CreateGate(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnsureBatchReportAllowedAsync(
            "org-001", "env-dev", "WO-001", "OP-10", cancellation.Token));
    }

    private static HttpMesFirstArticleGate CreateGate(HttpMessageHandler handler) =>
        new(
            new MesQualityHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://quality") }),
            new TestTokenProvider());

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static string Envelope(string status, string? result)
    {
        var resultLiteral = result is null ? "null" : $"\"{result}\"";
        return $"{{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{{\"workOrderId\":\"WO-001\",\"operationId\":\"OP-10\",\"status\":\"{status}\",\"result\":{resultLiteral},\"attemptNumber\":1,\"inspectionTaskId\":null,\"inspectionRecordId\":null}}}}";
    }

    private sealed class TestTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            this.responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return responder(request, cancellationToken);
        }
    }
}
