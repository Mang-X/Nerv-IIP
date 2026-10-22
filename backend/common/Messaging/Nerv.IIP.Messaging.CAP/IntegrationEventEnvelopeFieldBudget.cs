using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// 信封字段在**平台 inbox 表**上的承载上界（#3360）。
/// </summary>
/// <remarks>
/// <para><b>缺陷形状</b>：<see cref="IntegrationEventEnvelopeValidator"/> 此前只有
/// <c>IsNullOrWhiteSpace</c> 检查，**一条长度规则都没有**。而
/// <c>ProcessedIntegrationEventInbox.TryRecordAsync</c> **只 <c>dbSet.Add</c>、不 SaveChanges**，
/// inbox 行与命令 handler 在**同一个 UoW** 里落；超界 ⇒ Npgsql 22001 ⇒ <c>DbUpdateException</c>
/// 从消费者**逃出**（<see cref="IntegrationEventConsumerGuard{T}"/> 只对**信封校验失败**写死信，
/// <c>await handler(...)</c> 外**没有 try/catch**）⇒ CAP 重试耗尽 ⇒ **poison**：
/// 上游写库成功、事件永远送不到、症状不在上游那侧显现。</para>
///
/// <para><b>本类型治什么、不治什么</b>：它让超界**不再静默** —— 超界事件走
/// <see cref="IntegrationEventConsumerGuard{T}"/> 的死信分支，**可见、可重放**，
/// Notification 侧还有 <c>NotificationDeadLetterAlertMonitor</c> 告警。
/// 它**不让超界不再发生** —— 那是 #3339（producer 侧键预算 + 摘要回落）。两者互补，不互相取代：
/// #3339 落地后，**任何新原因**（新 producer、列宽变更、上游改模板）仍会被这道闸变成显式死信。</para>
///
/// <para><b>⛔ 不截断、不折叠</b>：本类型只**判定**，不改写任何键。把超界键截断会把两把不同的键
/// 折叠成一个（<c>InventoryIdempotencyKeyPolicy.Compose</c> 的注释写死禁止这件事），
/// 那会让第二条事实被下游当重放静默吞掉——比 poison 更坏，因为它连失败都没有。</para>
///
/// <para><b>为什么逐字段而不是一个统一上界</b>：四条承载列**宽度不同**
/// （<c>source_service</c> 只有 128，而 <c>idempotency_key</c> 有 512）。
/// 取统一最小值 128 会把长度 129..512 的**合法**信封键误判成死信 —— 那是把一个静默缺陷
/// 换成一个响亮的误报，不是修复。</para>
///
/// <para><b>⭐ <see cref="IdempotencyKey"/> 为什么直接复用 <see cref="IntegrationEventIdempotencyKey.Budget"/>
/// 而不是再独立派生一份</b>：这两个数**不是「碰巧相等的两个量」，是同一个量的两侧**——
/// producer 侧用它决定何时回落成摘要（#3339），consumer 侧用它决定何时判死信（本票）。
/// 两者必须**恒等**：闸比预算**严**会把 producer 合规产出的键判成假死信；闸比预算**宽**，
/// 超界键照样 poison，这道闸等于没装。复用让这条恒等**由构造成立**，
/// 而不是靠一条「两份独立派生今天同值」的断言维持（PR #3368 复审留下的正是这条判例：
/// 两份独立派生今天同值、但**不再由构造保证**，必须配反向枚举断言防分叉）。
/// 代价是 <c>Messaging.CAP</c> 依赖 <c>Contracts.IntegrationEvents</c> ——
/// 而它**本来就依赖**（<see cref="IIntegrationEventEnvelope"/> 就住在那里），**零新增耦合**。</para>
///
/// <para><b>数值的派生链（两跳，别读成一跳）</b>：</para>
/// <list type="number">
/// <item><see cref="IdempotencyKey"/> ← <see cref="IntegrationEventIdempotencyKey.Budget"/>
/// ← 由 <c>IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests</c>
/// 从 9 张 <c>processed_integration_events</c> 加 <c>notification_intents.dedupe_key</c>
/// 共 10 列的真 EF 模型取最小后对撞（#3368 落地）。
/// ⚠️ **删那条断言会静默抽掉本类型的派生根**，要删先在这里留替代。</item>
/// <item><see cref="EventId"/> / <see cref="EventType"/> / <see cref="SourceService"/>
/// ← 由 <c>IntegrationEventEnvelopeFieldBudgetContractTests</c> 从 **9 个服务**的真 EF 模型
/// 逐字段取最小后对撞，任一单边改动即红。</item>
/// </list>
///
/// <para><b>射程边界（声明放弃了什么，⛔ 别读成「poison 问题已解决」）</b>：</para>
/// <list type="number">
/// <item><b>只覆盖信封字段，不覆盖 payload。</b><c>handler</c> **内部**因 payload 字段超界
/// （或任何别的业务原因）抛出的异常**仍然原样逃逸成 poison** —— 那是 **#877**，本票不修。
/// 这道闸装在 <c>await handler(...)</c> **之前**，管不到它后面的事。</item>
/// <item><b>只覆盖有平台承载列的 4 个字段。</b><c>CorrelationId</c> / <c>CausationId</c> /
/// <c>OrganizationId</c> / <c>EnvironmentId</c> / <c>Actor</c> **inbox 根本不存**
/// ⇒ 平台层不存在它们的上界 ⇒ **故意不立规则**（凭空立一个数会误杀，且没有任何 EF 模型能钉住它）。
/// 它们若被某个**服务专属**的写入点落进更窄的列，按 #3281 取最小的责任在那个服务，不在这里
/// ——同 #3339 处置 <c>inspection_tasks.trigger_idempotency_key</c>(474) 的判例。</item>
/// <item><b>不证明所有消费者都走 <see cref="IntegrationEventConsumerGuard{T}"/>。</b>
/// 绕开 Guard 直接消费的代码不受这道闸保护。⛔ 按票面禁令**不新建源码文本扫描护栏**
/// 去看守这件事（#3176 / PR #3214 三轮实证不收敛）。</item>
/// </list>
/// </remarks>
public static class IntegrationEventEnvelopeFieldBudget
{
    /// <summary><c>processed_integration_events.event_id</c> 列宽（9 个服务同值）。</summary>
    public const int EventId = 256;

    /// <summary><c>processed_integration_events.event_type</c> 列宽（9 个服务同值）。</summary>
    public const int EventType = 256;

    /// <summary>
    /// <c>processed_integration_events.source_service</c> 列宽（9 个服务同值）。
    /// **四条里最窄的一条** —— 今天的取值（<c>business-inventory</c> 这一族）离它很远，
    /// 但公开契约对该字段**没有任何声明上界**，所以它仍是真权威。
    /// </summary>
    public const int SourceService = 128;

    /// <summary>
    /// <c>processed_integration_events.idempotency_key</c> 与
    /// <c>notification_intents.dedupe_key</c> 取最小后的列宽。
    /// **直接复用 producer 侧预算**，理由见类型注释里那段「同一个量的两侧」。
    /// </summary>
    public const int IdempotencyKey = IntegrationEventIdempotencyKey.Budget;

    /// <summary>
    /// 受本闸约束的信封字段闭集。键是 <see cref="IIntegrationEventEnvelope"/> 的属性名，
    /// 由 <c>nameof</c> 取得 —— 改属性名会编译期红，不会静默失配。
    /// <para>⚠️ **不在这张表里的信封字段是「声明放弃」，不是「遗漏」**，理由见类型注释第 2 条。</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ByFieldName =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(IIntegrationEventEnvelope.EventId)] = EventId,
            [nameof(IIntegrationEventEnvelope.EventType)] = EventType,
            [nameof(IIntegrationEventEnvelope.SourceService)] = SourceService,
            [nameof(IIntegrationEventEnvelope.IdempotencyKey)] = IdempotencyKey,
        };
}
