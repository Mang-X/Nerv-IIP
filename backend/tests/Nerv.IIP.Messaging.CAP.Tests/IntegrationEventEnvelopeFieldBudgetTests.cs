using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// 信封字段长度闸的行为契约（#3360）。
/// </summary>
/// <remarks>
/// <para><b>被证的性质</b>：超界信封字段走
/// <see cref="IntegrationEventConsumerGuard{T}"/> 的**死信分支**（可见、可重放），
/// 而不是放进 <c>handler</c> 后在 inbox 落库时抛 22001 逃逸成 poison。</para>
/// <para><b>本类不证明什么</b>：这里全是内存替身，**证不到「真库确实不再 22001」**——
/// 那条由 <c>NotificationEnvelopeFieldBudgetPostgresTests</c> 在真 PostgreSQL 上给运行时读数。
/// 本类也**证不到** handler 内部抛出的异常被接住（那是 #877，本票射程之外，见
/// <see cref="Handler_thrown_exceptions_still_escape_the_guard"/> 那条**把现状钉住**的断言）。</para>
/// </remarks>
public sealed class IntegrationEventEnvelopeFieldBudgetTests
{
    private const string ConsumerName = "budget.consumer";
    private const string EventType = "budget.Event";

    /// <summary>闸的边界：恰好等于预算放行，多一个字符判死信。</summary>
    [Theory]
    [InlineData(nameof(IIntegrationEventEnvelope.IdempotencyKey))]
    [InlineData(nameof(IIntegrationEventEnvelope.EventId))]
    [InlineData(nameof(IIntegrationEventEnvelope.EventType))]
    [InlineData(nameof(IIntegrationEventEnvelope.SourceService))]
    public async Task A_field_at_exactly_its_budget_passes_and_one_character_over_dead_letters(string fieldName)
    {
        var budget = IntegrationEventEnvelopeFieldBudget.ByFieldName[fieldName];

        var (atBudgetStore, atBudgetInvoked) = await RunAsync(EventWith(fieldName, budget));
        Assert.True(atBudgetInvoked, $"{fieldName} 长度恰为 {budget} 时必须放行给 handler。");
        Assert.Empty(await PendingAsync(atBudgetStore));

        var (overStore, overInvoked) = await RunAsync(EventWith(fieldName, budget + 1));
        Assert.False(overInvoked, $"{fieldName} 长度 {budget + 1} 超界时不得进入 handler。");
        var message = Assert.Single(await PendingAsync(overStore));
        Assert.Equal(IntegrationEventEnvelopeValidator.OversizedEnvelopeFieldFailureCode, message.FailureCode);
        Assert.Contains(fieldName, message.FailureMessage, StringComparison.Ordinal);
        Assert.Contains((budget + 1).ToString(), message.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 超界事件**没有被改写**：闸只判定、不改写，死信消息里带的仍是**逐字原值**。
    /// （落 EF 死信表时那一列会按诊断宽度截断，那是 #3101 为「别让死信自己炸掉」定的**诊断**行为，
    /// 与本闸无关，也不构成身份。）
    /// </summary>
    [Fact]
    public async Task The_gate_does_not_rewrite_or_fold_the_oversized_key()
    {
        var budget = IntegrationEventEnvelopeFieldBudget.IdempotencyKey;
        var first = new string('a', budget + 1);
        var second = new string('a', budget) + "b";

        var (firstStore, _) = await RunAsync(EventWith(nameof(IIntegrationEventEnvelope.IdempotencyKey), 0, first));
        var (secondStore, _) = await RunAsync(EventWith(nameof(IIntegrationEventEnvelope.IdempotencyKey), 0, second));

        var firstMessage = Assert.Single(await PendingAsync(firstStore));
        var secondMessage = Assert.Single(await PendingAsync(secondStore));

        // 两把只差最后一个字节的超界键，在死信记录里仍然是两条不同的事实，没有被折叠成一个。
        Assert.NotEqual(firstMessage.IdempotencyKey, secondMessage.IdempotencyKey);
        Assert.Equal(first, firstMessage.IdempotencyKey);
        Assert.Equal(second, secondMessage.IdempotencyKey);
    }

    /// <summary>缺失字段的诊断不被超界诊断抢走（两条规则的先后顺序是确定的）。</summary>
    [Fact]
    public async Task A_missing_field_still_reports_missing_even_when_another_field_is_oversized()
    {
        var oversized = new string('k', IntegrationEventEnvelopeFieldBudget.IdempotencyKey + 1);
        var (store, invoked) = await RunAsync(Valid() with { EventId = "  ", IdempotencyKey = oversized });

        Assert.False(invoked);
        var message = Assert.Single(await PendingAsync(store));
        Assert.Equal(IntegrationEventEnvelopeValidator.MissingEnvelopeFieldFailureCode, message.FailureCode);
    }

    /// <summary>
    /// 不在闭集里的信封字段**故意不受闸约束** —— 这条把「声明放弃」钉成可执行读数，
    /// 免得后来人以为它们也被保护了（护栏自称完备比有洞更坏）。
    /// </summary>
    [Theory]
    [InlineData(nameof(IIntegrationEventEnvelope.CorrelationId))]
    [InlineData(nameof(IIntegrationEventEnvelope.CausationId))]
    [InlineData(nameof(IIntegrationEventEnvelope.OrganizationId))]
    [InlineData(nameof(IIntegrationEventEnvelope.EnvironmentId))]
    [InlineData(nameof(IIntegrationEventEnvelope.Actor))]
    public async Task Fields_outside_the_closed_set_are_deliberately_not_gated(string fieldName)
    {
        Assert.False(
            IntegrationEventEnvelopeFieldBudget.ByFieldName.ContainsKey(fieldName),
            $"{fieldName} 已被纳入闭集，这条「声明放弃」的读数需要重写。");

        var (store, invoked) = await RunAsync(EventWith(fieldName, 0, new string('x', 100_000)));

        Assert.True(invoked, $"{fieldName} 不在闭集里，超长也必须放行（平台层没有它的承载列）。");
        Assert.Empty(await PendingAsync(store));
    }

    /// <summary>
    /// ⚠️ **射程声明，钉成断言**：handler **内部**抛出的异常**仍然原样逃逸**（#877）。
    /// 本票只把 <c>await handler(...)</c> **之前**的信封超界变成死信，
    /// ⛔ 别把它读成「poison 问题已解决」。这条现状哪天被改了，它会红，届时要一并更新射程叙述。
    /// </summary>
    [Fact]
    public async Task Handler_thrown_exceptions_still_escape_the_guard()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = NewGuard(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.HandleAsync(
            Valid(),
            (_, _) => throw new InvalidOperationException("payload column overflow stands in for #877"),
            CancellationToken.None));

        Assert.Empty(await PendingAsync(store));
    }

    private static async Task<(InMemoryIntegrationEventDeadLetterStore Store, bool Invoked)> RunAsync(
        BudgetSampleIntegrationEvent integrationEvent)
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var invoked = false;
        await NewGuard(store, integrationEvent.EventType).HandleAsync(
            integrationEvent,
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        return (store, invoked);
    }

    /// <summary>
    /// <paramref name="alsoSupportedEventType"/> 把被测事件自己的 <c>EventType</c> 一并登记为受支持类型。
    /// <para><b>为什么必须这样</b>：<c>EventType</c> 同时受消费者的 <c>SupportedEventTypes</c> 约束，
    /// 若不登记，「长度恰为预算」那一格会先撞上 <c>unexpected-event-type</c> 而**根本量不到长度闸**
    /// ——相邻同型守卫兜住变异，用例看着绿其实零鉴别力（本仓判例）。</para>
    /// </summary>
    private static IntegrationEventConsumerGuard<BudgetSampleIntegrationEvent> NewGuard(
        IIntegrationEventDeadLetterStore store,
        string? alsoSupportedEventType = null)
    {
        string[] supported = alsoSupportedEventType is null || alsoSupportedEventType == EventType
            ? [EventType]
            : [EventType, alsoSupportedEventType];
        return new IntegrationEventConsumerGuard<BudgetSampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(ConsumerName, supported, supportedEventVersion: 1));
    }

    private static async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> PendingAsync(
        IIntegrationEventDeadLetterStore store) =>
        await store.ListAsync(ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None);

    private static BudgetSampleIntegrationEvent Valid() => new(
        EventId: "event-001",
        EventType: EventType,
        EventVersion: 1,
        OccurredAtUtc: DateTimeOffset.UtcNow,
        SourceService: "budget",
        CorrelationId: "corr-001",
        CausationId: "cause-001",
        OrganizationId: "org-001",
        EnvironmentId: "env-001",
        Actor: "system:test",
        IdempotencyKey: "budget:event-001",
        Payload: new BudgetSamplePayload("value"));

    /// <summary>
    /// 把某个信封字段换成指定长度（或指定原值）的事件。
    /// <c>EventType</c> 一格特殊：它同时受消费者的 <c>SupportedEventTypes</c> 约束，
    /// 因此把被撑长的那个值**同时**登记为受支持类型，否则会先撞上
    /// <c>unexpected-event-type</c> 而量不到长度闸（相邻同型守卫会兜住变异）。
    /// </summary>
    private static BudgetSampleIntegrationEvent EventWith(string fieldName, int length, string? value = null)
    {
        var text = value ?? new string('v', length);
        return fieldName switch
        {
            nameof(IIntegrationEventEnvelope.EventId) => Valid() with { EventId = text },
            nameof(IIntegrationEventEnvelope.EventType) => Valid() with { EventType = text },
            nameof(IIntegrationEventEnvelope.SourceService) => Valid() with { SourceService = text },
            nameof(IIntegrationEventEnvelope.IdempotencyKey) => Valid() with { IdempotencyKey = text },
            nameof(IIntegrationEventEnvelope.CorrelationId) => Valid() with { CorrelationId = text },
            nameof(IIntegrationEventEnvelope.CausationId) => Valid() with { CausationId = text },
            nameof(IIntegrationEventEnvelope.OrganizationId) => Valid() with { OrganizationId = text },
            nameof(IIntegrationEventEnvelope.EnvironmentId) => Valid() with { EnvironmentId = text },
            nameof(IIntegrationEventEnvelope.Actor) => Valid() with { Actor = text },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "未知信封字段。"),
        };
    }

    private sealed record BudgetSamplePayload(string Value);

    private sealed record BudgetSampleIntegrationEvent(
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
        BudgetSamplePayload Payload) : IIntegrationEventEnvelope
    {
        object? IIntegrationEventEnvelope.PayloadObject => Payload;
    }
}
