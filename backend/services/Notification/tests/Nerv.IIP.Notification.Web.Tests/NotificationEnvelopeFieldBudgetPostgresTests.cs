using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using Nerv.IIP.Testing.PostgreSql;
using Npgsql;

namespace Nerv.IIP.Notification.Web.Tests;

/// <summary>
/// 信封字段长度闸的**真库运行时读数**（#3360 验收 3）。
/// </summary>
/// <remarks>
/// <para>内存替身证不到「超界在真 PostgreSQL 上到底发生什么」。这一类跑真库，给两条互补读数：</para>
/// <list type="number">
/// <item><see cref="Without_the_gate_an_oversized_key_raises_22001_on_postgres"/>：
/// <b>缺陷本身的运行时读数</b>。绕开闸直接把超界键交给
/// <c>ProcessedIntegrationEventInbox.TryRecordAsync</c> 再 <c>SaveChangesAsync</c>，
/// 实测抛 <c>DbUpdateException</c>、内层 Npgsql <c>SqlState = 22001</c>。
/// 这就是消费者里那条**逃逸**路径的真实形态（<c>IntegrationEventConsumerGuard</c> 在
/// <c>await handler(...)</c> 外没有 try/catch）。</item>
/// <item><see cref="Oversized_envelope_field_dead_letters_and_stays_replayable_on_postgres"/>：
/// <b>修好后的运行时读数</b>。同一把键经 Guard ⇒ 落 <c>integration_event_dead_letters</c>、
/// 状态 <c>Pending</c>、失败码 <c>oversized-envelope-field</c>，且 <c>event_json</c> 里
/// **完整保留**未截断的原值 ⇒ 重放拿得回原事件；随后 <c>MarkReplayedAsync</c> 把状态推到
/// <c>Replayed</c>。</item>
/// </list>
///
/// <para><b>⚠️ 端口纪律</b>：本类**不带任何默认连接串**，缺 <c>NERV_IIP_TEST_POSTGRES</c> 就跳过。
/// 同目录的 <c>NotificationPostgresProfileTests</c> 默认回落到 <c>localhost:15432</c>，
/// 那是本机长期共享实例；本类**刻意不沿用**那个回落，避免在无人值守时连上共享库。</para>
///
/// <para><b>本类不证明什么</b>：</para>
/// <list type="number">
/// <item>不证明 handler **内部**抛出的异常被接住 —— 那仍然逃逸（#877，本票射程之外）。
/// ⛔ 别把这两条读数读成「poison 问题已解决」。</item>
/// <item>不证明所有消费者都走 <c>IntegrationEventConsumerGuard</c>；绕开 Guard 的不受闸保护。</item>
/// <item>第 1 条读数用的是**平台 inbox 入口**而不是某个真实 handler，因此它证的是
/// 「这条列宽在真库上确实会炸」，不是「今天某个具体消费者一定会走到那里」。</item>
/// </list>
/// </remarks>
public sealed class NotificationEnvelopeFieldBudgetPostgresTests
{
    private const string ConsumerName = "notification.envelope-budget.postgres";
    private const string SampleEventType = "budget.PostgresEvent";

    [NotificationEnvelopeBudgetPostgresFact]
    public async Task Without_the_gate_an_oversized_key_raises_22001_on_postgres()
    {
        var baseConnectionString = LaneConnectionString();

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            baseConnectionString,
            "nerv_notification_envelope_budget");
        await using var provider = BuildProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.AssertOwns(dbContext.Database.GetConnectionString());
        await dbContext.Database.MigrateAsync();

        var oversized = new string('k', IntegrationEventEnvelopeFieldBudget.IdempotencyKey + 1);
        var recorded = await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            ConsumerName,
            SampleEvent(oversized),
            NewInboxRow,
            CancellationToken.None);
        Assert.True(recorded, "inbox 应当把这一行排进 UoW（缺陷正是在 SaveChanges 那一刻才炸）。");

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync(CancellationToken.None));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("22001", postgres.SqlState);
    }

    [NotificationEnvelopeBudgetPostgresFact]
    public async Task Oversized_envelope_field_dead_letters_and_stays_replayable_on_postgres()
    {
        var baseConnectionString = LaneConnectionString();

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            baseConnectionString,
            "nerv_notification_envelope_budget");
        await using var provider = BuildProvider(database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.AssertOwns(dbContext.Database.GetConnectionString());
        await dbContext.Database.MigrateAsync();

        var oversized = new string('k', IntegrationEventEnvelopeFieldBudget.IdempotencyKey + 1);
        var store = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        var guard = new IntegrationEventConsumerGuard<BudgetPostgresSampleEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(ConsumerName, SampleEventType, SupportedEventVersion: 1));
        var handlerInvoked = false;

        await guard.HandleAsync(
            SampleEvent(oversized),
            (_, _) =>
            {
                handlerInvoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(handlerInvoked, "超界事件不得进入 handler，否则会在 inbox 落库时炸成 poison。");

        var pending = await store.ListAsync(ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None);
        var message = Assert.Single(pending);
        Assert.Equal(IntegrationEventEnvelopeValidator.OversizedEnvelopeFieldFailureCode, message.FailureCode);

        // 可重放的前提：原事件完整留在无界的 jsonb 列里（身份列上的截断只是诊断）。
        using (var document = JsonDocument.Parse(message.EventJson))
        {
            var replayedKey = document.RootElement
                .GetProperty(nameof(IIntegrationEventEnvelope.IdempotencyKey))
                .GetString();
            Assert.Equal(oversized, replayedKey);
        }

        // 死信行自己确实落了库（不是只在内存里）。
        var persistedCount = await dbContext.Set<IntegrationEventDeadLetter>()
            .CountAsync(x => x.ConsumerName == ConsumerName);
        Assert.Equal(1, persistedCount);

        var replayedAtUtc = DateTimeOffset.UtcNow;
        await store.MarkReplayedAsync(message.Id, replayedAtUtc, CancellationToken.None);

        var afterReplay = await store.GetAsync(message.Id, CancellationToken.None);
        Assert.NotNull(afterReplay);
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, afterReplay!.Status);
        Assert.Empty(await store.ListAsync(ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    private static ProcessedIntegrationEvent NewInboxRow(ProcessedIntegrationEventInboxRecord record) =>
        new(
            record.ConsumerName,
            record.EventId,
            record.EventType,
            record.EventVersion,
            record.SourceService,
            record.IdempotencyKey,
            record.ProcessedAtUtc);

    private static BudgetPostgresSampleEvent SampleEvent(string idempotencyKey) => new(
        EventId: "event-budget-001",
        EventType: SampleEventType,
        EventVersion: 1,
        OccurredAtUtc: DateTimeOffset.UtcNow,
        SourceService: "budget",
        CorrelationId: "corr-budget-001",
        CausationId: "cause-budget-001",
        OrganizationId: "org-001",
        EnvironmentId: "env-001",
        Actor: "system:test",
        IdempotencyKey: idempotencyKey,
        Payload: new BudgetPostgresSamplePayload("value"));

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(NotificationIntent).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// ⛔ 不提供默认连接串，绝不回落到本机共享实例。
    /// <para>缺环境变量时由 <see cref="NotificationEnvelopeBudgetPostgresFactAttribute"/> 在**用例进入之前**
    /// 标成 Skip，所以走到这里就一定有值；真没有就抛，**不静默 return**。</para>
    /// </summary>
    private static string LaneConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                "NERV_IIP_TEST_POSTGRES 缺失却仍进入了真库用例：Skip 特性没起作用。")
            : connectionString;
    }

    /// <summary>
    /// 缺 <c>NERV_IIP_TEST_POSTGRES</c> 时把用例标成 **Skip**。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么不能用裸 <c>[Fact]</c> 加方法体内 <c>return</c></b>（PR #3371 复审抓出的形态）：
    /// 那样在无库环境下用例会被**计为通过**，报告里是「130 通过 / 0 跳过」——
    /// **「根本没跑」在读数里完全不可见**。skipped 至少看得见，假通过连线索都不留，
    /// 会让后来人（包括写它的我）拿一个空转的绿当成证据。
    /// 本仓既有同形写法：<c>AppHubRealPostgresFactAttribute</c> 等。
    /// </remarks>
    internal sealed class NotificationEnvelopeBudgetPostgresFactAttribute : FactAttribute
    {
        public NotificationEnvelopeBudgetPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            {
                Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL envelope-field budget dead-letter proof.";
            }
        }
    }

    private sealed record BudgetPostgresSamplePayload(string Value);

    private sealed record BudgetPostgresSampleEvent(
        string EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAtUtc,
        string SourceService,
        string CorrelationId,
        string CausationId,
        string OrganizationId,
        string EnvironmentId,
        string Actor,
        string IdempotencyKey,
        BudgetPostgresSamplePayload Payload) : IIntegrationEventEnvelope
    {
        object? IIntegrationEventEnvelope.PayloadObject => Payload;
    }
}
