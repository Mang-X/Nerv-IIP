namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**追溯断点**形状：产出批次谱系与报工物料消耗。
///
/// 这两张表对应业务前端「追溯查询」页的三种查法（按工单 / 按批次或序列号 / 按物料批次）。
/// 表本身此前恒为 0 行，于是三种查法都只能画出「工单 → 工序 → 报工」的骨架，
/// 看不到「消耗了哪些物料批次」「产出了哪个成品批次」——追溯页最核心的两跳缺失。
///
/// 与既有 L1 块同一套约定：**确定性纯函数**，不含随机源；批次号 / 领料单号一律复用
/// <see cref="WorldHistoryMesSpec"/> 已经写进库里的号段，保证跨表（领料单 / 报工 / 谱系 / 消耗）
/// 与跨域（BarcodeLabel 打印批、Inventory 批次台账）**对得上账**，而不是各写各的。
///
/// 跨库一律不查：本规格只做确定性镜像（与 <c>WorldHistoryMesSpec</c> 同一套纯函数），
/// 真正的引用完整性由 SeedService 在**本库已落库的报工行**上绑定。
/// </summary>
public static class WorldHistoryGenealogySpec
{
    /// <summary>L1 报工号前缀——谱系与消耗只挂在这个号段的报工上（不碰 L2 / 规模块）。</summary>
    public const string ProductionReportNoPrefix = "RPT-WO-2026-";

    /// <summary>
    /// 车间领料批号：与 <see cref="WorldHistorySeedService"/> 写进 <c>material_issue_requests</c> 的
    /// 线边收料批号**逐字相同**，追溯页才能从物料批次一路回到领料单。
    /// </summary>
    public static string MaterialLotNo(string componentSkuCode, string workOrderNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentSkuCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        return $"LOT-{componentSkuCode}-{workOrderNo}";
    }

    /// <summary>
    /// 某个组件对应的领料单号。
    ///
    /// 与 <c>WriteMaterialFacts</c> 的编号规则一致：序号沿组件顺序递增，
    /// 分批领料的工单每个组件占两个序号（消耗挂在**首批**上，即奇数序号）。
    /// </summary>
    public static string MaterialIssueRequestNo(string workOrderNo, int componentIndex, bool splitMaterialIssue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
        var ordinal = splitMaterialIssue ? (componentIndex * 2) + 1 : componentIndex + 1;
        return WorldHistoryMesSpec.MaterialIssueRequestNo(workOrderNo, ordinal);
    }

    /// <summary>
    /// 一张工单的物料消耗事实：组件 × 单件用量 × 工单数量。
    ///
    /// 消耗量按**工单数量**（= 好品量 + 报废量）计——投料在开工时一次性发生，
    /// 与齐套快照 / 领料单的数量口径同源，于是「领料 = 消耗」在追溯页上自洽。
    /// </summary>
    public static IReadOnlyList<WorldHistoryMaterialConsumption> BuildConsumptions(
        string workOrderNo,
        string skuCode,
        decimal workOrderQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(skuCode);
        if (workOrderQuantity <= 0m)
        {
            return [];
        }

        // SplitMaterialIssue 只由工单号决定（随机流键就是工单号），因此这里可以在不重建
        // 完整工单计划的前提下复现领料单号——传入的 SKU / 数量只影响与编号无关的字段。
        var plan = WorldHistoryMesSpec.BuildWorkOrderPlan(workOrderNo, skuCode, workOrderQuantity);
        var components = WorldHistoryMesSpec.Components(skuCode);
        var consumptions = new List<WorldHistoryMaterialConsumption>(components.Count);

        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            consumptions.Add(new WorldHistoryMaterialConsumption(
                MaterialId: component.SkuCode,
                MaterialLotId: MaterialLotNo(component.SkuCode, workOrderNo),
                UomCode: component.UomCode,
                ConsumedQuantity: component.QuantityPer * workOrderQuantity,
                MaterialIssueRequestNo: MaterialIssueRequestNo(workOrderNo, index, plan.SplitMaterialIssue)));
        }

        return consumptions;
    }

    /// <summary>一张工单产出的成品批号（与完工入库单、条码打印批同号）。</summary>
    public static string ProducedLotNo(string workOrderNo) => WorldHistoryMesSpec.ProducedLotNo(workOrderNo);
}

/// <summary>一条历史物料消耗事实（报工引用由 SeedService 绑到真实报工行上）。</summary>
public sealed record WorldHistoryMaterialConsumption(
    string MaterialId,
    string MaterialLotId,
    string UomCode,
    decimal ConsumedQuantity,
    string MaterialIssueRequestNo);
