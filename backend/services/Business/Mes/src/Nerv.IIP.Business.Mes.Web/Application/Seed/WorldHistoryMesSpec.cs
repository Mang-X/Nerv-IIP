using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// L1 背景历史的 **MES 独有**形状：工艺路线展开、工序任务、齐套需求、领料、报工人员与补产工单。
/// 销售侧的跨服务共享形状在 <see cref="WorldHistorySpec"/>。
///
/// 工序、工作中心、物料、人员编码全部**引用** L0（`WorldBibleSpec`，PR #1124）的既有实体，
/// 本引擎不新建任何主数据。工作中心归属按 L0 ProductEngineering 侧的 <c>RoutingStages()</c>
/// 同一公式重算，黄金向量测试锁定不漂移。
/// </summary>
public static class WorldHistoryMesSpec
{
    /// <summary>L0 §4 的 6 个车型平台，顺序即 platformIndex。</summary>
    public static readonly string[] PlatformCodes = ["P1", "P2", "S1", "S2", "M1", "E1"];

    /// <summary>L0 §4 的 8 道标准工序（下料→CNC 精车→精磨→阀系预装→总装→电泳→性能终检→包装）。</summary>
    public static readonly IReadOnlyList<WorldHistoryOperation> StandardOperations =
    [
        new(10, "OP-WB-CUT", "下料", 15, 2, 5, false, WorldHistoryWorkshop.Machining),
        new(20, "OP-WB-CNC", "CNC 精车", 20, 6, 5, false, WorldHistoryWorkshop.Machining),
        new(30, "OP-WB-GRD", "精磨", 12, 4, 4, false, WorldHistoryWorkshop.Machining),
        new(40, "OP-WB-VLV", "阀系预装", 8, 3, 3, false, WorldHistoryWorkshop.Assembly),
        new(50, "OP-WB-ASM", "总装", 10, 5, 4, false, WorldHistoryWorkshop.Assembly),
        new(60, "OP-WB-CTG", "电泳涂装", 25, 3, 8, false, WorldHistoryWorkshop.Surface),
        new(70, "OP-WB-TST", "性能终检", 6, 2, 2, true, WorldHistoryWorkshop.Surface),
        new(80, "OP-WB-PKG", "包装", 5, 1, 2, false, WorldHistoryWorkshop.Surface),
    ];

    /// <summary>
    /// 可跳过的两道工序（设定集 §7「每单 6–8 工序任务」）：
    /// 下料在直接投半成品时跳过，阀系预装在阀系外购已预装时跳过。核心 6 道恒在。
    /// </summary>
    public const int CuttingSequence = 10;
    public const int ValvePreAssemblySequence = 40;
    public const double CuttingIncludedProbability = 0.70;
    public const double ValvePreAssemblyIncludedProbability = 0.65;

    /// <summary>需要质检的工序序号——二期质量域接管检验任务时的预留引用点。</summary>
    public const int QualityInspectionSequence = 70;

    #region L0 工作中心归属（与 ProductEngineering 侧 RoutingStages() 同一公式）

    public static int PlatformIndex(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        for (var index = 0; index < PlatformCodes.Length; index++)
        {
            if (skuCode.Contains($"-{PlatformCodes[index]}-", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    public static bool IsFrontStrut(string skuCode) =>
        skuCode.StartsWith("FG-QJ-", StringComparison.Ordinal);

    /// <summary>某成品第 <paramref name="sequence"/> 道工序落在哪个工作中心。</summary>
    public static string WorkCenterCode(string skuCode, int sequence)
    {
        var platformIndex = PlatformIndex(skuCode);
        return sequence switch
        {
            10 => $"WC-TUB-{(platformIndex % 2) + 1:D2}",
            20 => $"WC-ROD-{(platformIndex % 2) + 1:D2}",
            30 => "WC-GRD-01",
            40 => "WC-VA-01",
            50 => IsFrontStrut(skuCode)
                ? $"WC-FA-{(platformIndex % 3) + 1:D2}"
                : $"WC-RA-{(platformIndex % 2) + 1:D2}",
            60 => "WC-CT-01",
            70 => "WC-TS-01",
            80 => "WC-PK-01",
            _ => throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Unknown world-bible routing sequence."),
        };
    }

    #endregion

    #region L0 §4 物料（齐套 / 领料引用点）

    /// <summary>某成品的 4 项主料，与 L0 ProductEngineering 的 MBOM 前四行同码同序。</summary>
    public static IReadOnlyList<WorldHistoryComponent> Components(string skuCode)
    {
        var platformIndex = PlatformIndex(skuCode);
        var componentIndex = IsFrontStrut(skuCode) ? platformIndex : (platformIndex + 3) % 6;
        return
        [
            new($"SF-ROD-{componentIndex + 1:D2}", 1m, "pcs"),
            new($"SF-TUB-{componentIndex + 1:D2}", 1m, "pcs"),
            new($"SF-VLV-{platformIndex + 1:D2}", 1m, "pcs"),
            new($"RM-SPR-{(platformIndex % 4) + 1:D2}", 1m, "pcs"),
        ];
    }

    /// <summary>该成品 4 项主料里**采购件**（悬架弹簧 <c>RM-SPR-##</c>）的下标。</summary>
    public const int PurchasedComponentIndex = 3;

    #endregion

    #region 齐套缺口分布（#1408）

    /// <summary>缺口配额块大小（照抄 ECO 状态分布「每块一份配额 + 块内确定性洗牌」的做法）。</summary>
    public const int MaterialShortageQuotaBlockSize = 10;

    /// <summary>
    /// 每 10 张「已下达待开工」工单的缺口配额：6 齐套 / 2 轻度 / 1 部分 / 1 严重。
    ///
    /// <para>
    /// **为什么是配额块而不是独立概率**：独立概率在小纵深（<c>Scale=0.02</c> 的快速验证，
    /// 或演示当天只翻头几页）下会整块缺档，演示配方点名的对象可能根本不存在。配额块保证
    /// 任何连续 10 个序号里四档皆在场——这正是计量器具校准状态与 ECO 状态分布被种子闭环
    /// 审计认可为样板的原因（#1381）。
    /// </para>
    /// <para>
    /// **为什么是 4:6 而不是对半**：缺口只落在「已下达待开工」档（约占全世界工单 3%），
    /// 于是全世界缺料工单约 1.2%，绝大多数工单仍然齐套——「大部分齐套、少数缺料」既是
    /// 真实工厂的样子，也同时保住 #1384 解开的逐工序开工演示。
    /// </para>
    /// </summary>
    private static readonly WorldHistoryMaterialShortageTier[] MaterialShortageQuota =
    [
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Kitted,
        WorldHistoryMaterialShortageTier.Minor,
        WorldHistoryMaterialShortageTier.Minor,
        WorldHistoryMaterialShortageTier.Partial,
        WorldHistoryMaterialShortageTier.Critical,
    ];

    /// <summary>
    /// 取一张订单工单的齐套缺口档位。
    ///
    /// <para>
    /// <paramref name="releasedCohortOrdinal"/> 是这张单在**「已下达待开工」队列里的序号**（从 1 起），
    /// 不是全局订单序号：已下达档只占全世界 3%，按全局序号分配会让整块配额落到不存在的单上，
    /// 实际到手的比例随「这一段里恰好有几张已下达」抖动。按队列序号分配，配额比例才是准的。
    /// 补产工单与其他执行档传 <c>null</c>/0，恒齐套。
    /// </para>
    /// <para>
    /// 配额按连续 10 个队列序号成块，块内以块号为流键**确定性洗牌**，于是档位既不与
    /// <c>ordinal % 10</c> 这类机械分档绑死，又在每个完整块里四档齐全。
    /// </para>
    /// <para>
    /// 只有「已下达待开工」（<see cref="WorldHistoryExecution.ReleasedOnly"/>）的工单会缺料，
    /// 两个理由：① 齐套快照是**下达时刻**的实况，已开工/已完工的工单当时确实齐套，
    /// 把它们改成缺料与「它们后来真的开工并完工了」自相矛盾；② 结构上也做不到——
    /// 在制/完工档的领料已过账，缺口公式里的 <c>received</c> 恒等于需求量，缺口被 clamp 回 0。
    /// </para>
    /// </summary>
    public static WorldHistoryMaterialShortageTier MaterialShortageTier(
        WorldHistoryExecution execution,
        int? releasedCohortOrdinal)
    {
        if (execution != WorldHistoryExecution.ReleasedOnly ||
            releasedCohortOrdinal is not { } ordinal ||
            ordinal <= 0)
        {
            return WorldHistoryMaterialShortageTier.Kitted;
        }

        var position = ordinal - 1;
        var block = position / MaterialShortageQuotaBlockSize;
        var shuffled = MaterialShortageQuota.ToArray();
        var random = new WorldHistoryRandom($"material-shortage-block:{block}");
        for (var cursor = shuffled.Length - 1; cursor > 0; cursor--)
        {
            var swap = random.NextInt(0, cursor + 1);
            (shuffled[cursor], shuffled[swap]) = (shuffled[swap], shuffled[cursor]);
        }

        return shuffled[position % MaterialShortageQuotaBlockSize];
    }

    /// <summary>
    /// 「已下达待开工」工单的排产优先级配额：重点单少、常规单多。
    ///
    /// <para>
    /// **优先级与紧迫度是两件事**，这是这份配额存在的全部理由。紧迫度是系统按交期算出来的
    /// **事实**；优先级是计划员下的**决定**——重点客户的单交期不紧也要先做。把优先级由紧迫度
    /// 推导等于取消这个字段的意义，所以这里独立铺档，不与交期偏移相关联。
    /// </para>
    /// <para>
    /// 修复前全世界常规工单一律写死 <c>10</c>（返修单 90），排产工作台的优先级列整列同值，
    /// 排产员既无从判断也无从演示「我手工把这张提上去」（第五轮走查 owner 点名）。
    /// </para>
    /// </summary>
    private static readonly int[] ReleasedPriorityQuota =
    [
        500, // 战略/重点客户单：每 10 张 1 张
        200, 200, // 重要单
        100, 100, 100, 100, // 常规单占多数
        50, 50, // 可延后
        10, // 填空单
    ];

    /// <summary>取一张「已下达待开工」工单的排产优先级；其余档位沿用既有常量。</summary>
    public static int? ReleasedPriority(
        WorldHistoryExecution execution,
        int? releasedCohortOrdinal)
    {
        if (execution != WorldHistoryExecution.ReleasedOnly ||
            releasedCohortOrdinal is not { } ordinal ||
            ordinal <= 0)
        {
            return null;
        }

        var position = ordinal - 1;
        var block = position / ReleasedDueOffsetQuotaBlockSize;
        var shuffled = ReleasedPriorityQuota.ToArray();
        // 与交期配额用不同的流键：否则「优先级最高的恰好也最晚」会成为固定规律，
        // 演示时反而讲不出「交期不紧但要先做」这条。
        var random = new WorldHistoryRandom($"released-priority-block:{block}");
        for (var cursor = shuffled.Length - 1; cursor > 0; cursor--)
        {
            var swap = random.NextInt(0, cursor + 1);
            (shuffled[cursor], shuffled[swap]) = (shuffled[swap], shuffled[cursor]);
        }

        return shuffled[position % ReleasedDueOffsetQuotaBlockSize];
    }

    /// <summary>每 10 张已下达工单一份交期配额（与缺口配额同一块大小，便于对照阅读）。</summary>
    public const int ReleasedDueOffsetQuotaBlockSize = 10;

    /// <summary>
    /// 「已下达待开工」工单相对 <c>asOfDate</c> 的交期偏移（天）：4 张已逾期 / 6 张尚未到期。
    ///
    /// <para>
    /// **为什么要单独给一份配额**：这批单的交期原本由 <c>订单日 + 18~40 天前置期</c> 推出来，
    /// 而已下达档的订单日被压在 <c>asOfDate</c> 附近，于是**整池工单交期全部落在 asOfDate 之前**
    /// ——排产工作台一打开，待排池清一色逾期（第五轮走查实测：默认窗口 8/2 起，池内交期全是
    /// 7/28~7/30）。两个后果：① 一个所有待排工单都已延误的工厂看着像瘫了；② 紧迫度整列飘红，
    /// 失去区分度，排产员没法据此决定先排谁。
    /// </para>
    /// <para>
    /// 真实工厂两者都有：有拖期的、也有还早的。四逾期六未到期既保住「有紧急单要插」的故事，
    /// 又让紧迫度这一列真的能排序。
    /// </para>
    /// <para>
    /// 用配额块而不是独立概率的理由与缺口配额相同：小纵深快速验证时独立概率会整块偏掉。
    /// </para>
    /// </summary>
    private static readonly int[] ReleasedDueOffsetQuota =
    [
        -6, -3, -2, -1, // 已逾期：越早的越紧急，紧迫度列因此有梯度
        1, 2, 4, 6, 9, 13, // 未到期：从明天到两周后
    ];

    /// <summary>
    /// 取一张「已下达待开工」工单的交期（相对 <paramref name="asOfDate"/>）。
    ///
    /// 非已下达档返回 <c>null</c>——在制/完工单的交期是历史事实，由订单前置期推出，不该被改写。
    /// </summary>
    public static DateOnly? ReleasedDueDate(
        WorldHistoryExecution execution,
        int? releasedCohortOrdinal,
        DateOnly asOfDate)
    {
        if (execution != WorldHistoryExecution.ReleasedOnly ||
            releasedCohortOrdinal is not { } ordinal ||
            ordinal <= 0)
        {
            return null;
        }

        var position = ordinal - 1;
        var block = position / ReleasedDueOffsetQuotaBlockSize;
        var shuffled = ReleasedDueOffsetQuota.ToArray();
        var random = new WorldHistoryRandom($"released-due-offset-block:{block}");
        for (var cursor = shuffled.Length - 1; cursor > 0; cursor--)
        {
            var swap = random.NextInt(0, cursor + 1);
            (shuffled[cursor], shuffled[swap]) = (shuffled[swap], shuffled[cursor]);
        }

        return asOfDate.AddDays(shuffled[position % ReleasedDueOffsetQuotaBlockSize]);
    }

    /// <summary>
    /// 一张工单每项主料的齐套覆盖比例（0 = 整料没有，1 = 齐套）。
    ///
    /// <para>
    /// 档内比例是**区间随机**而不是又一个单值：真实工厂里缺料程度有分布，
    /// 「所有缺料单都缺 50%」与「所有单都齐套」是同一种死板（#1381）。
    /// </para>
    /// <para>
    /// 部分/严重两档的缺口刻意压在**采购件**（<see cref="PurchasedComponentIndex"/>，
    /// 悬架弹簧 <c>RM-SPR-##</c>）上：它没有生产版本，MRP 的 make/buy 分流在缺计划参数时
    /// 回退到「有无生产版本」，必然把它判成 <c>planned-purchase</c>——缺料才走得到
    /// 「MRP 建议采购」这一步。轻度档缺自制半成品（<c>SF-*</c>），表达「备料延迟」而不是
    /// 「要去采购」，让缺料**原因**本身也不是单值。
    /// </para>
    /// </summary>
    public static IReadOnlyList<decimal> MaterialCoverageRatios(
        string workOrderNo,
        WorldHistoryMaterialShortageTier tier,
        int componentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(componentCount);

        var ratios = new decimal[componentCount];
        Array.Fill(ratios, 1m);
        if (tier == WorldHistoryMaterialShortageTier.Kitted)
        {
            return ratios;
        }

        var random = new WorldHistoryRandom($"material-shortage:{workOrderNo}");
        var semiFinishedIndex = random.NextInt(0, Math.Min(PurchasedComponentIndex, componentCount));
        var purchasedIndex = Math.Min(PurchasedComponentIndex, componentCount - 1);

        switch (tier)
        {
            case WorldHistoryMaterialShortageTier.Minor:
                // 差 8%~22%：一箱阀系组件还在配送途中，催一下就能开工。
                ratios[semiFinishedIndex] = 1m - (random.NextInt(8, 23) / 100m);
                break;
            case WorldHistoryMaterialShortageTier.Partial:
                // 弹簧与半成品同时短 35%~60%：净需求足够大，MRP 一定摊得出采购建议。
                ratios[purchasedIndex] = 1m - (random.NextInt(35, 61) / 100m);
                ratios[semiFinishedIndex] = 1m - (random.NextInt(35, 61) / 100m);
                break;
            case WorldHistoryMaterialShortageTier.Critical:
                // 弹簧整料没有：可用与已备料同时为 0，是「必须走完整采购历程」的那一张。
                ratios[purchasedIndex] = 0m;
                ratios[semiFinishedIndex] = 1m - (random.NextInt(20, 41) / 100m);
                break;
            default:
                break;
        }

        return ratios;
    }

    /// <summary>
    /// 把覆盖量拆成「线边可用」与「已备料」两笔。
    ///
    /// <para>
    /// 修复前两笔各自等于需求量，读面上「需求 100 / 可用 100 / 已备料 100」等于把同一批料数了两遍；
    /// 缺口公式 <c>required - available - staged</c> 因此恒为 <c>-required</c>，clamp 后永远是 0，
    /// **想压低其中一笔造缺口也压不出来**。改成拆分后两笔之和恰好等于覆盖量，缺口才是真实的，
    /// 读面三列也不再全世界恒等。
    /// </para>
    /// </summary>
    public static (decimal AvailableQuantity, decimal StagedQuantity) SplitCoverage(
        string workOrderNo,
        string componentSkuCode,
        decimal requiredQuantity,
        decimal coverageRatio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentSkuCode);

        var coverage = decimal.Round(requiredQuantity * coverageRatio, 2, MidpointRounding.AwayFromZero);
        if (coverage <= 0m)
        {
            return (0m, 0m);
        }

        var random = new WorldHistoryRandom($"material-coverage:{workOrderNo}:{componentSkuCode}");
        var available = decimal.Round(coverage * (random.NextInt(35, 76) / 100m), 2, MidpointRounding.AwayFromZero);
        if (available > coverage)
        {
            available = coverage;
        }

        return (available, coverage - available);
    }

    #endregion

    #region 二期库位（与库存 / 仓储侧 WorldHistoryPhase2Spec 同字面量）

    /// <summary>原料库。</summary>
    public const string RawMaterialLocationCode = "WH-WB-RM-01";

    /// <summary>半成品库。</summary>
    public const string SemiFinishedLocationCode = "WH-WB-SF-01";

    /// <summary>车间线边库：领料后物料的去向。</summary>
    public const string LineSideLocationCode = "WH-WB-LINE-01";

    /// <summary>历史领料的真实调拨库位：来源按物料常驻库位，目标是车间线边库。</summary>
    public static MaterialTransferLocations TransferLocationsFor(string skuCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        var sourceLocationCode = skuCode.StartsWith("SF-", StringComparison.Ordinal)
            ? SemiFinishedLocationCode
            : RawMaterialLocationCode;
        return new MaterialTransferLocations(
            WorldHistorySpec.SiteCode,
            sourceLocationCode,
            WorldHistorySpec.SiteCode,
            LineSideLocationCode);
    }

    #endregion

    #region L0 §5 班组成员（报工操作员）

    /// <summary>与 L0 <c>WorldBibleSpec</c> 完全同序的姓名池——派工人姓名快照必须与员工档案逐字一致。</summary>
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

    /// <summary>L0 <c>WorldBibleSpec.BuildEmployees()</c> 的同一姓名公式（0 基员工序号）。</summary>
    public static string EmployeeName(int ordinal) =>
        $"{Surnames[ordinal % Surnames.Length]}{GivenNames[(ordinal * 7) % GivenNames.Length]}";

    /// <summary>
    /// L0 的 25 名现场班组成员：6 名班组长（EMP-004..009）+ 19 名操作工（EMP-010..028）。
    /// 车间归属按 L0 <c>WorldBibleSpec.BuildEmployees()</c> 的同一轮转公式重算。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryOperator> Operators = BuildOperators();

    private static IReadOnlyList<WorldHistoryOperator> BuildOperators()
    {
        // L0 班组顺序：MC-A、MC-B、AS-A、AS-B、SP-A、SP-B（三车间各早/中班）。
        var teamWorkshops = new[]
        {
            WorldHistoryWorkshop.Machining, WorldHistoryWorkshop.Machining,
            WorldHistoryWorkshop.Assembly, WorldHistoryWorkshop.Assembly,
            WorldHistoryWorkshop.Surface, WorldHistoryWorkshop.Surface,
        };
        var teamCodes = new[] { "TEAM-WB-MC-A", "TEAM-WB-MC-B", "TEAM-WB-AS-A", "TEAM-WB-AS-B", "TEAM-WB-SP-A", "TEAM-WB-SP-B" };
        // 与 L0 WorldBibleSpec.Teams 同名（班组名称随派工落快照，读面无需回查主数据）。
        var teamNames = new[] { "机加车间早班组", "机加车间中班组", "装配车间早班组", "装配车间中班组", "表面与包装车间早班组", "表面与包装车间中班组" };

        var operators = new List<WorldHistoryOperator>(25);

        // 6 名班组长：L0 序号 3..8（0 基），每人一个班组。
        for (var leaderIndex = 0; leaderIndex < 6; leaderIndex++)
        {
            var ordinal = 3 + leaderIndex;
            operators.Add(new WorldHistoryOperator(
                $"user-emp-{ordinal + 1:D3}",
                $"EMP-{ordinal + 1:D3}",
                EmployeeName(ordinal),
                teamCodes[leaderIndex],
                teamWorkshops[leaderIndex],
                // L0 班组顺序里偶数下标是早班、奇数是中班。
                ShiftIndex: leaderIndex % 2,
                TeamName: teamNames[leaderIndex]));
        }

        // 19 名操作工：L0 序号 9..27（0 基），前 18 人按 6 个班组轮转，第 19 人补入末组。
        for (var operatorIndex = 0; operatorIndex < 19; operatorIndex++)
        {
            var ordinal = 9 + operatorIndex;
            var teamIndex = operatorIndex < 18 ? operatorIndex % 6 : 5;
            operators.Add(new WorldHistoryOperator(
                $"user-emp-{ordinal + 1:D3}",
                $"EMP-{ordinal + 1:D3}",
                EmployeeName(ordinal),
                teamCodes[teamIndex],
                teamWorkshops[teamIndex],
                ShiftIndex: teamIndex % 2,
                TeamName: teamNames[teamIndex]));
        }

        return operators;
    }

    /// <summary>按车间取可用报工人员（班组长也报工，符合小厂实际）。</summary>
    public static IReadOnlyList<WorldHistoryOperator> OperatorsIn(WorldHistoryWorkshop workshop) =>
        [.. Operators.Where(x => x.Workshop == workshop)];

    #endregion

    #region 补产工单（设定集 §7「约 3600 张工单，含内部补产」）

    /// <summary>补产比例：3200 张订单工单 + 12.5% 补产 ≈ 3600 张（设定集 §7）。</summary>
    public const double ReworkWorkOrderRatio = 0.125;

    /// <summary>补产工单号仍在 §9 的 <c>WO-2026-*</c> 段内，用 R 前缀与订单工单区分。</summary>
    public static string ReworkWorkOrderNo(int sequence) => $"WO-2026-R{sequence:D4}";

    #endregion

    /// <summary>
    /// 单张工单的确定性形状：工序子集、投料放大量、报工次数与报废量。
    ///
    /// 关键不变量：<c>好品产出 == 订单数量</c>。投料按报废量放大（工单数量 = 订单数量 + 报废量），
    /// 于是「工单完工 → 完工入库 → 发货」的数量链逐件对得上，不会出现少发货。
    /// </summary>
    public static WorldHistoryWorkOrderPlan BuildWorkOrderPlan(string workOrderNo, string skuCode, decimal goodQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        var random = new WorldHistoryRandom($"workorder:{workOrderNo}");

        var sequences = new List<int>(8);
        foreach (var operation in StandardOperations)
        {
            var included = operation.Sequence switch
            {
                CuttingSequence => random.Chance(CuttingIncludedProbability),
                ValvePreAssemblySequence => random.Chance(ValvePreAssemblyIncludedProbability),
                _ => true,
            };
            if (included)
            {
                sequences.Add(operation.Sequence);
            }
        }

        // 约 30% 的工单有报废（设定集 §7 不合格率 2.3% 的量级），报废量为好品量的 1%–3%。
        var scrapQuantity = random.Chance(0.30)
            ? Math.Max(1m, decimal.Round(goodQuantity * (random.NextInt(1, 4) / 100m), 0, MidpointRounding.AwayFromZero))
            : 0m;

        return new WorldHistoryWorkOrderPlan(
            WorkOrderNo: workOrderNo,
            SkuCode: skuCode,
            GoodQuantity: goodQuantity,
            ScrapQuantity: scrapQuantity,
            OperationSequences: sequences,
            ReportCount: random.NextInt(2, 6),
            SplitMaterialIssue: random.Chance(0.40));
    }

    /// <summary>工序任务的计划工时：准备 + 单件工时 × 数量 + 收尾。</summary>
    public static TimeSpan OperationDuration(WorldHistoryOperation operation, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var minutes = operation.SetupMinutes + (double)quantity * operation.RunMinutesPerUnit + operation.TeardownMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    public static WorldHistoryOperation Operation(int sequence) =>
        StandardOperations.Single(x => x.Sequence == sequence);

    #region L0 §3 设备（历史派工的设备绑定）

    /// <summary>
    /// 各工序可用的设备段（设定集 §3 的 46 台设备台账）：机加→CNC/磨床、装配→装配工作站、
    /// 电泳→涂装设备、终检→试验台、包装→包装线体。下料与 CNC 精车共用 CNC 段。
    /// </summary>
    public static string DeviceAssetCode(int sequence, WorldHistoryRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var (prefix, count) = sequence switch
        {
            10 or 20 => ("DEV-CNC", 10),
            30 => ("DEV-GRD", 4),
            40 or 50 => ("DEV-ASM", 12),
            60 => ("DEV-CTG", 3),
            70 => ("DEV-TST", 4),
            80 => ("DEV-PKG", 2),
            _ => throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Unknown world-bible routing sequence."),
        };
        return $"{prefix}-{random.NextInt(1, count + 1):D2}";
    }

    #endregion

    #region 号段

    public static string OperationTaskId(string workOrderNo, int sequence) => $"{workOrderNo}-OP-{sequence:D2}";

    /// <summary>
    /// 历史工序的排程方案号：按生产周落 <c>SP-2026-W##</c> 段——与规模排产在制块（SCALE 段）
    /// 和 L2 演示排程互不侵占，派工看板能按周聚合出「哪一版计划派的工」。
    /// </summary>
    public static string SchedulePlanId(DateOnly productionDay) =>
        $"SP-{System.Globalization.ISOWeek.GetYear(productionDay.ToDateTime(TimeOnly.MinValue)):D4}-W{System.Globalization.ISOWeek.GetWeekOfYear(productionDay.ToDateTime(TimeOnly.MinValue)):D2}";
    public static string MaterialIssueRequestNo(string workOrderNo, int ordinal) => $"MIR-{workOrderNo}-{ordinal:D2}";
    public static string ProductionReportNo(string workOrderNo, int ordinal) => $"RPT-{workOrderNo}-{ordinal:D2}";
    public static string FinishedGoodsReceiptNo(string workOrderNo) => $"FGR-{workOrderNo}";
    public static string ProducedLotNo(string workOrderNo) => $"LOT-{workOrderNo}";

    #endregion
}

/// <summary>L0 §2 的三个车间，决定报工人员池与工序归属。</summary>
public enum WorldHistoryWorkshop
{
    /// <summary>一车间 · 机加车间（WS-01）。</summary>
    Machining,

    /// <summary>二车间 · 装配车间（WS-02）。</summary>
    Assembly,

    /// <summary>三车间 · 表面与包装车间（WS-03）。</summary>
    Surface,
}

public sealed record WorldHistoryOperation(
    int Sequence,
    string OperationCode,
    string OperationName,
    int SetupMinutes,
    double RunMinutesPerUnit,
    int TeardownMinutes,
    bool RequiresQualityInspection,
    WorldHistoryWorkshop Workshop);

public sealed record WorldHistoryComponent(string SkuCode, decimal QuantityPer, string UomCode);

public sealed record WorldHistoryOperator(
    string UserId,
    string EmployeeNo,
    string Name,
    string TeamCode,
    WorldHistoryWorkshop Workshop,
    int ShiftIndex,
    string TeamName);

/// <summary>
/// 「已下达待开工」工单的齐套缺口档位（#1408）。
///
/// <para>
/// #1384 把 <c>availableQuantity</c>/<c>stagedQuantity</c> 一律写满需求量，解开了开工门禁，
/// 但同时把**全世界的齐套状态压成了单值**（与 #1381 诊断的「22 个取值维度被压成单值」同类）：
/// 一张缺口都没有，于是「缺料 → MRP 采购建议 → 请购 → 采购订单 → 收货 → 入库 → 齐套转绿 → 开工」
/// 这条链在数据上**没有起点**。
/// </para>
/// </summary>
public enum WorldHistoryMaterialShortageTier
{
    /// <summary>齐套：可用 + 已备料恰好覆盖需求，逐工序开工不被物料拦（保住 #1384 的成果）。</summary>
    Kitted,

    /// <summary>轻度缺口：单项自制半成品差一小截，车间催一下配送就能开工。</summary>
    Minor,

    /// <summary>部分缺口：采购件（弹簧）与一项半成品同时短，是 MRP 采购建议的主力对象。</summary>
    Partial,

    /// <summary>严重缺口：采购件整料没有（可用与已备料同时为 0），必须走完整的采购历程。</summary>
    Critical,
}

public sealed record WorldHistoryWorkOrderPlan(
    string WorkOrderNo,
    string SkuCode,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyList<int> OperationSequences,
    int ReportCount,
    bool SplitMaterialIssue)
{
    /// <summary>工单投料数量 = 好品数量 + 报废数量。</summary>
    public decimal WorkOrderQuantity => GoodQuantity + ScrapQuantity;

    /// <summary>本工单是否包含需要质检的性能终检工序（二期质量域的引用点）。</summary>
    public bool RequiresQualityInspection =>
        OperationSequences.Contains(WorldHistoryMesSpec.QualityInspectionSequence);
}
