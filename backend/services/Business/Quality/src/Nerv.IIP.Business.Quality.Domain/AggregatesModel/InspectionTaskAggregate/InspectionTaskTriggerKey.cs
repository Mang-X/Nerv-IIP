namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

/// <summary>
/// <c>inspection_tasks.trigger_idempotency_key</c> 的承载上界（#2977）。
///
/// **缺陷形状**：该列改前宽 300，而写进它的键**从来不由任何声明关系收敛**——五个事件触发点里有四个
/// 把上游 <c>IIntegrationEventEnvelope.IdempotencyKey</c>（公开契约里是**无 <c>MaxLength</c> 的裸
/// <c>string</c>）原样透传或再追加一段来源行号。超宽时 <c>SaveChangesAsync</c> 抛 Npgsql <c>22001</c>，
/// 在 CAP 消费者里逃逸成 poison message（#877），而该列上的唯一索引
/// <c>ux_inspection_tasks_scope_trigger_key</c> 决定了**不能改键构成**：存量键与新键会并存，
/// 在制工单重复开任务且不可回滚。故本票只放宽列宽 + 加守卫。
///
/// **受治理写面（<see cref="MaxLength"/> 的派生集合）**
///
/// 「受治理」的判据是**这一段的取值由调用方控制且没有声明上界**——只有这种写面才需要用列宽兜住。
/// 逐条由机器可查的关系派生，任何一段被单边改动都会红：
/// <list type="table">
/// <item><term>WMS 收货（<c>InspectionTaskTriggerIntegrationEventHandlers.cs:73</c>）</term>
/// <description><c>{事件键}:{line.LineReference}</c>；上界追到 **WMS 生产侧**
/// （<c>inbound_orders</c> / <c>inbound_order_lines</c> 列宽），由
/// <c>InspectionTaskTriggerKeyCrossServiceWidthContractTests</c> 跑真实 converter 实测 → 425。</description></item>
/// <item><term>ERP 收货（同文件 <c>:129</c>）</term>
/// <description>同形；追到 <c>purchase_receipts</c> / <c>purchase_receipt_lines</c> 列宽 → 433，
/// 是四条上游写面里最宽的一条。</description></item>
/// <item><term>MES 工序完工（同文件 <c>:183</c>）</term><description>裸透传；追到 MES 侧 → 351。</description></item>
/// <item><term>MES 成品入库申请（同文件 <c>:230</c>）</term><description>裸透传；追到 MES 侧 → 337。</description></item>
/// <item><term>首件（同文件 <c>:371</c>）</term>
/// <description><c>FirstArticleInspection</c> 拼 <c>{org}:{env}:{workOrderId}:{operationTaskId}</c>；
/// 后两段合起来就是同一行的 <c>source_document_id</c>，故上界由**本表自己的列宽**派生 → **474，
/// 全部受治理写面里最宽的一条，<see cref="MaxLength"/> 取自它**。</description></item>
/// <item><term>周期检（时间窗 / 数量窗两处）</term>
/// <description><see cref="PeriodicInspectionSourceLine.TriggerIdempotencyKey"/>，闭集 kind + Guid + long → 83。</description></item>
/// </list>
///
/// **⚠️ 世界观历史 seed 是「只进 fits、不进 defines」的写面**
///
/// <c>WorldHistorySeedService.cs:245</c> 也写这一列，形状是
/// <c>seed:world-history:{sourceService}:{sourceDocumentId}:{sourceDocumentLineId}</c>。
/// 若按「三段都写进本行的有界列」去饱和，会得到 621——但那是**假想饱和**：
/// **seed 的三段输入全部来自它自己的定长生成器**（<c>WorldHistorySpec.WorkOrderNo(i)</c> 产出
/// <c>"WO-2026-00001"</c>，13 字符），实际键长约 40 字符，没有任何调用方能把它撑宽。
///
/// **所以它不进派生集合。** 让一段**计划拆除**的演示代码去定界一张增长型事实表上带唯一索引的列，
/// 等于围绕世界观种子建立一条维护契约——owner 已明确裁定停止这么做。种子拆除时，这里
/// **不需要任何缩窄迁移**：<see cref="MaxLength"/> 由首件那条写面定界，与种子无关。
/// 种子越界（今天不可达）由 <see cref="EnsureWithinColumn"/> 在 seed 期就地 fail-closed。
///
/// **没有摘要回落（#3290 的教训已实读排除）**
///
/// <c>FirstArticleInspection.TriggerIdempotencyKey</c>、
/// <see cref="PeriodicInspectionSourceLine.TriggerIdempotencyKey"/> 与 seed 侧三处都是纯插值，
/// 四条事件写面是透传或再拼一段，**全链路没有任何 hash / 截断**。所以这一列确实**逐字承载**调用方的键，
/// 列宽在这里真的构成上界——不同于 PR #3290 里那两个「写入前无条件 SHA256、落库永远 75 定长」的收据列。
///
/// **值域边界（声明放弃了什么，别读成完备）**
/// <list type="number">
/// <item>上游 <c>IdempotencyKey</c> 在公开契约里**仍然无声明上界**（<c>Contracts.Wms</c> / <c>Contracts.Erp</c> /
/// <c>Contracts.Mes</c> 三处信封与 <c>IntegrationEventEnvelopeValidator</c> 都只查非空、不查长度）。
/// 425/433/351/337 是**今天这些 converter 的形状**，不是契约保证。上游换个更长的前缀、
/// 或新增一个直接构造信封的生产者，契约用例算不到——那时兜底的是下面这条守卫，不是这些数字。</item>
/// <item>因此 <see cref="EnsureWithinColumn"/> **必须留着**：它把「未预料的长键」从落库时不可归因的
/// <c>22001</c> 变成构造时可归因的 <see cref="ArgumentOutOfRangeException"/>。
/// 两者**同样都是 poison message**（本仓 CAP 消费者不捕获 handler 异常，#877 仍 OPEN），
/// 本票**没有**把它改成死信投递，别把守卫读成「消费链不会再卡」。</item>
/// <item>本类**不做**源码文本扫描去证明「所有写面都走这里」。#3176 / PR #3214 三轮实证这类扫描不收敛。
/// 守卫住在**聚合构造函数**里，是结构性的：任何写面都必须经过 <see cref="InspectionTask.CreatePending"/>，
/// 绕不开。但「写面枚举是否完整」本身仍靠人读，本 PR 的枚举维度写在 PR 正文。</item>
/// <item>放宽列宽**不改任何既有键的构成**，因此 <c>ux_inspection_tasks_scope_trigger_key</c> 上的
/// 去重语义零变化；存量行零迁移动作。</item>
/// </list>
/// </summary>
public static class InspectionTaskTriggerKey
{
    /// <summary>
    /// <c>inspection_tasks.trigger_idempotency_key</c> 列宽。EF 侧仍写死
    /// <c>HasMaxLength(474)</c>（迁移的真相在那边），由
    /// <c>InspectionTaskTriggerIdempotencyKeyLengthContractTests</c> 从 EF 模型闭集枚举后与本常量对撞，
    /// 任一单边改动即红。
    ///
    /// **「不许少留」与「不许多留」由两条强度不同的断言分别看守**：
    /// 前者是 <c>列宽 &gt;= 任一受治理写面最坏情况</c>（任何写面变宽都会逼一次重新推导），
    /// 后者是 <c>列宽 == 最宽受治理写面最坏情况</c>（并点名那条写面）。
    /// 拆成两条是为了让「某条写面变窄或消失」只红掉可安全更新的第二条，
    /// 而不是逼一次列宽缩窄迁移——该列在增长型事实表上且带唯一索引，缩窄要全表重写 + 校验扫描。
    /// </summary>
    public const int MaxLength = 474;

    /// <summary>
    /// 落库前的长度守卫：**绝不截断**（截断会把仅末几位不同的两个键折叠成同一个，
    /// 那会让两个不同的上游事实在唯一索引上撞成一张任务），超出列宽就地抛。
    /// </summary>
    public static string EnsureWithinColumn(string triggerIdempotencyKey, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(triggerIdempotencyKey);
        return triggerIdempotencyKey.Length <= MaxLength
            ? triggerIdempotencyKey
            : throw new ArgumentOutOfRangeException(
                parameterName,
                triggerIdempotencyKey.Length,
                $"Inspection task trigger idempotency key exceeds the persisted column width {MaxLength}.");
    }
}
