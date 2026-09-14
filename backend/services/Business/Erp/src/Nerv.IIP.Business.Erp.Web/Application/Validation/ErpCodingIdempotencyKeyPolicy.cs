using Nerv.IIP.Coding;
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
/// （<c>BusinessGatewayIdempotencyKey</c>）只拒超出钳值的键，对「加后缀后才超」这类缺陷**零保护**
/// ——改前它的值恰等于列宽 150、连「原始键本身就等于列宽」这种巧合都盖不住后缀那几个字符，
/// #3327 把它抬到端点级最大值之后更是与本位点的有效上界无关（不写今天的钳值：那个数会变）。
/// 而后两处走 Erp 自有端点，全局钳压根不在路径上。
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
///
/// **射程边界一：本类型只管「加后缀」那三处，不管前缀式同列写入。**
/// 已知**不在射程内**的同列写入还有集成事件消费者的
/// <c>$"{ConsumerName}:{payload.IdempotencyKey}"</c> **前缀**式构造——
/// <c>ErpReturnIntegrationEventHandlers.cs:247</c>、同文件 <c>:269</c>
/// （形如 <c>{ConsumerName}:{key}:{InvoiceNo}</c>，段数更多）、同文件 <c>:451</c>，
/// 以及 <c>WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.cs:180</c>。
/// 那些键由发布侧 converter 生成而非调用方直接可控，与本票「合法 API 输入即可触发」不同族，
/// **未处理，也不由本类型或其契约用例看守**。
///
/// **射程边界二（#3307 已收口，改写自「只钉住 7 份里的 1 份」）。**
/// <c>CodeIdempotencyKey</c> 是共享实体（<c>common/Coding/Nerv.IIP.Coding/CodeEntities.cs</c>）。
/// 改前它的 EF 配置 <c>CodeEntityTypeConfigurations.cs</c> 在 **7 个服务里逐字节复制**、各写一遍
/// <c>HasMaxLength(150)</c>，本常量当时是手抄的第 8 份，只与 Erp 那一份对撞。
/// #3307 已把那 7 份整体删除、配置收进
/// <c>CodingModelBuilderExtensions.ConfigureCodingEntities</c>，<see cref="ColumnMaxLength"/>
/// 也不再手抄数字而是直接引用 <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/>。
/// **仍然别读成「列宽已被全仓钉死」**：收拢消灭的是「7 份互相漂移」，
/// 不是「谁都改不动这一列」；剩下的失效方向（服务不调那个扩展 / 另写一份本地配置盖掉）
/// 由 <c>CodeIdempotencyKeyCrossServiceWidthContractTests</c> 从 7 个服务的真实 EF 模型逐个读取来看守。
/// </remarks>
public static class ErpCodingIdempotencyKeyPolicy
{
    /// <summary>
    /// <c>code_idempotency_keys.idempotency_key</c> 列宽。
    /// **不再手抄数字**：直接引用共享实体上的 <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/>，
    /// 那也是 EF 配置（<c>CodingModelBuilderExtensions.ConfigureCodingEntities</c>）用的同一个常量（#3307）。
    ///
    /// <para>于是 <c>ErpCodingIdempotencyKeyLengthContractTests</c> 里那条「模型宽度 == 本常量」的对撞
    /// 对**列宽本身**已退化成同义反复；它没有被删，因为它的值域里还有一条不属于本票的具名豁免
    /// （死信箱那一列的 500）和一条闭集计数——那两段仍有鉴别力。
    /// 真正承担「7 个服务的列宽一致」的是
    /// <c>CodeIdempotencyKeyCrossServiceWidthContractTests</c>。</para>
    /// </summary>
    public const int ColumnMaxLength = CodeIdempotencyKey.IdempotencyKeyMaxLength;

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
    /// **可达性口径（与 <c>ErpKnownExceptionMessageArchitectureTests</c> 的台账登记同一句话）**：
    /// 这条 <see cref="KnownException"/> 在调用图上**沿同步公开 facade 可达**
    /// （调用方之一是 <c>ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler</c>，
    /// 属「sync requisition conversion facade」），因此它按 <c>Target</c> 登记、
    /// 消息必须是可静态分析的中文且不泄露内部细节——这一点不因下面那句而放松。
    ///
    /// 同时它是一条**防御性**分支：走 HTTP 的请求会先被命令校验器按
    /// <see cref="BaseMaxLengthFor"/> 的上界拒在入口，所以在**当前**调用图下真正能走到这里的
    /// 只有绕过校验器的调用方（内部直接构造命令、或后续新增的消费者路径）。
    /// 「类型上可达」与「今天谁会走到」是两件事，两者都如实写在这里，别只引其中一句。
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
