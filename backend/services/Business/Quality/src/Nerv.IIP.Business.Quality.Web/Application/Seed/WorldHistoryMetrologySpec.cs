using System.Globalization;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **质量域三期**：计量 / SPC / CAPA 的确定性事实流。
///
/// 三块都不自造上游单据，全部挂在已经落库的真实事实上：
/// <list type="number">
/// <item>**计量器具台账**（<c>MD-*</c>）：质量部与生产部在用的量具，是检验记录背后「拿什么量的」那一层；</item>
/// <item>**校准记录**（<c>CAL-2026-####</c>）：每台器具按检定周期回溯，只落在 [上线日, asOfDate] 窗口内的那几次；</item>
/// <item>**SPC 控制图**（控制限锁定）：挂在真实存在的计量型检验特性上，控制限由
///       <see cref="WorldHistoryQualitySpec"/> 同一套实测值公式推出——所以图上的中心线与
///       <c>inspection_result_lines</c> 里真实存在的数值不可能自相矛盾；</item>
/// <item>**CAPA**（<c>CAPA-2026-###</c>）：挂在真实存在的 <c>NCR-2026-####</c> 上，
///       只有「重大（报废）」与「重复（同物料同原因累计第 4 次）」两类才升 CAPA。</item>
/// </list>
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张事实表，seed 与校验器共用它。
/// </summary>
public static class WorldHistoryMetrologySpec
{
    #region §9 号段（三期新增）

    /// <summary>计量器具编码：<c>MD-{类别缩写}-##</c>。</summary>
    public static string MeasuringDeviceCode(string categoryToken, int ordinal) =>
        $"MD-{categoryToken}-{ordinal:D2}";

    /// <summary>校准证书 / 校准记录号：<c>CAL-2026-####</c>。</summary>
    public static string CalibrationNo(int sequence) =>
        $"CAL-2026-{sequence:D4}";

    /// <summary>纠正预防措施单号：<c>CAPA-2026-###</c>（与运行时 <c>CAPA-{org}-{env}-{guid}</c> 字面隔离）。</summary>
    public static string CorrectiveActionCode(int sequence) =>
        $"CAPA-2026-{sequence:D3}";

    /// <summary>三期产出的全部号段前缀，供隔离性回归测试断言不与固定演示事实相交。</summary>
    public static readonly string[] NumberSegmentPrefixes =
    [
        "MD-", "CAL-2026-", "CAPA-2026-",
    ];

    #endregion

    #region 计量器具台账（设定集 §5 质量部 / 设备部在用量具）

    /// <summary>校准状态的目标分布（设定集要求：多数在有效期内、少量临近到期、个别已过期）。</summary>
    public const int OverdueDeviceCount = 2;
    public const int WarningDeviceCount = 3;
    public const int DisabledDeviceCount = 2;
    public const int RetiredDeviceCount = 1;

    /// <summary>临期预警窗口，与 <c>GetCalibrationDashboardEndpoint</c> 的默认值同为 7 天。</summary>
    public const int WarningDays = 7;

    /// <summary>
    /// 器具类别（中文名 + 量程规格 + 精度等级 + 检定周期）。
    ///
    /// **一个被领域约束逼出来的形状**：<c>MeasuringDevice</c> 聚合只有
    /// <c>DeviceCode / DeviceType / Accuracy</c> 三个描述列，没有「使用部门」列。
    /// 本批不为演示数据加迁移，因此规格与精度等级合并写进 <c>Accuracy</c>，
    /// 部门归属留待读面需要时另开迁移。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryMeasuringDeviceCategory> DeviceCategories =
    [
        new("CLP", "数显卡尺", "0–150mm / 0.01mm / Ⅱ级", 365, 10),
        new("MIC", "外径千分尺", "0–25mm / 0.001mm / 0 级", 365, 6),
        new("HGT", "数显高度规", "0–300mm / 0.01mm / Ⅱ级", 365, 3),
        new("TRQ", "数显扭力扳手", "10–100N·m / ±1% / Ⅰ级", 180, 6),
        new("CMM", "三坐标测量机", "700×1000×600mm / 2.5μm / 高精度级", 365, 2),
        new("RGH", "表面粗糙度仪", "Ra 0.005–16μm / 0.001μm / Ⅰ级", 365, 3),
        new("HRD", "洛氏硬度计", "20–70HRC / ±0.5HRC / Ⅰ级", 365, 3),
        new("PRS", "精密压力表", "0–2.5MPa / 0.4 级", 180, 8),
        new("FRC", "阻尼力标准测力仪", "0–5000N / ±0.3% / Ⅰ级", 180, 3),
    ];

    /// <summary>第三方校准机构（校准记录的出证方）。</summary>
    public static readonly IReadOnlyList<string> CalibrationProviders =
    [
        "江苏省计量科学研究院",
        "南京市计量监督检测院",
        "苏州华测计量检定有限公司",
        "宁沪减振计量室（内部校准）",
    ];

    /// <summary>
    /// 计量器具台账（44 台，规模不随 <c>scale</c> 变化）。
    ///
    /// 台账是「上线时一次性建档」的资产清单，不是随产量增减的历史流水——
    /// 把它挂到 <c>scale</c> 上只会让 <c>Scale=0.1</c> 的快速验证出现「工厂只有 4 把卡尺」的荒诞形状。
    /// 变的是每台器具的校准链落在窗口内的第几次，那由 <paramref name="asOfDate"/> 决定。
    /// </summary>
    public static IReadOnlyList<WorldHistoryMeasuringDeviceFact> BuildMeasuringDeviceFacts(DateOnly asOfDate)
    {
        var drafts = new List<(string Code, WorldHistoryMeasuringDeviceCategory Category)>(48);
        foreach (var category in DeviceCategories)
        {
            for (var ordinal = 1; ordinal <= category.Count; ordinal++)
            {
                drafts.Add((MeasuringDeviceCode(category.Token, ordinal), category));
            }
        }

        // 确定性「打散」：按器具编码的 FNV 哈希排序后按目标计数切片，
        // 于是过期件不会永远都是第一把卡尺，同时计数仍然精确可断言。
        var shuffled = drafts
            .OrderBy(draft => WorldHistoryRandom.Fnv1a64($"metrology-lifecycle:{draft.Code}"))
            .ThenBy(draft => draft.Code, StringComparer.Ordinal)
            .ToArray();

        var lifecycles = new Dictionary<string, WorldHistoryMeasuringDeviceLifecycle>(StringComparer.Ordinal);
        var cursor = 0;
        cursor = Assign(shuffled, lifecycles, cursor, OverdueDeviceCount, WorldHistoryMeasuringDeviceLifecycle.Overdue);
        cursor = Assign(shuffled, lifecycles, cursor, WarningDeviceCount, WorldHistoryMeasuringDeviceLifecycle.Warning);
        cursor = Assign(shuffled, lifecycles, cursor, DisabledDeviceCount, WorldHistoryMeasuringDeviceLifecycle.Disabled);
        cursor = Assign(shuffled, lifecycles, cursor, RetiredDeviceCount, WorldHistoryMeasuringDeviceLifecycle.Retired);
        for (; cursor < shuffled.Length; cursor++)
        {
            lifecycles[shuffled[cursor].Code] = WorldHistoryMeasuringDeviceLifecycle.Current;
        }

        var facts = new List<WorldHistoryMeasuringDeviceFact>(drafts.Count);
        var calibrationSequence = 0;
        foreach (var (code, category) in drafts)
        {
            var lifecycle = lifecycles[code];
            var random = new WorldHistoryRandom($"metrology-device:{code}");
            var targetDueAtUtc = ResolveCalibrationDue(asOfDate, category, lifecycle, random);
            var chain = BuildCalibrationChain(targetDueAtUtc, category, code, ref calibrationSequence);
            facts.Add(new WorldHistoryMeasuringDeviceFact(
                DeviceCode: code,
                DeviceType: category.DisplayName,
                Accuracy: category.Accuracy,
                CalibrationIntervalDays: category.CalibrationIntervalDays,
                Lifecycle: lifecycle,
                InitialCalibratedAtUtc: chain.InitialCalibratedAtUtc,
                CalibrationDueAtUtc: chain.EffectiveDueAtUtc,
                Calibrations: chain.Records));
        }

        return facts;
    }

    private static int Assign(
        IReadOnlyList<(string Code, WorldHistoryMeasuringDeviceCategory Category)> shuffled,
        Dictionary<string, WorldHistoryMeasuringDeviceLifecycle> lifecycles,
        int cursor,
        int count,
        WorldHistoryMeasuringDeviceLifecycle lifecycle)
    {
        for (var step = 0; step < count && cursor < shuffled.Count; step++, cursor++)
        {
            lifecycles[shuffled[cursor].Code] = lifecycle;
        }

        return cursor;
    }

    /// <summary>
    /// 反推「下次校准到期时刻」：先定目标状态，再倒推到期日，最后倒推末次校准日。
    /// 这样校准状态的分布是**设计出来的**（可断言），而不是随机撞出来的。
    /// </summary>
    private static DateTimeOffset ResolveCalibrationDue(
        DateOnly asOfDate,
        WorldHistoryMeasuringDeviceCategory category,
        WorldHistoryMeasuringDeviceLifecycle lifecycle,
        WorldHistoryRandom random)
    {
        var asOfUtc = new DateTimeOffset(asOfDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc));
        return lifecycle switch
        {
            // 已过期：3–40 天前就该送检，正是演示要讲的「计量失效风险」。
            WorldHistoryMeasuringDeviceLifecycle.Overdue => asOfUtc.AddDays(-random.NextInt(3, 41)),

            // 临近到期：落在 7 天预警窗口之内。下界取 3 天而非 1 天，
            // 是因为末次校准时刻还要被 AtWorkingMoment 往前挪最多一天半（避开周日、落到白班），
            // 到期日会跟着往前挪同样的量——留 3 天余量，挪完仍稳稳落在 (now, now+7]。
            WorldHistoryMeasuringDeviceLifecycle.Warning => asOfUtc.AddDays(random.NextInt(3, WarningDays + 1)),

            // 停用 / 报废件的到期时刻不参与判定（状态已短路为 unavailable），给一个过去的日期即可。
            WorldHistoryMeasuringDeviceLifecycle.Disabled or WorldHistoryMeasuringDeviceLifecycle.Retired =>
                asOfUtc.AddDays(-random.NextInt(10, 90)),

            // 有效期内：至少越过预警窗口 13 天，避免与 warning 档口径重叠。
            _ => asOfUtc.AddDays(random.NextInt(WarningDays + 13, category.CalibrationIntervalDays + 1)),
        };
    }

    /// <summary>
    /// 按检定周期从到期日往回铺校准链。落在 [上线日, 到期日] 内的每一次都成为一条校准记录；
    /// 更早的那一次只作为建档时的「上线导入初始校准状态」，不落记录行——
    /// 电子历史自 2026-01-05 起（设定集 §1），系统里不该出现上线之前开出的证书。
    /// </summary>
    private static (
        DateTimeOffset InitialCalibratedAtUtc,
        DateTimeOffset EffectiveDueAtUtc,
        IReadOnlyList<WorldHistoryCalibrationFact> Records)
        BuildCalibrationChain(
            DateTimeOffset targetDueAtUtc,
            WorldHistoryMeasuringDeviceCategory category,
            string deviceCode,
            ref int calibrationSequence)
    {
        var goLiveUtc = new DateTimeOffset(
            WorldHistoryCalendar.GoLiveDate.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc));
        var cursor = targetDueAtUtc.AddDays(-category.CalibrationIntervalDays);
        var inWindow = new List<DateTimeOffset>(4);
        while (cursor >= goLiveUtc && inWindow.Count < 4)
        {
            inWindow.Add(cursor);
            cursor = cursor.AddDays(-category.CalibrationIntervalDays);
        }

        // cursor 现在是上线日之前的那一次：它就是建档锚点（无论窗口内有没有记录）。
        var initial = cursor;
        var records = new List<WorldHistoryCalibrationFact>(inWindow.Count);
        foreach (var calibratedAtUtc in Enumerable.Reverse(inWindow))
        {
            calibrationSequence++;
            var random = new WorldHistoryRandom($"metrology-calibration:{deviceCode}:{calibrationSequence:D4}");
            var provider = random.PickWeighted(CalibrationProviders, [35, 30, 20, 15]);
            var actualCalibratedAtUtc = AtWorkingMoment(calibratedAtUtc, deviceCode, calibrationSequence);
            records.Add(new WorldHistoryCalibrationFact(
                CalibrationNo: CalibrationNo(calibrationSequence),
                CalibratedAtUtc: actualCalibratedAtUtc,
                CalibrationProvider: provider,
                CertificateFileId: $"file-cal-{CalibrationNo(calibrationSequence)}",
                Conclusion: "合格",
                NextDueAtUtc: actualCalibratedAtUtc.AddDays(category.CalibrationIntervalDays)));
        }

        // 到期日以**实际落库的末次校准**为准：领域层 RecordCalibration 就是这么算的，
        // 规格层若还抱着「目标到期日」不放，台账页上的「末次校准 + 周期 ≠ 到期日」会立刻穿帮。
        var effectiveDue = records.Count > 0 ? records[^1].NextDueAtUtc : targetDueAtUtc;
        return (initial, effectiveDue, records);
    }

    /// <summary>把校准时刻夹到工作日的白班时段——周日停产，历史里不该出现周日出具的校准证书。</summary>
    private static DateTimeOffset AtWorkingMoment(DateTimeOffset candidate, string deviceCode, int sequence)
    {
        var day = DateOnly.FromDateTime(candidate.UtcDateTime);
        while (!WorldHistoryCalendar.IsWorkingDay(day) && day > WorldHistoryCalendar.GoLiveDate)
        {
            day = day.AddDays(-1);
        }

        var random = new WorldHistoryRandom($"metrology-calibration-moment:{deviceCode}:{sequence:D4}");
        return WorldHistoryCalendar.ShiftMoment(WorldHistoryCalendar.SnapToWorkingDay(day), 0, random.NextInt(0, 420));
    }

    #endregion

    #region SPC 控制图（挂在真实的计量型检验特性上）

    /// <summary>X-bar/R 子组容量，与既有 SPC 查询默认值一致。</summary>
    public const int SpcSubgroupSize = 5;

    /// <summary>
    /// 锁定控制限所用的试运行子组数。
    ///
    /// 教科书口径是 25 组，但那要求单个 (SKU, 特性) 至少 125 次实测：设定集 §7 的 29 周历史
    /// 摊到 24 个成品上，每个成品的性能终检也就 70–110 次，按 25 组立图会得到**一张图都立不出来**。
    /// 取 10 组（50 次实测）是这个工厂规模下能同时满足「立得出图」与「控制限有统计意义」的落点。
    /// </summary>
    public const int SpcTrialSubgroupCount = 10;

    /// <summary>立图门槛：至少要有 <see cref="SpcTrialSubgroupCount"/> 组完整子组才谈得上锁控制限。</summary>
    public const int SpcMinimumMeasurements = SpcSubgroupSize * SpcTrialSubgroupCount;

    /// <summary>
    /// 立图的计量型特性——**只能取工序检验（性能终检）这一路**。
    ///
    /// 来料检验的 <c>dimension</c> 同样是计量型，但来料检验没有工作中心归属，
    /// 而 SPC 控制图的自然键里 <c>WorkCenterId</c> 是必填：给它编一个工作中心
    /// 只会让台账上出现一个工厂里并不存在的车间。
    /// </summary>
    public static readonly IReadOnlyList<string> SpcCharacteristicCodes = ["damping-force", "stroke"];

    /// <summary>性能终检（工序 70）固定落在性能试验工作中心，与 MES 侧 <c>WorkCenterCode</c> 同一公式。</summary>
    public static readonly string SpcWorkCenterId = WorldHistoryMesSpec.WorkCenterCode("FG-QJ-P1-L", 70);

    /// <summary>
    /// 按 (SKU, 特性) 汇出可立图的实测值序列。
    ///
    /// 实测值用与 <c>WorldHistorySeedService.BuildPassingLine</c> / <c>BuildDefectLine</c>
    /// **逐字相同**的公式重算，因此控制限与 <c>inspection_result_lines</c> 里真实落库的数值
    /// 出自同一组数——这也是「不与真实检验数据自相矛盾」的唯一可靠做法。
    /// </summary>
    public static IReadOnlyList<WorldHistorySpcSeries> BuildSpcSeries(DateOnly asOfDate, double scale)
    {
        var facts = WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, scale);
        var plan = WorldHistoryQualitySpec.PlanFor("operation");
        var series = new Dictionary<(string Sku, string Code), List<WorldHistorySpcMeasurement>>();

        foreach (var fact in facts
            .Where(fact => string.Equals(fact.SourceType, "operation", StringComparison.Ordinal))
            .Where(fact => fact.HasRecord)
            .OrderBy(fact => fact.CompletedAtUtc!.Value)
            .ThenBy(fact => fact.SourceDocumentId, StringComparer.Ordinal))
        {
            foreach (var characteristicCode in SpcCharacteristicCodes)
            {
                var characteristic = plan.Characteristics.Single(x =>
                    string.Equals(x.Code, characteristicCode, StringComparison.Ordinal));
                var key = (fact.SkuCode, characteristicCode);
                if (!series.TryGetValue(key, out var points))
                {
                    points = [];
                    series[key] = points;
                }

                points.Add(new WorldHistorySpcMeasurement(
                    fact.SourceDocumentId,
                    fact.CompletedAtUtc!.Value,
                    MeasuredValue(fact, characteristic)));
            }
        }

        return
        [
            .. series
                .Where(entry => entry.Value.Count >= SpcMinimumMeasurements)
                .OrderBy(entry => entry.Key.Sku, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.Code, StringComparer.Ordinal)
                .Select(entry => new WorldHistorySpcSeries(
                    entry.Key.Sku,
                    entry.Key.Code,
                    SpcWorkCenterId,
                    SpcSubgroupSize,
                    entry.Value)),
        ];
    }

    /// <summary>与 <c>WorldHistorySeedService</c> 的实测值公式逐字一致（含不合格行的越界值）。</summary>
    public static decimal MeasuredValue(
        WorldHistoryInspectionFact fact,
        WorldHistoryInspectionCharacteristic characteristic)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(characteristic);
        var isDefect = fact.HasNonconformance
            && string.Equals(characteristic.Code, fact.DefectCharacteristicCode, StringComparison.Ordinal);
        if (isDefect)
        {
            return decimal.Round((characteristic.UpperSpecLimit ?? characteristic.NominalValue!.Value) * 1.02m, 2);
        }

        var random = new WorldHistoryRandom($"measure:{fact.TriggerIdempotencyKey}:{characteristic.Code}");
        var nominal = characteristic.NominalValue!.Value;
        var halfTolerance = (characteristic.UpperSpecLimit ?? nominal) - nominal;
        var offset = halfTolerance * 0.8m * (random.NextInt(-50, 51) / 50m);
        return decimal.Round(nominal + offset, 2);
    }

    #endregion

    #region CAPA（挂在真实存在的 NCR 上）

    /// <summary>「重复不合格」的判定阈值：同一 SKU + 同一原因码累计第 4 次才升 CAPA。</summary>
    public const int RepeatDefectThreshold = 4;

    /// <summary>CAPA 从开单到应关闭的标准周期（天）。</summary>
    public const int CapaDueDays = 30;

    /// <summary>开单满 <see cref="CapaClosedAgeDays"/> 天的 CAPA 原则上应已闭环。</summary>
    public const int CapaClosedAgeDays = 45;

    /// <summary>开单满 <see cref="CapaVerifiedAgeDays"/> 天的 CAPA 至少走到效果验证。</summary>
    public const int CapaVerifiedAgeDays = 20;

    /// <summary>「本该关掉却拖着没关」的比例——逾期未闭环是演示里要讲的管理抓手。</summary>
    public const double CapaOverdueRate = 0.10;

    private static readonly IReadOnlyDictionary<string, WorldHistoryCapaNarrative> Narratives =
        new Dictionary<string, WorldHistoryCapaNarrative>(StringComparer.Ordinal)
        {
            ["RSN-DIMENSION"] = new(
                "活塞杆精车工序刀具磨损补偿未按周期执行，累积尺寸漂移超出公差带",
                "隔离本批并对在制品 100% 全检，超差件转返工",
                "修订精车刀具寿命管理规程，将补偿周期由 400 件收紧至 250 件",
                "在 CNC 程序中加入刀补到期强制提示，操作工未确认不得开工"),
            ["RSN-APPEARANCE"] = new(
                "电泳前脱脂槽液浓度低于工艺下限，导致涂层附着不良出现流挂与色差",
                "隔离本批外观件，返修抛光后重新电泳",
                "槽液浓度检测频次由每班 1 次提高到每 4 小时 1 次并留痕",
                "在电泳线加装槽液电导率在线监测，超限自动报警停线"),
            ["RSN-LABELING"] = new(
                "换型时标签模板未随工单切换，前一批号码延用到本批",
                "召回已贴错标产品，重新打印并核对标签",
                "包装工位增加换型首件标签核对签字环节",
                "标签打印与工单号绑定校验，工单不匹配时打印机拒绝出标"),
            ["RSN-PACKAGING"] = new(
                "纸箱供应商更换瓦楞纸克重，堆码强度下降导致运输途中压损",
                "受损件重新包装后放行，通知客户端加固托盘",
                "对新供应商纸箱补做堆码试验并纳入进货检验必检项",
                "包材变更纳入 ECO 流程，未经验证的替代料不得投用"),
            ["RSN-FUNC-FAIL"] = new(
                "阀系预装扭矩设定值被误改，导致阻尼阀片压紧力不足、阻尼力失效",
                "本批全部隔离报废，追溯同扭矩设定下的相邻批次",
                "拧紧枪参数改为工艺员密码保护，操作工不可修改",
                "每班首件扭矩复核并上传拧紧曲线，异常自动拦截"),
            ["RSN-CONTAMINATION"] = new(
                "减振油过滤精度不达标，装配环境金属屑随油液进入缸筒",
                "本批报废，清洗油路并更换滤芯",
                "注油机滤芯更换周期由 3 个月缩短为 1 个月并挂标签",
                "装配区增设油液清洁度定期检测（NAS 8 级以内）"),
        };

    private static readonly WorldHistoryCapaNarrative DefaultNarrative = new(
        "同类不合格重复出现，工序参数控制未形成闭环",
        "隔离在库同批次并对在制品加严检验",
        "修订作业指导书并对相关岗位重新培训考核",
        "将该参数纳入首件确认与巡检必检项");

    /// <summary>
    /// CAPA 事实流。开单判据只有两条，都对得上「重大 / 重复」的现实口径：
    /// <list type="bullet">
    /// <item>**重大**：处置为报废的 NCR——批量报废在任何工厂都必然触发纠正措施；</item>
    /// <item>**重复**：同一 SKU + 同一不良原因码累计到第 <see cref="RepeatDefectThreshold"/> 次，
    ///       由该次 NCR 开单（再往后的同类不再重复开单，否则台账会被同一个问题灌满）。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<WorldHistoryCapaFact> BuildCapaFacts(DateOnly asOfDate, double scale)
    {
        var asOfUtc = new DateTimeOffset(asOfDate.ToDateTime(new TimeOnly(23, 59), DateTimeKind.Utc));
        var nonconforming = WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, scale)
            .Where(fact => fact.HasNonconformance && fact.NcrCode is not null)
            .OrderBy(fact => fact.NcrCode, StringComparer.Ordinal)
            .ToArray();

        var repeatCounters = new Dictionary<(string Sku, string Reason), int>();
        var capas = new List<WorldHistoryCapaFact>(nonconforming.Length / 4);
        var sequence = 0;
        foreach (var fact in nonconforming)
        {
            var reasonCode = fact.DefectReasonCode!;
            var key = (fact.SkuCode, reasonCode);
            repeatCounters.TryGetValue(key, out var seen);
            repeatCounters[key] = seen + 1;

            var major = fact.Disposition == WorldHistoryInspectionDisposition.Scrap;
            var repeated = repeatCounters[key] == RepeatDefectThreshold;
            if (!major && !repeated)
            {
                continue;
            }

            sequence++;
            capas.Add(BuildCapaFact(fact, sequence, major ? "重大" : "重复", asOfUtc));
        }

        return capas;
    }

    private static WorldHistoryCapaFact BuildCapaFact(
        WorldHistoryInspectionFact fact,
        int sequence,
        string trigger,
        DateTimeOffset asOfUtc)
    {
        var capaCode = CorrectiveActionCode(sequence);
        var random = new WorldHistoryRandom($"metrology-capa:{capaCode}");
        var narrative = Narratives.TryGetValue(fact.DefectReasonCode!, out var found) ? found : DefaultNarrative;

        // 开单在 NCR 关单后的下一个工作日：先把单据处置掉，再谈系统性纠正。
        var openedAtUtc = WorldHistoryPhase2Spec.MomentOn(
            WorldHistoryQualitySpec.ClampToHistory(
                WorldHistoryCalendar.AddWorkingDays(DateOnly.FromDateTime(fact.NcrClosedAtUtc!.Value.UtcDateTime), 1),
                DateOnly.FromDateTime(asOfUtc.UtcDateTime)),
            capaCode,
            "capa-opened");
        if (openedAtUtc <= fact.NcrClosedAtUtc.Value)
        {
            openedAtUtc = fact.NcrClosedAtUtc.Value.AddHours(6);
        }

        var dueAtUtc = openedAtUtc.AddDays(CapaDueDays);
        var ageDays = (asOfUtc - openedAtUtc).TotalDays;
        var owner = WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.QualityEngineers, capaCode);

        var stage = ageDays >= CapaClosedAgeDays
            // 到点本该关掉的那批里，留一小撮拖期未闭环——这才是真实的 CAPA 台账。
            ? (random.Chance(CapaOverdueRate) ? WorldHistoryCapaStage.Overdue : WorldHistoryCapaStage.Closed)
            : ageDays >= CapaVerifiedAgeDays
                ? WorldHistoryCapaStage.EffectivenessVerified
                : WorldHistoryCapaStage.Open;

        var actions = BuildCapaActions(capaCode, openedAtUtc, narrative, stage, random);
        var lastCompletedAtUtc = actions.Where(x => x.CompletedAtUtc is not null).Select(x => x.CompletedAtUtc!.Value).DefaultIfEmpty(openedAtUtc).Max();
        var verifiedAtUtc = stage is WorldHistoryCapaStage.EffectivenessVerified or WorldHistoryCapaStage.Closed
            ? lastCompletedAtUtc.AddDays(random.NextInt(2, 6))
            : (DateTimeOffset?)null;
        var closedAtUtc = stage == WorldHistoryCapaStage.Closed
            ? verifiedAtUtc!.Value.AddDays(random.NextInt(1, 4))
            : (DateTimeOffset?)null;

        return new WorldHistoryCapaFact(
            CapaCode: capaCode,
            NcrCode: fact.NcrCode!,
            SkuCode: fact.SkuCode,
            Trigger: trigger,
            RootCause: $"{fact.DefectReasonText}（{trigger}）：{narrative.RootCause}",
            ContainmentAction: narrative.Containment,
            OwnerUserId: owner.UserId,
            OpenedAtUtc: openedAtUtc,
            DueAtUtc: dueAtUtc,
            Stage: stage,
            VerifiedByUserId: verifiedAtUtc is null ? null : WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.QualityEngineers, $"verify:{capaCode}").UserId,
            EffectivenessResult: verifiedAtUtc is null ? null : "措施有效，连续三批复检合格，未再出现同类不合格",
            EffectivenessVerifiedAtUtc: verifiedAtUtc,
            ClosedByUserId: closedAtUtc is null ? null : owner.UserId,
            ClosedAtUtc: closedAtUtc,
            Actions: actions);
    }

    private static IReadOnlyList<WorldHistoryCapaActionFact> BuildCapaActions(
        string capaCode,
        DateTimeOffset openedAtUtc,
        WorldHistoryCapaNarrative narrative,
        WorldHistoryCapaStage stage,
        WorldHistoryRandom random)
    {
        var drafts = new List<(string Type, string Description, int DueOffsetDays)>
        {
            ("containment", $"临时措施：{narrative.Containment}", 3),
            ("corrective", $"纠正措施：{narrative.Corrective}", 14),
            ("preventive", $"预防措施：{narrative.Preventive}", CapaDueDays - 3),
        };

        // 约四成 CAPA 多一条培训类纠正措施（8D 的 D6 往往拆成两步）。
        if (random.Chance(0.4))
        {
            drafts.Insert(2, ("corrective", "纠正措施：对相关岗位重新培训并考核上岗，培训记录归档", 10));
        }

        // 只有走到效果验证的 CAPA 才可能全部完成——领域层强约束：有未完成项就不能验证效果。
        var allCompleted = stage is WorldHistoryCapaStage.EffectivenessVerified or WorldHistoryCapaStage.Closed;
        var actions = new List<WorldHistoryCapaActionFact>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var (type, description, dueOffsetDays) = drafts[index];
            var actionDueAtUtc = openedAtUtc.AddDays(dueOffsetDays);
            var itemRandom = new WorldHistoryRandom($"metrology-capa-action:{capaCode}:{index:D2}");

            // 进行中的 CAPA：靠前的措施已完成，靠后的还开着——列表页才有「进度 2/4」这种真实观感。
            // 逾期档卡在最后一步（预防措施迟迟不落地），这正是逾期 CAPA 在现实里最常见的样子。
            var completedThreshold = stage switch
            {
                WorldHistoryCapaStage.Overdue => drafts.Count - 1,
                WorldHistoryCapaStage.Open => 1,
                _ => 0,
            };
            var completed = allCompleted || index < completedThreshold;
            actions.Add(new WorldHistoryCapaActionFact(
                ActionType: type,
                Description: description,
                OwnerUserId: WorldHistoryPhase2Spec.Assign(WorldHistoryPhase2Spec.QualityEngineers, $"{capaCode}:{index:D2}").UserId,
                DueAtUtc: actionDueAtUtc,
                CompletedAtUtc: completed ? actionDueAtUtc.AddDays(-itemRandom.NextInt(0, 3)) : null));
        }

        return actions;
    }

    #endregion

    /// <summary>诊断用：把一条事实渲染成可比对的中文串（校验器样本行）。</summary>
    public static string Describe(WorldHistoryCapaFact capa) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{capa.CapaCode} ← {capa.NcrCode}（{capa.Trigger}）{capa.Stage} 措施 {capa.Actions.Count} 项");
}

public sealed record WorldHistoryMeasuringDeviceCategory(
    string Token,
    string DisplayName,
    string Accuracy,
    int CalibrationIntervalDays,
    int Count);

/// <summary>计量器具在 asOfDate 这一刻的台账落点。</summary>
public enum WorldHistoryMeasuringDeviceLifecycle
{
    /// <summary>校准有效期内。</summary>
    Current,

    /// <summary>临近到期（7 天预警窗口内）。</summary>
    Warning,

    /// <summary>已过期未校准。</summary>
    Overdue,

    /// <summary>停用（待修）。</summary>
    Disabled,

    /// <summary>报废退出台账。</summary>
    Retired,
}

public sealed record WorldHistoryCalibrationFact(
    string CalibrationNo,
    DateTimeOffset CalibratedAtUtc,
    string CalibrationProvider,
    string CertificateFileId,
    string Conclusion,
    DateTimeOffset NextDueAtUtc);

public sealed record WorldHistoryMeasuringDeviceFact(
    string DeviceCode,
    string DeviceType,
    string Accuracy,
    int CalibrationIntervalDays,
    WorldHistoryMeasuringDeviceLifecycle Lifecycle,
    DateTimeOffset InitialCalibratedAtUtc,
    DateTimeOffset CalibrationDueAtUtc,
    IReadOnlyList<WorldHistoryCalibrationFact> Calibrations);

public sealed record WorldHistorySpcMeasurement(
    string SourceDocumentId,
    DateTimeOffset MeasuredAtUtc,
    decimal MeasuredValue);

public sealed record WorldHistorySpcSeries(
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize,
    IReadOnlyList<WorldHistorySpcMeasurement> Measurements);

public sealed record WorldHistoryCapaNarrative(
    string RootCause,
    string Containment,
    string Corrective,
    string Preventive);

/// <summary>CAPA 在 asOfDate 这一刻的推进阶段。</summary>
public enum WorldHistoryCapaStage
{
    /// <summary>开单进行中，措施尚未做完。</summary>
    Open,

    /// <summary>措施完成、效果已验证，尚未关单。</summary>
    EffectivenessVerified,

    /// <summary>已关闭。</summary>
    Closed,

    /// <summary>逾期未闭环：早该关掉却还开着。</summary>
    Overdue,
}

public sealed record WorldHistoryCapaActionFact(
    string ActionType,
    string Description,
    string OwnerUserId,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record WorldHistoryCapaFact(
    string CapaCode,
    string NcrCode,
    string SkuCode,
    string Trigger,
    string RootCause,
    string ContainmentAction,
    string OwnerUserId,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset DueAtUtc,
    WorldHistoryCapaStage Stage,
    string? VerifiedByUserId,
    string? EffectivenessResult,
    DateTimeOffset? EffectivenessVerifiedAtUtc,
    string? ClosedByUserId,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<WorldHistoryCapaActionFact> Actions);
