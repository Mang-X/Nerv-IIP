namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>人工走查使用的工厂结构、物料和往来单位。</summary>
public static class WalkthroughSeedSpec
{
    public const string FinishedSkuCode = "FG-QJ-P1-L";
    public const string SiteCode = "SITE-001";
    public const string SiteName = "一号工厂";
    public const string SiteTimezone = "Asia/Shanghai";
    public const string CalendarCode = "STANDARD";

    /// <summary>走查工作制：早班 08:00–16:00、中班 16:00–24:00（各 480 分钟）。</summary>
    public static readonly WalkthroughShift[] Shifts =
    [
        new("EARLY", "早班", new TimeOnly(8, 0), new TimeOnly(16, 0), 480),
        new("MIDDLE", "中班", new TimeOnly(16, 0), new TimeOnly(0, 0), 480),
    ];

    /// <summary>走查使用的部门。前 5 个编码与常规种子一致（存在即保留，不改名）。</summary>
    public static readonly WalkthroughDepartment[] Departments =
    [
        new("DEPT-PROD", "生产部"),
        new("DEPT-PLAN", "计划部"),
        new("DEPT-QA", "质量部"),
        new("DEPT-EQ", "设备部"),
        new("DEPT-WH", "仓储部"),
        new("DEPT-BIZ", "经营部"),
    ];

    public const string MachiningWorkshopCode = "WS-01";
    public const string AssemblyWorkshopCode = "WS-02";
    public const string SurfaceWorkshopCode = "WS-03";

    public static readonly WalkthroughWorkshop[] Workshops =
    [
        new(MachiningWorkshopCode, "一车间 · 机加车间", "棒料→活塞杆；管料→缸筒"),
        new(AssemblyWorkshopCode, "二车间 · 装配车间", "部件→总成"),
        new(SurfaceWorkshopCode, "三车间 · 表面与包装车间", "涂装/终检/包装入库"),
    ];

    /// <summary>机加 5 条、装配 6 条、表面与包装 3 条。</summary>
    public static readonly WalkthroughProductionLine[] ProductionLines =
    [
        new("LINE-WB-ROD-01", "活塞杆一线", MachiningWorkshopCode),
        new("LINE-WB-ROD-02", "活塞杆二线", MachiningWorkshopCode),
        new("LINE-WB-TUB-01", "缸筒一线", MachiningWorkshopCode),
        new("LINE-WB-TUB-02", "缸筒二线", MachiningWorkshopCode),
        new("LINE-WB-GRD-01", "精磨线", MachiningWorkshopCode),
        new("LINE-WB-FA-01", "前减装配一线", AssemblyWorkshopCode),
        new("LINE-WB-FA-02", "前减装配二线", AssemblyWorkshopCode),
        new("LINE-WB-FA-03", "前减装配三线", AssemblyWorkshopCode),
        new("LINE-WB-RA-01", "后减装配一线", AssemblyWorkshopCode),
        new("LINE-WB-RA-02", "后减装配二线", AssemblyWorkshopCode),
        new("LINE-WB-VA-01", "阀系预装线", AssemblyWorkshopCode),
        new("LINE-WB-CT-01", "电泳涂装线", SurfaceWorkshopCode),
        new("LINE-WB-TS-01", "性能检测线", SurfaceWorkshopCode),
        new("LINE-WB-PK-01", "包装线", SurfaceWorkshopCode),
    ];

    /// <summary>
    /// 每条产线一个工作中心（14 个），另加 3 个车间级辅助工作中心承载空压机/冷干机等公用设备。
    /// 平台数据模型没有独立的「公用工程」层，辅助工作中心的产线归属取该车间末道线。
    /// </summary>
    public static readonly WalkthroughWorkCenter[] WorkCenters =
    [
        new("WC-ROD-01", "活塞杆加工中心一线", "LINE-WB-ROD-01", MachiningWorkshopCode, 960, 3),
        new("WC-ROD-02", "活塞杆加工中心二线", "LINE-WB-ROD-02", MachiningWorkshopCode, 960, 3),
        new("WC-TUB-01", "缸筒加工中心一线", "LINE-WB-TUB-01", MachiningWorkshopCode, 960, 4),
        new("WC-TUB-02", "缸筒加工中心二线", "LINE-WB-TUB-02", MachiningWorkshopCode, 960, 4),
        new("WC-GRD-01", "精磨中心", "LINE-WB-GRD-01", MachiningWorkshopCode, 960, 4),
        new("WC-FA-01", "前减装配中心一线", "LINE-WB-FA-01", AssemblyWorkshopCode, 960, 2),
        new("WC-FA-02", "前减装配中心二线", "LINE-WB-FA-02", AssemblyWorkshopCode, 960, 2),
        new("WC-FA-03", "前减装配中心三线", "LINE-WB-FA-03", AssemblyWorkshopCode, 960, 2),
        new("WC-RA-01", "后减装配中心一线", "LINE-WB-RA-01", AssemblyWorkshopCode, 960, 2),
        new("WC-RA-02", "后减装配中心二线", "LINE-WB-RA-02", AssemblyWorkshopCode, 960, 2),
        new("WC-VA-01", "阀系预装中心", "LINE-WB-VA-01", AssemblyWorkshopCode, 960, 2),
        new("WC-CT-01", "电泳涂装中心", "LINE-WB-CT-01", SurfaceWorkshopCode, 960, 3),
        new("WC-TS-01", "性能检测中心", "LINE-WB-TS-01", SurfaceWorkshopCode, 960, 4),
        new("WC-PK-01", "包装中心", "LINE-WB-PK-01", SurfaceWorkshopCode, 960, 2),
        new("WC-AUX-MC", "机加车间辅助动力", "LINE-WB-GRD-01", MachiningWorkshopCode, 1440, 3),
        new("WC-AUX-AS", "装配车间辅助动力", "LINE-WB-VA-01", AssemblyWorkshopCode, 1440, 3),
        new("WC-AUX-SP", "表面与包装车间辅助动力", "LINE-WB-PK-01", SurfaceWorkshopCode, 1440, 2),
    ];

    public static readonly (string Code, string Name, string WorkCenterCode)[] Devices =
    [
        ("DEV-CNC-01", "五轴加工中心 1#", "WC-ROD-01"),
        ("DEV-AUX-01", "车间螺杆空压机 1#", "WC-AUX-MC"),
        ("DEV-CTG-01", "电泳涂装槽 1#", "WC-CT-01"),
        ("DEV-ASM-01", "前减压装机 1#", "WC-FA-01"),
        ("DEV-TST-01", "减振器阻尼试验台 1#", "WC-TS-01"),
    ];

    public static IReadOnlyList<WalkthroughSku> Skus { get; } =
    [
        new(FinishedSkuCode, "P1 平台前滑柱总成（左）", "pcs", "finished-goods"),
        new("SF-ROD-01", "活塞杆 φ20×380", "pcs", "semi-finished"),
        new("SF-TUB-01", "缸筒 φ45×260", "pcs", "semi-finished"),
        new("SF-VLV-01", "阀系组件 舒适型", "pcs", "semi-finished"),
        new("RM-SPR-01", "悬架弹簧 轿车前（首选供应商）", "pcs", "raw-material"),
        new("RM-SPR-05", "悬架弹簧 前（二供）", "pcs", "raw-material"),
        new("RM-SEL-01", "油封 φ20 骨架式", "pcs", "raw-material"),
        new("RM-SEL-02", "油封 φ22 骨架式", "pcs", "raw-material"),
        new("RM-OIL-01", "减振器专用油 10#", "l", "raw-material"),
        new("RM-OIL-02", "减振器专用油 15#", "l", "raw-material"),
        new("RM-ACC-01", "连接环 上安装环", "pcs", "raw-material"),
        new("RM-ACC-02", "连接环 下安装环", "pcs", "raw-material"),
        new("RM-ACC-04", "防尘罩 短款", "pcs", "raw-material"),
        new("RM-ACC-07", "紧固件 M10 法兰螺母", "pcs", "raw-material"),
        new("RM-BAR-01", "45# 钢棒料 φ22", "kg", "raw-material"),
        new("RM-TUB-01", "精密钢管 φ45×2.0", "kg", "raw-material"),
        new("PK-BOX-01", "纸箱 小号（4 件装）", "pcs", "packaging"),
        new("PK-BOX-02", "纸箱 中号（6 件装）", "pcs", "packaging"),
        new("PK-LBL-01", "标签纸 物料标签", "pcs", "packaging"),
        new("PK-LBL-03", "标签纸 成品箱贴", "pcs", "packaging"),
    ];

    public static IReadOnlyList<WalkthroughPartner> Customers { get; } =
    [
        new("CUST-WB-001", "长三角整车一厂", "customer", 30_000_000m, "CNY"),
        new("CUST-WB-002", "长三角整车二厂", "customer", 22_000_000m, "CNY"),
        new("CUST-WB-003", "华中商用车配套", "customer", 12_000_000m, "CNY"),
        new("CUST-WB-004", "比德新能源", "customer", 15_000_000m, "CNY"),
        new("CUST-WB-005", "路航售后连锁", "customer", 1_500_000m, "CNY"),
    ];

    public static IReadOnlyList<WalkthroughPartner> Suppliers { get; } =
    [
        new("SUP-WB-BAR-01", "江阴特钢制品有限公司", "supplier"),
        new("SUP-WB-TUB-01", "无锡精密钢管股份有限公司", "supplier"),
        new("SUP-WB-SPR-02", "常州恒力弹簧有限公司", "supplier"),
        new("SUP-WB-SEL-01", "宁波密封件制造有限公司", "supplier"),
        new("SUP-WB-OIL-01", "上海高华润滑油有限公司", "supplier"),
    ];

    public static IReadOnlyList<string> SkuCodes { get; } = [.. Skus.Select(sku => sku.Code)];
    public static IReadOnlyList<string> CustomerCodes { get; } = [.. Customers.Select(customer => customer.Code)];
    public static IReadOnlyList<string> SupplierCodes { get; } = [.. Suppliers.Select(supplier => supplier.Code)];
}

public sealed record WalkthroughShift(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);

public sealed record WalkthroughDepartment(string Code, string Name);

public sealed record WalkthroughWorkshop(string Code, string Name, string Description);

public sealed record WalkthroughProductionLine(string Code, string Name, string WorkshopCode);

public sealed record WalkthroughWorkCenter(
    string Code,
    string Name,
    string LineCode,
    string WorkshopCode,
    int CapacityMinutesPerDay,
    int NumberOfCapacities);

public sealed record WalkthroughSku(string Code, string Name, string Unit, string Category);

public sealed record WalkthroughPartner(
    string Code,
    string Name,
    string PartnerType,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null);
