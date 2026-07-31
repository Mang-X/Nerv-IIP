using System.Globalization;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史 **二期条码标签域**的确定性形状（设定集 §7：
/// 标签模板 4 套（物料 / 批次 / 工位 / 成品箱贴）、打印批次约 900、扫码记录与领料 / 入库 / 出库动作对应、
/// 时间戳与源单据一致）。
///
/// 本类型是纯函数：同一 <c>(asOfDate, scale)</c> 必得同一张模板表、打印批次表与扫码表，
/// seed 与校验器共用它，于是「写入的东西」与「校验的东西」不可能漂移。
/// 所有源单据号都来自共享形状（<see cref="WorldHistorySpec"/> / <see cref="WorldHistoryPhase2Spec"/> /
/// <see cref="WorldHistoryProcurementSpec"/>），条码域不自造任何业务事实、不跨服务查询。
///
/// <para>
/// 裁决点一 · **打印批次靠「按目标抽样」而不是「按比例抽样」**。
/// 源单据总量远大于 900（全量下完工入库约 2.9k、发货约 2.8k、采购收货约 0.43k），
/// 若用固定 1/N 抽样率，总数会随节假日曲线与规模抖动漂移，校验器只能给个宽容差。
/// 这里改成反向推：先定目标 <see cref="PrintBatchTarget"/>×scale，扣掉上线日一次性打印的工位标签，
/// 余额按 批次 45% / 箱贴 35% / 物料 20% 分配，再对各族源单据**按等距步长**取样
/// （<c>population[i * n / k]</c>）。等距步长保证抽出来的单据沿时间轴均匀分布——
/// 「一月到七月每周都有打印批次」而不是集中在某几周——同时总数是可精确预测的整数，
/// 校验器因此可以做等值断言而不是容差断言。
/// </para>
/// <para>
/// 裁决点二 · **成品箱贴走 GS1-128，其余三套走非 GS1**。
/// 箱贴是唯一会离厂、被客户端扫的标签，只有它需要 SGTIN 序列化与 EPCIS 溯源事件；
/// 领域层对 GS1 规则的约束（13 位 mod10 GTIN 根 + 公司前缀长度 6..12 且短于根）在这里能干净满足：
/// GTIN 根取 <see cref="CartonGtinRoot"/>，公司前缀长度 7。物料 / 批次 / 工位三套是厂内周转标签，
/// 用 code128 / datamatrix / qr，避免为纯内部标签白白背上 GS1 编码治理成本。
/// </para>
/// <para>
/// 裁决点三 · **每条扫码记录的条码值都由「拥有它的条码规则」现算**。
/// 规格层不复制 <c>BarcodeRule.GenerateValue</c> 的公式，只给出
/// （规则编码、源单据类型、源单据号、序号）四元组，由 seed 侧调用真实规则实例生成，
/// 校验器再用同一规则实例复算比对。于是「扫到的值是这台设备当时真能扫到的值」
/// 是一条可断言的不变量，而不是一句注释。
/// </para>
/// <para>
/// 裁决点四 · **扫码同样按目标抽样**。领料明细全量约 1.9 万条，逐条落扫码记录既没有演示价值
/// 也会让启动预算失控。目标 <see cref="ScanRecordTarget"/>×scale，按
/// 领料 40% / 完工入库 22% / 采购收货 16%（每张收货单两条：wms.receiving + inventory.receipt）/
/// 完工报工 14% / 质量检验 8% 分配，仍用等距步长取样。
/// </para>
/// </summary>
public static class WorldHistoryLabelSpec
{
    #region 目标口径（设定集 §7）

    /// <summary>设定集 §7「打印批次约 900」。</summary>
    public const int PrintBatchTarget = 900;

    /// <summary>扫码记录目标条数：领料 / 入库 / 出库动作的可演示样本量。</summary>
    public const int ScanRecordTarget = 3000;

    /// <summary>打印批次在三个单据族之间的分配（工位标签固定按工作中心数量另算）。</summary>
    public const int LotBatchSharePercent = 45;
    public const int CartonBatchSharePercent = 35;

    /// <summary>扫码记录在五个动作族之间的分配（合计 100）。</summary>
    public const int IssueScanSharePercent = 40;
    public const int FinishedGoodsScanSharePercent = 22;
    public const int PurchaseScanSharePercent = 16;
    public const int ProductionScanSharePercent = 14;
    public const int QualityScanSharePercent = 8;

    /// <summary>打印失败率：产线打印机卡纸 / 离线的真实量级。</summary>
    public const double PrintFailureRate = 0.06;

    /// <summary>已打印批次里出现「作废重打」的比例。</summary>
    public const double VoidedItemRate = 0.04;

    /// <summary>已打印批次里出现「补打一张」的比例。</summary>
    public const double ReprintedItemRate = 0.05;

    /// <summary>扫码被拒率：标签污损 / 扫错单的真实量级。</summary>
    public const double ScanRejectionRate = 0.025;

    #endregion

    #region 号段与常量

    /// <summary>成品箱贴的 GTIN 根（13 位，mod10 校验位由领域层追加）。</summary>
    public const string CartonGtinRoot = "6901234000001";

    /// <summary>GS1 公司前缀长度（6..12 且短于 13 位 GTIN 根）。</summary>
    public const int CartonCompanyPrefixLength = 7;

    /// <summary>非 GS1 规则的条码长度上限：足以容纳 <c>{前缀}{单据号去符号}{4 位序号}</c> 的最长形态。</summary>
    public const int PlainBarcodeLength = 40;

    /// <summary>GS1 箱贴的条码长度上限（AI 串含 GTIN / 批次 / 序列号）。</summary>
    public const int CartonBarcodeLength = 96;

    public const string OwnerType = "company";
    public const string UnrestrictedQualityStatus = "unrestricted";
    public const string QualityInspectionStatus = "quality";

    /// <summary>源单据类型（<c>BarcodeRule.AllowedSourceDocumentTypes</c> 与打印批次共用同一批字面量）。</summary>
    public const string PurchaseReceiptDocumentType = "purchase-receipt";
    public const string MaterialIssueDocumentType = "material-issue";
    public const string FinishedGoodsReceiptDocumentType = "finished-goods-receipt";
    public const string WorkOrderDocumentType = "work-order";
    public const string WorkCenterDocumentType = "work-center";
    public const string DeliveryOrderDocumentType = "delivery-order";

    /// <summary>本域产出的全部编码前缀，供隔离性回归测试断言不与固定演示事实 / 规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["TPL-WB-", "BR-WB-", "PB-", "SCAN-", "PRN-WB-"];

    #endregion

    #region 设备与打印机

    /// <summary>
    /// 现场扫码设备清单：车间 6 台 PDA + 收货暂存区、成品库各一台固定扫码枪。
    /// <c>ScanRecord</c> 没有「扫码人」列（只有 <c>DeviceCode</c>），因此库管的工号不落在这张表上——
    /// 人员维度由仓储域的作业任务承担（<see cref="WorldHistoryPhase2Spec.Storekeepers"/>）。
    /// </summary>
    public static readonly IReadOnlyList<string> Devices =
    [
        "PDA-WB-01", "PDA-WB-02", "PDA-WB-03", "PDA-WB-04", "PDA-WB-05", "PDA-WB-06",
        "SCN-WB-STG-01", "SCN-WB-FG-01",
    ];

    /// <summary>收货暂存区固定扫码枪。</summary>
    public const string StagingScanner = "SCN-WB-STG-01";

    /// <summary>成品库固定扫码枪。</summary>
    public const string FinishedGoodsScanner = "SCN-WB-FG-01";

    private static readonly IReadOnlyList<string> HandheldDevices =
        [.. Devices.Where(x => x.StartsWith("PDA-", StringComparison.Ordinal))];

    /// <summary>标签打印机：机加、装配、表面包装三车间各一台，仓库一台。</summary>
    public static readonly IReadOnlyList<string> Printers = ["PRN-WB-01", "PRN-WB-02", "PRN-WB-03", "PRN-WB-04"];

    #endregion

    #region 工作中心（工位标签的打印对象）

    /// <summary>
    /// 全部工作中心：按 <see cref="WorldHistoryMesSpec.WorkCenterCode"/> 对 24 个成品 × 8 道工序求并集，
    /// 而不是另抄一份清单——工艺路线一旦变，工位标签跟着变，不会留下贴在墙上的幽灵工位。
    /// </summary>
    public static readonly IReadOnlyList<string> WorkCenterCodes = BuildWorkCenterCodes();

    private static IReadOnlyList<string> BuildWorkCenterCodes() =>
        [.. WorldHistorySpec.FinishedGoodSkus
            .SelectMany(sku => WorldHistoryMesSpec.StandardOperations
                .Select(operation => WorldHistoryMesSpec.WorkCenterCode(sku, operation.Sequence)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>工作中心中文名（按编码第二段推导，与 L0 工艺路线的工序名同口径）。</summary>
    public static string WorkCenterName(string workCenterCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workCenterCode);
        var segments = workCenterCode.Split('-');
        var family = segments.Length >= 2 ? segments[1] : workCenterCode;
        var stage = family switch
        {
            "TUB" => "下料",
            "ROD" => "CNC 精车",
            "GRD" => "精磨",
            "VA" => "阀系预装",
            "FA" => "前滑柱总装",
            "RA" => "后减总装",
            "CT" => "电泳涂装",
            "TS" => "性能终检",
            "PK" => "包装",
            _ => "通用",
        };

        return $"{stage}工位 {workCenterCode}";
    }

    #endregion

    #region 模板与条码规则（4 套，设定集 §7）

    /// <summary>
    /// 四套标签模板及其配套条码规则。模板文件 id 是合成的 FileStorage 文件 id
    /// （不含 <c>objectKey</c>，否则领域层直接拒绝——模板引用的必须是文件 id 而不是对象键）。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryLabelTemplateDefinition> Templates =
    [
        new(
            Family: WorldHistoryLabelFamily.Material,
            TemplateCode: "TPL-WB-MAT-001",
            TemplateName: "物料标签（外购来料）",
            TemplateFileId: "file-wb-label-mat-001",
            VariableSchemaJson: BuildSchema(
                ("skuCode", "物料编码"),
                ("lotNo", "来料批次"),
                ("quantity", "数量"),
                ("uomCode", "计量单位"),
                ("supplierCode", "供应商编码"),
                ("sourceDocumentId", "收货单号"),
                ("printedOn", "打印日期")),
            RuleCode: "BR-WB-MAT-001",
            BarcodeType: "code128",
            Prefix: "MAT",
            Length: PlainBarcodeLength,
            ChecksumRule: "none",
            // 物料标签同时服务「采购收货贴标」与「车间领料复扫」两个动作，两类源单据都要在白名单里。
            AllowedSourceDocumentTypes: [PurchaseReceiptDocumentType, MaterialIssueDocumentType],
            Gs1CompanyPrefixLength: null),
        new(
            Family: WorldHistoryLabelFamily.Lot,
            TemplateCode: "TPL-WB-LOT-001",
            TemplateName: "批次标签（自制产出批）",
            TemplateFileId: "file-wb-label-lot-001",
            VariableSchemaJson: BuildSchema(
                ("skuCode", "成品编码"),
                ("lotNo", "产出批次"),
                ("workOrderNo", "工单号"),
                ("quantity", "入库数量"),
                ("uomCode", "计量单位"),
                ("sourceDocumentId", "完工入库单号"),
                ("printedOn", "打印日期")),
            RuleCode: "BR-WB-LOT-001",
            BarcodeType: "datamatrix",
            Prefix: "LOT",
            Length: PlainBarcodeLength,
            ChecksumRule: "none",
            AllowedSourceDocumentTypes: [FinishedGoodsReceiptDocumentType, WorkOrderDocumentType],
            Gs1CompanyPrefixLength: null),
        new(
            Family: WorldHistoryLabelFamily.Station,
            TemplateCode: "TPL-WB-STA-001",
            TemplateName: "工位标签（工作中心挂牌）",
            TemplateFileId: "file-wb-label-sta-001",
            VariableSchemaJson: BuildSchema(
                ("workCenterCode", "工作中心编码"),
                ("workCenterName", "工作中心名称"),
                ("siteCode", "厂区"),
                ("printedOn", "打印日期")),
            RuleCode: "BR-WB-STA-001",
            BarcodeType: "qr",
            Prefix: "STA",
            Length: PlainBarcodeLength,
            ChecksumRule: "none",
            AllowedSourceDocumentTypes: [WorkCenterDocumentType],
            Gs1CompanyPrefixLength: null),
        new(
            Family: WorldHistoryLabelFamily.Carton,
            TemplateCode: "TPL-WB-CTN-001",
            TemplateName: "成品箱贴（发货装箱）",
            TemplateFileId: "file-wb-label-ctn-001",
            VariableSchemaJson: BuildSchema(
                ("gtin", "GTIN"),
                ("lotNo", "产出批次"),
                ("serialPrefix", "序列号前缀"),
                ("skuCode", "成品编码"),
                ("customerCode", "客户编码"),
                ("quantity", "发货数量"),
                ("sourceDocumentId", "发货单号")),
            RuleCode: "BR-WB-CTN-001",
            BarcodeType: "gs1-128",
            Prefix: CartonGtinRoot,
            Length: CartonBarcodeLength,
            ChecksumRule: "gs1-mod10",
            AllowedSourceDocumentTypes: [DeliveryOrderDocumentType],
            Gs1CompanyPrefixLength: CartonCompanyPrefixLength),
    ];

    public static WorldHistoryLabelTemplateDefinition TemplateFor(WorldHistoryLabelFamily family) =>
        Templates.Single(x => x.Family == family);

    private static string BuildSchema(params (string Name, string Label)[] variables)
    {
        var schema = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["version"] = 1,
            ["variables"] = variables
                .Select(variable => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = variable.Name,
                    ["label"] = variable.Label,
                    ["type"] = "string",
                })
                .ToArray(),
        };

        return JsonSerializer.Serialize(schema);
    }

    #endregion

    #region 打印批次

    /// <summary>
    /// 全量打印批次，已按「创建时刻 → 幂等键」排好序。
    ///
    /// 数量算术：目标 <c>round(900 × scale)</c>，先扣掉上线日一次性打印的工位标签
    /// （每个工作中心一批，全量下 <see cref="WorkCenterCodes"/> 共 14 个），
    /// 余额按 批次 45% / 箱贴 35% / 物料 20%（余数归物料）分配，再各自等距抽样。
    /// 任何一族源单据不足时按实际数量封顶，因此极小 <c>scale</c> 下不会抛错。
    /// </summary>
    public static IReadOnlyList<WorldHistoryPrintBatchFact> BuildPrintBatchFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var orderPlans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var purchasePlans = WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale);

        var finishedGoods = workOrderFacts.Where(fact => fact.HasFinishedGoodsReceipt).ToArray();
        // #1374：装箱标签在装箱环节打印，与发运与否无关。
        var deliveries = orderPlans.Where(plan => plan.IsProductionClosed).ToArray();
        var receipts = purchasePlans.Where(plan => plan.IsReceived).ToArray();

        var budget = Math.Max(0, RoundTarget(PrintBatchTarget, scale) - WorkCenterCodes.Count);
        var lotCount = Math.Min(finishedGoods.Length, budget * LotBatchSharePercent / 100);
        var cartonCount = Math.Min(deliveries.Length, budget * CartonBatchSharePercent / 100);
        var materialCount = Math.Min(receipts.Length, budget - lotCount - cartonCount);

        var drafts = new List<WorldHistoryPrintBatchFact>(budget + WorkCenterCodes.Count);
        AppendStationBatches(drafts);
        AppendLotBatches(drafts, SampleEvenly(finishedGoods, lotCount), asOfDate);
        AppendCartonBatches(drafts, SampleEvenly(deliveries, cartonCount), asOfDate);
        AppendMaterialBatches(drafts, SampleEvenly(receipts, materialCount), asOfDate);

        return [.. drafts
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.IdempotencyKey, StringComparer.Ordinal)];
    }

    /// <summary>工位标签：上线日一次性打印挂墙，每个工作中心一批两张（设备本体 + 料架）。</summary>
    private static void AppendStationBatches(List<WorldHistoryPrintBatchFact> drafts)
    {
        var template = TemplateFor(WorldHistoryLabelFamily.Station);
        foreach (var workCenterCode in WorkCenterCodes)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workCenterCode"] = workCenterCode,
                ["workCenterName"] = WorkCenterName(workCenterCode),
                ["siteCode"] = WorldHistorySpec.SiteCode,
                ["printedOn"] = Iso(WorldHistoryCalendar.GoLiveDate),
            };
            drafts.Add(BuildBatch(
                template,
                WorkCenterDocumentType,
                workCenterCode,
                values,
                requestedQuantity: 2,
                sourceDate: WorldHistoryCalendar.GoLiveDate));
        }
    }

    /// <summary>批次标签：一张完工入库单一批，按 50 件一托打印，封顶 8 张。</summary>
    private static void AppendLotBatches(
        List<WorldHistoryPrintBatchFact> drafts,
        IReadOnlyList<WorldHistoryWorkOrderFact> facts,
        DateOnly asOfDate)
    {
        var template = TemplateFor(WorldHistoryLabelFamily.Lot);
        foreach (var fact in facts)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["skuCode"] = fact.Plan.SkuCode,
                ["lotNo"] = fact.ProducedLotNo,
                ["workOrderNo"] = fact.Plan.WorkOrderNo,
                ["quantity"] = Number(fact.Plan.GoodQuantity),
                ["uomCode"] = WorldHistorySpec.UomCode,
                ["sourceDocumentId"] = fact.FinishedGoodsReceiptNo,
                ["printedOn"] = Iso(ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate)),
            };
            drafts.Add(BuildBatch(
                template,
                FinishedGoodsReceiptDocumentType,
                fact.FinishedGoodsReceiptNo,
                values,
                requestedQuantity: PalletCount(fact.Plan.GoodQuantity, 50m, 8),
                sourceDate: ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate)));
        }
    }

    /// <summary>成品箱贴：一张发货单一批，按 40 件一箱打印，封顶 8 张。</summary>
    private static void AppendCartonBatches(
        List<WorldHistoryPrintBatchFact> drafts,
        IReadOnlyList<WorldHistoryOrderPlan> orders,
        DateOnly asOfDate)
    {
        var template = TemplateFor(WorldHistoryLabelFamily.Carton);
        foreach (var order in orders)
        {
            var deliveryOrderNo = WorldHistorySpec.DeliveryOrderNo(order.Index);
            var shipDate = ClampToHistory(WorldHistoryTimeline.For(order, asOfDate).ShipDate, asOfDate);
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gtin"] = CartonGtinRoot,
                // 箱贴的批次是这张订单对应工单的产出批，序列号前缀带上发货单号保证全库唯一 EPC。
                ["lotNo"] = WorldHistoryMesSpec.ProducedLotNo(order.WorkOrderNo),
                ["serialPrefix"] = $"{deliveryOrderNo}-",
                ["skuCode"] = order.SkuCode,
                ["customerCode"] = order.CustomerCode,
                ["quantity"] = Number(order.Quantity),
                ["sourceDocumentId"] = deliveryOrderNo,
            };
            drafts.Add(BuildBatch(
                template,
                DeliveryOrderDocumentType,
                deliveryOrderNo,
                values,
                requestedQuantity: PalletCount(order.Quantity, 40m, 8),
                sourceDate: shipDate));
        }
    }

    /// <summary>物料标签：一张采购收货单一批，按 500 单位一件打印，封顶 6 张。</summary>
    private static void AppendMaterialBatches(
        List<WorldHistoryPrintBatchFact> drafts,
        IReadOnlyList<WorldHistoryPurchasePlan> purchases,
        DateOnly asOfDate)
    {
        var template = TemplateFor(WorldHistoryLabelFamily.Material);
        foreach (var purchase in purchases)
        {
            var receiptDate = ClampToHistory(purchase.ReceiptDate, asOfDate);
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["skuCode"] = purchase.SkuCode,
                ["lotNo"] = WorldHistoryProcurementSpec.PurchasedLotNo(purchase.PurchaseOrderNo),
                ["quantity"] = Number(purchase.Quantity),
                ["uomCode"] = purchase.UomCode,
                ["supplierCode"] = purchase.SupplierCode,
                ["sourceDocumentId"] = purchase.PurchaseReceiptNo,
                ["printedOn"] = Iso(receiptDate),
            };
            drafts.Add(BuildBatch(
                template,
                PurchaseReceiptDocumentType,
                purchase.PurchaseReceiptNo,
                values,
                requestedQuantity: PalletCount(purchase.Quantity, 500m, 6),
                sourceDate: receiptDate));
        }
    }

    /// <summary>
    /// 单张打印批次的生命周期：绝大多数走完 pending → sent-to-printer → printed，
    /// <see cref="PrintFailureRate"/> 直接 pending → failed（打印机离线 / 卡纸），
    /// 已打印的批次里另有小比例出现作废重打或补打——这三种痕迹是「标签管理页面有东西可看」的来源。
    /// </summary>
    private static WorldHistoryPrintBatchFact BuildBatch(
        WorldHistoryLabelTemplateDefinition template,
        string sourceDocumentType,
        string sourceDocumentId,
        IReadOnlyDictionary<string, string> labelValues,
        int requestedQuantity,
        DateOnly sourceDate)
    {
        var idempotencyKey = WorldHistoryPhase2Spec.PrintBatchKey(sourceDocumentId, template.TemplateCode);
        var random = new WorldHistoryRandom($"print-batch:{idempotencyKey}");
        var createdAtUtc = WorldHistoryPhase2Spec.MomentOn(sourceDate, idempotencyKey, "label-print");
        var printed = !random.Chance(PrintFailureRate);
        var printerId = random.Pick(Printers);

        int? voidedSequenceNo = null;
        int? reprintedSequenceNo = null;
        if (printed)
        {
            if (random.Chance(VoidedItemRate))
            {
                voidedSequenceNo = 1;
            }
            else if (random.Chance(ReprintedItemRate))
            {
                reprintedSequenceNo = requestedQuantity;
            }
        }

        // 班内时刻加分钟不会跨 UTC 自然日：早班 UTC 00:00–07:59、中班 UTC 08:00–15:59，
        // 最多再加 23 分钟仍落在同一个（非周日的）UTC 日内，工作日与历史区间断言因此恒成立。
        var sentAtUtc = printed ? createdAtUtc.AddMinutes(random.NextInt(3, 16)) : (DateTimeOffset?)null;
        var completedAtUtc = printed
            ? sentAtUtc!.Value.AddMinutes(random.NextInt(1, 8))
            : createdAtUtc.AddMinutes(random.NextInt(2, 11));

        return new WorldHistoryPrintBatchFact(
            Family: template.Family,
            TemplateCode: template.TemplateCode,
            RuleCode: template.RuleCode,
            SourceDocumentType: sourceDocumentType,
            SourceDocumentId: sourceDocumentId,
            IdempotencyKey: idempotencyKey,
            LabelValuesJson: JsonSerializer.Serialize(labelValues),
            RequestedQuantity: requestedQuantity,
            Printed: printed,
            PrinterId: printerId,
            PrintJobId: $"JOB-{idempotencyKey}",
            FailureReason: printed ? null : "打印机离线（网络中断），批次已挂起等待重发",
            VoidedSequenceNo: voidedSequenceNo,
            VoidReason: voidedSequenceNo is null ? null : "打印偏移导致条码不可读，作废重打",
            ReprintedSequenceNo: reprintedSequenceNo,
            CreatedAtUtc: createdAtUtc,
            SentToPrinterAtUtc: sentAtUtc,
            CompletedAtUtc: completedAtUtc);
    }

    /// <summary>按「每 <paramref name="perLabel"/> 单位一张」算标签张数，至少 1 张、最多 <paramref name="cap"/> 张。</summary>
    public static int PalletCount(decimal quantity, decimal perLabel, int cap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perLabel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        var count = (int)Math.Ceiling(quantity / perLabel);
        return Math.Clamp(count, 1, cap);
    }

    #endregion

    #region 扫码记录

    /// <summary>
    /// 全量扫码记录，已按「扫码时刻 → 幂等键」排好序。
    ///
    /// 五个动作族（设定集 §7「扫码记录与领料 / 入库 / 出库动作对应」）：
    /// <list type="bullet">
    /// <item>领料出库 <c>inventory.issue</c>：一条领料明细 <c>MIR-{工单}-{序号}</c> 一条，落在线边库
    ///       （库存侧 <c>issue-in</c> 那一腿——PDA 扫的是物料到线边的确认，不是仓库拣出那一刻）；</item>
    /// <item>完工入库 <c>inventory.receipt</c>：一张完工入库单 <c>FGR-{工单}</c> 一条，落在成品库；</item>
    /// <item>采购收货 <c>wms.receiving</c> + <c>inventory.receipt</c>：一张收货单 <c>PR-2026-####</c> 两条，
    ///       落在收货暂存区、质量状态 <c>quality</c>（待检）——与库存侧 <c>receipt-in</c> 同维度；</item>
    /// <item>完工报工 <c>production.report</c>：一张已完工工单一条；</item>
    /// <item>质量检验 <c>quality.inspection</c>：有性能终检工序的工单抽样。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<WorldHistoryScanFact> BuildScanFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var workOrderFacts = WorldHistoryPhase2Spec.BuildWorkOrderFacts(asOfDate, scale);
        var purchasePlans = WorldHistoryProcurementSpec.BuildPurchasePlans(asOfDate, scale);

        var issues = workOrderFacts.SelectMany(WorldHistoryPhase2Spec.MaterialIssues).ToArray();
        var finishedGoods = workOrderFacts.Where(fact => fact.HasFinishedGoodsReceipt).ToArray();
        var inspected = workOrderFacts.Where(fact => fact.HasFinalInspection && fact.HasFinishedGoodsReceipt).ToArray();
        var receipts = purchasePlans.Where(plan => plan.IsReceived).ToArray();

        var target = RoundTarget(ScanRecordTarget, scale);
        var facts = new List<WorldHistoryScanFact>(target);

        AppendIssueScans(facts, SampleEvenly(issues, target * IssueScanSharePercent / 100), asOfDate);
        AppendFinishedGoodsScans(facts, SampleEvenly(finishedGoods, target * FinishedGoodsScanSharePercent / 100), asOfDate);
        // 采购收货族每张单据出两条扫码，因此文档数取份额的一半。
        AppendPurchaseScans(facts, SampleEvenly(receipts, target * PurchaseScanSharePercent / 200), asOfDate);
        AppendProductionScans(facts, SampleEvenly(finishedGoods, target * ProductionScanSharePercent / 100), asOfDate);
        AppendQualityScans(facts, SampleEvenly(inspected, target * QualityScanSharePercent / 100), asOfDate);

        return [.. facts
            .OrderBy(x => x.ScannedAtUtc)
            .ThenBy(x => x.IdempotencyKey, StringComparer.Ordinal)];
    }

    private static void AppendIssueScans(
        List<WorldHistoryScanFact> facts,
        IReadOnlyList<WorldHistoryMaterialIssue> issues,
        DateOnly asOfDate)
    {
        foreach (var issue in issues)
        {
            // 与库存域 issue-out / issue-in 完全同一个时刻公式，「时间戳与源单据一致」由此成立。
            var sourceMoment = WorldHistoryPhase2Spec.MomentOn(
                ClampToHistory(issue.IssueDate, asOfDate), issue.RequestNo, "stock-issue");
            facts.Add(BuildScan(
                sourceWorkflow: "inventory.issue",
                sourceDocumentId: issue.RequestNo,
                ordinal: 1,
                ruleCode: "BR-WB-MAT-001",
                valueSourceDocumentType: MaterialIssueDocumentType,
                valueSequence: 1,
                deviceSelector: DeviceSelector.Handheld,
                skuCode: issue.SkuCode,
                uomCode: issue.UomCode,
                locationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                qualityStatus: UnrestrictedQualityStatus,
                quantity: issue.Quantity,
                sourceMomentUtc: sourceMoment));
        }
    }

    private static void AppendFinishedGoodsScans(
        List<WorldHistoryScanFact> facts,
        IReadOnlyList<WorldHistoryWorkOrderFact> workOrders,
        DateOnly asOfDate)
    {
        foreach (var fact in workOrders)
        {
            var sourceMoment = WorldHistoryPhase2Spec.MomentOn(
                ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate),
                fact.FinishedGoodsReceiptNo,
                "stock-fg-receipt");
            facts.Add(BuildScan(
                sourceWorkflow: "inventory.receipt",
                sourceDocumentId: fact.FinishedGoodsReceiptNo,
                ordinal: 1,
                ruleCode: "BR-WB-LOT-001",
                valueSourceDocumentType: FinishedGoodsReceiptDocumentType,
                valueSequence: 1,
                deviceSelector: DeviceSelector.FinishedGoods,
                skuCode: fact.Plan.SkuCode,
                uomCode: WorldHistorySpec.UomCode,
                locationCode: WorldHistoryPhase2Spec.FinishedGoodsLocationCode,
                qualityStatus: UnrestrictedQualityStatus,
                quantity: fact.Plan.GoodQuantity,
                sourceMomentUtc: sourceMoment));
        }
    }

    private static void AppendPurchaseScans(
        List<WorldHistoryScanFact> facts,
        IReadOnlyList<WorldHistoryPurchasePlan> purchases,
        DateOnly asOfDate)
    {
        foreach (var purchase in purchases)
        {
            var sourceMoment = WorldHistoryPhase2Spec.MomentOn(
                ClampToHistory(purchase.ReceiptDate, asOfDate), purchase.PurchaseReceiptNo, "stock-receipt");

            // 1) 卸货时的仓储收货扫码（只观测，不驱动库存）。
            facts.Add(BuildScan(
                sourceWorkflow: "wms.receiving",
                sourceDocumentId: purchase.PurchaseReceiptNo,
                ordinal: 1,
                ruleCode: "BR-WB-MAT-001",
                valueSourceDocumentType: PurchaseReceiptDocumentType,
                valueSequence: 1,
                deviceSelector: DeviceSelector.Staging,
                skuCode: purchase.SkuCode,
                uomCode: purchase.UomCode,
                locationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                qualityStatus: null,
                quantity: purchase.Quantity,
                sourceMomentUtc: sourceMoment));

            // 2) 入暂存区的库存收货扫码，质量状态 quality（待检），与库存侧 receipt-in 同维度。
            facts.Add(BuildScan(
                sourceWorkflow: "inventory.receipt",
                sourceDocumentId: purchase.PurchaseReceiptNo,
                ordinal: 2,
                ruleCode: "BR-WB-MAT-001",
                valueSourceDocumentType: PurchaseReceiptDocumentType,
                valueSequence: 2,
                deviceSelector: DeviceSelector.Staging,
                skuCode: purchase.SkuCode,
                uomCode: purchase.UomCode,
                locationCode: WorldHistoryPhase2Spec.ReceivingStagingLocationCode,
                qualityStatus: QualityInspectionStatus,
                quantity: purchase.Quantity,
                sourceMomentUtc: sourceMoment));
        }
    }

    private static void AppendProductionScans(
        List<WorldHistoryScanFact> facts,
        IReadOnlyList<WorldHistoryWorkOrderFact> workOrders,
        DateOnly asOfDate)
    {
        foreach (var fact in workOrders)
        {
            var sourceMoment = WorldHistoryPhase2Spec.MomentOn(
                ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate),
                fact.Plan.WorkOrderNo,
                "production-report");
            facts.Add(BuildScan(
                sourceWorkflow: "production.report",
                sourceDocumentId: fact.Plan.WorkOrderNo,
                ordinal: 1,
                ruleCode: "BR-WB-LOT-001",
                valueSourceDocumentType: WorkOrderDocumentType,
                valueSequence: 1,
                deviceSelector: DeviceSelector.Handheld,
                skuCode: fact.Plan.SkuCode,
                uomCode: WorldHistorySpec.UomCode,
                locationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                qualityStatus: null,
                quantity: fact.Plan.GoodQuantity,
                sourceMomentUtc: sourceMoment));
        }
    }

    private static void AppendQualityScans(
        List<WorldHistoryScanFact> facts,
        IReadOnlyList<WorldHistoryWorkOrderFact> workOrders,
        DateOnly asOfDate)
    {
        foreach (var fact in workOrders)
        {
            var sourceMoment = WorldHistoryPhase2Spec.MomentOn(
                ClampToHistory(fact.Timeline.ProductionCompletionDate, asOfDate),
                fact.Plan.WorkOrderNo,
                "quality-inspection");
            facts.Add(BuildScan(
                sourceWorkflow: "quality.inspection",
                sourceDocumentId: fact.Plan.WorkOrderNo,
                ordinal: 2,
                ruleCode: "BR-WB-LOT-001",
                valueSourceDocumentType: WorkOrderDocumentType,
                valueSequence: 2,
                deviceSelector: DeviceSelector.Handheld,
                skuCode: fact.Plan.SkuCode,
                uomCode: WorldHistorySpec.UomCode,
                locationCode: WorldHistoryPhase2Spec.LineSideLocationCode,
                qualityStatus: null,
                quantity: fact.Plan.WorkOrderQuantity,
                sourceMomentUtc: sourceMoment));
        }
    }

    private static readonly string[] RejectionReasons =
    [
        "标签已作废，请使用重新打印的标签",
        "条码与单据物料不符",
        "该批次已在上一工位扫走",
        "标签污损，条码无法识别",
    ];

    private static WorldHistoryScanFact BuildScan(
        string sourceWorkflow,
        string sourceDocumentId,
        int ordinal,
        string ruleCode,
        string valueSourceDocumentType,
        int valueSequence,
        DeviceSelector deviceSelector,
        string skuCode,
        string uomCode,
        string locationCode,
        string? qualityStatus,
        decimal quantity,
        DateTimeOffset sourceMomentUtc)
    {
        var idempotencyKey = WorldHistoryPhase2Spec.ScanKey(sourceDocumentId, ordinal);
        var random = new WorldHistoryRandom($"scan:{idempotencyKey}");
        var rejected = random.Chance(ScanRejectionRate);
        var device = deviceSelector switch
        {
            DeviceSelector.Staging => StagingScanner,
            DeviceSelector.FinishedGoods => FinishedGoodsScanner,
            _ => random.Pick(HandheldDevices),
        };

        return new WorldHistoryScanFact(
            SourceWorkflow: sourceWorkflow,
            SourceDocumentId: sourceDocumentId,
            IdempotencyKey: idempotencyKey,
            RuleCode: ruleCode,
            ValueSourceDocumentType: valueSourceDocumentType,
            ValueSourceDocumentId: sourceDocumentId,
            ValueSequence: valueSequence,
            DeviceCode: device,
            Result: rejected ? "rejected" : "accepted",
            RejectionReason: rejected ? random.Pick(RejectionReasons) : null,
            SkuCode: skuCode,
            UomCode: uomCode,
            SiteCode: WorldHistorySpec.SiteCode,
            LocationCode: locationCode,
            QualityStatus: qualityStatus,
            OwnerType: qualityStatus is null ? null : OwnerType,
            Quantity: quantity,
            SourceMomentUtc: sourceMomentUtc,
            // 扫码总是发生在源单据动作的同一班次内、其后 0–23 分钟；见 BuildBatch 注释的跨日说明。
            ScannedAtUtc: sourceMomentUtc.AddMinutes(random.NextInt(0, 24)));
    }

    private enum DeviceSelector
    {
        Handheld,
        Staging,
        FinishedGoods,
    }

    #endregion

    #region 公共小工具

    /// <summary>
    /// 等距抽样：从 <paramref name="population"/> 里取 <paramref name="count"/> 条，
    /// 下标 <c>i × n / k</c> 保证抽样点沿原序（即时间轴）均匀分布且互不重复。
    /// </summary>
    public static IReadOnlyList<T> SampleEvenly<T>(IReadOnlyList<T> population, int count)
    {
        ArgumentNullException.ThrowIfNull(population);
        var take = Math.Min(Math.Max(count, 0), population.Count);
        if (take == 0)
        {
            return [];
        }

        var sample = new List<T>(take);
        for (var index = 0; index < take; index++)
        {
            sample.Add(population[(int)((long)index * population.Count / take)]);
        }

        return sample;
    }

    /// <summary>目标条数按 <paramref name="scale"/> 缩放并取整（至少 0）。</summary>
    public static int RoundTarget(int target, double scale) =>
        Math.Max(0, (int)Math.Round(target * scale, MidpointRounding.AwayFromZero));

    /// <summary>把候选日夹进历史区间：不晚于 <paramref name="asOfDate"/>，且回退到最近的工作日。</summary>
    public static DateOnly ClampToHistory(DateOnly candidate, DateOnly asOfDate)
    {
        var cursor = candidate > asOfDate ? asOfDate : candidate;
        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return cursor;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion
}

/// <summary>四套标签的族别（设定集 §7）。</summary>
public enum WorldHistoryLabelFamily
{
    /// <summary>物料标签（外购来料）。</summary>
    Material,

    /// <summary>批次标签（自制产出批）。</summary>
    Lot,

    /// <summary>工位标签（工作中心挂牌）。</summary>
    Station,

    /// <summary>成品箱贴（发货装箱，GS1-128 序列化）。</summary>
    Carton,
}

/// <summary>一套标签模板及其配套条码规则的定义。</summary>
public sealed record WorldHistoryLabelTemplateDefinition(
    WorldHistoryLabelFamily Family,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    IReadOnlyList<string> AllowedSourceDocumentTypes,
    int? Gs1CompanyPrefixLength)
{
    /// <summary>是否 GS1 序列化标签（只有成品箱贴是）。</summary>
    public bool IsGs1 => BarcodeType.StartsWith("gs1-", StringComparison.Ordinal);
}

/// <summary>一张历史打印批次的确定性形状。</summary>
public sealed record WorldHistoryPrintBatchFact(
    WorldHistoryLabelFamily Family,
    string TemplateCode,
    string RuleCode,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string LabelValuesJson,
    int RequestedQuantity,
    bool Printed,
    string PrinterId,
    string PrintJobId,
    string? FailureReason,
    int? VoidedSequenceNo,
    string? VoidReason,
    int? ReprintedSequenceNo,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentToPrinterAtUtc,
    DateTimeOffset CompletedAtUtc)
{
    /// <summary>终态：只有 printed / failed 两种，历史里没有挂在半路的批次。</summary>
    public string TerminalStatus => Printed ? "printed" : "failed";
}

/// <summary>一条历史扫码记录的确定性形状。</summary>
public sealed record WorldHistoryScanFact(
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey,
    string RuleCode,
    string ValueSourceDocumentType,
    string ValueSourceDocumentId,
    int ValueSequence,
    string DeviceCode,
    string Result,
    string? RejectionReason,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? QualityStatus,
    string? OwnerType,
    decimal Quantity,
    DateTimeOffset SourceMomentUtc,
    DateTimeOffset ScannedAtUtc)
{
    /// <summary>是否是驱动库存移动的扫码（领料 / 入库 / 调整）。</summary>
    public bool RequiresInventoryContext =>
        SourceWorkflow.StartsWith("inventory.", StringComparison.Ordinal);

    /// <summary>是否被接受。</summary>
    public bool IsAccepted => string.Equals(Result, "accepted", StringComparison.Ordinal);
}
