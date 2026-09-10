namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <summary>
/// MES 保留上下文/时间线**入站**来源单据身份的长度边界唯一出处（#3315）。
/// </summary>
/// <remarks>
/// 缺陷形状（可达性已由 <c>QualityFirstArticleMesQualityHoldAcceptanceTests</c> 在真实
/// <c>postgres:18</c> 上跑出读数，不是静态推导）：
/// <c>QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext</c> 把
/// <c>payload.SourceDocumentId</c> **逐字**（只 Trim，无截断、无派生）写进
/// <c>quality_hold_contexts.source_document_id</c> 与 <c>quality_hold_transitions.source_document_id</c>。
/// 改前这两列都是 <c>varchar(100)</c>，而 Quality 侧同一个值的产出列是 <c>varchar(250)</c>：
/// 首件用复合身份 <c>{workOrderId}:{operationTaskId}</c>（两段各取 MES 工单/工序 id 列宽 100 ⇒ **201**），
/// 周期检用复合行号 <c>{operationId}:{kind}:{contextId}:{sequence}</c>。两支都能超过 100。
///
/// **后果比一次写入失败更糟**：这是 CAP 消费者，handler 只对 <c>InvalidOperationException</c>
/// （以及 <c>ArgumentException</c>）有死信分支，<c>SaveChangesAsync</c> 抛的
/// <c>DbUpdateException</c>（PostgreSQL <c>22001</c>）不在其中 ⇒ 异常逃逸出
/// <c>HandleAsync</c>，命中本仓已登记的系统性缺口 **#877**（CAP 消费者异常逃逸成 poison message），
/// 整条 <c>business-mes.quality-inspection-result</c> 消费链卡死。**本票不修 #877 本身**，
/// 只让这一条消费链不再成为它的实例。
///
/// **止血由两层组成，缺一不可**：
/// <list type="number">
/// <item>把 MES 侧两列加宽到 250（= Quality 侧产出列宽），让今天**合法**的首件/周期检复合身份
/// 真的落库，而不是全部进死信——只加守卫不加列宽等于把首件质量保留整类静默关掉；</item>
/// <item>写入前按 <see cref="ColumnMaxLength"/> 显式拦截并走**死信**，让「列宽不够」这件事
/// 无论阈值取多少都不再表现为消费链卡死——只加列宽等于把阈值推高，Quality 侧一旦再放宽
/// （或出现本策略射程外的新写面），链还是会卡。</item>
/// </list>
///
/// **绝不截断**：截断会把仅末几位不同的两个来源身份折叠成同一个，而
/// <c>ux_quality_hold_contexts_scope_source</c> 正是按 <c>source_document_id</c> 去重的——
/// 两道不同工序的首件结论会被折叠成同一条保留上下文，后判定的那道静默复用前一道的结论。
///
/// **射程边界（声明放弃了什么，别读成完备）**：
/// <list type="number">
/// <item>本类只管 <c>quality_hold_contexts</c> / <c>quality_hold_transitions</c> 这两列。
/// MES 模型里同名的 <c>work_orders.source_document_id</c>（owned type，DemandPlanning 计划单溯源，宽 100）
/// **不由本策略管辖**，在契约用例里按具名豁免计数封闭。</item>
/// <item><see cref="ColumnMaxLength"/> 是 MES 一侧的事实。它与 Quality 侧产出列宽的关系由
/// <c>Nerv.IIP.Business.Acceptance.Tests.QualitySourceDocumentIdCrossServiceWidthContractTests</c>
/// 跨服务对撞（该测试项目同时引用两个服务的 Web 程序集，能同时读到两边的 EF 模型）。
/// 本类自身**不能**证明 Quality 侧没有单边改窄。</item>
/// <item>不新建源码文本扫描护栏证明「所有写入都过这道守卫」——#3176 / PR #3214 三轮实证这类扫描
/// 不收敛并已按裁定移除。本类的绿**不能**读成「没有别的路径把值写进那两列」。</item>
/// </list>
/// </remarks>
public static class MesQualityHoldSourceDocumentIdPolicy
{
    /// <summary>
    /// 该值被逐字写进的 MES 列的**共同**列宽。EF 侧仍写死 <c>HasMaxLength(250)</c>
    /// （迁移的真相在那边），由 <c>MesQualityHoldSourceDocumentIdLengthContractTests</c>
    /// 从 EF 模型**闭集枚举**后与本常量对撞，任一侧被单边改动即红。
    ///
    /// #3281 判据「一个值写进多列时有效上界取列宽最小值」在这里退化成一个数：契约用例要求受管的
    /// 每一列都等于本常量，所以最小值与本常量恒等，运行期不需要再取 <c>Min</c>；
    /// 一旦有人把其中一列单边改窄，红的是那条契约用例，而不是让守卫悄悄放行到炸库。
    /// </summary>
    public const int ColumnMaxLength = 250;

    /// <summary>死信 <c>FailureCode</c>：来源单据身份超出 MES 承载列宽。</summary>
    public const string OverlongFailureCode = "source-document-id-too-long";

    /// <summary>
    /// 入站来源单据身份是否超出承载列宽。<c>true</c> 时调用方必须走死信并返回，
    /// **不得**截断、不得继续落库、也不得抛出（抛出就是回到 poison message）。
    /// </summary>
    public static bool ExceedsColumn(string sourceDocumentId)
    {
        ArgumentNullException.ThrowIfNull(sourceDocumentId);
        return sourceDocumentId.Length > ColumnMaxLength;
    }

    /// <summary>
    /// 死信 <c>FailureMessage</c>。**只报长度、不回显取值**：越界取值天然无上界，回显会把
    /// <c>failure_message</c> 也变成一个越界写面（持久化侧虽然会截断，但那样就把可诊断信息截没了）。
    /// </summary>
    public static string OverlongFailureMessage(string sourceDocumentId)
    {
        ArgumentNullException.ThrowIfNull(sourceDocumentId);
        return $"Quality source document identity length {sourceDocumentId.Length} exceeds the MES quality hold column width {ColumnMaxLength}.";
    }
}
