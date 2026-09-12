using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;

/// <summary>
/// 完工入库 Inventory 过账幂等键的**唯一构造与识别出处**（#3332）。
/// </summary>
/// <remarks>
/// <para><b>缺陷形状</b>：原实现是纯前缀拼接
/// <c>"mes:finished-goods-receipt:{org}:{env}:{requestNo}"</c>（重投再拼 <c>":{原始键}"</c>）。
/// 拼接长度单调 ⇒ 下游 Inventory 的 <see cref="ColumnMaxLength"/> 是**真权威**（不是 #3290 那种
/// 无条件摘要产出的定长幽灵权威）。而三段各自的 Mes 列宽都是 100，
/// 最坏前缀 <c>27 + 101 + 101 + 100 = 329</c>、重投再 <c>+1</c> ⇒ <b>连空的原始键都放不下</b>。
/// 越界值经 <c>InventoryMovementRequestedIntegrationEventHandlerForPostingMovement</c>
/// （<c>ICapSubscribe</c>）落库，异常逃逸成 poison message：调用方拿 2xx、货进死信箱、界面看不见（#877 同族）。</para>
///
/// <para><b>修法：两段式回落（#3332 裁定的方案乙）</b>。整键短则**逐字保持**，长则**两段一起摘要**：</para>
/// <list type="number">
/// <item><b>整键装得下 ⇒ 一个字节都不动。</b>这不是优化，是硬约束：Inventory 的幂等查找
/// <c>FindMovementByIdempotencyKeyAsync</c> 按 <b>精确键</b> 在
/// <c>(org, env, sourceService, sourceDocumentId, idempotencyKey)</c> 上查，键一旦变形就查不到已落库的行，
/// 同一笔重投会**重复落库**。今天能走通的键（≤ <see cref="ColumnMaxLength"/>）因此必须原样保留，
/// 只有今天**根本走不通**的长键才回落。故本类型不需要 Wms <c>ReplayIdempotencyKeys</c> 那样的双键查找。</item>
/// <item><b>装不下 ⇒ 作用域段与尾段一律摘要</b>，产出
/// <c>{前缀}{作用域摘要}</c> 或 <c>{前缀}{作用域摘要}.{尾段摘要}</c>。
/// 作用域摘要的输入只有 <c>(org, env, requestNo)</c> ⇒
/// <see cref="BelongsToScope"/> 仍能由那三个值**原样重算**并比对。
/// 这正是本处不采用「整键摘要」的原因：<c>GetFinishedGoodsReceiptCostAuthorityQuery</c>
/// 拿 <see cref="BelongsToScope"/> 当**成本授权的作用域守卫**，整键摘要会把它从
/// 「这把键属于这一单」弱化成「这把键是完工入库键」——护栏悄悄失去牙齿比一开始就没有更坏。
/// 本仓先例 PR #3273 / PR #3275 收敛「闭集 kind + 摘要回落」时，键的下游**只是存储**，没有这种守卫，
/// 所以那两次没付过这个代价，不能照抄。</item>
/// </list>
///
/// <para><b>为什么「只有尾段超界」时也要把作用域段一起摘要</b>（PR #3340 审核抓出的阻断）：
/// 只摘尾段会让两条出口产出**同一个键**——
/// 取 <c>K2</c> 为任意超界原始键、<c>K1 = 摘要(K2)</c>（恰好 <see cref="DigestLength"/> 字符、谁都能算），
/// 则 <c>K1</c> 走逐字出口、<c>K2</c> 走回落出口，两者都落成 <c>{可读作用域}:{K1}</c>。
/// 两把不同的原始键折叠成一个派生键 ⇒ 第二次重投被 Inventory 当成第一次的重放**静默吞掉**，
/// 与 <c>InventoryIdempotencyKeyPolicy.Compose</c> 注释里写死禁止的「截断把两个键折叠成一个」是同一件事。
/// 「尾段按长度互斥」只在回落分支内部成立，**管不住逐字分支**——逐字分支的尾段就是调用方原文，
/// 长度可以恰好等于摘要长度。</para>
///
/// <para><b>两种形态的判别器：前缀之后是否出现 <c>:</c>。这一位完全在调用方控制之外。</b>
/// <list type="bullet">
/// <item><b>逐字形态必含</b>：那两个 <c>:</c> 是 <c>Readable</c> 那个格式串里的**字面量**，
/// **无条件存在**——连空段形态 <c>mes:finished-goods-receipt:::</c> 在前缀之后也还是 2 个，
/// 与三段是否非空**无关**。
/// <para>⚠️ **不要把这条地基挂在 <c>DomainGuard.Required</c> 上**：构造路径
/// （<see cref="Build"/> / <see cref="BuildRetry"/>）上的 org / env / requestNo
/// **根本没经过**那道守卫（只有 <see cref="BelongsToScope"/> 的调用入口
/// <c>FinishedGoodsReceiptRequest.IsInventoryPostingIdempotencyKey</c> 对三元组做了它，
/// 而 <see cref="BuildRetry"/> 那道只作用在 <c>idempotencyKey</c> 上）。
/// 「三段非空」既不是这两个冒号存在的原因，也不是必要条件。</para>
/// <para>真正让调用方拿不掉它们的是：**作用域三段来自聚合**——org / env 来自租户上下文、
/// <c>requestNo</c> 来自号码分配器，**不是调用方逐请求可控的输入**。
/// 调用方能做的只是往尾段里**再加** <c>:</c>（把冒号数往上加），**减不掉**。</para></item>
/// <item><b>回落形态恒不含</b>：两段都是 base64url（字符集 <c>[A-Za-z0-9-_]</c>，**不含** <c>:</c>），
/// 段间分隔符特意取 <c>.</c> 而不是 <c>:</c>。更根本的是——**回落形态里没有任何一个调用方原文字节**，
/// 调用方连注入的位置都没有。</item>
/// </list>
/// ⇒ 两种形态**按字符类互斥**，且互斥性不依赖任何长度巧合。
/// <para>顺带排除掉的一条歧路：**不能靠在尾段里加内容标记来判别**。调用方控制原始键，
/// 任何用允许字符集拼出来的标记都可伪造；而 Inventory 的 <c>^[A-Za-z0-9_.:-]+$</c> ⊆ 网关允许集，
/// **没有可用的保留字符**。</para></para>
///
/// <para><b>为什么摘要用 base64url 而不是本仓先例的 64 位十六进制</b>：两段都要回落，
/// 而 <c>27 + 64 + 1 + 64 = 156 &gt; 128</c> —— 十六进制装不下两段摘要，硬要装就得**截断摘要位数**，
/// 那才是需要新论证的过度设计。base64url 是标准编码（<see cref="Base64Url"/>），
/// 字符集落在 Inventory <c>InventoryValidationRules</c> 的 <c>^[A-Za-z0-9_.:-]+$</c> 之内，
/// <see cref="DigestLength"/> = 43，<c>27 + 43 + 1 + 43 = 114 ≤ 128</c>，**不截断**，摘要仍是完整 SHA-256。</para>
///
/// <para><b>27 字节字面前缀 <see cref="Prefix"/> 必须原样出现在每一种形态里</b>：它是**跨三个服务**的路由契约，
/// 不是 Mes 私有的可读装饰。已实读的解析点（扫描面见下）：
/// <list type="bullet">
/// <item><c>Inventory/Application/Valuation/InventoryUnitCostAuthority.cs</c>
/// <c>RequiresMesFinishedGoodsAuthority</c>：前缀决定「缺 authority reference 时是 Pending 还是
/// <c>NotRequired()</c>」。**前缀丢了会退化成 <c>NotRequired()</c>，调用方自带的 <c>UnitCost</c> 直接被采信**——
/// 四个解析点里后果最重的一个。</item>
/// <item><c>Mes/Application/IntegrationEventHandlers/StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed.cs</c>：
/// 按前缀把过账失败路由回完工入库聚合。</item>
/// <item><c>Erp/Application/IntegrationEventHandlers/WorkOrderCostIntegrationEventHandlers.cs</c>：
/// 按前缀区分完工入库与生产领用。</item>
/// <item>本类型的 <see cref="BelongsToScope"/>（Mes 成本授权守卫）——它解析的**不只是**字面前缀，
/// 还要重建整段作用域，见上文第 2 条。</item>
/// </list></para>
///
/// <para><b>本类型不证明什么</b>：上面那份解析点清单的**扫描面是「字面量 + 本类型的构造点」**
/// （全仓 <c>*.cs</c> / <c>*.ts</c> / <c>*.json</c>，排除 <c>obj</c>、<c>bin</c>）。
/// 若有人把该前缀抽成别处的常量再引用，这个面**扫不到**。**别把它读成穷举。**</para>
///
/// <para><b>与下游列宽的关系（交给 #3327 的口径，两支都要读）</b>：修好后 <c>stock_movements.idempotency_key</c>
/// 对本位点是**条件性派生**，与 <c>WmsText.LineIdempotencyKey</c> 同形，**不是** #3290 那种纯幽灵权威：
/// <list type="bullet">
/// <item>整键 ≤ <see cref="ColumnMaxLength"/> ⇒ 逐字保持 ⇒ 那一列**是**这一支的真权威；</item>
/// <item>整键 &gt; <see cref="ColumnMaxLength"/> ⇒ 回落成定长 ⇒ 那一列对原始键**零约束**。</item>
/// </list>
/// 因此可登记的**有效上界**取 Mes 命令校验器（<c>RetryFinishedGoodsReceiptInventoryPostingCommandValidator</c>），
/// 而该列的 128 只约束前一支。</para>
///
/// <para><b>射程边界</b>：本类型只管这把键**自己**装不装得进 Inventory 那两列。
/// 它被 Inventory 再次拼进 <c>StockMovementPosted</c> 信封键
/// （<c>EventIds.Idempotency</c> 是纯拼接）后是否装得进下游 <c>processed_integration_events</c> 的 512，
/// 是**同族的下一跳溢出、不同位点**，已另行立票，本类型不承担。</para>
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
