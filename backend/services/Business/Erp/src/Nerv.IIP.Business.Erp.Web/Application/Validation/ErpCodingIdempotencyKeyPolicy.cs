using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Application.Validation;

/// <summary>
/// Erp 编码幂等键「列宽 / 校验器上界 / handler 落库前追加量」三者关系的唯一出处（#3288）。
/// </summary>
/// <remarks>
/// 缺陷形状（可达性由 #3288 评论逐位点坐实，全部实读自 <c>origin/main</c>）：
/// <c>code_idempotency_keys.idempotency_key</c> 列宽 150，而 <c>ErpProcurementCommands</c> 里有三处
/// handler 在把键交给 <c>CodeAllocator</c> 之前给它追加后缀——
/// <list type="bullet">
/// <item><c>ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler</c> 追加 <see cref="RequestForQuotationSuffix"/>；</item>
/// <item><c>RecordSupplierInvoiceCommandHandler</c> 与
/// <c>ReleaseSupplierInvoicePaymentHoldCommandHandler</c> 追加 <see cref="AccountPayableSuffix"/>。</item>
/// </list>
/// 于是**有效上界不是列宽本身，而是「列宽 − 该写面最长后缀」**。改前三处里两处**完全没有**
/// 幂等键长度规则、第三处手抄了 <c>MaximumLength(150)</c>；<c>CodeAllocator.Normalize</c> 只 Trim、
/// 只查零长度，分配器到落库之间没有第二道闸；业务网关的全局钳
/// （<c>BusinessGatewayIdempotencyKey</c>）取值恰等于列宽 150、只拒 &gt;150，
/// 因此对「加后缀后才超」这类缺陷**零保护**，而后两处走 Erp 自有端点，全局钳压根不在路径上。
///
/// **行为变化方向**：≤ 有效上界的键零变化；超出有效上界的键从「落库时 Npgsql 22001」
/// 变成「入口 400 可归因拒绝」——严格更好，但**不是**「不再抛异常」。
///
/// **不做源码文本扫描证明「所有拼接都走 <see cref="Compose"/>」**：#3176 / PR #3214 三轮实证这类扫描
/// 不收敛（本质是用文本近似手搓 C# 词法分析器），已按裁定移除；#3231 另已实测「换成不可拼接的
/// 包装类型」只关得掉 <c>key + ":rfq"</c> 一种写法，<c>$"{key}:rfq"</c> / <c>string.Concat</c> /
/// <c>string.Format</c> / <c>key.ToString() + ":rfq"</c> 照样编译并产出完全相同的键。
/// 所以「<see cref="Compose"/> 是唯一入口」在本仓**只是约定，不是护栏**，别把契约用例的绿读成
/// 「没有人绕开它拼这把键」。
/// </remarks>
public static class ErpCodingIdempotencyKeyPolicy
{
    /// <summary>
    /// <c>code_idempotency_keys.idempotency_key</c> 列宽。EF 侧仍写死
    /// <c>HasMaxLength(150)</c>（迁移的真相在那边），由 <c>ErpCodingIdempotencyKeyLengthContractTests</c>
    /// 从 EF 模型闭集枚举后与本常量对撞，任一单边改动即红。
    /// </summary>
    public const int ColumnMaxLength = 150;

    /// <summary>
    /// 采购申请转 RFQ 写面在落库前追加的后缀。
    /// </summary>
    public const string RequestForQuotationSuffix = ":rfq";

    /// <summary>
    /// 应付单写面在落库前追加的后缀。**两个 handler 共用同一个常量而不是各写一份同值字面量**——
    /// 各写一份时「只改一处后缀」的变异对另一处零鉴别力，那种形状在 Inventory 侧（#3176）已被显式
    /// 记为弱点，这里不再复制。
    /// </summary>
    public const string AccountPayableSuffix = ":account-payable";

    /// <summary>
    /// 该写面的基础幂等键上界 = 列宽 − 本写面 handler 可能追加的最长后缀。
    /// <paramref name="suffixes"/> 必须是 handler 真正拿去拼接的那些字面量本身，
    /// 上界由它们算出来，**不许再在校验器里手抄数字**——手抄正是本票要消灭的形状。
    ///
    /// 同一条命令的幂等键若既被原样使用又被加后缀使用（本仓三处里有两处如此），
    /// 有效上界取各写面的最小值，即列宽减去**最长**后缀（#3281 判据的同族推论）。
    /// </summary>
    public static int BaseMaxLengthFor(params string[] suffixes)
    {
        ArgumentNullException.ThrowIfNull(suffixes);
        if (suffixes.Length == 0)
        {
            return ColumnMaxLength;
        }

        return ColumnMaxLength - suffixes.Max(suffix => suffix.Length);
    }

    /// <summary>
    /// 落库前给幂等键追加后缀的入口：**绝不截断**（截断会把仅末几位不同的两个键折叠成同一个，
    /// 那会让两次不同的创建请求换回同一个业务号），超出列宽就地抛
    /// <see cref="KnownException"/>，而不是把越界值送进数据库换一个 22001。
    ///
    /// 命中这条的前提是调用方绕过了命令校验器（例如内部直接构造命令、或消费者路径），
    /// 走 HTTP 的请求会先被校验器按 <see cref="BaseMaxLengthFor"/> 的上界拒在入口。
    /// </summary>
    public static string Compose(string idempotencyKey, string suffix)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(suffix);
        if (idempotencyKey.Length + suffix.Length > ColumnMaxLength)
        {
            throw new KnownException("幂等键追加写面后缀后超出长度上限，请缩短幂等键后重试。");
        }

        return idempotencyKey + suffix;
    }
}
