namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 的**生产准备底座**形状：设备 ↔ 工作中心映射、SKU 可用性投影。
///
/// 这两张表不上页面表格，但它们是「生产准备检查」页背后的两块地基：
/// - <c>device_asset_work_center_mappings</c>：遥测（<c>TelemetryProductionCountDelta</c>）落到
///   哪个工作中心、进而自动报到哪道工序，全靠它。表为空时整条遥测自动报工链静默失效。
/// - <c>mes_sku_availabilities</c>：<see cref="MasterData.MesSkuAvailabilityGate"/> 的**黑名单**。
///   表为空时「已停用 SKU 不得建工单」这条业务规则在演示里根本无从展示。
///
/// 确定性纯函数、无随机源：这两张表是主数据投影，不是历史事实，形状必须逐次可复现。
/// </summary>
public static class WorldHistoryFoundationSpec
{
    #region 设备 ↔ 工作中心映射（设定集 §2 车间归属 × §3 设备台账）

    /// <summary>
    /// 38 条映射：46 台设备中的**生产设备**逐台绑到其所属车间的工作中心。
    ///
    /// 归属严格按设定集 §2/§3：
    /// - 机加车间：CNC 车床/加工中心 → 活塞杆线 / 缸筒线；数控磨床 → 精磨线。
    /// - 装配车间：装配工作站 → 前减 3 线 / 后减 2 线 / 阀系预装线（每工作中心 2 台）；
    ///   焊接机器人挂总成焊接所在的装配工作中心。
    /// - 表面与包装车间：电泳设备 → 电泳涂装线；性能试验台 → 性能检测线；包装线体 → 包装线。
    ///
    /// <c>DEV-AUX-01..08</c>（空压机 / 冷干机等辅助设备）**刻意不映射**：它们服务全厂而非某条产线，
    /// 一旦挂到某个工作中心，其压力 / 温度遥测就会被当成那条线的产量计数来源，
    /// 自动报工会把辅助设备的读数记成产出。宁可留空，也不给错的归属。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryDeviceAssetMapping> DeviceAssetMappings =
    [
        // 机加车间 · 活塞杆线 1/2（棒料 → 活塞杆）
        new("DEV-CNC-01", "WC-ROD-01"),
        new("DEV-CNC-02", "WC-ROD-01"),
        new("DEV-CNC-03", "WC-ROD-02"),
        new("DEV-CNC-04", "WC-ROD-02"),
        new("DEV-CNC-05", "WC-ROD-01"),
        // 机加车间 · 缸筒线 1/2（管料 → 缸筒）
        new("DEV-CNC-06", "WC-TUB-01"),
        new("DEV-CNC-07", "WC-TUB-01"),
        new("DEV-CNC-08", "WC-TUB-02"),
        new("DEV-CNC-09", "WC-TUB-02"),
        new("DEV-CNC-10", "WC-TUB-01"),
        // 机加车间 · 精磨线
        new("DEV-GRD-01", "WC-GRD-01"),
        new("DEV-GRD-02", "WC-GRD-01"),
        new("DEV-GRD-03", "WC-GRD-01"),
        new("DEV-GRD-04", "WC-GRD-01"),
        // 装配车间 · 前减装配 1/2/3
        new("DEV-ASM-01", "WC-FA-01"),
        new("DEV-ASM-02", "WC-FA-01"),
        new("DEV-ASM-03", "WC-FA-02"),
        new("DEV-ASM-04", "WC-FA-02"),
        new("DEV-ASM-05", "WC-FA-03"),
        new("DEV-ASM-06", "WC-FA-03"),
        // 装配车间 · 后减装配 1/2
        new("DEV-ASM-07", "WC-RA-01"),
        new("DEV-ASM-08", "WC-RA-01"),
        new("DEV-ASM-09", "WC-RA-02"),
        new("DEV-ASM-10", "WC-RA-02"),
        // 装配车间 · 阀系预装线
        new("DEV-ASM-11", "WC-VA-01"),
        new("DEV-ASM-12", "WC-VA-01"),
        // 装配车间 · 焊接机器人（总成焊接工位，随其所在装配线）
        new("DEV-WLD-01", "WC-FA-01"),
        new("DEV-WLD-02", "WC-FA-02"),
        new("DEV-WLD-03", "WC-RA-01"),
        // 表面与包装车间 · 电泳涂装线
        new("DEV-CTG-01", "WC-CT-01"),
        new("DEV-CTG-02", "WC-CT-01"),
        new("DEV-CTG-03", "WC-CT-01"),
        // 表面与包装车间 · 性能检测线
        new("DEV-TST-01", "WC-TS-01"),
        new("DEV-TST-02", "WC-TS-01"),
        new("DEV-TST-03", "WC-TS-01"),
        new("DEV-TST-04", "WC-TS-01"),
        // 表面与包装车间 · 包装线
        new("DEV-PKG-01", "WC-PK-01"),
        new("DEV-PKG-02", "WC-PK-01"),
    ];

    /// <summary>刻意不映射的辅助设备段（服务全厂，无产线归属）。</summary>
    public const string UnmappedAuxiliaryDevicePrefix = "DEV-AUX-";

    #endregion

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

/// <summary>一条设备 ↔ 工作中心映射。</summary>
public sealed record WorldHistoryDeviceAssetMapping(string DeviceAssetId, string WorkCenterId);

/// <summary>一条 SKU 停用事实。</summary>
public sealed record WorldHistoryDisabledSku(string SkuCode, string DisabledReason);
