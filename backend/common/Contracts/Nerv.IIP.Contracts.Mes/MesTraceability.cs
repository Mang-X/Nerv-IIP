namespace Nerv.IIP.Contracts.Mes;

/// <summary>
/// MES 追溯图里需要跨服务识别的节点类型。
///
/// 追溯图本身由 MES 发出，节点类型大多只有 MES 自己解释；但检验结论节点带出缺陷码与处置结论，
/// BusinessGateway 的追溯门面要按 <c>business.mes.quality.read</c> 把它从响应里裁掉（#1948 / PR #2677）。
/// 门面的匹配依据就是这个值，故它是跨服务承重的公开契约，两侧都必须引用本常量：
/// 一旦两边各写一份字面量，MES 改名后门面会静默失去匹配、权限泄漏原样复发（#2686）。
/// </summary>
public static class MesTraceabilityNodeTypes
{
    /// <summary>检验结论节点（缺陷记录及其处置状态），受 <c>business.mes.quality.read</c> 分层。</summary>
    public const string InspectionResult = "InspectionResult";
}
