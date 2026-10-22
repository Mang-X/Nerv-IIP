using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

/// <summary>
/// #3738：死信读取与重放端点的行为面。端点实现只有一份（<c>Nerv.IIP.Messaging.CAP.Endpoints</c> 的
/// 共享基类），这里通过一个真实服务的 test host 走通全部 6 条路由；各服务是否接上这份实现由
/// <c>IntegrationEventDeadLetterEndpointCoverageTests</c> 钉住。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class IndustrialTelemetryDeadLetterEndpointTests
{
    private const string RoutePrefix = "/api/business/v1/iiot/dlq";

    [Fact]
    public async Task Dead_letters_are_listed_with_consumer_event_type_status_filters_and_paging()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var first = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        var second = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.FromMinutes(1));
        var other = await SeedAsync(store, "iiot.alarm-cleared", "AlarmCleared", TimeSpan.FromMinutes(2));
        using var client = CreateAuthorizedClient(factory);

        var all = await ReadAsync<ListResponse>(client, RoutePrefix);
        Assert.Equal(
            new[] { first.Id, second.Id, other.Id },
            all.Data.Items.Select(x => x.Id).ToArray());

        var byConsumer = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?consumerName=iiot.alarm-raised");
        Assert.Equal(new[] { first.Id, second.Id }, byConsumer.Data.Items.Select(x => x.Id).ToArray());

        var byEventType = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?eventType=AlarmCleared");
        Assert.Equal(new[] { other.Id }, byEventType.Data.Items.Select(x => x.Id).ToArray());

        var byStatus = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?status=Replayed");
        Assert.Empty(byStatus.Data.Items);

        // #3739：失败码与时间窗由事实所有方过滤，不在 Gateway 对已取回的窗口二次筛选。
        var byFailureCode = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?failureCode=handler-retry-exhausted");
        Assert.Equal(new[] { first.Id, second.Id, other.Id }, byFailureCode.Data.Items.Select(x => x.Id).ToArray());

        var byUnknownFailureCode = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?failureCode=never-emitted");
        Assert.Empty(byUnknownFailureCode.Data.Items);

        var from = Uri.EscapeDataString(second.DeadLetteredAtUtc.ToString("O"));
        var to = Uri.EscapeDataString(second.DeadLetteredAtUtc.ToString("O"));
        var inWindow = await ReadAsync<ListResponse>(
            client,
            $"{RoutePrefix}?deadLetteredFromUtc={from}&deadLetteredToUtc={to}");
        Assert.Equal(new[] { second.Id }, inWindow.Data.Items.Select(x => x.Id).ToArray());

        var paged = await ReadAsync<ListResponse>(client, $"{RoutePrefix}?skip=1&take=1");
        Assert.Equal(new[] { second.Id }, paged.Data.Items.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task Metrics_report_counts_per_status_and_event_type()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var pending = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.FromMinutes(1));
        var ignored = await SeedAsync(store, "iiot.alarm-cleared", "AlarmCleared", TimeSpan.FromMinutes(2));
        await store.MarkIgnoredAsync(ignored.Id, "已人工处理", DateTimeOffset.UtcNow, CancellationToken.None);
        using var client = CreateAuthorizedClient(factory);

        var metrics = await ReadAsync<MetricsResponse>(client, $"{RoutePrefix}/metrics");

        Assert.Equal(2, metrics.Data.PendingCount);
        Assert.Equal(1, metrics.Data.IgnoredCount);
        Assert.Equal(2, metrics.Data.ActionableCount);
        var raised = Assert.Single(metrics.Data.EventTypes, x => x.EventType == "AlarmRaised");
        Assert.Equal(2, raised.PendingCount);
        Assert.Equal(pending.EventType, raised.EventType);
    }

    [Fact]
    public async Task Detail_returns_the_stored_envelope_and_404_for_an_unknown_id()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var message = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        using var client = CreateAuthorizedClient(factory);

        var detail = await ReadAsync<DetailResponse>(client, $"{RoutePrefix}/{message.Id}");
        Assert.Equal(message.EventJson, detail.Data.EventJson);
        Assert.Equal(message.EventClrType, detail.Data.EventClrType);
        Assert.Equal("pending", detail.Data.Status);

        var missing = await client.GetAsync($"{RoutePrefix}/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_dead_letter_writes_back_replayed_status_and_timestamp()
    {
        var handler = new RecordingReplayHandler();
        using var factory = CreateFactory(handler);
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var message = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsync($"{RoutePrefix}/{message.Id}/replay", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replay = await response.Content.ReadFromJsonAsync<ReplayResponse>();
        Assert.True(replay!.Data.Succeeded);
        Assert.Equal("replayed", replay.Data.Status);
        Assert.Equal([message.Id], handler.ReplayedIds);

        var stored = await store.GetAsync(message.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, stored!.Status);
        Assert.NotNull(stored.ReplayedAtUtc);
    }

    [Fact]
    public async Task Replay_failure_writes_back_failed_status_with_the_reason()
    {
        var handler = new RecordingReplayHandler(failWith: "下游仍不可用");
        using var factory = CreateFactory(handler);
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var message = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsync($"{RoutePrefix}/{message.Id}/replay", content: null);

        var replay = await response.Content.ReadFromJsonAsync<ReplayResponse>();
        Assert.False(replay!.Data.Succeeded);
        Assert.Equal("failed", replay.Data.Status);
        Assert.Equal("下游仍不可用", replay.Data.Message);

        var stored = await store.GetAsync(message.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed, stored!.Status);
        Assert.Equal("下游仍不可用", stored.FailureMessage);
        Assert.Equal("replay-handler-failed", stored.FailureCode);
        // 失败也会落 ReplayedAtUtc：该列在既有存储实现里是「最后一次终态处置时间」，不是「重放成功时间」。
        Assert.NotNull(stored.ReplayedAtUtc);
    }

    /// <summary>
    /// #3738 审核规格轴阻断：本服务（以及另外 7 个服务）一条重放 handler 都没有。此时重放**一次都没尝试过**，
    /// 不得改写原行——store 没有任何回到 Pending 的方法，而原始 FailureCode/FailureMessage 是这条死信仅存的取证。
    /// </summary>
    [Fact]
    public async Task Replay_without_a_registered_handler_leaves_the_row_untouched()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var message = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsync($"{RoutePrefix}/{message.Id}/replay", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replay = await response.Content.ReadFromJsonAsync<ReplayResponse>();
        Assert.False(replay!.Data.Succeeded);
        Assert.Equal("noHandler", replay.Data.Status);

        var stored = await store.GetAsync(message.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, stored!.Status);
        Assert.Equal(message.FailureCode, stored.FailureCode);
        Assert.Equal(message.FailureMessage, stored.FailureMessage);
        Assert.Null(stored.ReplayedAtUtc);
    }

    /// <summary>
    /// 同一条不变量的批量面：一次无参 <c>replay-batch</c> 曾把该服务当前整批 Pending 不可逆改写成 Failed。
    /// </summary>
    [Fact]
    public async Task Batch_replay_without_a_registered_handler_leaves_every_row_untouched()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var first = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        var second = await SeedAsync(store, "iiot.alarm-cleared", "AlarmCleared", TimeSpan.FromMinutes(1));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync($"{RoutePrefix}/replay-batch", new { }, CancellationToken.None);

        var batch = await response.Content.ReadFromJsonAsync<BatchReplayResponse>();
        Assert.Equal([first.Id, second.Id], batch!.Data.Items.Select(x => x.Id).ToArray());
        Assert.All(batch.Data.Items, item => Assert.Equal("noHandler", item.Status));
        foreach (var seeded in new[] { first, second })
        {
            var stored = await store.GetAsync(seeded.Id, CancellationToken.None);
            Assert.Equal(IntegrationEventDeadLetterStatus.Pending, stored!.Status);
            Assert.Equal(seeded.FailureMessage, stored.FailureMessage);
        }
    }

    [Fact]
    public async Task Batch_replay_only_touches_the_selected_consumer()
    {
        var handler = new RecordingReplayHandler();
        using var factory = CreateFactory(handler);
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var selected = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        var untouched = await SeedAsync(store, "iiot.alarm-cleared", "AlarmCleared", TimeSpan.FromMinutes(1));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync(
            $"{RoutePrefix}/replay-batch",
            new { consumerName = "iiot.alarm-raised" },
            CancellationToken.None);

        var batch = await response.Content.ReadFromJsonAsync<BatchReplayResponse>();
        Assert.Equal([selected.Id], batch!.Data.Items.Select(x => x.Id).ToArray());
        Assert.Equal([selected.Id], handler.ReplayedIds);
        Assert.Equal(
            IntegrationEventDeadLetterStatus.Pending,
            (await store.GetAsync(untouched.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Ignoring_a_dead_letter_requires_a_reason_and_records_it()
    {
        using var factory = CreateFactory();
        var store = factory.Services.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var message = await SeedAsync(store, "iiot.alarm-raised", "AlarmRaised", TimeSpan.Zero);
        using var client = CreateAuthorizedClient(factory);

        var rejected = await client.PostAsJsonAsync(
            $"{RoutePrefix}/{message.Id}/ignore",
            new { reason = "   " },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(
            IntegrationEventDeadLetterStatus.Pending,
            (await store.GetAsync(message.Id, CancellationToken.None))!.Status);

        var accepted = await client.PostAsJsonAsync(
            $"{RoutePrefix}/{message.Id}/ignore",
            new { reason = "告警源已下线，不再重放" },
            CancellationToken.None);
        var detail = await accepted.Content.ReadFromJsonAsync<DetailResponse>();
        Assert.Equal("ignored", detail!.Data.Status);
        Assert.Equal("告警源已下线，不再重放", detail.Data.FailureMessage);
    }

    private static async Task<IntegrationEventDeadLetterMessage> SeedAsync(
        IIntegrationEventDeadLetterStore store,
        string consumerName,
        string eventType,
        TimeSpan offset)
    {
        var message = new IntegrationEventDeadLetterMessage(
            Guid.CreateVersion7(),
            consumerName,
            $"event-{Guid.CreateVersion7():N}",
            eventType,
            2,
            "business-iiot",
            $"idem-{Guid.CreateVersion7():N}",
            "Nerv.IIP.Contracts.IndustrialTelemetry.AlarmRaisedIntegrationEvent",
            """{"eventType":"AlarmRaised"}""",
            "handler-retry-exhausted",
            "下游暂时不可用",
            IntegrationEventDeadLetterStatus.Pending,
            new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero) + offset,
            null);
        return await store.AddAsync(message, CancellationToken.None);
    }

    private static async Task<TResponse> ReadAsync<TResponse>(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static WebApplicationFactory<Program> CreateFactory(IIntegrationEventDeadLetterReplayHandler? replayHandler = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                if (replayHandler is not null)
                {
                    builder.ConfigureTestServices(services => services.AddSingleton(replayHandler));
                }
            });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        return client;
    }

    private sealed class RecordingReplayHandler(string? failWith = null) : IIntegrationEventDeadLetterReplayHandler
    {
        private readonly List<Guid> replayedIds = [];

        public IReadOnlyList<Guid> ReplayedIds => replayedIds;

        public bool CanReplay(IntegrationEventDeadLetterMessage message) => true;

        public Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
        {
            if (failWith is not null)
            {
                throw new InvalidOperationException(failWith);
            }

            replayedIds.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed record ListResponse(ListPayload Data);

    private sealed record ListPayload(IReadOnlyList<DeadLetterItem> Items);

    private sealed record DeadLetterItem(Guid Id, string ConsumerName, string? EventType, string Status);

    private sealed record DetailResponse(DetailPayload Data);

    private sealed record DetailPayload(Guid Id, string EventClrType, string EventJson, string FailureMessage, string Status);

    private sealed record MetricsResponse(MetricsPayload Data);

    private sealed record MetricsPayload(
        int ActionableCount,
        int PendingCount,
        int FailedCount,
        int IgnoredCount,
        int ReplayedCount,
        IReadOnlyList<EventTypeMetricsPayload> EventTypes);

    private sealed record EventTypeMetricsPayload(string EventType, int PendingCount, int FailedCount, int IgnoredCount, int ReplayedCount);

    private sealed record ReplayResponse(ReplayPayload Data);

    private sealed record ReplayPayload(Guid Id, bool Succeeded, string Status, string? Message);

    private sealed record BatchReplayResponse(BatchReplayPayload Data);

    private sealed record BatchReplayPayload(IReadOnlyList<ReplayPayload> Items);
}
