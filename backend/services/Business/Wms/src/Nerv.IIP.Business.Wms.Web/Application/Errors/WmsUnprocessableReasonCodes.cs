namespace Nerv.IIP.Business.Wms.Web.Application.Errors;

/// <summary>
/// WMS 422（领域拒绝）的**稳定机读原因代码**。
///
/// 背景（#1397 / 第三轮走查台账 #81）：出库复核必 422，但响应体恒为
/// <c>{"message":"unprocessable","errorData":[]}</c> —— 拒绝理由只进了服务端日志，
/// 用户既不知道卡在哪，也不知道该做什么。
///
/// 为什么用代码而不是直接回中文：
/// <list type="number">
/// <item>BusinessGateway 的 <c>IsStrictSafeDownstreamMessage</c> 只放行
/// <c>[A-Za-z0-9-_.]</c> 的下游消息，这是防止下游自由文本经网关泄漏的护栏，不该为显示文案拆掉；</item>
/// <item><c>errorData</c> 是 FluentValidation 的字段袋，各服务的 error writer 一律写死 <c>[]</c>；
/// 422 是领域拒绝、不是字段校验，本来就没有字段级条目可填，<c>message</c> 是唯一载体。</item>
/// </list>
/// 因此契约是：**本服务承诺稳定代码，前端按代码映射中文人话**
/// （与既有的 <c>downstream-timeout</c> 同款做法，见 business-console 的 <c>notify.ts</c>）。
///
/// 新增代码时必须同步 <c>frontend/apps/business-console/src/utils/wmsReasonCodes.ts</c>：
/// 那边没登记的代码会落回分层兜底文案（不会崩，但用户又看不到原因了）。
/// </summary>
public static class WmsUnprocessableReasonCodes
{
    /// <summary>复核结论为「不通过」，却仍要求完成出库。</summary>
    public const string OutboundPackReviewNotPassed = "outbound-pack-review-not-passed";

    /// <summary>出库单一张拣货任务都没有。</summary>
    public const string OutboundPickingTaskMissing = "outbound-picking-task-missing";

    /// <summary>拣货任务还没到终态（待拣 / 拣货中），复核的前置事实不成立。</summary>
    public const string OutboundPickingNotCompleted = "outbound-picking-not-completed";

    /// <summary>存在差异完成的拣货任务，但没有落库的差异原因。</summary>
    public const string OutboundPickingDifferenceReasonMissing =
        "outbound-picking-difference-reason-missing";

    /// <summary>出库单有明细行没有对应的终态拣货任务。</summary>
    public const string OutboundLinePickingTaskMissing = "outbound-line-picking-task-missing";

    /// <summary>拣货数量与计划量不一致，必须填写差异原因。</summary>
    public const string PickingDifferenceReasonRequired = "picking-difference-reason-required";

    /// <summary>实拣数量超过计划量的 110% 硬上限。</summary>
    public const string PickingOverLimit = "picking-over-limit";

    /// <summary>执行数量超出计划量或为负。</summary>
    public const string ExecutedQuantityOutOfRange = "executed-quantity-out-of-range";

    /// <summary>
    /// 把仓库任务聚合抛出的 <see cref="ArgumentException"/> 归类成稳定代码。
    ///
    /// 按 <see cref="ArgumentException.ParamName"/> 判定而**不是**匹配消息文本：
    /// 参数名是聚合的结构性契约，改文案不会让分类失效；匹配英文消息则一改就悄悄退化成兜底
    /// （仓库里已有「测试把错误固化成契约」的前车之鉴）。
    /// </summary>
    public static string FromWarehouseTaskArgument(ArgumentException exception) =>
        exception.ParamName switch
        {
            "completionReason" => PickingDifferenceReasonRequired,
            "pickingOverLimit" => PickingOverLimit,
            "executedQuantity" => ExecutedQuantityOutOfRange,
            _ => WmsUnprocessableException.SafeCode,
        };
}
