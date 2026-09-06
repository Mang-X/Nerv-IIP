using System.Reflection;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// #2976：Inventory 按来源环节分流时，两个桶必须**恰好**划分公开契约词表。
/// 这条契约的作用是让「Quality 新增第七个来源环节」变成一次红，而不是静默落进放行那一边——
/// 名单式实现的失败方向本来是不可见的，靠这条把它变成可见的。
///
/// **本契约只闭合「分类完备性」，对运行时的门零鉴别力。** 三条断言全部作用在静态集合形状上，
/// 没有一条执行消费者——实测把 <c>HandleValidEventAsync</c> 里的 gate 分支改成恒不进入，
/// 本契约 2/2 全绿，**整个常规（非 Postgres）Inventory 单测套件也 281/0/skip 5 全绿**，
/// 也就是说 #2976 那条 poison 缺陷可以完整回潮而常规门禁不红。这是反射式静态契约的**正确边界**，
/// 不是缺陷。
///
/// **运行时的门唯一的防线在 Postgres lane 上**：
/// <c>QualityInspectionInventoryStockGateAcceptanceTests</c> 的「被挡侧必须留痕（含 EventId）」
/// 与「放行侧每个取值都产生 2 条 status-transfer 流水」两条断言，均为
/// <c>[RealPostgresFact]</c>（environment-gated / requiredLane: postgres），**无真库时全部 skip**。
/// 改动本门时不要以本契约绿作为「门还在工作」的依据（#3186 复审）。
/// </summary>
public sealed class QualityInspectionSourceTypeGateContractTests
{
    [Fact]
    public void Contract_vocabulary_freezes_every_inspection_source_type_and_the_legacy_service_axis_value()
    {
        // 词表本身冻结：新增常量必须在这里被显式登记，顺带把「Wms 是服务轴遗留项」写死成契约，
        // 免得后来人把它当成第七个来源环节塞进分流里。
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Wms"] = "wms",
            ["Receiving"] = "receiving",
            ["Operation"] = "operation",
            ["Final"] = "final",
            ["FirstArticle"] = "first-article",
            ["Maintenance"] = "maintenance",
            ["CustomerReturn"] = "customer-return",
        };

        Assert.Equal(expected, PublicStringConstantsOf(typeof(QualityInspectionSourceTypes)));
        Assert.DoesNotContain(QualityInspectionSourceTypes.Wms, QualityInspectionSourceTypes.All);
    }

    [Fact]
    public void Inventory_gate_partitions_every_inspection_source_type_exactly_once()
    {
        var nonStockBearing = QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.NonStockBearingSourceTypes;
        var stockBearing = QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.StockBearingSourceTypes;
        var vocabulary = QualityInspectionSourceTypes.All;

        // ① 覆盖：词表里的每个取值都必须被归类，一个都不能漏。
        Assert.Equal(
            vocabulary.Order(StringComparer.Ordinal),
            nonStockBearing.Concat(stockBearing).Order(StringComparer.Ordinal));

        // ② 互斥：不能有取值同时落进两个桶。
        Assert.Empty(nonStockBearing.Intersect(stockBearing, StringComparer.OrdinalIgnoreCase));

        // ③ 本票的裁定本身也钉住：#2976 只挡首件，其余五个取值沿用今天的行为。
        Assert.Equal([QualityInspectionSourceTypes.FirstArticle], nonStockBearing.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> PublicStringConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
