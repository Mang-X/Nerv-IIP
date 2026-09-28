namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 的**生产准备底座**形状：SKU 可用性投影。
///
/// <c>mes_sku_availabilities</c> 不上页面表格，但它是 <see cref="MasterData.MesSkuAvailabilityGate"/> 的**黑名单**。
/// 表为空时「已停用 SKU 不得建工单」这条业务规则在演示里根本无从展示。
///
/// 设备 ↔ 工作中心归属不在这里：它由 MasterData 设备台账拥有，MES 运行时直接查询（#3878）。
///
/// 确定性纯函数、无随机源：这张表是主数据投影，不是历史事实，形状必须逐次可复现。
/// </summary>
public static class WorldHistoryFoundationSpec
{
    #region SKU 可用性投影（停用黑名单）

    /// <summary>
    /// 演示环境中被停用的 SKU。
    ///
    /// **这张表是黑名单**：<c>MesSkuAvailabilityGate</c> 命中即抛 <c>DisabledMesSkuException</c>，
    /// 挡住该 SKU 的建单能力（含急件工单与「计划建议转工单」集成事件处理器）。
    /// 因此本清单只能挑**永远不会作为工单 SKU 出现**的物料：
    ///
    /// 1. 不是设定集 §4 的 24 个成品（<c>WorldHistorySpec.FinishedGoodSkus</c>）——工单 SKU 一律是成品；
    /// 2. 不在任何成品的用料表里（<c>WorldHistoryMesSpec.Components</c> 只用
    ///    SF-ROD/SF-TUB/SF-VLV 与 RM-SPR-01..04），停用不影响历史消耗与齐套；
    /// 3. 不是工程版本演进故事里的二供弹簧 <c>RM-SPR-05/06</c>（那两个 V2 生产版本正在用）。
    ///
    /// 结论：油封 φ25 双唇式与叉臂式连接环——两个真实存在于 L0 主数据、
    /// 但从未进入演示主链的原材料。停用理由取自设定集的供应商 / 工程口径。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryDisabledSku> DisabledSkus =
    [
        new("RM-SEL-04", "供应商质量整改期间暂停采购与投产（二供未定点）"),
        new("RM-ACC-03", "工程淘汰件：叉臂式连接环已被上/下安装环方案替代"),
    ];

    /// <summary>停用事实的来源事件号——历史回填不经 MasterData 集成事件，故用固定的可追溯前缀。</summary>
    public static string DisabledSourceEventId(string skuCode) => $"WORLD-HISTORY-SKU-DISABLED-{skuCode}";

    /// <summary>
    /// 停用时点：上线日之后的一个确定性历史时刻（按清单序号错开），
    /// 让「停用发生在系统上线之后」这条时间线成立。
    /// </summary>
    public static DateTimeOffset DisabledAtUtc(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var day = WorldHistoryCalendar.SnapToWorkingDay(WorldHistoryCalendar.GoLiveDate.AddDays(60 + (index * 23)));
        return new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 30)), TimeSpan.Zero);
    }

    #endregion
}

/// <summary>一条 SKU 停用事实。</summary>
public sealed record WorldHistoryDisabledSku(string SkuCode, string DisabledReason);
