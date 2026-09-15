using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands;

/// <summary>
/// 记账凭证号的分配入口（GitHub #3278 / S6）：凭证号一律取 <c>journal-voucher</c> 规则的
/// 分配器短号（<c>JV-yyyyMMdd-NNNNNN</c>），**不再从上游单号派生**。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么凭证号可以不再承载来源身份</b>：S5（PR #3480）已把「这张凭证是否已记」的判据
/// 搬到 <c>journal_vouchers.(source_type, source_no)</c> 两列并在其上建了 partial unique index。
/// 凭证号从那一刻起只是**账面显示值**，不是键。本类型只负责取号，
/// ⛔ 不负责去重——去重由调用方各自的来源列谓词与那条唯一索引承担。
/// </para>
/// <para>
/// <b>取号幂等键为什么取来源单据身份</b>：<c>CodeAllocator</c> **只按 <c>idempotencyKey</c> 去重**
/// （规则键 + 组织 + 环境 + 这把键），键不稳定就会在重放时多烧一个号。
/// 这里取的正是 S5 那条唯一索引的键——同一张来源单据重复触发时拿回**同一个**凭证号，
/// 于是「多记一张」在取号这一层就已经不成立，落库那条唯一索引是第二道而不是唯一一道。
/// </para>
/// <para>
/// <b>键有两种形态，⛔ 都不截断</b>（截断会把仅末几位不同的两张来源单据折叠成同一把键，
/// 换回同一个凭证号）：
/// <list type="number">
/// <item>原样式 <c>{类型码}{SourceKeySeparator}{来源单号}</c>——合得下时用，便于排障时直接读懂那一行；</item>
/// <item>摘要式 <c>{类型码}{DigestMarker}{SHA-256 十六进制}</c>——合不下时用，定长 <see cref="DigestLength"/>。</item>
/// </list>
/// <b>为什么需要形态 ②</b>：<c>journal_vouchers.source_no</c> 列宽 <b>150</b>，而
/// <c>code_idempotency_keys.idempotency_key</c> 列宽也是 150
/// （<see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/>）。原样式还要再加类型码与分隔符，
/// 所以顶格的来源单号**必然越界** —— 越界不会在入口被拒，而是在 <c>SaveChangesAsync</c>
/// 换来 PostgreSQL <c>22001</c>（#3288 / #3229 同形）。
/// ⚠️ 这不是「今天够用」的推断：两个列宽都从模型读出来对撞，见
/// <c>JournalVoucherNoAllocationTests.Allocation_idempotency_key_stays_within_the_code_idempotency_column</c>。
/// ⚠️ 但也别读成「所有调用方都会走到形态 ②」——当前 9 个命令侧调用点传的来源单号
/// 都受各自上游列宽（100）约束，实际全部落在形态 ①；形态 ② 是**结构性兜底**，不是当前热路径。
/// </para>
/// <para>
/// <b>两种形态不相撞</b>：<see cref="JournalVoucherSourceType.Code"/> 是闭集里的 <c>[A-Z]</c> 串，
/// 既不含 <see cref="SourceKeySeparator"/> 也不含 <see cref="DigestMarker"/>，
/// 所以类型码之后那一位（<c>:</c> 还是 <c>~</c>）唯一地区分两种形态，两个值域天然不相交；
/// 形态 ① 内部由「第一个 <c>:</c>」划出类型边界；形态 ② 内部的摘要输入是**带长度前缀、
/// 以 U+001F 分隔**的规范串，不同段划分拼不出同一个输入，故只剩 SHA-256 碰撞。
/// ⭐ 这与 #3278 §A2 排除掉的单列 <c>"{type}:{no}"</c> 形状**不是**一回事：那条排除针对的是
/// **落库承载业务身份、要被查询与反解**的列；本键既不落到 <c>journal_vouchers</c> 任何一列，
/// 也没有任何读面去反解它。
/// </para>
/// <para>
/// <b>本类型不覆盖的面</b>：seed（<c>WorldHistorySeedService</c>）的两处凭证按 #3278「显式不做」
/// 仍直接写 <c>JV-2026-S{n}</c> / <c>JV-2026-C{n}</c>（世界种子要 backdate 且必须可复算，
/// 而 <c>Document</c> 规则含 <c>yyyyMMdd</c> + 按日重置序列）。
/// ⇒ 落地后凭证号**并存三种格式**：分配器短号、存量派生号、种子号。
/// 集成事件消费侧的 5 个建凭证位点属 #3278 / S7，不在本类型的当前调用面上。
/// </para>
/// </remarks>
internal static class JournalVoucherNoAllocation
{
    /// <summary><see cref="Nerv.IIP.Contracts.Coding.StandardCodeRules"/> 里记账凭证那条规则的键。</summary>
    public const string RuleKey = "journal-voucher";

    /// <summary>原样式里类型码与来源单号之间的分隔符。</summary>
    public const string SourceKeySeparator = ":";

    /// <summary>摘要式里类型码之后的标记位；类型码字符集不含它，故两种形态的值域不相交。</summary>
    public const string DigestMarker = "~";

    /// <summary>SHA-256 转大写十六进制后的固定长度。</summary>
    public const int DigestLength = 64;

    /// <summary>取号幂等键的列宽上界，直接引用共享实体上的唯一出处（⛔ 不手抄数字）。</summary>
    public const int KeyMaxLength = CodeIdempotencyKey.IdempotencyKeyMaxLength;

    /// <summary>规范串里的段分隔符，取 ASCII 单元分隔符（US, U+001F）。</summary>
    private const char CanonicalSeparator = '';

    /// <summary>
    /// 按来源单据身份取一个凭证号。同一 <paramref name="sourceType"/> + <paramref name="sourceNo"/>
    /// 重复调用拿回同一个号（分配器按幂等键回放），⛔ 不会推进序列。
    /// </summary>
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
            // 指纹与幂等键同源：同一来源单据的重复触发**永远**是回放，不会变成
            // 「同键不同 payload」冲突。凭证与来源单据一一对应这件事由那条唯一索引承担，
            // 不需要在取号这一层再判一次 payload 是否相同。
            ErpCodingService.Fingerprint(sourceType.Code, sourceNo),
            cancellationToken);
        return allocation.Code;
    }

    /// <summary>
    /// 取号用的幂等键。合得下时用原样式，合不下时退到定长摘要式；**任何情况下都不截断**。
    /// <c>internal</c> 只为让契约用例能直接量它的长度上界与两种形态的值域不相交。
    /// </summary>
    public static string AllocationIdempotencyKey(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(sourceNo);
        var raw = string.Concat(sourceType.Code, SourceKeySeparator, sourceNo);
        return raw.Length <= KeyMaxLength ? raw : DigestAllocationIdempotencyKey(sourceType, sourceNo);
    }

    /// <summary>摘要式的构造，单独暴露只为让用例能直接断言两种形态的值域不相交。</summary>
    public static string DigestAllocationIdempotencyKey(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(sourceNo);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalKey(sourceType, sourceNo))));
        return string.Concat(sourceType.Code, DigestMarker, digest);
    }

    /// <summary>
    /// 摘要输入的规范串：每段前置十进制长度并以 U+001F 分隔，
    /// 故不同的段划分不可能拼出同一个输入。
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
}
