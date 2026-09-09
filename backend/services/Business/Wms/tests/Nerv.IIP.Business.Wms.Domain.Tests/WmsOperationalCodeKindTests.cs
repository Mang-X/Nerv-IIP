using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WmsOperationalCodeKindTests
{
    /// <summary>
    /// 承载多列的单号，其上界必须落在最窄的那一列上——把它读成「自己那一列」的宽度，
    /// 正是 #3228 的缺陷本体。
    /// </summary>
    [Fact]
    public void Supplier_return_kind_takes_the_narrowest_carrying_column()
    {
        Assert.Equal(
            Math.Min(
                WmsOperationalCodeKind.SupplierReturnNoColumnMaxLength,
                WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength),
            WmsOperationalCodeKind.SupplierReturn.MaxLength);
        Assert.Equal("RTS", WmsOperationalCodeKind.SupplierReturn.Prefix);
    }

    /// <summary>
    /// 「上界放不下回落形态」在**构造种类时**就被拒绝，因此
    /// <see cref="WmsText.StableOperationalCode"/> 内部不必再留一条运行期分支。
    /// </summary>
    [Fact]
    public void Kind_whose_narrowest_column_cannot_hold_the_hashed_fallback_is_rejected_at_construction()
    {
        // "RTS-" + 64 位摘要 = 68 个字符，67 宽的承载列放不下。
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WmsOperationalCodeKind.CreateForContractProbe("RTS", 300, 67));
        Assert.Equal(68, WmsOperationalCodeKind.CreateForContractProbe("RTS", 300, 68).MaxLength);
    }

    [Fact]
    public void Kind_requires_at_least_one_carrying_column()
    {
        Assert.Throws<ArgumentException>(() => WmsOperationalCodeKind.CreateForContractProbe("RTS"));
    }

    /// <summary>
    /// 每一类已声明的单号都必须落在自己那类承载列以内。**枚举从类型系统来**：
    /// 反射拿到 <see cref="WmsOperationalCodeKind"/> 上全部静态种类，新增一类却不给上界即红。
    /// </summary>
    [Fact]
    public void Every_declared_kind_can_hold_its_own_hashed_fallback()
    {
        var kinds = typeof(WmsOperationalCodeKind)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(WmsOperationalCodeKind))
            .Select(property => (WmsOperationalCodeKind)property.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(kinds);
        Assert.All(kinds, kind => Assert.True(
            kind.MaxLength >= kind.Prefix.Length + 1 + 64,
            $"单号种类 {kind.Prefix} 的上界 {kind.MaxLength} 放不下自己的回落形态。"));
        Assert.Equal(kinds.Length, kinds.Select(kind => kind.Prefix).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 朴素拼法长度落在 <c>(outbound 列宽, supplier_return 列宽]</c> 区间时才分得清两种上界读法。
    /// </summary>
    /// <remarks>
    /// **鉴别力分布（实测，不要读成三条等强）**：把上界改回退供自己那列（300）时，
    /// 只有前两格红；第三格「各组件顶格」（朴素拼法 356）在 100 与 300 两种上界下**都**回落到
    /// 摘要形态，是一组等价输入，**从未单独承重**。它留下来只作回归护栏，
    /// 真正的鉴别力由前两格承担——别把它当独立防线。
    /// </remarks>
    [Theory]
    [InlineData(95, 1, 1)]      // 朴素拼法 101：只超出 outbound 列宽 1 个字符（唯一能分辨两种上界的区间）
    [InlineData(200, 1, 1)]     // 朴素拼法 206：仍在 supplier_return 列宽以内
    [InlineData(100, 100, 150)] // 各组件按自己列宽取满：朴素拼法 356（回归护栏，零独立鉴别力）
    public void Supplier_return_no_stays_within_the_outbound_order_no_column(int inboundLength, int lineLength, int inspectionLength)
    {
        var inboundOrderNo = new string('I', inboundLength);
        var lineNo = new string('L', lineLength);
        var inspectionRecordId = new string('Q', inspectionLength);
        var naive = $"RTS-{inboundOrderNo}-{lineNo}-{inspectionRecordId}";
        Assert.True(
            naive.Length > WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength,
            $"夹具没有越界：朴素拼法只有 {naive.Length} 字符。");

        var supplierReturnNo = SupplierReturnRequest.ComposeSupplierReturnNo(inboundOrderNo, lineNo, inspectionRecordId);

        Assert.True(
            supplierReturnNo.Length <= WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength,
            $"退供单号长度 {supplierReturnNo.Length} 超出 outbound_order_no 的 {WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength}。");
        Assert.StartsWith("RTS-", supplierReturnNo, StringComparison.Ordinal);
    }

    [Fact]
    public void Supplier_return_no_exactly_at_the_outbound_column_width_keeps_the_readable_form()
    {
        var inboundOrderNo = new string('I', WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength - 6 - 8 - 6);
        var naive = $"RTS-{inboundOrderNo}-LINE-001-QI-001";
        Assert.Equal(WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength, naive.Length);

        Assert.Equal(naive, SupplierReturnRequest.ComposeSupplierReturnNo(inboundOrderNo, "LINE-001", "QI-001"));
    }

    [Fact]
    public void Supplier_return_no_is_deterministic_across_replays()
    {
        var first = SupplierReturnRequest.ComposeSupplierReturnNo(new string('I', 95), "L", "Q");
        var replay = SupplierReturnRequest.ComposeSupplierReturnNo(new string('I', 95), "L", "Q");

        Assert.Equal(first, replay);
    }

    [Fact]
    public void Different_inspection_records_do_not_collapse_into_one_supplier_return_no()
    {
        var first = SupplierReturnRequest.ComposeSupplierReturnNo(new string('I', 95), "L", "QA");
        var second = SupplierReturnRequest.ComposeSupplierReturnNo(new string('I', 95), "L", "QB");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Short_inputs_keep_the_readable_form()
    {
        Assert.Equal(
            "RTS-IN-QA-RETURN-001-LINE-001-QI-001",
            SupplierReturnRequest.ComposeSupplierReturnNo("IN-QA-RETURN-001", "LINE-001", "QI-001"));
    }

    /// <summary>
    /// 退供聚合必须走 <see cref="SupplierReturnRequest.ComposeSupplierReturnNo"/>，
    /// 不许自己拼一遍格式串——只测构造入口而不测聚合，聚合里退回朴素拼接时这层不会红（实测）。
    /// </summary>
    [Fact]
    public void Supplier_return_aggregate_takes_its_number_from_the_bounded_composition_entry()
    {
        var inboundOrderNo = new string('I', 95);
        var request = SupplierReturnRequest.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            "LINE-001",
            "QI-001",
            "SKU-001",
            "kg",
            "SITE-01",
            "LOC-HOLD",
            null,
            null,
            "company",
            null,
            5m,
            "critical-defect");

        Assert.Equal(
            SupplierReturnRequest.ComposeSupplierReturnNo(inboundOrderNo, "LINE-001", "QI-001"),
            request.SupplierReturnNo);
        Assert.True(
            request.SupplierReturnNo.Length <= WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength,
            $"聚合上的退供单号长度 {request.SupplierReturnNo.Length} 超出 outbound_order_no 的 {WmsOperationalCodeKind.OutboundOrderNoColumnMaxLength}。");
    }

    /// <summary>缺量单号与补货任务号也各自只有一个构造入口，且受各自承载列宽约束。</summary>
    [Theory]
    [InlineData("BO")]
    [InlineData("RPL")]
    public void Backorder_and_replenishment_numbers_stay_within_their_carrying_columns(string prefix)
    {
        var outboundOrderNo = new string('O', 100);
        var lineNo = new string('L', 100);
        var composed = prefix == "BO"
            ? BackorderOrder.ComposeBackorderOrderNo(outboundOrderNo, lineNo)
            : BackorderOrder.ComposeReplenishmentTaskNo(outboundOrderNo, lineNo);
        var bound = prefix == "BO"
            ? WmsOperationalCodeKind.BackorderOrderNoColumnMaxLength
            : WmsOperationalCodeKind.WarehouseTaskNoColumnMaxLength;

        Assert.StartsWith($"{prefix}-", composed, StringComparison.Ordinal);
        Assert.True(composed.Length <= bound, $"{prefix} 单号长度 {composed.Length} 超出承载列宽 {bound}。");
    }
}
