using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands;

/// <summary>
/// 记账凭证号的分配入口（GitHub #3278 / S6）：凭证号一律取 <c>journal-voucher</c> 规则的
/// 分配器短号（<c>JV-yyyyMMdd-NNNNNN</c>），不再从上游单号派生。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么凭证号可以不再承载来源身份</b>：S5（PR #3480）已把「这张凭证是否已记」的判据
/// 搬到 <c>journal_vouchers.(source_type, source_no)</c> 两列并在其上建了 partial unique index。
/// 凭证号从那一刻起只是**账面显示值**，不是键。本类型只负责取号，
/// 不负责去重——去重由调用方各自的来源列谓词与那条唯一索引承担。
/// </para>
/// <para>
/// <b>幂等键取 <c>(来源类型, 来源单号)</c></b>：<c>CodeAllocator</c> **只按 <c>idempotencyKey</c> 去重**
/// （规则键 + 组织 + 环境 + 这把键），键不稳定就会在重放时多烧一个号。
/// 这里取的正是 S5 那条唯一索引的键——同一张来源单据重复触发时拿回**同一个**凭证号。
/// </para>
/// <para>
/// ⭐ <b>键是定长摘要 <c>jv:{类型码}:{SHA-256 十六进制}</c>，不是可读拼串</b>。
/// 键长恒为 <see cref="KeyPrefix"/>(3) + 类型码 + 1 + <see cref="DigestLength"/>(64)，
/// **与来源单号长度无关**，因此没有「合得下 / 合不下」两种形态，也就没有回落分支。
/// </para>
/// <para>
/// ⚠️ <b>承重理由只有一条，⛔ 不是「修一个今天可达的溢出」</b>——今天**两侧都不越界**：
/// <list type="bullet">
/// <item>命令侧（本入口）9 个调用点传的来源单号受各自上游列宽约束
///   （<c>ErpVoucherNoLengthContractTests.UpstreamNoColumnWidth</c> = 100）⇒ 可读拼法上界
///   <b>108 / 150、余量 42</b>；</item>
/// <item>消费侧 <c>WorkOrderCapitalization</c> 族的来源单号是发布侧
///   <c>InventoryIntegrationEventConverters</c> 的 <c>movementId.ToString()</c>，
///   <c>StockMovementId</c> 是 <c>IGuidStronglyTypedId</c> ⇒ GUID 文本恒 36（**类型级事实**，
///   ⛔ 不是列宽约束）⇒ 上界 <b>40</b>。</item>
/// </list>
/// 真正承重的是**失败形态**这条三步链：<c>EfCoreCodeStore.AddIdempotencyRecord</c> 只做
/// <c>DbSet.Add</c>、**不 SaveChanges**（<c>EfCoreCodeStore.cs:67</c>）＋ <c>CodeIdempotencyKey</c>
/// 构造**不校长度** ⇒ 键溢出的 <c>22001</c> 在**调用方 UoW** 才抛、落在消费者 gate 的 <c>try</c> **之外**
/// ⇒ 逃逸成 poison message（#877 仍 OPEN）。定长键把这条链的入口整个去掉。
/// 第二条（两侧共有）：一条规则两种文法会让同族的行一半可读一半不可读——任一上游列一加宽，
/// 该族就**静默**翻到摘要形态，比统一不可读更难排障。
/// ⭐ 代价如实登记：排障时这一行读不出对应哪张来源单据，只看得出族。
/// </para>
/// <para>
/// ⛔⭐ <b>上界常量一律引 <c>ErpVoucherNoLengthContractTests</c>，不要自己从别处推</b>。
/// 这张票上「把一个紧的上界配一条不属于该链路的宽列」已经**复发三次**，全部是我写的：
/// <list type="number">
/// <item><b>158</b> = 闭集码长 7 × <c>journal_vouchers.source_no</c> 列宽 150
///   —— <c>source_no</c> 是**本表自己**的列，不约束调用方传进来的值；</item>
/// <item><b>183</b> = <c>source_type</c> 列宽 32 × <c>source_no</c> 150
///   —— 类型码是私有构造的闭集，12 个码最长 <c>SUPPINV</c> = 7，**没有生产者写得出 32 字符类型码**；</item>
/// <item><b>154</b> = <c>WOC</c> 3 × <c>WmsEntityTypeConfigurations.cs:471</c> 的 150
///   —— 那是 **WMS 自己存 Inventory 回传值的下游副本列**，对 ERP 收到的 payload 零约束。</item>
/// </list>
/// 三次的共同形状：**列宽只约束「谁写这一列」，不约束「谁把值传过来」**；
/// 要算某条链路的上界，得追到**发布侧**的取值。读数写在
/// <c>ErpVoucherNoLengthContractTests</c>（<c>UpstreamNoColumnWidth</c> = 100、
/// <c>WidestAdjustmentSourceIdWidth</c> = 134）。
/// ⚠️ <b>两个数的钉住程度不一样，⛔ 别当成同一档事实</b>：
/// <list type="bullet">
/// <item><b>134 有活断言</b>——<c>Widest_production_source_identifier_matches_the_shape_it_is_derived_from</c>
///   把它与形状实例化后的实际长度对撞（形状少一段即红），且被
///   <c>JournalVoucherSourceContractTests</c> 当作 <c>source_no</c> 列宽的下界引用。</item>
/// <item>⛔ <b>100 是手抄数，当前没有任何断言把它钉到真实列宽上</b>。它要对应的是**生产者侧**
///   MES <c>OperationTaskEntityTypeConfiguration</c> 的 <c>operation_task_id</c> 列宽；
///   #3278 / S8 实测：把那一列改成 200、Erp 侧常量一个字节不动 ⇒ <b>Erp 全量用例零红</b>
///   （而真实最宽 sourceId 会变成 <c>machine-{200}-r{19}-void</c> = 234，撞 <c>source_no</c> 的 150
///   ⇒ 顶格 <c>22001</c>）。
///   ⭐ <b>失效方向就是这条</b>：上游把 <c>operation_task_id</c> 加宽时，本仓没有任何东西会转红。</item>
/// </list>
/// ⭐ <b>为什么 S8 没把 100 也钉住</b>：承重列在 <b>MES</b>，而
/// <c>Nerv.IIP.Business.Erp.Web.Tests</c> **零 MES 项目引用**（实读 csproj）。
/// 在 Erp 侧能读到的 <c>operation_task_id</c> 只有 <c>ErpCostAccountingEntityTypeConfigurations</c>
/// 那 4 处——那是 <b>Erp 自己存 MES 回传值的下游副本列</b>，⛔ 对 Erp 收到的 payload 零约束，
/// 拿它当锚正是上面 158/183/154 那三次的同一形状（会造出一条「看起来钉住了其实没有」的断言，
/// 比明说没钉住更坏）。要真正钉住只能落在同时引用两服务的跨服务测试项目里，超出 S8 范围。
/// ⛔ 别再从别的表的列宽反推。
/// </para>
/// <para>
/// ⚠️ <b>与客户端可写幂等键的关系（⛔ 别读成「值域不相交」）</b>。
/// <c>journal-voucher</c> 这条规则改前只有一个消费者 <c>PostJournalVoucherCommand</c>，
/// 它的 <c>IdempotencyKey</c> 由 <c>ErpSalesFinanceEndpoints</c> 直通请求体；本入口把另外 9 个位点
/// 也接到同一条规则上，于是客户端可写的幂等键与本入口的派生键落在同一个
/// <c>(org, env, rule_key, idempotency_key)</c> 命名空间里，
/// ⛔ <b>两类键的值域并不是不相交的</b>——新文法只保证它们**不会自然相撞**：
/// 派生键是 <c>jv:{类型码}:{规范串的 SHA-256 大写十六进制}</c>，客户端要撞上必须主动算出正确的摘要，
/// ⛔ 不是随手写得出的串（改文法之前 <c>{类型}:{单号}</c> 恰好就是派生键，复审实测可触发，
/// 那才是必须换文法的理由）。
/// <b>失效方向</b>：
/// ① 真撞上不会静默——<c>CodeAllocator.ToReplay</c> 吃「同键不同指纹」抛
/// <see cref="NetCorePal.Extensions.Primitives.KnownException"/>，对应来源单据建不出凭证，
/// 是响亮失败不是坏账；
/// ② 有意投毒在**任何**规则的客户端键上都做得到（<c>account-payable</c> 等同理），
/// ⛔ 不是本入口的安全边界；
/// ③ 若日后要把它变成**真正**的不相交，判据必须跑在 <c>CodeAllocator.Normalize</c>
/// （<c>value.Trim()</c>）**之后**——只在 FluentValidation 里比前缀会被前导空白
/// （空格 / 制表符 / 换行 / U+00A0，复审实测四种）绕过，而且会误伤
/// <c>jv:2026-09-16-001</c> 这类自然键。本入口**没有**加那条规则。
/// </para>
/// <para>
/// ⭐ <b>全仓唯一的一份键派生</b>：命令侧 9 个位点直接调 <see cref="AllocateAsync"/>；
/// 集成事件消费侧 5 个位点经 <c>ConsumerJournalVoucherNumber.TryAllocateAsync</c>
/// （gate-and-skip 包装）落到**本类型这同一套** <see cref="CanonicalKey"/> / <see cref="Digest"/>。
/// 两侧写的是同一条 <c>journal-voucher</c> 规则、同一张 <c>code_idempotency_keys</c> 表，
/// 因此「两套键文法」这件事在结构上不可能再发生。
/// 文法本身（前缀、分隔、长度前缀、SHA-256、大写十六进制）由
/// <c>ConsumerJournalVoucherNumberKeyContractTests.Digest_input_is_frozen_by_golden_vectors</c>
/// 的**外部独立算出的冻结向量**（Python <c>hashlib</c>）钉住，⛔ 不靠源码互读。
/// </para>
/// <para>
/// <b>本类型不覆盖的面</b>：seed（<c>WorldHistorySeedService</c>）的两处凭证按 #3278「显式不做」
/// 仍直接写 <c>JV-2026-S{n}</c> / <c>JV-2026-C{n}</c>（世界种子要 backdate 且必须可复算，
/// 而 <c>Document</c> 规则含 <c>yyyyMMdd</c> + 按日重置序列）。
/// ⇒ 落地后凭证号**并存三种格式**：分配器短号、存量派生号、种子号。
/// </para>
/// </remarks>
internal static class JournalVoucherNoAllocation
{
    /// <summary><see cref="Nerv.IIP.Contracts.Coding.StandardCodeRules"/> 里记账凭证那条规则的键。</summary>
    public const string RuleKey = "journal-voucher";

    /// <summary>
    /// 派生幂等键的前缀。
    /// ⛔ 它**不是**任何校验器的判据（见上面「与客户端可写幂等键的关系」）。
    /// </summary>
    public const string KeyPrefix = "jv:";

    /// <summary>SHA-256 转大写十六进制后的固定长度。</summary>
    public const int DigestLength = 64;

    /// <summary>规范串里的段分隔符，取 ASCII 单元分隔符（US, U+001F）。</summary>
    private const char CanonicalSeparator = '';

    /// <summary>
    /// 按来源单据身份取一个凭证号。同一 <paramref name="sourceType"/> + <paramref name="sourceNo"/>
    /// 重复调用拿回同一个号（分配器按幂等键回放），不会推进序列。
    /// </summary>
    /// <remarks>
    /// ⛔ 本方法**不捕获异常**：命令处理器里异常要返回给调用方。
    /// 消费侧入口 <c>ConsumerJournalVoucherNumber.TryAllocateAsync</c> 在本方法之外另包一层
    /// gate-and-skip，因为 CAP 消费者里抛业务异常会逃逸成 poison message（#877 仍 OPEN）；
    /// 那一层**不属于**本入口的职责 —— ⭐ 它也正是消费侧与本入口之间**仅存的真实差异**。
    /// </remarks>
    public static async Task<string> AllocateAsync(
        ErpCodingService codingService,
        string organizationId,
        string environmentId,
        JournalVoucherSourceType sourceType,
        string sourceNo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codingService);
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNo);

        var allocation = await codingService.AllocateAsync(
            organizationId,
            environmentId,
            RuleKey,
            // ⛔ 不接受调用方给定码：凭证号的值域从此只由规则产生。
            requestedCode: null,
            AllocationIdempotencyKey(sourceType, sourceNo),
            // 指纹取的就是键里那个摘要：同键必同指纹 ⇒「同键不同指纹」那条 KnownException
            // 在**本入口自己的调用之间**不可达。⚠️ 它并非全局不可达：外部直接篡改
            // code_idempotency_keys 行、或客户端用同一把串走 PostJournalVoucherCommand
            // 都够得到（见上面「与客户端可写幂等键的关系」）。
            Digest(sourceType, sourceNo),
            cancellationToken);
        return allocation.Code;
    }

    /// <summary>
    /// 取号幂等键：<c>jv:{来源类型码}:{SHA-256}</c>。
    /// org/env 不必写进串里——分配器自己按 org/env/ruleKey 分桶。
    /// </summary>
    public static string AllocationIdempotencyKey(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNo);
        return string.Concat(KeyPrefix, sourceType.Code, ":", Digest(sourceType, sourceNo));
    }

    /// <summary>
    /// 摘要输入的规范串：两段各自前置十进制长度、以 U+001F 分隔，
    /// 故不同的段划分不可能拼出同一个输入（只剩 SHA-256 碰撞）。
    /// </summary>
    public static string CanonicalKey(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(sourceNo);
        return new StringBuilder()
            .Append(sourceType.Code.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CanonicalSeparator)
            .Append(sourceType.Code)
            .Append(CanonicalSeparator)
            .Append(sourceNo.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CanonicalSeparator)
            .Append(sourceNo)
            .ToString();
    }

    /// <summary>规范串的 SHA-256 大写十六进制。键与指纹都取它。</summary>
    public static string Digest(JournalVoucherSourceType sourceType, string sourceNo)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalKey(sourceType, sourceNo))));
    }
}
