using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WmsOperationalCodePolicyTests
{
    [Fact]
    public void Bound_is_the_minimum_of_every_carrying_column()
    {
        Assert.Equal(80, WmsOperationalCodePolicy.MaxLengthFor(120, 80, 300));
        Assert.Equal(120, WmsOperationalCodePolicy.MaxLengthFor(120));
        Assert.Throws<ArgumentException>(() => WmsOperationalCodePolicy.MaxLengthFor());
    }

    /// <summary>
    /// 朴素拼法长度落在 <c>(outbound 列宽, supplier_return 列宽]</c> 区间时才分得清两种上界读法。
    /// 直接取「各组件顶格」（朴素拼法 356）**不具备**这份鉴别力——356 在 100 与 300 两种上界下
    /// 都会回落到摘要形态，是一组等价输入。
    /// </summary>
    [Theory]
    [InlineData(95, 0, 0)]    // 朴素拼法 101：只超出 outbound 列宽 1 个字符
    [InlineData(200, 0, 0)]   // 朴素拼法 206：仍在 supplier_return 列宽以内
    [InlineData(100, 100, 150)] // 各组件按自己列宽取满：朴素拼法 356
    public void Supplier_return_no_stays_within_the_outbound_order_no_column(int inboundLength, int lineLength, int inspectionLength)
    {
        var inboundOrderNo = new string('I', inboundLength == 0 ? 1 : inboundLength);
        var lineNo = new string('L', lineLength == 0 ? 1 : lineLength);
        var inspectionRecordId = new string('Q', inspectionLength == 0 ? 1 : inspectionLength);
        var naive = $"RTS-{inboundOrderNo}-{lineNo}-{inspectionRecordId}";
        Assert.True(
            naive.Length > WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength,
            $"夹具没有越界：朴素拼法只有 {naive.Length} 字符。");

        var supplierReturnNo = SupplierReturnRequest.ComposeSupplierReturnNo(inboundOrderNo, lineNo, inspectionRecordId);

        Assert.True(
            supplierReturnNo.Length <= WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength,
            $"退供单号长度 {supplierReturnNo.Length} 超出 outbound_order_no 的 {WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength}。");
        Assert.StartsWith("RTS-", supplierReturnNo, StringComparison.Ordinal);
    }

    [Fact]
    public void Supplier_return_no_exactly_at_the_outbound_column_width_keeps_the_readable_form()
    {
        var inboundOrderNo = new string('I', WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength - 6 - 8 - 6);
        var naive = $"RTS-{inboundOrderNo}-LINE-001-QI-001";
        Assert.Equal(WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength, naive.Length);

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
    /// 不许自己拼一遍格式串——只测构造入口而不测聚合，聚合里退回朴素拼接时这层不会红（M2 实测）。
    /// </summary>
    [Fact]
    public void Aggregate_takes_its_number_from_the_bounded_composition_entry()
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
            request.SupplierReturnNo.Length <= WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength,
            $"聚合上的退供单号长度 {request.SupplierReturnNo.Length} 超出 outbound_order_no 的 {WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength}。");
    }

    [Fact]
    public void Bound_too_small_for_the_hashed_fallback_is_rejected_in_place()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WmsText.StableOperationalCode("RTS", 60, new string('I', 100), new string('L', 100)));
    }
}
