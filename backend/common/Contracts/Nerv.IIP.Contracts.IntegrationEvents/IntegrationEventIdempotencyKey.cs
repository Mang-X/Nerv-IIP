using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Contracts.IntegrationEvents;

/// <summary>
/// 集成事件**信封键**（<see cref="IIntegrationEventEnvelope.IdempotencyKey"/>）的平台级预算与构造出处（#3339）。
/// </summary>
/// <remarks>
/// <para><b>缺陷形状</b>：各 producer 的信封键是**纯拼接**（各服务各写各的 <c>EventIds.Idempotency</c>，
/// 或干脆内联插值），长度单调 ⇒ 下游承载列是**真权威**（不是 #3290 那种无条件摘要产出的定长幽灵权威）。
/// 按各 producer 自己的 EF 列宽饱和后，多条键的最坏长度超过平台 inbox 的
/// <see cref="Budget"/>，落库 22001 在 CAP 消费者里逃逸成 poison message（#877 同族）。</para>
///
/// <para><b>修法：整键摘要回落，保留可读字面前缀</b>。
/// 与 #3332 的 <c>FinishedGoodsReceiptInventoryPostingKey</c> **形态同族但回落粒度不同**：
/// 那边必须保住「作用域可重算」（<c>BelongsToScope</c> 是成本授权守卫），所以只摘作用域段；
/// 本处**信封键零下游解析点**（扫描读数见 PR 正文），因此可以整键摘要。
/// 保留字面前缀不是装饰：排障要按前缀定位，且将来有人加前缀路由时不至于再返工
/// （照 <c>FinishedGoodsReceiptInventoryPostingKey.Prefix</c> 的姿势）。</para>
///
/// <para><b>⛔ 为什么不是截断</b>：截断会把仅末几位不同的两把键折叠成同一个
/// （<c>InventoryIdempotencyKeyPolicy.Compose</c> 的注释写死禁止这件事）。
/// 本处回落的**摘要输入是整条可读键**，两把不同的键只有在 SHA-256 碰撞时才折叠。
/// 回落形态里没有任何一段是被截掉后原样保留的调用方数据。</para>
///
/// <para><b>⛔ 为什么不是就地拒绝</b>：调用点是**域事件转换器**，事实（入库、告警、心跳丢失）
/// 已经发生并已落库。在这里拒绝等于把上游自己的写操作打掉。</para>
///
/// <para><b>两种形态的判别式，以及它为什么不可伪造</b>（#3340 在这一点上栽过一次）：</para>
/// <list type="bullet">
/// <item><b>逐字形态在 <paramref name="prefix"/> 之后恒含至少一个 <see cref="ReadableSeparator"/></b>：
/// <see cref="Compose"/> 要求 <c>tailParts.Length &gt;= 2</c>，那个分隔符是
/// <c>string.Join</c> 吐出来的**结构字面量**，不是任何调用方字节。
/// 调用方能往段里**再加**冒号（把冒号数往上加），**减不掉**这一个。
/// ⚠️ 这条地基**不依赖**任何一段非空：<c>Compose("p:", "", "")</c> 产出 <c>"p::"</c>，前缀之后仍是 1 个冒号。</item>
/// <item><b>回落形态在 <paramref name="prefix"/> 之后恒不含 <see cref="ReadableSeparator"/></b>：
/// 那一段是 base64url（字符集 <c>[A-Za-z0-9-_]</c>，**不含** <c>:</c>），
/// 而且**整段没有一个调用方原文字节**——调用方连注入的位置都没有。</item>
/// </list>
/// <para>⇒ 两种形态**按字符类互斥**，互斥性不依赖任何长度巧合。
/// #3340 第一版栽的正是「按长度互斥」：取 <c>K2</c> 为任意超界键、<c>K1 = 摘要(K2)</c>
/// （恰好 <see cref="DigestLength"/> 字符、谁都能算），<c>K1</c> 走逐字出口、<c>K2</c> 走回落出口，
/// 两把不同的键折叠成一个，第二把被下游当重放**静默吞掉**。本处那条构造走不通：
/// <c>K1</c> 想走逐字出口就得经过 <see cref="Compose"/>，而 <see cref="Compose"/> 一定会在前缀后写下那个冒号。</para>
/// <para>顺带排除掉的一条歧路：**不能靠在段里加内容标记来判别**。信封键的段来自外部系统
/// （外部告警号、外部任务号、连接器上报的 tag key），字符集没有任何声明上界，
/// **没有可用的保留字符**。</para>
///
/// <para><b>存量键逐字保持（#3332 第 1 条同构）</b>：只有今天**根本落不了库**（&gt; <see cref="Budget"/>）
/// 的键才走回落；今天能落库的键**一个字节都不动**。
/// 因此消费侧幂等表（<c>processed_integration_events</c> 按精确相等查、
/// <c>notification_intents</c> 的 <c>GetByDedupeKeyAsync</c> 也按精确相等查）对存量行照查得到，
/// **不需要** Wms <c>ReplayIdempotencyKeys</c> 那样的双键查找。</para>
///
/// <para><b>本类型不证明什么（值域边界，别读成完备）</b>：</para>
/// <list type="number">
/// <item><b>不证明所有 producer 都走这里。</b>今天接入的是 IndustrialTelemetry / Wms / AppHub / Inventory
/// 四个服务的全部信封键构造点；其余 producer 仍是纯拼接。**本类型不新建源码文本扫描护栏**去看守这件事
/// （#3176 / PR #3214 三轮实证这类扫描不收敛），接入面枚举写在 PR 正文。</item>
/// <item><b><see cref="Budget"/> 只覆盖平台 inbox 那一族承载列。</b>
/// 若某条信封键还被写进**更窄的、事件专属的**列，有效上界按 #3281 取最小值，
/// 那一段不由本类型保证。今天已实读的一例：<c>inspection_tasks.trigger_idempotency_key</c>(474)
/// 逐字承载 Mes 两条事件的信封键（另两条是 <c>{事件键}:{行号}</c>），
/// 由 <c>InspectionTaskTriggerKey</c> 与其跨服务契约用例自己看守（#2977 / #3318）。
/// 本类型**没有**把它并进 <see cref="Budget"/>：并进来会把全平台预算压到 474，
/// 让今天长度落在 475..512 的键（在自己的链路上完全合法）无谓改形，反而破坏存量键逐字保持。</item>
/// <item><b>不证明不同 <paramref name="prefix"/> 之间不撞。</b><c>p1 + X == p2 + Y</c>
/// 这类前缀混淆是纯拼接键的**既有**性质（事件名互为前缀时即可构造），本类型未引入也未消除。</item>
/// <item><b>不证明逐字形态内部的分段是单射。</b><c>("a:b","c")</c> 与 <c>("a","b:c")</c>
/// 拼出同一条可读键——同样是纯拼接的**既有**性质。</item>
/// </list>
/// </remarks>
public static class IntegrationEventIdempotencyKey
{
    /// <summary>
    /// 平台 inbox 承载列的列宽（多列取最小，#3281 判据）。
    /// <para>今天登记的承载列共 10 条、全部是 512：9 个服务各自的
    /// <c>processed_integration_events.idempotency_key</c>（AppHub / Notification / Wms / Quality /
    /// Scheduling / Mes / DemandPlanning / Maintenance / Erp），以及 Notification 的
    /// <c>notification_intents.dedupe_key</c>（5 列唯一索引的组成列）。
    /// 两者在同一个 UoW 落库（<c>ProcessedIntegrationEventInbox.TryRecordAsync</c> 只 <c>Add</c>、不 SaveChanges），
    /// 先炸哪一列都一样、整事务回滚 ⇒ 取最小值是唯一正确口径。</para>
    /// <para>各服务侧仍各自写死 <c>HasMaxLength(512)</c>（迁移的真相在那边），
    /// 由 <c>IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests</c> 从各服务真 EF 模型
    /// 闭集枚举后与本常量对撞，任一单边改动即红。
    /// （<c>integration_event_dead_letters.idempotency_key</c>(500) 是
    /// <c>TruncateOptional</c> 截断写入的**诊断记录不是身份**，不构成上界、不登记。）</para>
    /// </summary>
    public const int Budget = 512;

    /// <summary>可读形态的段间分隔符，也是**形态判别式**所看的那个字符。</summary>
    public const char ReadableSeparator = ':';

    /// <summary>
    /// <see cref="Compose"/> 允许的最少尾段数。
    /// <para><b>这不是风格约束，是判别式的前提</b>：只有 <c>&gt;= 2</c> 才能保证
    /// <c>string.Join</c> 在前缀之后写下至少一个 <see cref="ReadableSeparator"/>，
    /// 逐字形态与回落形态才按字符类互斥。少于 2 段一律抛，**不静默降级**——
    /// 静默降级会让那一条出口悄悄退回「按长度互斥」，也就是 #3340 修掉的那个缺陷。</para>
    /// </summary>
    public const int MinimumTailParts = 2;

    /// <summary>
    /// 摘要形态的字符数，由「SHA-256 输出字节数」经 base64url 编码长度**派生**，不手抄。
    /// </summary>
    public static readonly int DigestLength = Base64Url.GetEncodedLength(SHA256.HashSizeInBytes);

    /// <summary>
    /// 组装信封幂等键。整键装得进 <see cref="Budget"/> 就**逐字保持**（与改动前完全相同的字节），
    /// 装不下则回落成 <c>{prefix}{整键摘要}</c>。
    /// </summary>
    /// <param name="prefix">
    /// 可读字面前缀，必须非空且以 <see cref="ReadableSeparator"/> 收尾。两种形态都以它开头。
    /// 它必须短到留得下摘要（见 <see cref="MaxPrefixLength"/>），否则抛。
    /// </param>
    /// <param name="tailParts">
    /// 尾段，至少 <see cref="MinimumTailParts"/> 段（见该常量的注释：这是判别式的前提）。
    /// <c>null</c> 段按空串处理，与改动前 <c>string.Join</c> 的行为一致。
    /// </param>
    public static string Compose(string prefix, params string?[] tailParts)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(tailParts);

        if (prefix[^1] != ReadableSeparator)
        {
            throw new ArgumentException(
                $"信封幂等键前缀必须以 '{ReadableSeparator}' 收尾，实际是 \"{prefix}\"。",
                nameof(prefix));
        }

        if (prefix.Length > MaxPrefixLength)
        {
            throw new ArgumentException(
                $"信封幂等键前缀长度 {prefix.Length} 超过 {MaxPrefixLength}，回落形态放不进预算 {Budget}。",
                nameof(prefix));
        }

        if (tailParts.Length < MinimumTailParts)
        {
            throw new ArgumentException(
                $"信封幂等键至少需要 {MinimumTailParts} 个尾段（逐字形态与回落形态靠前缀后那个 "
                + $"'{ReadableSeparator}' 互斥），实际只给了 {tailParts.Length} 段。",
                nameof(tailParts));
        }

        var readable = prefix + string.Join(ReadableSeparator, tailParts);
        return readable.Length <= Budget ? readable : prefix + Digest(readable);
    }

    /// <summary>
    /// <c>{servicePrefix}{parts[0]}:{parts[1..] 以 ':' 相连}</c> 形态的便捷入口，
    /// 供各服务那份 <c>EventIds.Idempotency(params string[] parts)</c> 直接转调。
    /// <para>产出与改动前的 <c>$"{servicePrefix}{string.Join(':', parts)}"</c> **逐字相同**
    /// （当 <c>parts.Length &gt;= 1 + <see cref="MinimumTailParts"/></c> 时），
    /// 因为多插进去的那个 <see cref="ReadableSeparator"/> 正是 <c>string.Join</c> 本来就会写下的那一个。</para>
    /// </summary>
    /// <param name="servicePrefix">服务段，形如 <c>"inventory:"</c>，必须以 <see cref="ReadableSeparator"/> 收尾。</param>
    /// <param name="parts">第一段是事件 kind（进前缀），其余是尾段。</param>
    public static string ComposeServiceScoped(string servicePrefix, params string?[] parts)
    {
        ArgumentException.ThrowIfNullOrEmpty(servicePrefix);
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Length < 1 + MinimumTailParts)
        {
            throw new ArgumentException(
                $"信封幂等键需要 1 个 kind 段加至少 {MinimumTailParts} 个尾段，实际只给了 {parts.Length} 段。",
                nameof(parts));
        }

        return Compose($"{servicePrefix}{parts[0]}{ReadableSeparator}", parts[1..]);
    }

    /// <summary>
    /// 前缀的长度上界，由 <see cref="Budget"/> 与 <see cref="DigestLength"/> **派生**，不手抄。
    /// 超过它时回落形态自己就装不进承载列。
    /// </summary>
    public static int MaxPrefixLength => Budget - DigestLength;

    /// <summary>
    /// 这把键是不是回落形态（前缀之后不含 <see cref="ReadableSeparator"/>）。
    /// <para><b>只给排障与用例用，不是路由判据</b>：判别式的价值在于两种形态互斥，
    /// 而不在于有人按形状分流。今天没有任何下游解析信封键。</para>
    /// </summary>
    public static bool IsDigested(string prefix, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(key);

        return key.StartsWith(prefix, StringComparison.Ordinal)
            && key.AsSpan(prefix.Length).IndexOf(ReadableSeparator) < 0;
    }

    private static string Digest(string value) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
