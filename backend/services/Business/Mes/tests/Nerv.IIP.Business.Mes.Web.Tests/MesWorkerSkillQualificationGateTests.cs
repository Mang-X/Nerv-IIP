using System.Net;
using System.Text;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkerSkillQualificationGateTests
{
    [Fact]
    public async Task Qualified_worker_uses_exact_scoped_directory_query_and_internal_bearer()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            QualifiedEnvelope().Replace("worker-001", "user+1", StringComparison.Ordinal)
                .Replace("cnc-operation", "cnc/level-2", StringComparison.Ordinal)));
        var gate = CreateGate(handler);

        await gate.EnsureQualifiedAsync("org/a", "env dev", "user+1", "cnc/level-2", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/api/business/v1/master-data/workers?organizationId=org%2Fa&environmentId=env%20dev&userId=user%2B1&skillCode=cnc%2Flevel-2&includeDisabled=true&pageIndex=1&pageSize=2",
            request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer test-internal-token", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Missing_required_skill_does_not_call_master_data()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("不应调用 MasterData。"));
        var gate = CreateGate(handler);

        await gate.EnsureQualifiedAsync("org-001", "env-dev", null, null, CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Required_skill_without_assigned_worker_is_rejected_before_network_call()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("不应调用 MasterData。"));
        var gate = CreateGate(handler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", null, "cnc-operation", CancellationToken.None));

        Assert.Contains("必须先指派人员", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("missing-worker", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("missing-skill", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":2}}")]
    public async Task Worker_without_current_qualification_is_a_business_rejection(string _, string responseBody)
    {
        var gate = CreateGate(new RecordingHandler(_ => Json(HttpStatusCode.OK, responseBody)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.DoesNotContain("WORKER_SKILL_SOURCE_UNAVAILABLE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("技能缺失、登记停用、尚未生效或已过期", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "active", "已停用")]
    [InlineData(true, "departed", "不是在职状态")]
    public async Task Inactive_or_departed_worker_is_rejected(bool active, string employmentStatus, string expectedMessage)
    {
        var body = QualifiedEnvelope(active: active, employmentStatus: employmentStatus);
        var gate = CreateGate(new RecordingHandler(_ => Json(HttpStatusCode.OK, body)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-worker", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-999\",\"employeeNo\":\"E999\",\"name\":\"乙\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("duplicate-workers", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v1\"},{\"userId\":\"worker-001\",\"employeeNo\":\"E002\",\"name\":\"甲2\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v2\"}],\"totalCount\":2,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("empty-level", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"\"}],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("mismatched-page", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"employmentStatus\":\"active\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":2,\"pageSize\":2}}")]
    [InlineData("missing-employment-status", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"active\":true,\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("missing-active", "{\"success\":true,\"message\":\"\",\"code\":200,\"data\":{\"items\":[{\"userId\":\"worker-001\",\"employeeNo\":\"E001\",\"name\":\"甲\",\"employmentStatus\":\"active\",\"teams\":[],\"skills\":[{\"skillCode\":\"cnc-operation\",\"skillName\":\"数控操作\",\"level\":\"L2\"}],\"snapshotVersion\":\"v1\"}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("unsuccessful-envelope", "{\"success\":false,\"message\":\"failed\",\"code\":200,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":2}}")]
    [InlineData("malformed-json", "{not-json")]
    public async Task Malformed_or_non_closed_response_fails_as_source_unavailable(string _, string responseBody)
    {
        var gate = CreateGate(new RecordingHandler(_ => Json(HttpStatusCode.OK, responseBody)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.StartsWith("WORKER_SKILL_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Non_success_status_fails_as_source_unavailable(HttpStatusCode statusCode)
    {
        var gate = CreateGate(new RecordingHandler(_ => Json(statusCode, "{}")));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.StartsWith("WORKER_SKILL_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_failure_fails_as_source_unavailable()
    {
        var gate = CreateGate(new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.StartsWith("WORKER_SKILL_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_timeout_fails_as_source_unavailable()
    {
        var gate = CreateGate(new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("request timeout"))));

        var exception = await Assert.ThrowsAsync<KnownException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", CancellationToken.None));

        Assert.StartsWith("WORKER_SKILL_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
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
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnsureQualifiedAsync(
            "org-001", "env-dev", "worker-001", "cnc-operation", cancellation.Token));
    }

    private static HttpMesWorkerSkillQualificationGate CreateGate(HttpMessageHandler handler) =>
        new(
            new MesMasterDataHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://master-data") }),
            new TestTokenProvider());

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static string QualifiedEnvelope(bool active = true, string employmentStatus = "active") =>
        """
        {"success":true,"message":"","code":200,"data":{"items":[{"userId":"worker-001","employeeNo":"E001","name":"甲","employmentStatus":"__STATUS__","active":__ACTIVE__,"teams":[],"skills":[{"skillCode":"cnc-operation","skillName":"数控操作","level":"L2"}],"snapshotVersion":"v1"}],"totalCount":1,"pageIndex":1,"pageSize":2}}
        """
        .Replace("__STATUS__", employmentStatus, StringComparison.Ordinal)
        .Replace("__ACTIVE__", active ? "true" : "false", StringComparison.Ordinal);

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
