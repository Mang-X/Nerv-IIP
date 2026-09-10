namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <summary>
/// MES 缺陷记录**入站**处置引用身份（<c>defect_records.disposition_reference_id</c>）的长度边界唯一出处（#3318）。
/// </summary>
/// <remarks>
/// 缺陷形状（可达性已由 <c>MesDefectDispositionReferenceAcceptanceTests</c> 在真实 <c>postgres:18</c>
/// 上跑出读数，不是静态推导）：
/// <c>NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect</c> 按 <c>DispositionType</c> 三选一取出
/// <c>payload.ReworkWorkOrderId</c> / <c>ScrapMovementId</c> / <c>ReturnDocumentId</c>，
/// 交给 <c>DefectRecord.AcceptDisposition</c> **逐字**（只 Trim，无截断、无派生）写进
/// <c>defect_records.disposition_reference_id</c>。改前这一列是 <c>varchar(100)</c>，
/// 而 Quality 侧这三个值的产出列 <c>nonconformance_reports.{rework_work_order_id, scrap_movement_id, return_document_id}</c>
/// 都是 <c>varchar(150)</c>：101–150 字符的合法处置引用落库即 <c>22001</c>。
///
/// **后果比一次写入失败更糟**：这是 CAP 消费者，而该 handler 函数体内 <c>catch</c> 计数为 **0**
/// （比 #3315 的 handler 还少一层：那边至少接 <c>InvalidOperationException</c>）。
/// <c>SaveChangesAsync</c> 抛的 <c>DbUpdateException</c>（PostgreSQL <c>22001</c>）直接逃逸出
/// <c>HandleAsync</c>，命中本仓已登记的系统性缺口 **#877**（CAP 消费者异常逃逸成 poison message），
/// 整条 <c>business-mes.quality-ncr-disposition</c> 消费链卡死。**本票不修 #877 本身**，
/// 只让这一条消费链不再成为它的实例。
///
/// **止血由两层组成，缺一不可**（沿用 #3315 / PR #3317 的裁定）：
/// <list type="number">
/// <item>把 MES 承载列加宽到 150（= Quality 三个产出列的列宽），让今天**合法**的 101–150 字符处置引用
/// 真的落库，而不是整类静默进死信——只加守卫不加列宽等于把「质量处置回写 MES 缺陷记录」这条链
/// 悄悄关掉，比卡链更隐蔽；</item>
/// <item>写入前按 <see cref="ColumnMaxLength"/> 显式拦截并走**死信**，让「列宽不够」这件事
/// 无论阈值取多少都不再表现为消费链卡死——只加列宽等于把阈值推高，Quality 单边放宽时链还是会卡。</item>
/// </list>
///
/// **绝不截断，但本处的理由与 #3315 不同，别照抄那句话**：#3315 的截断危害是**折叠**
/// （<c>ux_quality_hold_contexts_scope_source</c> 按来源身份去重，两道工序的结论被折叠成一条）。
/// 本处**没有**那条去重键——<c>disposition_reference_id</c> 不参与 <c>defect_records</c> 的任何索引
/// （该结论由 <c>MesDefectDispositionReferenceIdLengthContractTests</c> 从 EF 模型枚举索引成员证出来，
/// 不是从注释推断）。本处截断的危害是**静默伪造一个下游引用**：返修工单号 / 报废流水号 / 退供单号
/// 被砍掉尾部之后仍然像一个引用，却指不到任何对象，而行上没有任何字段记录它被截断过。
/// 因此守卫只拒绝、只发死信，不改值。
///
/// **射程边界（声明放弃了什么，别读成完备）**：
/// <list type="number">
/// <item>本类只管 <c>defect_records.disposition_reference_id</c> 这一列。该列名在 MES 模型里是**唯一**的
/// （由契约用例按闭集枚举封闭，新增一张带同名列的表会红），因此本类没有具名豁免面。</item>
/// <item><see cref="ColumnMaxLength"/> 是 MES 一侧的事实。它与 Quality 侧三个产出列宽的关系由
/// <c>Nerv.IIP.Business.Acceptance.Tests.NcrDispositionReferenceCrossServiceWidthContractTests</c>
/// 跨服务对撞。本类自身**不能**证明 Quality 侧没有单边放宽。</item>
/// <item>不新建源码文本扫描护栏证明「所有写入都过这道守卫」——#3176 / PR #3214 三轮实证这类扫描
/// 不收敛并已按裁定移除。本类的绿**不能**读成「没有别的路径把值写进那一列」。</item>
/// </list>
/// </remarks>
public static class MesDefectDispositionReferenceIdPolicy
{
    /// <summary>
    /// 该值被逐字写进的 MES 列 <c>defect_records.disposition_reference_id</c> 的列宽。
    /// EF 侧仍写死 <c>HasMaxLength(150)</c>（迁移的真相在那边），由
    /// <c>MesDefectDispositionReferenceIdLengthContractTests</c> 从 EF 模型**闭集枚举**后与本常量对撞，
    /// 任一侧被单边改动即红。
    ///
    /// #3281 判据「一个值写进多列时有效上界取列宽最小值」在这里退化成一个数：MES 承载面只有这一列，
    /// 且契约用例要求它恰等于本常量，所以最小值与本常量恒等；哪一侧被单边改窄，红的是那条契约用例，
    /// 而不是让守卫悄悄放行到炸库。
    /// </summary>
    public const int ColumnMaxLength = 150;

    /// <summary>死信 <c>FailureCode</c>：处置引用身份超出 MES 承载列宽。</summary>
    public const string OverlongFailureCode = "disposition-reference-id-too-long";

    /// <summary>
    /// 入站处置引用身份是否超出承载列宽。<c>true</c> 时调用方必须走死信并返回，
    /// **不得**截断、不得继续落库、也不得抛出（抛出就是回到 poison message）。
    /// </summary>
    public static bool ExceedsColumn(string dispositionReferenceId)
    {
        ArgumentNullException.ThrowIfNull(dispositionReferenceId);
        return dispositionReferenceId.Length > ColumnMaxLength;
    }

    /// <summary>
    /// 死信 <c>FailureMessage</c>。**只报长度、不回显取值**：越界取值天然无上界，回显会把
    /// <c>failure_message</c> 也变成一个越界写面（持久化侧虽然会截断，但那样就把可诊断信息截没了）。
    /// </summary>
    public static string OverlongFailureMessage(string dispositionReferenceId)
    {
        ArgumentNullException.ThrowIfNull(dispositionReferenceId);
        return $"Quality disposition reference identity length {dispositionReferenceId.Length} exceeds the MES defect record column width {ColumnMaxLength}.";
    }
}
