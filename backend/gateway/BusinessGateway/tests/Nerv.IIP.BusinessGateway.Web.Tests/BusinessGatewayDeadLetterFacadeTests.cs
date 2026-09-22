using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #3739：死信运维面 facade。扇出覆盖共享清单 <see cref="IntegrationEventDeadLetterServices.All"/>
/// 的全部服务，来源清单不在本文件里点名——写死名单就测不出「新增服务漏接网关」。
/// </summary>
public sealed class BusinessGatewayDeadLetterFacadeTests
{
    private const string Scope = "organizationId=org-001&environmentId=env-dev";

    [Fact]
    public async Task List_returns_every_reachable_source_and_marks_only_the_broken_one_unavailable()
    {
        var broken = IntegrationEventDeadLetterServices.Erp.Name;
        var deadLetters = new FakeDeadLetterClient
        {
            ListFailures = { [broken] = () => new HttpRequestException("connection refused") },
        };
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var lease = Lease(auth, deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync($"/api/business-console/v1/dead-letters?{Scope}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;

        var expectedServices = IntegrationEventDeadLetterServices.All
            .Select(service => service.Name)
            .Where(name => name != broken)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        // 单源不可用不吞成空列表：其余每个服务的行都还在。
        Assert.Equal(
            expectedServices,
            data["items"]!.AsArray()
                .Select(item => item!["service"]!.GetValue<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        var statuses = data["sourceStatuses"]!.AsArray()
            .ToDictionary(
                node => node!["service"]!.GetValue<string>(),
                node => (node!["status"]!.GetValue<string>(), node["reason"]?.GetValue<string>()),
                StringComparer.Ordinal);
        Assert.Equal(IntegrationEventDeadLetterServices.All.Count, statuses.Count);
        Assert.Equal(("unavailable", "sourceUnavailable"), statuses[broken]);
        Assert.All(
            statuses.Where(entry => entry.Key != broken),
            entry => Assert.Equal(("available", (string?)null), entry.Value));
    }

    [Fact]
    public async Task A_source_that_never_answers_is_reported_as_a_timeout_rather_than_hanging_the_page()
    {
        var slow = IntegrationEventDeadLetterServices.Wms.Name;
        var deadLetters = new FakeDeadLetterClient { NeverAnsweringServices = { slow } };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        // 「不挂住」是本用例要证的事实之一，因此请求本身走受治理的超时原语：
        // 扇出若漏掉逐源上限，这里会以命名超时失败，而不是把整条 lane 拖死。
        var response = await TestTimeout.RunAsync(
            "business-console dead-letter fan-out with a source that never answers",
            async token => await client.GetAsync($"/api/business-console/v1/dead-letters?{Scope}", token),
            TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        var status = Assert.Single(
            data["sourceStatuses"]!.AsArray(),
            node => node!["service"]!.GetValue<string>() == slow);
        Assert.Equal("unavailable", status!["status"]!.GetValue<string>());
        Assert.Equal("sourceTimeout", status["reason"]!.GetValue<string>());
        Assert.DoesNotContain(
            data["items"]!.AsArray(),
            item => item!["service"]!.GetValue<string>() == slow);
    }

    [Fact]
    public async Task List_fans_out_to_every_registered_service_and_pushes_all_filters_into_the_downstream_query()
    {
        var handler = new RecordingDeadLetterHandler();
        await using var lease = LeaseOverHttp(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/dead-letters?{Scope}"
            + "&consumerName=erp.machine-overhead&eventType=OperationMachineOverhead"
            + "&failureCode=unavailable-machine-time-fact&status=Pending"
            + "&deadLetteredFromUtc=2026-09-01T00%3A00%3A00%2B00%3A00"
            + "&deadLetteredToUtc=2026-09-30T00%3A00%3A00%2B00%3A00&take=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 每个登记服务各被请求一次，路径由共享清单的前缀 + 共享路由后缀拼出。
        var expected = IntegrationEventDeadLetterServices.All
            .Select(service => service.RoutePrefix + IntegrationEventDeadLetterRoutes.Collection)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, handler.Paths.OrderBy(path => path, StringComparer.Ordinal).ToArray());

        Assert.All(handler.Queries, query =>
        {
            Assert.Contains("consumerName=erp.machine-overhead", query, StringComparison.Ordinal);
            Assert.Contains("eventType=OperationMachineOverhead", query, StringComparison.Ordinal);
            Assert.Contains("failureCode=unavailable-machine-time-fact", query, StringComparison.Ordinal);
            Assert.Contains("status=Pending", query, StringComparison.Ordinal);
            Assert.Contains("deadLetteredFromUtc=2026-09-01T00%3A00%3A00.0000000%2B00%3A00", query, StringComparison.Ordinal);
            Assert.Contains("deadLetteredToUtc=2026-09-30T00%3A00%3A00.0000000%2B00%3A00", query, StringComparison.Ordinal);
            Assert.Contains("take=50", query, StringComparison.Ordinal);
        });
        Assert.All(handler.Authorizations, value => Assert.Equal("Bearer internal-dlq-token", value));
    }

    [Fact]
    public async Task Selecting_one_service_queries_only_that_source()
    {
        var deadLetters = new FakeDeadLetterClient();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            $"/api/business-console/v1/dead-letters?{Scope}&service={IntegrationEventDeadLetterServices.Mes.Name}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([IntegrationEventDeadLetterServices.Mes.Name], deadLetters.ListedServices);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        Assert.Single(data["sourceStatuses"]!.AsArray());
    }

    [Fact]
    public async Task An_unknown_service_is_rejected_before_any_downstream_call()
    {
        var deadLetters = new FakeDeadLetterClient();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync($"/api/business-console/v1/dead-letters?{Scope}&service=NotAService");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unknown-dead-letter-service", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(deadLetters.ListedServices);
    }

    [Fact]
    public async Task Metrics_group_counts_per_service_and_only_total_the_sources_that_answered()
    {
        var broken = IntegrationEventDeadLetterServices.Quality.Name;
        var deadLetters = new FakeDeadLetterClient
        {
            MetricsFailures = { [broken] = () => new HttpRequestException("connection refused") },
        };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync($"/api/business-console/v1/dead-letters/metrics?{Scope}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        var reached = IntegrationEventDeadLetterServices.All.Count - 1;
        Assert.Equal(reached, data["services"]!.AsArray().Count);
        Assert.Equal(reached * FakeDeadLetterClient.PendingPerService, data["pendingCount"]!.GetValue<int>());
        Assert.Equal(reached * FakeDeadLetterClient.PendingPerService, data["actionableCount"]!.GetValue<int>());
        var perService = Assert.Single(
            data["services"]!.AsArray(),
            node => node!["service"]!.GetValue<string>() == IntegrationEventDeadLetterServices.Mes.Name);
        var eventType = Assert.Single(perService!["metrics"]!["eventTypes"]!.AsArray());
        Assert.Equal(FakeDeadLetterClient.PendingPerService, eventType!["pendingCount"]!.GetValue<int>());
        Assert.DoesNotContain(
            data["services"]!.AsArray(),
            node => node!["service"]!.GetValue<string>() == broken);
    }

    [Fact]
    public async Task Replaying_one_dead_letter_echoes_the_downstream_result_status()
    {
        var deadLetters = new FakeDeadLetterClient();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var deadLetterId = Guid.CreateVersion7();

        // 作用域走请求体，与生成客户端对 POST facade 的调用形状一致（既有 mark-read facade 同形）。
        var response = await client.PostAsJsonAsync(
            $"/api/business-console/v1/dead-letters/{IntegrationEventDeadLetterServices.Erp.Name}/{deadLetterId}/replay",
            new { organizationId = "org-001", environmentId = "env-dev" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        Assert.Equal(deadLetterId, data["id"]!.GetValue<Guid>());
        Assert.False(data["succeeded"]!.GetValue<bool>());
        // 受控枚举：前端要按 noHandler / failed 分开显示，拿到的必须是枚举取值而不是自由文本。
        Assert.Equal("noHandler", data["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Batch_replay_echoes_one_result_status_per_row()
    {
        var deadLetters = new FakeDeadLetterClient();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            $"/api/business-console/v1/dead-letters/{IntegrationEventDeadLetterServices.Wms.Name}/replay-batch",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                consumerName = "wms.outbound-completed",
                status = "pending",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!["items"]!.AsArray();
        Assert.Equal(["replayed", "failed"], items.Select(item => item!["status"]!.GetValue<string>()).ToArray());
        Assert.Equal("wms.outbound-completed", deadLetters.LastBatchRequest!.ConsumerName);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, deadLetters.LastBatchRequest.Status);
    }

    [Fact]
    public async Task Ignoring_a_dead_letter_forwards_the_reason_and_returns_the_updated_row()
    {
        var deadLetters = new FakeDeadLetterClient();
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var deadLetterId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business-console/v1/dead-letters/{IntegrationEventDeadLetterServices.Mes.Name}/{deadLetterId}/ignore",
            new { organizationId = "org-001", environmentId = "env-dev", reason = "上游已下线，不再重放" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("上游已下线，不再重放", deadLetters.LastIgnoreReason);
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        Assert.Equal("ignored", data["status"]!.GetValue<string>());
    }

    /// <summary>
    /// #3738 引入 <c>business.dlq.read</c> / <c>business.dlq.manage</c> 时两码零消费方，
    /// 强制点由本票落地：读面只认 read 码，写面只认 manage 码，且拒绝发生在任何下游调用之前。
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/business-console/v1/dead-letters", "business.dlq.read")]
    [InlineData("GET", "/api/business-console/v1/dead-letters/metrics", "business.dlq.read")]
    [InlineData("GET", "/api/business-console/v1/dead-letters/Mes/0198f6a0-0000-7000-8000-000000000001", "business.dlq.read")]
    [InlineData("POST", "/api/business-console/v1/dead-letters/Mes/0198f6a0-0000-7000-8000-000000000001/replay", "business.dlq.manage")]
    [InlineData("POST", "/api/business-console/v1/dead-letters/Mes/replay-batch", "business.dlq.manage")]
    [InlineData("POST", "/api/business-console/v1/dead-letters/Mes/0198f6a0-0000-7000-8000-000000000001/ignore", "business.dlq.manage")]
    public async Task Every_facade_route_enforces_its_dead_letter_permission_code(
        string method,
        string path,
        string expectedPermissionCode)
    {
        var otherCode = expectedPermissionCode == BusinessGatewayPermissions.DeadLettersRead
            ? BusinessGatewayPermissions.DeadLettersManage
            : BusinessGatewayPermissions.DeadLettersRead;
        var deadLetters = new FakeDeadLetterClient();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(otherCode);
        await using var lease = Lease(auth, deadLetters);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await SendAsync(client, method, $"{path}?{Scope}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(expectedPermissionCode, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Empty(deadLetters.ListedServices);
        Assert.Equal(0, deadLetters.WriteCallCount);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url) =>
        method == "GET"
            ? client.GetAsync(url)
            : client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));

    private static BusinessGatewayTestHostLease Lease(
        IBusinessGatewayAuthorizationClient auth,
        IBusinessDeadLetterClient deadLetters) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessDeadLetterClient>();
            services.AddSingleton(deadLetters);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-dlq-token"));
        });

    private static BusinessGatewayTestHostLease LeaseOverHttp(
        IBusinessGatewayAuthorizationClient auth,
        RecordingDeadLetterHandler handler) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessDeadLetterClient>();
            services.AddSingleton<IBusinessDeadLetterClient>(new HttpBusinessDeadLetterClient(new HttpClient(handler)));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-dlq-token"));
        });

    /// <summary>真实 HTTP 面：钉住扇出确实按共享清单的前缀发出请求，且筛选条件进了下游 query。</summary>
    private sealed class RecordingDeadLetterHandler : HttpMessageHandler
    {
        private readonly Lock syncRoot = new();

        public List<string> Paths { get; } = [];

        public List<string> Queries { get; } = [];

        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                Paths.Add(request.RequestUri!.AbsolutePath);
                Queries.Add(request.RequestUri.Query);
                Authorizations.Add(request.Headers.Authorization?.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"success":true,"data":{"items":[]}}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class FakeDeadLetterClient : IBusinessDeadLetterClient
    {
        public const int PendingPerService = 2;

        private readonly Lock syncRoot = new();
        private readonly List<string> listedServices = [];

        public Dictionary<string, Func<Exception>> ListFailures { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Func<Exception>> MetricsFailures { get; } = new(StringComparer.Ordinal);

        public HashSet<string> NeverAnsweringServices { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> ListedServices
        {
            get
            {
                lock (syncRoot)
                {
                    return listedServices.ToArray();
                }
            }
        }

        public int WriteCallCount { get; private set; }

        public ReplayIntegrationEventDeadLetterBatchRequest? LastBatchRequest { get; private set; }

        public string? LastIgnoreReason { get; private set; }

        public async Task<IntegrationEventDeadLetterListResponse> ListAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            ListIntegrationEventDeadLettersRequest request,
            CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                listedServices.Add(source.Name);
            }

            await AnswerOrFailAsync(source, ListFailures, cancellationToken);
            return new IntegrationEventDeadLetterListResponse([Row(source, IntegrationEventDeadLetterStatus.Pending)]);
        }

        public async Task<IntegrationEventDeadLetterMetricsResponse> GetMetricsAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            CancellationToken cancellationToken)
        {
            await AnswerOrFailAsync(source, MetricsFailures, cancellationToken);
            return new IntegrationEventDeadLetterMetricsResponse(
                PendingPerService,
                PendingPerService,
                0,
                0,
                0,
                [new IntegrationEventDeadLetterEventTypeMetricsResponse($"{source.Name}Event", PendingPerService, PendingPerService, 0, 0, 0)]);
        }

        public Task<IntegrationEventDeadLetterDetailResponse> GetAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            Guid deadLetterId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Detail(source, deadLetterId, IntegrationEventDeadLetterStatus.Pending));

        public Task<IntegrationEventDeadLetterReplayResponse> ReplayAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            Guid deadLetterId,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            return Task.FromResult(new IntegrationEventDeadLetterReplayResponse(
                deadLetterId,
                false,
                IntegrationEventDeadLetterReplayStatus.NoHandler,
                "No replay handler is registered."));
        }

        public Task<IntegrationEventDeadLetterBatchReplayResponse> ReplayBatchAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            ReplayIntegrationEventDeadLetterBatchRequest request,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            LastBatchRequest = request;
            return Task.FromResult(new IntegrationEventDeadLetterBatchReplayResponse(
            [
                new(Guid.CreateVersion7(), true, IntegrationEventDeadLetterReplayStatus.Replayed, null),
                new(Guid.CreateVersion7(), false, IntegrationEventDeadLetterReplayStatus.Failed, "下游仍不可用"),
            ]));
        }

        public Task<IntegrationEventDeadLetterDetailResponse> IgnoreAsync(
            string internalBearerToken,
            BusinessDeadLetterSource source,
            Guid deadLetterId,
            IgnoreIntegrationEventDeadLetterRequest request,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            LastIgnoreReason = request.Reason;
            return Task.FromResult(Detail(source, deadLetterId, IntegrationEventDeadLetterStatus.Ignored));
        }

        private async Task AnswerOrFailAsync(
            BusinessDeadLetterSource source,
            IReadOnlyDictionary<string, Func<Exception>> failures,
            CancellationToken cancellationToken)
        {
            if (failures.TryGetValue(source.Name, out var failure))
            {
                throw failure();
            }

            if (NeverAnsweringServices.Contains(source.Name))
            {
                // 这个来源永远不主动回答。等待上限只能由被测对象施加，这里不设自有时限。
                //
                // 不睡固定时长（determinism 治理禁的就是那个）。#3739 第 2 轮 CI 回归的成因是
                // 这里曾经抛裸 OperationCanceledException：扇出在同一个 deadline 上有两条出路
                // （WaitAsync 超时 / 逐源 token 取消），而降级判据只认 TaskCanceledException 与
                // TimeoutException，于是哪条先到决定这次是 200 还是 500（本地 WaitAsync 先到、
                // CI 取消先到）。
                //
                // 修法不是让假下游去模仿 HttpClient 的异常类型，而是让**两条出路给出同一个可观察
                // 结果**：被放弃时抛 TimeoutException——与 WaitAsync 抛的是同一个类型，也正是
                // 「在上限内没有回答」这件事本身。于是不存在「哪条先到」能改变的结果。
                //
                // 为什么不能干脆不观察取消：那样这次请求在服务端永远挂着，删掉扇出上限后整条 lane
                // 会挂死而不是报错（实测 600s 无输出），哨兵读数就没了。观察取消让它能收尾。
                // RunContinuationsAsynchronously：不加的话 TrySetResult 会让本方法的续体——包括下面那句
                // throw——**内联跑在 CancelAfter 的定时器线程上**，即用户代码跑在取消回调里。
                // 这里要的是把续体挡在那个线程之外，不是修自锁。
                var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => abandoned.TrySetResult());
                await abandoned.Task;
                throw new TimeoutException("The fake source never answers; the fan-out abandoned it.");
            }
        }

        private static IntegrationEventDeadLetterResponse Row(
            BusinessDeadLetterSource source,
            IntegrationEventDeadLetterStatus status) =>
            new(
                Guid.CreateVersion7(),
                $"{source.Name.ToLowerInvariant()}.consumer",
                "event-001",
                $"{source.Name}Event",
                2,
                source.Name,
                "idem-001",
                "handler-retry-exhausted",
                "下游暂时不可用",
                status,
                new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero),
                null);

        private static IntegrationEventDeadLetterDetailResponse Detail(
            BusinessDeadLetterSource source,
            Guid deadLetterId,
            IntegrationEventDeadLetterStatus status) =>
            new(
                deadLetterId,
                $"{source.Name.ToLowerInvariant()}.consumer",
                "event-001",
                $"{source.Name}Event",
                2,
                source.Name,
                "idem-001",
                "Nerv.IIP.Contracts.Sample.SampleIntegrationEvent",
                """{"eventType":"Sample"}""",
                "handler-retry-exhausted",
                "下游暂时不可用",
                status,
                new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero),
                null);
    }
}
