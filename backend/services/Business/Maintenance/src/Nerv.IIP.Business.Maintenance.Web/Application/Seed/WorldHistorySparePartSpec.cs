namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **四期（Maintenance 侧）**：维修备件消耗行的确定性纯函数 Spec。
///
/// <para><b>备件目录为什么自成 <c>MRO-</c> 号段、而不复用 L0 的 <c>RM-/PK-</c> 物料</b>：</para>
/// L0 §4 的物料主数据是**减振器的用料**（棒料 / 钢管 / 弹簧 / 油封 / 防尘罩 / 包材），
/// 它们进 BOM、进领料、进成品成本。维修备件是 **MRO 物耗**（轴承 / 滤芯 / 接触器 / 砂轮 / 液压油），
/// 与产品 BOM 无关。把「机床主轴轴承」记成 <c>RM-SEL-01 油封</c> 会让物料主数据、BOM 与库存
/// 三处同时失真，代价远大于多一个号段。因此本目录是 Maintenance 自有的 MRO 备件字面量，
/// **不写入 MasterData SKU 主数据、不进库存台账**（边界理由见
/// <see cref="WorldHistorySeedService"/> 上的说明）。
///
/// <para><b>确定性</b>：每张工单的备件由 <c>spare-parts:{工单号}</c> 单独取流，
/// 与截止日、缩放比例、其他工单是否存在无关——0.1 缩放跑出来的 <c>MWO-2026-0123</c>
/// 与全量跑出来的逐字段相同。</para>
///
/// <para><b>备件与故障原因对得上账</b>：候选池按共享报警计划的 <c>FailureCauseCode</c> 选取
/// （轴承磨损换轴承 + 润滑脂、电气故障换接触器 / 继电器、槽液漂移换 pH 电极 / 加热管……），
/// 演示时「为什么这张单换了这个件」能当场解释。</para>
/// </summary>
public static class WorldHistorySparePartSpec
{
    /// <summary>备件金额币种（L0 §6 与 ERP 侧同）。</summary>
    public const string CurrencyCode = "CNY";

    /// <summary>无备件消耗的工单占比（清洁 / 复位 / 参数调整类维修确实不换件）。</summary>
    public const double NoIssueProbability = 0.28d;

    /// <summary>MRO 备件目录（中文名 + 规格 + 单位 + 参考单价，单价为 2026 年常见市价量级）。</summary>
    public static readonly IReadOnlyList<WorldHistoryMroSparePart> Catalog =
    [
        new("MRO-BRG-01", "深沟球轴承", "6206-2RS", "pcs", 42.00m),
        new("MRO-BRG-02", "角接触球轴承", "7008C P4", "pcs", 186.00m),
        new("MRO-BRG-03", "圆锥滚子轴承", "30206", "pcs", 68.00m),
        new("MRO-SEL-01", "骨架油封", "TC 35×52×7 丁腈", "pcs", 12.50m),
        new("MRO-SEL-02", "骨架油封", "TC 45×65×8 氟胶", "pcs", 16.00m),
        new("MRO-ORG-01", "O 型密封圈", "NBR φ30×3.1", "pcs", 2.40m),
        new("MRO-ORG-02", "O 型密封圈", "氟胶 φ50×3.5", "pcs", 6.80m),
        new("MRO-BLT-01", "伺服同步带", "HTD 8M-1200", "pcs", 158.00m),
        new("MRO-BLT-02", "窄 V 带", "SPB-1800", "pcs", 46.00m),
        new("MRO-FLT-01", "液压油滤芯", "HX-40×10", "pcs", 88.00m),
        new("MRO-FLT-02", "空压机油气分离滤芯", "SA-75 专用", "pcs", 420.00m),
        new("MRO-FLT-03", "冷干机前置过滤滤芯", "CD-20 专用", "pcs", 210.00m),
        new("MRO-ELC-01", "交流接触器", "CJX2-2510 220V", "pcs", 76.00m),
        new("MRO-ELC-02", "中间继电器", "MY4NJ 24VDC", "pcs", 18.00m),
        new("MRO-ELC-03", "熔断器", "RT18-32 20A", "pcs", 6.50m),
        new("MRO-PNE-01", "标准气缸", "SC63×100", "pcs", 145.00m),
        new("MRO-PNE-02", "二位五通电磁阀", "4V210-08 DC24V", "pcs", 62.00m),
        new("MRO-PNE-03", "快插气管接头", "PU8-02 直通", "pcs", 4.20m),
        new("MRO-GRD-01", "白刚玉砂轮", "350×40×127 A60", "pcs", 236.00m),
        new("MRO-TOL-01", "数控车刀片", "CNMG120408-PM", "pcs", 34.00m),
        new("MRO-TOL-02", "焊接导电嘴", "M8×φ1.2 铬锆铜", "pcs", 9.80m),
        new("MRO-OIL-01", "抗磨液压油", "L-HM46 桶装", "l", 18.60m),
        new("MRO-OIL-02", "空压机专用润滑油", "8000h 长效型", "l", 52.00m),
        new("MRO-GRS-01", "高温锂基润滑脂", "EP2 罐装", "kg", 46.00m),
        new("MRO-SNS-01", "铂电阻温度传感器", "PT100 φ6×100", "pcs", 128.00m),
        new("MRO-SNS-02", "增量式编码器", "1024P/R 推挽输出", "pcs", 560.00m),
        new("MRO-PRC-01", "pH 复合电极", "工业在线型", "pcs", 780.00m),
        new("MRO-PRC-02", "槽液加热管", "3kW/380V 钛材", "pcs", 320.00m),
        new("MRO-MEC-01", "弹性联轴器", "梅花型 D40", "pcs", 96.00m),
        new("MRO-MEC-02", "定位销与紧固件套件", "M8/M10 混装", "set", 28.00m),
        new("MRO-COL-01", "冷却水泵机械密封", "104 型 φ25", "pcs", 74.00m),
        new("MRO-COL-02", "轴流散热风扇", "200×200 230VAC", "pcs", 88.00m),
    ];

    private static readonly Dictionary<string, WorldHistoryMroSparePart> CatalogByCode =
        Catalog.ToDictionary(x => x.SkuCode, StringComparer.Ordinal);

    /// <summary>
    /// 故障原因 → 备件候选池（与 <see cref="WorldHistoryDeviceSpec"/> 的
    /// <c>FailureCauseCode</c> 字面量一一对应；权重让「主修件」比「顺带件」更常出现）。
    /// </summary>
    private static readonly Dictionary<string, (string[] Codes, int[] Weights)> CandidatesByCause =
        new(StringComparer.Ordinal)
        {
            ["bearing-wear"] = (["MRO-BRG-01", "MRO-BRG-02", "MRO-BRG-03", "MRO-GRS-01", "MRO-SEL-01"], [8, 4, 5, 6, 3]),
            ["fixture-loose"] = (["MRO-MEC-02", "MRO-MEC-01", "MRO-PNE-01", "MRO-ORG-01"], [9, 4, 3, 4]),
            ["lubrication"] = (["MRO-GRS-01", "MRO-OIL-01", "MRO-FLT-01", "MRO-SEL-02"], [8, 6, 5, 3]),
            ["cooling"] = (["MRO-COL-01", "MRO-COL-02", "MRO-SNS-01", "MRO-OIL-01"], [7, 6, 4, 3]),
            ["tooling-drift"] = (["MRO-TOL-01", "MRO-GRD-01", "MRO-MEC-02", "MRO-ORG-02"], [8, 6, 4, 2]),
            ["electrical"] = (["MRO-ELC-01", "MRO-ELC-02", "MRO-ELC-03", "MRO-SNS-02", "MRO-BLT-01"], [7, 6, 5, 2, 3]),
            ["process-drift"] = (["MRO-PRC-01", "MRO-PRC-02", "MRO-FLT-03", "MRO-ORG-02"], [4, 5, 6, 4]),
            ["air-leak"] = (["MRO-PNE-03", "MRO-PNE-02", "MRO-ORG-01", "MRO-FLT-02"], [8, 5, 6, 2]),
            ["overload"] = (["MRO-ELC-03", "MRO-ELC-01", "MRO-MEC-01", "MRO-BLT-02"], [7, 5, 4, 4]),
        };

    /// <summary>候选池兜底（新增故障原因码时不至于抛异常，落到通用易损件）。</summary>
    private static readonly (string[] Codes, int[] Weights) FallbackCandidates =
        (["MRO-MEC-02", "MRO-ORG-01", "MRO-ELC-02"], [5, 4, 3]);

    /// <summary>按编码取备件（校验器与测试用）。</summary>
    public static WorldHistoryMroSparePart Get(string skuCode) => CatalogByCode[skuCode];

    /// <summary>
    /// 一张**已完工**维修工单的备件消耗行。开放尾部（未完工）工单不产生消耗行——
    /// 备件在完工登记时才记账，这与领域模型一致（<c>Complete</c> 才收 spareParts）。
    /// </summary>
    public static IReadOnlyList<WorldHistorySparePartIssue> BuildIssues(string workOrderNo, string failureCauseCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCauseCode);

        var random = new WorldHistoryRandom($"spare-parts:{workOrderNo}");
        var lineCount = ResolveLineCount(random);
        if (lineCount == 0)
        {
            return [];
        }

        var (codes, weights) = CandidatesByCause.TryGetValue(failureCauseCode, out var pool)
            ? pool
            : FallbackCandidates;

        var issues = new List<WorldHistorySparePartIssue>(lineCount);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < lineCount * 4 && issues.Count < lineCount; attempt++)
        {
            var code = random.PickWeighted(codes, weights);
            if (!used.Add(code))
            {
                continue;
            }

            var part = CatalogByCode[code];
            issues.Add(new WorldHistorySparePartIssue(part, ResolveQuantity(random, part)));
        }

        return issues;
    }

    /// <summary>一张工单的备件金额合计（写入 <c>maintenance_work_orders.spare_part_cost_amount</c>）。</summary>
    public static decimal TotalAmount(IReadOnlyList<WorldHistorySparePartIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var total = 0m;
        foreach (var issue in issues)
        {
            total += issue.Amount;
        }

        return decimal.Round(total, 2);
    }

    /// <summary>0–3 行：28% 不换件 / 42% 一行 / 22% 两行 / 8% 三行 ≈ 平均 1.10 行/完工单。</summary>
    private static int ResolveLineCount(WorldHistoryRandom random)
    {
        var roll = random.NextDouble();
        if (roll < NoIssueProbability)
        {
            return 0;
        }

        if (roll < 0.70d)
        {
            return 1;
        }

        return roll < 0.92d ? 2 : 3;
    }

    /// <summary>数量按单价档位取：贵重件只换 1 件，中价件 1–2，低值易损 2–6（液体/油脂按整数升/公斤）。</summary>
    private static decimal ResolveQuantity(WorldHistoryRandom random, WorldHistoryMroSparePart part)
    {
        if (part.UnitPrice >= 200m)
        {
            return 1m;
        }

        if (part.UnitPrice >= 50m)
        {
            return random.NextInt(1, 3);
        }

        return random.NextInt(2, 7);
    }
}

/// <summary>MRO 备件目录项（中文名 + 规格 + 单位 + 参考单价）。</summary>
public sealed record WorldHistoryMroSparePart(
    string SkuCode,
    string Name,
    string Specification,
    string UomCode,
    decimal UnitPrice)
{
    /// <summary>台账展示名（备件行只存 SKU 编码，名称与规格由本 Spec 提供，不加迁移列）。</summary>
    public string DisplayName => $"{Name} {Specification}";
}

/// <summary>一条备件消耗行的计划值。</summary>
public sealed record WorldHistorySparePartIssue(WorldHistoryMroSparePart Part, decimal Quantity)
{
    public string SkuCode => Part.SkuCode;

    public string UomCode => Part.UomCode;

    /// <summary>行金额 = 单价 × 数量（两位小数）。</summary>
    public decimal Amount => decimal.Round(Part.UnitPrice * Quantity, 2);
}
