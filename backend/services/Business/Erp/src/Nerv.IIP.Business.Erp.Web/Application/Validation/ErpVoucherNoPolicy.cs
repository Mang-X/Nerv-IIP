using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.Erp.Web.Application.Validation;

/// <summary>
/// 派生凭证号的族。**闭集类型，不是字符串**：私有构造 + 静态实例，
/// 所以「族名含分隔符」「族名过长」这两条不变量在**编译期**就无法被违反，
/// 不需要运行期校验，也就没有一条只会以坏形态失败的异常路径。
/// </summary>
/// <remarks>
/// 族名必须只含 <c>[A-Z0-9]</c>（既不含 <see cref="ErpVoucherNoPolicy.RawSeparator"/>
/// 也不含 <see cref="ErpVoucherNoPolicy.DigestMarker"/>），且短到摘要式仍塞得进列宽——
/// 这两条由 <c>ErpVoucherNoLengthContractTests</c> 从 <see cref="All"/> 枚举后断言；
/// 而 <see cref="All"/> 是否漏登记，由同一个类里的反射对撞断言看住（含族名互异）。
/// </remarks>
public sealed class VoucherFamily
{
    private VoucherFamily(string name)
    {
        Name = name;
    }

    /// <summary>工单成本资本化凭证。</summary>
    public static VoucherFamily WorkOrderCapitalization { get; } = new("WOC");

    /// <summary>
    /// 工单成本迟到调整凭证。**改前是 <c>JV-WOC-ADJ-</c>**，与资本化族的前缀相互包含
    /// （工单号形如 <c>ADJ-x</c> 时两族撞号），故改名 <c>WOCADJ</c>；该族没有任何按号查重的调用方。
    /// </summary>
    public static VoucherFamily WorkOrderCostAdjustment { get; } = new("WOCADJ");

    /// <summary>采购收货 GR/IR 暂估凭证。</summary>
    public static VoucherFamily GoodsReceiptIrAccrual { get; } = new("GRIR");

    /// <summary>采购退货凭证。</summary>
    public static VoucherFamily PurchaseReturn { get; } = new("PRTN");

    /// <summary>客户红字通知单凭证。</summary>
    public static VoucherFamily CreditNote { get; } = new("CN");

    /// <summary>应付凭证。</summary>
    public static VoucherFamily AccountPayable { get; } = new("AP");

    /// <summary>应收凭证。</summary>
    public static VoucherFamily AccountReceivable { get; } = new("AR");

    /// <summary>成本待定档凭证。</summary>
    public static VoucherFamily CostCandidate { get; } = new("COST");

    /// <summary>
    /// 全部族的登记表。契约用例从这里枚举。
    /// **这是手工登记表**：类型里声明一个族却不追加到这里，编译期不会拦——
    /// 补住这个方向的是契约用例里那条反射对撞（<c>All_enumerates_every_declared_family_and_names_stay_distinct</c>），
    /// 不是这个列表本身。
    /// </summary>
    public static IReadOnlyList<VoucherFamily> All { get; } =
    [
        WorkOrderCapitalization,
        WorkOrderCostAdjustment,
        GoodsReceiptIrAccrual,
        PurchaseReturn,
        CreditNote,
        AccountPayable,
        AccountReceivable,
        CostCandidate,
    ];

    /// <summary>写进凭证号的族名。</summary>
    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// 记账凭证号「列宽 / 派生构造上界」关系的唯一出处（#3229）。
/// </summary>
/// <remarks>
/// 缺陷形状：<c>journal_vouchers.voucher_no</c> 列宽 100，且 <c>(organization_id, environment_id,
/// voucher_no)</c> 上有唯一索引；但所有派生凭证号都是「固定前缀 + 一个 100 宽的上游单号（+ 再一个上游标识）」，
/// 前缀一加必然越界。也就是说**有效上界不是上游单号的列宽，而是「凭证号列宽 − 前缀 − 分隔符 − 其余段」**，
/// 上游单号顶格时落库炸 <c>22001 value too long</c>。种子里的 <c>JV-2026-S00001</c> 固定 15 字符，
/// 所以演示数据一直是绿的——这正是它长期潜伏的原因。
///
/// 因此本类型承担两件事：
/// 1. <see cref="ColumnMaxLength"/>——凭证号可用长度的唯一数字出处，由契约测试与 EF 模型绑死，
///    校验器和构造式都从它派生，不许再各自手抄 100；
/// 2. <see cref="Compose"/>——派生凭证号拼接的入口，**绝不截断**（截断会把仅末几位不同的两个上游单据
///    折叠成同一个凭证号，撞上那条唯一索引就是「第二张凭证根本记不上」），越界时改用摘要式。
///
/// **不塌成同号的论证**（本票的风险点）：输出只有两种形态——
/// <list type="number">
/// <item>原样式 <c>JV-{FAMILY}-{seg}-{seg}…</c>（合得下时；除 <see cref="VoucherFamily.WorkOrderCostAdjustment"/>
/// 外与改前逐字节相同，故存量行与既有按号查重全部沿用）；</item>
/// <item>摘要式 <c>JV-{FAMILY}~{SHA256 十六进制}</c>（合不下时）。</item>
/// </list>
/// ① 族名是 <see cref="VoucherFamily"/> 闭集里的 <c>[A-Z0-9]</c> 串，既不含 <see cref="RawSeparator"/>
/// 也不含 <see cref="DigestMarker"/>，因此「<c>JV-</c> 之后第一个 <c>-</c> 或 <c>~</c>」唯一地划出族名边界——
/// 族名的字符集由契约用例逐族断言，而登记表是否漏了某个已声明的族由反射对撞断言看住；
/// **私有构造只关死了「外部造新族」，没关死「类型内声明却不登记」**，后者靠那条断言而不是靠类型。
/// ② 同一族内，原样式与摘要式在族名后那一位分别是 <c>-</c> 与 <c>~</c>，两个值域天然不相交。
/// ③ 摘要式之间：摘要输入是**带长度前缀、以 U+001F 分隔**的规范串（见 <see cref="CanonicalKey"/>），
/// 不同段划分不会拼成同一个输入，故只剩 SHA-256 碰撞。
/// ④ **未关闭的既有近似**：原样式内部仍以 <c>-</c> 连接各段，而上游单号本身含 <c>-</c>，
/// 所以 <c>(a="X-Y", b="Z")</c> 与 <c>(a="X", b="Y-Z")</c> 在原样式下仍会得到同一个凭证号。
/// 这是改前就有的形状，本次**没有**关闭——关闭它要改掉存量行的凭证号格式，超出本票范围。
///
/// **失败形态**：本类型**没有**运行期可达的失败路径——族是闭集，长度由 <see cref="Compose"/> 自行兜底。
/// 段为空/为 null 属编译期程序员错误，抛 <see cref="ArgumentException"/>。
/// <c>WorkOrderCostIntegrationEventHandlers</c> 的两处调用方在 CAP 消费者里，
/// 任何异常在消费者内都会逃逸（#877 同族）；本类型消除的是溢出，**不是**逃逸。
/// </remarks>
internal static class ErpVoucherNoPolicy
{
    /// <summary>
    /// 凭证号列宽。EF 侧仍写死 <c>HasMaxLength(100)</c>（迁移的真相在那边），
    /// 由契约测试从 EF 模型读出后断言两侧相等，任一单边改动即红。
    /// </summary>
    public const int ColumnMaxLength = 100;

    /// <summary>所有派生凭证号共用的全局前缀。</summary>
    public const string GlobalPrefix = "JV-";

    /// <summary>原样式里族名与各段之间、以及各段之间的分隔符。</summary>
    public const string RawSeparator = "-";

    /// <summary>摘要式里族名之后的标记位；族名字符集不含它，故两种形态的值域不相交。</summary>
    public const string DigestMarker = "~";

    /// <summary>SHA-256 转大写十六进制后的固定长度。</summary>
    public const int DigestLength = 64;

    /// <summary>规范串里的段分隔符，取 ASCII 单元分隔符（US, U+001F）。</summary>
    private const char CanonicalSeparator = '\u001F';

    /// <summary>
    /// 派生凭证号的唯一构造入口。合得下时逐字节沿用改前的 <c>JV-{FAMILY}-{段}-{段}</c>，
    /// 合不下时退到定长摘要式；**任何情况下都不截断**。
    /// </summary>
    public static string Compose(VoucherFamily family, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A derived voucher number needs at least one source segment.", nameof(segments));
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException("A derived voucher number segment must not be blank.", nameof(segments));
            }
        }

        var raw = string.Concat(GlobalPrefix, family.Name, RawSeparator, string.Join(RawSeparator, segments));
        return raw.Length <= ColumnMaxLength ? raw : Digest(family, segments);
    }

    /// <summary>摘要式的构造，单独暴露只为让用例能直接断言两种形态的值域不相交。</summary>
    public static string Digest(VoucherFamily family, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(family);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalKey(family, segments))));
        return string.Concat(GlobalPrefix, family.Name, DigestMarker, digest);
    }

    /// <summary>
    /// 摘要输入的规范串：段以 U+001F 分隔并各自前置十进制长度，
    /// 故不同的段划分不可能拼出同一个输入。
    /// </summary>
    public static string CanonicalKey(VoucherFamily family, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(segments);
        var builder = new StringBuilder();
        builder
            .Append(family.Name.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CanonicalSeparator)
            .Append(family.Name);
        foreach (var segment in segments)
        {
            builder
                .Append(CanonicalSeparator)
                .Append(segment.Length.ToString(CultureInfo.InvariantCulture))
                .Append(CanonicalSeparator)
                .Append(segment);
        }

        return builder.ToString();
    }
}
