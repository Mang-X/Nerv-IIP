namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》（`docs/superpowers/plans/2026-07-26-factory-world-bible.md`）L0 主数据的
/// MasterData 侧固定形状：3 车间 / 14 产线 / 17 工作中心 / 46 台设备 / 84 SKU / 6 部门 / 6 班组 /
/// 10 项技能 / 58 名员工 / 8 客户 / 10 供应商（宁沪减振科技有限公司）。
///
/// 号段（`WS-` `LINE-WB-` `WC-` `DEV-` `FG-/SF-/RM-/PK-` `CUST-` `SUP-` `EMP-`）与 MAN-519 固定演示
/// 事实（`*-DEMO-*`）、千单规模块（`*-SCALE-*`）完全隔离；本块只创建结构性主数据，不创建任何
/// 结果事实。SKU / 工作中心 / 工序编码在 ProductEngineering 与 IndustrialTelemetry 侧按同一字面量
/// 重复声明，每侧各有黄金向量测试防止漂移。
/// </summary>
public static class WorldBibleSpec
{
    public const string SiteCode = "SITE-001";
    public const string SiteName = "一号工厂";
    public const string SiteTimezone = "Asia/Shanghai";
    public const string CalendarCode = "STANDARD";

    /// <summary>平台上线日（设定集 §1）：所有 L0 时间锚点都用它。</summary>
    public static readonly DateOnly GoLiveDate = new(2026, 1, 5);

    /// <summary>技能/班组成员有效期上限，避免出现「永不失效」的假事实。</summary>
    public static readonly DateOnly AssignmentValidTo = new(2029, 12, 31);

    #region §1 组织 / 班次 / 部门

    /// <summary>设定集 §1 工作制：早班 08:00–16:00、中班 16:00–24:00（各 480 分钟）。</summary>
    public static readonly WorldBibleShift[] Shifts =
    [
        new("EARLY", "早班", new TimeOnly(8, 0), new TimeOnly(16, 0), 480),
        new("MIDDLE", "中班", new TimeOnly(16, 0), new TimeOnly(0, 0), 480),
    ];

    /// <summary>设定集 §1 的 6 个部门。前 5 个编码与常规种子一致（存在即保留，不改名）。</summary>
    public static readonly WorldBibleDepartment[] Departments =
    [
        new("DEPT-PROD", "生产部"),
        new("DEPT-PLAN", "计划部"),
        new("DEPT-QA", "质量部"),
        new("DEPT-EQ", "设备部"),
        new("DEPT-WH", "仓储部"),
        new("DEPT-BIZ", "经营部"),
    ];

    #endregion

    #region §2 车间 / 产线 / 工作中心

    public const string MachiningWorkshopCode = "WS-01";
    public const string AssemblyWorkshopCode = "WS-02";
    public const string SurfaceWorkshopCode = "WS-03";

    public static readonly WorldBibleWorkshop[] Workshops =
    [
        new(MachiningWorkshopCode, "一车间 · 机加车间", "棒料→活塞杆；管料→缸筒"),
        new(AssemblyWorkshopCode, "二车间 · 装配车间", "部件→总成"),
        new(SurfaceWorkshopCode, "三车间 · 表面与包装车间", "涂装/终检/包装入库"),
    ];

    /// <summary>
    /// 设定集 §2 表格逐行展开：机加 5 条 + 装配 6 条 + 表面与包装 3 条 = 14 条。
    /// （设定集表头写「13 产线」，与逐行合计不一致，本实现以逐行表格为准。）
    /// </summary>
    public static readonly WorldBibleProductionLine[] ProductionLines =
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
    public static readonly WorldBibleWorkCenter[] WorkCenters =
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

    #endregion

    #region §3 设备台账（46 台）

    /// <summary>设定集 §3 的 8 个类别合计 46 台，编码段与型号逐条对应。</summary>
    public static readonly IReadOnlyList<WorldBibleDevice> Devices = BuildDevices();

    private static IReadOnlyList<WorldBibleDevice> BuildDevices()
    {
        WorldBibleDevice Cnc(int index, string workCenterCode, string model) =>
            new($"DEV-CNC-{index:D2}", model, "cnc", workCenterCode, "high", "沈阳机床");

        return
        [
            Cnc(1, "WC-ROD-01", "数控车床 CK6150"),
            Cnc(2, "WC-ROD-01", "数控车床 CK6150"),
            Cnc(3, "WC-ROD-01", "数控车床 CK6150"),
            Cnc(4, "WC-ROD-02", "数控车床 CK6150"),
            Cnc(5, "WC-ROD-02", "数控车床 CK6150"),
            Cnc(6, "WC-ROD-02", "数控车床 CK6150"),
            Cnc(7, "WC-TUB-01", "立式加工中心 VMC-850"),
            Cnc(8, "WC-TUB-01", "立式加工中心 VMC-850"),
            Cnc(9, "WC-TUB-02", "立式加工中心 VMC-850"),
            Cnc(10, "WC-TUB-02", "立式加工中心 VMC-850"),

            new("DEV-GRD-01", "数控外圆磨床 MK1332", "grinder", "WC-GRD-01", "high", "上海机床厂"),
            new("DEV-GRD-02", "数控外圆磨床 MK1332", "grinder", "WC-GRD-01", "high", "上海机床厂"),
            new("DEV-GRD-03", "数控外圆磨床 MK1332", "grinder", "WC-GRD-01", "high", "上海机床厂"),
            new("DEV-GRD-04", "数控外圆磨床 MK1332", "grinder", "WC-GRD-01", "high", "上海机床厂"),

            new("DEV-ASM-01", "减振器装配台（气动压装）", "assembly-station", "WC-FA-01", "medium", "宁沪自制"),
            new("DEV-ASM-02", "减振器装配台（气动压装）", "assembly-station", "WC-FA-01", "medium", "宁沪自制"),
            new("DEV-ASM-03", "减振器装配台（气动压装）", "assembly-station", "WC-FA-02", "medium", "宁沪自制"),
            new("DEV-ASM-04", "减振器装配台（气动压装）", "assembly-station", "WC-FA-02", "medium", "宁沪自制"),
            new("DEV-ASM-05", "减振器装配台（气动压装）", "assembly-station", "WC-FA-03", "medium", "宁沪自制"),
            new("DEV-ASM-06", "减振器装配台（气动压装）", "assembly-station", "WC-FA-03", "medium", "宁沪自制"),
            new("DEV-ASM-07", "减振器装配台（气动压装）", "assembly-station", "WC-RA-01", "medium", "宁沪自制"),
            new("DEV-ASM-08", "减振器装配台（气动压装）", "assembly-station", "WC-RA-01", "medium", "宁沪自制"),
            new("DEV-ASM-09", "减振器装配台（气动压装）", "assembly-station", "WC-RA-02", "medium", "宁沪自制"),
            new("DEV-ASM-10", "减振器装配台（气动压装）", "assembly-station", "WC-RA-02", "medium", "宁沪自制"),
            new("DEV-ASM-11", "阀系预装台（伺服压装）", "assembly-station", "WC-VA-01", "medium", "宁沪自制"),
            new("DEV-ASM-12", "阀系预装台（伺服压装）", "assembly-station", "WC-VA-01", "medium", "宁沪自制"),

            new("DEV-WLD-01", "六轴焊接机器人", "welding-robot", "WC-TUB-01", "high", "埃斯顿"),
            new("DEV-WLD-02", "六轴焊接机器人", "welding-robot", "WC-TUB-01", "high", "埃斯顿"),
            new("DEV-WLD-03", "六轴焊接机器人", "welding-robot", "WC-TUB-02", "high", "埃斯顿"),

            new("DEV-CTG-01", "电泳前处理槽", "coating", "WC-CT-01", "high", "华远涂装"),
            new("DEV-CTG-02", "电泳槽", "coating", "WC-CT-01", "high", "华远涂装"),
            new("DEV-CTG-03", "固化炉", "coating", "WC-CT-01", "high", "华远涂装"),

            new("DEV-TST-01", "电液伺服试验台", "test-bench", "WC-TS-01", "high", "长春试验机"),
            new("DEV-TST-02", "电液伺服试验台", "test-bench", "WC-TS-01", "high", "长春试验机"),
            new("DEV-TST-03", "电液伺服试验台", "test-bench", "WC-TS-01", "high", "长春试验机"),
            new("DEV-TST-04", "电液伺服试验台", "test-bench", "WC-TS-01", "high", "长春试验机"),

            new("DEV-PKG-01", "自动装箱线", "packaging-line", "WC-PK-01", "medium", "苏州包装机械"),
            new("DEV-PKG-02", "自动装箱线", "packaging-line", "WC-PK-01", "medium", "苏州包装机械"),

            new("DEV-AUX-01", "螺杆空压机 SA-75", "utility", "WC-AUX-MC", "high", "阿特拉斯"),
            new("DEV-AUX-02", "螺杆空压机 SA-75", "utility", "WC-AUX-MC", "high", "阿特拉斯"),
            new("DEV-AUX-03", "冷冻式干燥机 CD-20", "utility", "WC-AUX-MC", "medium", "阿特拉斯"),
            new("DEV-AUX-04", "螺杆空压机 SA-55", "utility", "WC-AUX-AS", "high", "阿特拉斯"),
            new("DEV-AUX-05", "螺杆空压机 SA-55", "utility", "WC-AUX-AS", "high", "阿特拉斯"),
            new("DEV-AUX-06", "冷冻式干燥机 CD-15", "utility", "WC-AUX-AS", "medium", "阿特拉斯"),
            new("DEV-AUX-07", "螺杆空压机 SA-37", "utility", "WC-AUX-SP", "high", "阿特拉斯"),
            new("DEV-AUX-08", "冷冻式干燥机 CD-10", "utility", "WC-AUX-SP", "medium", "阿特拉斯"),
        ];
    }

    #endregion

    #region §4 产品与物料（84 SKU）

    /// <summary>6 车型平台（设定集 §4）。</summary>
    public static readonly WorldBiblePlatform[] Platforms =
    [
        new("P1", "A 级轿车 P1"),
        new("P2", "A 级轿车 P2"),
        new("S1", "SUV S1"),
        new("S2", "SUV S2"),
        new("M1", "MPV M1"),
        new("E1", "新能源 E1"),
    ];

    /// <summary>成品 24：6 平台 × 前滑柱/后减 × 左右件。</summary>
    public static readonly IReadOnlyList<WorldBibleSku> FinishedGoods = BuildFinishedGoods();

    /// <summary>半成品 18：活塞杆 6 + 缸筒 6 + 阀系组件 6。</summary>
    public static readonly WorldBibleSku[] SemiFinishedGoods =
    [
        new("SF-ROD-01", "活塞杆 φ20×380", "pcs", "semi-finished"),
        new("SF-ROD-02", "活塞杆 φ20×420", "pcs", "semi-finished"),
        new("SF-ROD-03", "活塞杆 φ22×420", "pcs", "semi-finished"),
        new("SF-ROD-04", "活塞杆 φ22×460", "pcs", "semi-finished"),
        new("SF-ROD-05", "活塞杆 φ25×460", "pcs", "semi-finished"),
        new("SF-ROD-06", "活塞杆 φ25×500", "pcs", "semi-finished"),
        new("SF-TUB-01", "缸筒 φ45×260", "pcs", "semi-finished"),
        new("SF-TUB-02", "缸筒 φ45×300", "pcs", "semi-finished"),
        new("SF-TUB-03", "缸筒 φ50×300", "pcs", "semi-finished"),
        new("SF-TUB-04", "缸筒 φ50×340", "pcs", "semi-finished"),
        new("SF-TUB-05", "缸筒 φ55×340", "pcs", "semi-finished"),
        new("SF-TUB-06", "缸筒 φ55×380", "pcs", "semi-finished"),
        new("SF-VLV-01", "阀系组件 舒适型", "pcs", "semi-finished"),
        new("SF-VLV-02", "阀系组件 标准型", "pcs", "semi-finished"),
        new("SF-VLV-03", "阀系组件 运动型", "pcs", "semi-finished"),
        new("SF-VLV-04", "阀系组件 加强型", "pcs", "semi-finished"),
        new("SF-VLV-05", "阀系组件 静音型", "pcs", "semi-finished"),
        new("SF-VLV-06", "阀系组件 商用加强型", "pcs", "semi-finished"),
    ];

    /// <summary>原材料 30：棒料 4 + 钢管 4 + 弹簧 6 + 油封 4 + 减振油 2 + 连接环/防尘罩/紧固件 10。</summary>
    public static readonly WorldBibleSku[] RawMaterials =
    [
        new("RM-BAR-01", "45# 钢棒料 φ22", "kg", "raw-material"),
        new("RM-BAR-02", "45# 钢棒料 φ25", "kg", "raw-material"),
        new("RM-BAR-03", "45# 钢棒料 φ28", "kg", "raw-material"),
        new("RM-BAR-04", "45# 钢棒料 φ32", "kg", "raw-material"),
        new("RM-TUB-01", "精密钢管 φ45×2.0", "kg", "raw-material"),
        new("RM-TUB-02", "精密钢管 φ50×2.0", "kg", "raw-material"),
        new("RM-TUB-03", "精密钢管 φ55×2.5", "kg", "raw-material"),
        new("RM-TUB-04", "精密钢管 φ60×2.5", "kg", "raw-material"),
        new("RM-SPR-01", "悬架弹簧 轿车前（首选供应商）", "pcs", "raw-material"),
        new("RM-SPR-02", "悬架弹簧 轿车后（首选供应商）", "pcs", "raw-material"),
        new("RM-SPR-03", "悬架弹簧 SUV 前（首选供应商）", "pcs", "raw-material"),
        new("RM-SPR-04", "悬架弹簧 SUV 后（首选供应商）", "pcs", "raw-material"),
        new("RM-SPR-05", "悬架弹簧 前（二供）", "pcs", "raw-material"),
        new("RM-SPR-06", "悬架弹簧 后（二供）", "pcs", "raw-material"),
        new("RM-SEL-01", "油封 φ20 骨架式", "pcs", "raw-material"),
        new("RM-SEL-02", "油封 φ22 骨架式", "pcs", "raw-material"),
        new("RM-SEL-03", "油封 φ25 骨架式", "pcs", "raw-material"),
        new("RM-SEL-04", "油封 φ25 双唇式", "pcs", "raw-material"),
        new("RM-OIL-01", "减振器专用油 10#", "l", "raw-material"),
        new("RM-OIL-02", "减振器专用油 15#", "l", "raw-material"),
        new("RM-ACC-01", "连接环 上安装环", "pcs", "raw-material"),
        new("RM-ACC-02", "连接环 下安装环", "pcs", "raw-material"),
        new("RM-ACC-03", "连接环 叉臂式", "pcs", "raw-material"),
        new("RM-ACC-04", "防尘罩 短款", "pcs", "raw-material"),
        new("RM-ACC-05", "防尘罩 长款", "pcs", "raw-material"),
        new("RM-ACC-06", "防尘罩 带缓冲块", "pcs", "raw-material"),
        new("RM-ACC-07", "紧固件 M10 法兰螺母", "pcs", "raw-material"),
        new("RM-ACC-08", "紧固件 M12 法兰螺母", "pcs", "raw-material"),
        new("RM-ACC-09", "紧固件 M10 高强螺栓", "pcs", "raw-material"),
        new("RM-ACC-10", "紧固件 M12 高强螺栓", "pcs", "raw-material"),
    ];

    /// <summary>包材辅料 12：纸箱 4 + 护角 2 + 标签纸 3 + 托盘 2 + 缠绕膜 1。</summary>
    public static readonly WorldBibleSku[] PackagingMaterials =
    [
        new("PK-BOX-01", "纸箱 小号（4 件装）", "pcs", "packaging"),
        new("PK-BOX-02", "纸箱 中号（6 件装）", "pcs", "packaging"),
        new("PK-BOX-03", "纸箱 大号（8 件装）", "pcs", "packaging"),
        new("PK-BOX-04", "纸箱 出口加固型", "pcs", "packaging"),
        new("PK-COR-01", "纸护角 L 型", "pcs", "packaging"),
        new("PK-COR-02", "纸护角 U 型", "pcs", "packaging"),
        new("PK-LBL-01", "标签纸 物料标签", "pcs", "packaging"),
        new("PK-LBL-02", "标签纸 批次标签", "pcs", "packaging"),
        new("PK-LBL-03", "标签纸 成品箱贴", "pcs", "packaging"),
        new("PK-PLT-01", "托盘 木质 1200×1000", "pcs", "packaging"),
        new("PK-PLT-02", "托盘 塑料 1200×1000", "pcs", "packaging"),
        new("PK-FLM-01", "缠绕膜 500mm", "kg", "packaging"),
    ];

    /// <summary>84 SKU 全量（24 + 18 + 30 + 12）。</summary>
    public static readonly WorldBibleSku[] AllSkus =
    [
        .. FinishedGoods,
        .. SemiFinishedGoods,
        .. RawMaterials,
        .. PackagingMaterials,
    ];

    private static IReadOnlyList<WorldBibleSku> BuildFinishedGoods()
    {
        var types = new[] { ("QJ", "前滑柱总成"), ("HJ", "后减振器总成") };
        var sides = new[] { ("L", "左"), ("R", "右") };
        return
        [
            .. Platforms
                .SelectMany(_ => types, (platform, type) => (platform, type))
                .SelectMany(_ => sides, (pair, side) => new WorldBibleSku(
                    $"FG-{pair.type.Item1}-{pair.platform.Code}-{side.Item1}",
                    $"{pair.platform.Code} 平台{pair.type.Item2}（{side.Item2}）",
                    "pcs",
                    "finished-goods")),
        ];
    }

    #endregion

    #region §6 客户与供应商

    /// <summary>
    /// 设定集 §6 的 8 家客户。`CUST-DEMO-001`（华东汽车零部件采购中心）是 MAN-519 固定演示事实，
    /// 由 <see cref="LeaderDemoSeedService"/> 拥有，本块只登记其余 7 家。
    /// </summary>
    public const string ExistingCustomerCode = "CUST-DEMO-001";

    public static readonly WorldBiblePartner[] Customers =
    [
        new("CUST-WB-001", "长三角整车一厂", "customer"),
        new("CUST-WB-002", "长三角整车二厂", "customer"),
        new("CUST-WB-003", "华中商用车配套", "customer"),
        new("CUST-WB-004", "比德新能源", "customer"),
        new("CUST-WB-005", "路航售后连锁", "customer"),
        new("CUST-WB-006", "皖江 Tier1 汽车系统", "customer"),
        new("CUST-WB-007", "华远国际贸易", "customer"),
    ];

    /// <summary>设定集 §6 的 10 家供应商（品类配比按设定集，企业名为本实现自拟）。</summary>
    public static readonly WorldBiblePartner[] Suppliers =
    [
        new("SUP-WB-BAR-01", "江阴特钢制品有限公司", "supplier"),
        new("SUP-WB-BAR-02", "沙钢精特材料有限公司", "supplier"),
        new("SUP-WB-TUB-01", "无锡精密钢管股份有限公司", "supplier"),
        new("SUP-WB-SPR-01", "杭州弹簧工业有限公司", "supplier"),
        new("SUP-WB-SPR-02", "常州恒力弹簧有限公司", "supplier"),
        new("SUP-WB-SEL-01", "宁波密封件制造有限公司", "supplier"),
        new("SUP-WB-SEL-02", "苏州橡塑密封科技有限公司", "supplier"),
        new("SUP-WB-OIL-01", "上海高华润滑油有限公司", "supplier"),
        new("SUP-WB-PKG-01", "昆山鑫达包装材料有限公司", "supplier"),
        new("SUP-WB-PKG-02", "太仓绿印包装制品有限公司", "supplier"),
    ];

    #endregion

    #region §5 技能目录 / 班组 / 员工

    /// <summary>
    /// 设定集 §5 的 10 项技能。其中 6 项编码与常规种子重合（存在即保留，显示名沿用既有值，
    /// 避免改写租户已维护的技能目录）。
    /// </summary>
    public static readonly WorldBibleSkill[] Skills =
    [
        new("cnc-operation", "CNC 操作", "设备操作", true, 24, "数控加工中心上下料、程序调用与首件确认"),
        new("grinding", "磨床操作", "设备操作", true, 24, "数控外圆磨床装夹、砂轮修整与尺寸控制"),
        new("assembly", "减振器装配", "装配作业", false, null, "减振器总成装配线标准作业与扭矩控制"),
        new("welding", "焊接", "特种作业", true, 36, "储油缸筒焊接，需持特种作业操作证"),
        new("coating", "电泳工艺", "表面处理", true, 24, "电泳槽液管理、膜厚控制与固化炉操作"),
        new("performance-test", "性能试验", "质量管理", true, 12, "电液伺服试验台阻尼力曲线采集与判定"),
        new("inspection", "质量检验", "质量管理", true, 12, "首件、巡检与成品检验，含量具使用"),
        new("equipment-maintenance", "设备维护", "设备管理", false, null, "设备点检保养与一般故障处理"),
        new("forklift", "叉车驾驶", "物流仓储", true, 48, "厂内叉车驾驶与物料转运，需持证上岗"),
        new("production-planning", "计划排产", "生产计划", false, null, "主生产计划编制、工单下达与齐套跟踪"),
    ];

    /// <summary>三车间各 2 个班组（早班 / 中班），合计 6 个，与 §5 的 6 名班组长对应。</summary>
    public static readonly WorldBibleTeam[] Teams =
    [
        new("TEAM-WB-MC-A", "机加车间早班组", MachiningWorkshopCode, "EARLY"),
        new("TEAM-WB-MC-B", "机加车间中班组", MachiningWorkshopCode, "MIDDLE"),
        new("TEAM-WB-AS-A", "装配车间早班组", AssemblyWorkshopCode, "EARLY"),
        new("TEAM-WB-AS-B", "装配车间中班组", AssemblyWorkshopCode, "MIDDLE"),
        new("TEAM-WB-SP-A", "表面与包装车间早班组", SurfaceWorkshopCode, "EARLY"),
        new("TEAM-WB-SP-B", "表面与包装车间中班组", SurfaceWorkshopCode, "MIDDLE"),
    ];

    /// <summary>按车间划分的操作工可选技能池（每人 1–3 项，确定性选取）。</summary>
    private static readonly Dictionary<string, string[]> OperatorSkillPools = new(StringComparer.Ordinal)
    {
        [MachiningWorkshopCode] = ["cnc-operation", "grinding", "equipment-maintenance"],
        [AssemblyWorkshopCode] = ["assembly", "welding", "inspection"],
        [SurfaceWorkshopCode] = ["coating", "performance-test", "forklift"],
    };

    private static readonly string[] Surnames =
    [
        "王", "李", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴",
        "徐", "孙", "胡", "朱", "高", "林", "何", "郭", "马", "罗",
    ];

    private static readonly string[] GivenNames =
    [
        "建国", "秀英", "志强", "桂芳", "海涛", "丽娟", "文斌", "春梅", "国庆", "晓东",
        "淑芬", "永强", "秀兰", "俊杰", "玉兰", "小磊", "凤霞", "明辉", "雅琴", "浩然",
        "美玲", "立新", "婷婷", "德华", "红梅", "天宇", "金花", "伟东", "雪梅", "宏伟",
    ];

    /// <summary>设定集 §5 的 58 名在册员工（部门 → 岗位 → 人数）。</summary>
    public static readonly IReadOnlyList<WorldBibleEmployee> Employees = BuildEmployees();

    private static IReadOnlyList<WorldBibleEmployee> BuildEmployees()
    {
        var plan = new (string DepartmentCode, string RoleName, int Count)[]
        {
            ("DEPT-PROD", "车间主任", 3),
            ("DEPT-PROD", "班组长", 6),
            ("DEPT-PROD", "操作工", 19),
            ("DEPT-PLAN", "计划主管", 1),
            ("DEPT-PLAN", "计划员", 3),
            ("DEPT-QA", "质量主管", 1),
            ("DEPT-QA", "检验员", 6),
            ("DEPT-QA", "质量工程师", 2),
            ("DEPT-EQ", "设备主管", 1),
            ("DEPT-EQ", "维修技师", 4),
            ("DEPT-EQ", "点检员", 1),
            ("DEPT-WH", "仓储主管", 1),
            ("DEPT-WH", "库管", 4),
            ("DEPT-WH", "配送叉车工", 2),
            ("DEPT-BIZ", "销售", 2),
            ("DEPT-BIZ", "采购", 2),
        };

        var employees = new List<WorldBibleEmployee>(58);
        var ordinal = 0;
        var supervisorIndex = 0;
        var leaderIndex = 0;
        var operatorIndex = 0;
        foreach (var (departmentCode, roleName, count) in plan)
        {
            for (var index = 0; index < count; index++)
            {
                var employeeNo = $"EMP-{ordinal + 1:D3}";
                var name = $"{Surnames[ordinal % Surnames.Length]}{GivenNames[(ordinal * 7) % GivenNames.Length]}";
                string? workshopCode = null;
                string? teamCode = null;
                var isTeamLeader = false;

                switch (roleName)
                {
                    case "车间主任":
                        workshopCode = Workshops[supervisorIndex++].Code;
                        break;
                    case "班组长":
                        teamCode = Teams[leaderIndex].Code;
                        workshopCode = Teams[leaderIndex].WorkshopCode;
                        isTeamLeader = true;
                        leaderIndex++;
                        break;
                    case "操作工":
                        // 19 名操作工按 6 个班组轮转：前 18 人每组 3 名，第 19 人补入末组。
                        var teamIndex = operatorIndex < 18 ? operatorIndex % 6 : 5;
                        teamCode = Teams[teamIndex].Code;
                        workshopCode = Teams[teamIndex].WorkshopCode;
                        operatorIndex++;
                        break;
                }

                employees.Add(new WorldBibleEmployee(
                    UserId: $"user-emp-{ordinal + 1:D3}",
                    EmployeeNo: employeeNo,
                    Name: name,
                    DepartmentCode: departmentCode,
                    RoleName: roleName,
                    WorkshopCode: workshopCode,
                    TeamCode: teamCode,
                    IsTeamLeader: isTeamLeader,
                    SkillCodes: ResolveSkills(roleName, workshopCode, ordinal)));
                ordinal++;
            }
        }

        return [.. employees];
    }

    private static string[] ResolveSkills(string roleName, string? workshopCode, int ordinal)
    {
        if (roleName == "操作工" && workshopCode is not null)
        {
            var pool = OperatorSkillPools[workshopCode];
            var take = 1 + (ordinal % 3);
            return [.. Enumerable.Range(0, take).Select(offset => pool[(ordinal + offset) % pool.Length])];
        }

        return roleName switch
        {
            "班组长" when workshopCode is not null => [OperatorSkillPools[workshopCode][0], "equipment-maintenance"],
            "车间主任" => ["production-planning"],
            "计划主管" or "计划员" => ["production-planning"],
            "质量主管" or "检验员" => ["inspection"],
            "质量工程师" => ["inspection", "performance-test"],
            "设备主管" or "维修技师" or "点检员" => ["equipment-maintenance"],
            "仓储主管" or "库管" or "配送叉车工" => ["forklift"],
            _ => [],
        };
    }

    /// <summary>技能等级按序号确定性轮转，保证同一员工每次生成结果一致。</summary>
    public static string SkillLevel(int ordinal) => (ordinal % 4) switch
    {
        0 => "junior",
        1 => "intermediate",
        2 => "senior",
        _ => "expert",
    };

    #endregion
}

public sealed record WorldBibleShift(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);

public sealed record WorldBibleDepartment(string Code, string Name);

public sealed record WorldBibleWorkshop(string Code, string Name, string Description);

public sealed record WorldBibleProductionLine(string Code, string Name, string WorkshopCode);

public sealed record WorldBibleWorkCenter(
    string Code,
    string Name,
    string LineCode,
    string WorkshopCode,
    int CapacityMinutesPerDay,
    int NumberOfCapacities);

public sealed record WorldBibleDevice(
    string Code,
    string Model,
    string AssetClassCode,
    string WorkCenterCode,
    string Criticality,
    string Manufacturer);

public sealed record WorldBiblePlatform(string Code, string Name);

public sealed record WorldBibleSku(string Code, string Name, string Unit, string Category);

public sealed record WorldBiblePartner(string Code, string Name, string PartnerType);

public sealed record WorldBibleSkill(
    string Code,
    string Name,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string Description);

public sealed record WorldBibleTeam(string Code, string Name, string WorkshopCode, string ShiftCode);

public sealed record WorldBibleEmployee(
    string UserId,
    string EmployeeNo,
    string Name,
    string DepartmentCode,
    string RoleName,
    string? WorkshopCode,
    string? TeamCode,
    bool IsTeamLeader,
    string[] SkillCodes);
