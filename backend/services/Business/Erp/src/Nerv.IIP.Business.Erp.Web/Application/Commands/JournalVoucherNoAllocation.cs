using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands;

/// <summary>
/// 按来源单据身份分配 <c>journal-voucher</c> 规则的凭证号（<c>JV-yyyyMMdd-NNNNNN</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 凭证行通过组织、环境、来源类型和来源单号上的部分唯一索引去重（#3278 / S5）。
/// 分配器在组织、环境和规则范围内以同一来源身份回放原号。种子数据为支持回填日期和复算
/// 直接使用种子号，已有的派生号也随存量凭证保留。
/// </para>
/// <para>
/// 幂等键采用 <c>jv:{类型码}:{SHA-256 大写十六进制}</c>。固定摘要长度使键长与来源单号长度无关。
/// <c>EfCoreCodeStore.AddIdempotencyRecord</c> 只把记录加入 EF 跟踪，实际列宽约束在调用方工作单元保存时生效。
/// 超长键会在取号异常处理之外触发 PostgreSQL <c>22001</c>。排查来源单号长度时应追到上游生产者，
/// 例如库存发布的移动 GUID。<c>ErpVoucherNoLengthContractTests</c> 验证最宽调整来源标识的形状，
/// 其中手填的上游单号宽度未与 MES 的实际列宽联动。摘要键保留来源类型，排障时需从业务记录查具体单号。
/// </para>
/// <para>
/// 键和指纹共享摘要，避开重投时可能重新计算的金额、差异额和过账日。
/// 手工过账的客户端键与派生键在同一分配器命名空间。客户端可以显式提交相同的摘要键。
/// 同键异指纹时 <c>CodeAllocator</c> 抛出
/// <see cref="NetCorePal.Extensions.Primitives.KnownException"/>。
/// 键的文法与摘要输入由 <c>ConsumerJournalVoucherNumberKeyContractTests</c> 的独立冻结向量验证。
/// </para>
/// </remarks>
internal static class JournalVoucherNoAllocation
{
    /// <summary><see cref="Nerv.IIP.Contracts.Coding.StandardCodeRules"/> 里记账凭证那条规则的键。</summary>
    public const string RuleKey = "journal-voucher";

    /// <summary>分配器派生幂等键的固定前缀。</summary>
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
    /// 命令处理器向调用方传递分配异常。集成事件消费侧在外层将预期分配失败转换为死信结果。
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

        var digest = Digest(sourceType, sourceNo);
        var allocation = await codingService.AllocateAsync(
            organizationId,
            environmentId,
            RuleKey,
            requestedCode: null,
            FormatIdempotencyKey(sourceType.Code, digest),
            digest,
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
        return FormatIdempotencyKey(sourceType.Code, Digest(sourceType, sourceNo));
    }

    private static string FormatIdempotencyKey(string sourceTypeCode, string digest)
        => string.Concat(KeyPrefix, sourceTypeCode, ":", digest);

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
