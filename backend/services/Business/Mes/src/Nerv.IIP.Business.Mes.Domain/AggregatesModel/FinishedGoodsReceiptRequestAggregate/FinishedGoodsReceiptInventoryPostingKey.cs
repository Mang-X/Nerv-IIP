using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;

/// <summary>
/// 生成完工入库过账键，并校验键是否属于指定完工入库单。
/// </summary>
/// <remarks>
/// Inventory 按精确键重放，能装入库存列的旧键必须逐字保留。
/// 超界时把作用域和原始键分别编码为完整 SHA-256 base64url 摘要。作用域可独立重算，
/// 供成本授权校验。前缀之后的冒号仅出现在可读形态，防止调用方原始键与摘要形态相互冒充。
/// 两种形态共用固定前缀，它也是 Inventory 成本授权及 MES、ERP 事件路由的契约。
/// </remarks>
public static class FinishedGoodsReceiptInventoryPostingKey
{
    /// <summary>
    /// 跨服务路由契约前缀。每一种形态都以它开头，三处下游 <c>StartsWith</c> 因此在新形态下仍成立。
    /// </summary>
    public const string Prefix = "mes:finished-goods-receipt:";

    /// <summary>可读形态的段间分隔符，也是**形态判别器**所看的那个字符。</summary>
    public const char ReadableSeparator = ':';

    /// <summary>
    /// 回落形态里作用域摘要与尾段摘要之间的分隔符。
    /// **特意不取 <see cref="ReadableSeparator"/>**：这样回落形态在前缀之后恒不含 <c>:</c>，
    /// 与逐字形态按字符类互斥。<c>.</c> 落在 Inventory 的 <c>^[A-Za-z0-9_.:-]+$</c> 之内。
    /// </summary>
    public const char FallbackSeparator = '.';

    /// <summary>
    /// 下游 Inventory 承载该键的列宽（多列取最小，#3281 判据）。
    /// 今天两条承载列都是 128：<c>stock_movements.idempotency_key</c> 与
    /// <c>authority_resolution_pending_audits.idempotency_key</c>，两者**都原样落调用方送来的键**。
    /// （<c>integration_event_dead_letters.idempotency_key</c> 500 是 <c>TruncateOptional</c> 截断写入，
    /// 不构成上界、不是权威。）
    /// <para>Inventory 侧仍各自写死 <c>HasMaxLength(128)</c>（迁移的真相在那边），
    /// 由 <c>MesFinishedGoodsReceiptInventoryPostingKeyBoundContractTests</c> 从 Inventory EF 模型
    /// 闭集枚举后与本常量对撞，任一单边改动即红。</para>
    /// </summary>
    public const int ColumnMaxLength = 128;

    /// <summary>
    /// 摘要形态的字符数，由「SHA-256 输出字节数」经 base64url 编码长度**派生**，不手抄。
    /// </summary>
    public static readonly int DigestLength = Base64Url.GetEncodedLength(SHA256.HashSizeInBytes);

    /// <summary>回落形态里作用域段的长度（定长）= 前缀 + 摘要。</summary>
    public static readonly int DigestedScopeLength = Prefix.Length + DigestLength;

    /// <summary>
    /// 回落形态产出的最长键 = 作用域摘要段 + 分隔符 + 尾段摘要。
    /// <para><b>这条不等式由谁看守，说准</b>：<see cref="EnsureFallbackFits"/> 是**首次使用即失败**的
    /// 兜底（C# 静态初始化是惰性的，首次触碰类型才跑，**不是启动期护栏**；真在生产上触发时会以
    /// <c>TypeInitializationException</c> 走与 poison message **完全同一条**逃逸出口）。
    /// 真正承担它的是 <c>MesFinishedGoodsReceiptInventoryPostingKeyBoundContractTests</c> 的两条对撞断言，
    /// 它们在 CI 上**更早**报红。</para>
    /// </summary>
    public static int FallbackMaxLength { get; } = EnsureFallbackFits();

    /// <summary>
    /// 基础键（首次过账 / ERP 资本化后补发）。可读形态装得下就原样，否则整段作用域换摘要。
    /// </summary>
    public static string Build(string organizationId, string environmentId, string requestNo)
    {
        var readable = Readable(organizationId, environmentId, requestNo);
        return readable.Length <= ColumnMaxLength ? readable : DigestedScope(readable);
    }

    /// <summary>
    /// 重投键：在基础键之上拼调用方原始键。
    /// <para>整键装得下就**逐字保持**（与改动前完全相同的字节）；装不下则**两段一起摘要**——
    /// 只摘尾段会与逐字出口产生别名，见类型注释里那段反例。</para>
    /// </summary>
    public static string BuildRetry(
        string organizationId,
        string environmentId,
        string requestNo,
        string idempotencyKey)
    {
        var readable = Readable(organizationId, environmentId, requestNo);
        var candidate = $"{readable}{ReadableSeparator}{idempotencyKey}";
        return candidate.Length <= ColumnMaxLength
            ? candidate
            : $"{DigestedScope(readable)}{FallbackSeparator}{Digest(idempotencyKey)}";
    }

    /// <summary>
    /// 这把键是否属于 <c>(organizationId, environmentId, requestNo)</c> 这一单的过账作用域。
    /// <para>四种可接受形态**全部由这三个值原样重算**，没有一种是「按形状放行」——
    /// 这条性质就是 #3332 选两段式回落而不是整键摘要的理由，改动不得让它退化。</para>
    /// <para><b>值域边界（pre-existing，本票不修）</b>：两种形态都用
    /// <c>StartsWith(作用域 + 分隔符)</c> 判定，因此**前提是作用域三段自身既不含
    /// <see cref="ReadableSeparator"/> 也不含 <see cref="FallbackSeparator"/>**：
    /// <list type="bullet">
    /// <item>若 <c>requestNo</c> 含 <c>:</c>，单据 <c>A</c> 的守卫会接受单据 <c>A:B</c> 的键；</item>
    /// <item>若 <c>organizationId</c> 形如 <c>{受害单据的作用域摘要}.x</c>，
    /// 受害单据的**回落分支**会接受它的键。</item>
    /// </list>
    /// 两者同族。今天由 org / env 来自鉴权作用域、<c>RequestNo</c> 来自号码分配器保证
    /// （调用方都不能自选），**不由本类型保证**——改动前用单一 <c>:</c> 时同样构造也成立，
    /// 本票未引入也未消除。</para>
    /// </summary>
    public static bool BelongsToScope(string organizationId, string environmentId, string requestNo, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var readable = Readable(organizationId, environmentId, requestNo);
        return IsScopedBy(key, readable, ReadableSeparator)
            || IsScopedBy(key, DigestedScope(readable), FallbackSeparator);
    }

    private static bool IsScopedBy(string key, string scope, char separator)
    {
        return string.Equals(key, scope, StringComparison.Ordinal)
            || key.StartsWith(scope + separator, StringComparison.Ordinal);
    }

    private static string Readable(string organizationId, string environmentId, string requestNo)
    {
        return $"{Prefix}{organizationId}{ReadableSeparator}{environmentId}{ReadableSeparator}{requestNo}";
    }

    /// <summary>
    /// 作用域段的摘要形态。摘要输入只有可读作用域本身 ⇒ <see cref="BelongsToScope"/> 可重算。
    /// 产出恒 <see cref="DigestedScopeLength"/> 字符，且在前缀之后不含 <see cref="ReadableSeparator"/>。
    /// </summary>
    private static string DigestedScope(string readable) => $"{Prefix}{Digest(readable)}";

    private static string Digest(string value) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static int EnsureFallbackFits()
    {
        var widest = DigestedScopeLength + 1 + DigestLength;
        return widest <= ColumnMaxLength
            ? widest
            : throw new InvalidOperationException(
                $"完工入库过账幂等键的回落形态需要 {widest} 个字符，下游承载列只放得下 {ColumnMaxLength} 个。");
    }
}
