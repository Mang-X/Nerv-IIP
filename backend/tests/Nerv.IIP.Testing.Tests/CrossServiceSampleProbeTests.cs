namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// 跨服务抽样探针的格式与抽样算术契约（#1826）。
///
/// 六个服务各自输出证据行、由 <c>scripts/verify-world-history.ps1</c> 对账，
/// 因此「同一个值在两侧被格式化成同一个字符串」和「六侧取到同一批序号」
/// 是这套对账成立的两个前提，这里逐条钉住。
/// </summary>
public sealed class CrossServiceSampleProbeTests
{
    [Fact]
    public void SampleIndexes_MatchesTheFullScaleVectorMeasuredAgainstRealPostgres()
    {
        // 3283 是 2026-07-26 全量 seed 的真实订单数；这一串是真机跑出来的那一串，
        // PowerShell 侧 Get-NervWorldHistorySampleIndex 必须复算出同一串。
        var indexes = CrossServiceSampleProbe.SampleIndexes(3283);

        Assert.Equal(
            "1,165,329,493,657,821,985,1150,1314,1478,1642,1806,1970,2134,2299,2463,2627,2791,2955,3119",
            string.Join(',', indexes));
    }

    [Fact]
    public void SampleIndexes_CoversEveryOrderWhenThePopulationIsSmallerThanTheSample()
    {
        Assert.Equal([1, 2, 3], CrossServiceSampleProbe.SampleIndexes(3));
    }

    [Fact]
    public void SampleIndexes_IsEmptyWithoutOrders()
    {
        Assert.Empty(CrossServiceSampleProbe.SampleIndexes(0));
    }

    [Fact]
    public void FormatBasis_EmitsTheFieldsTheReconcilerComparesAcrossServices()
    {
        var line = CrossServiceSampleProbe.FormatBasis(
            "erp-world-history",
            new DateOnly(2026, 7, 26),
            1.0d,
            3,
            [1, 2, 3]);

        Assert.Equal(
            "erp-world-history-crossdomain-basis: asOfDate=2026-07-26;scale=1;totalOrders=3;sampleSize=3;indexes=1,2,3",
            line);
    }

    [Fact]
    public void FormatRow_EmitsInvariantFixedPointValuesAndUtcTimestamps()
    {
        // 时间戳带 +08:00 偏移进来：两侧的 offset 可能不同，字符串比较前必须归一化成 UTC。
        var line = CrossServiceSampleProbe.FormatRow(
            "wms-world-history",
            new CrossServiceSampleProbeRow(
                Index: 42,
                Link: CrossServiceSampleProbe.Links.Shipment,
                Kind: "wms-delivery-outbound",
                DocumentNo: "OB-DO-2026-00042",
                Expected: true,
                Exists: true,
                Quantity: 80.5m,
                Amount: 24000.25m,
                TimestampUtc: new DateTimeOffset(2026, 1, 19, 9, 21, 0, TimeSpan.FromHours(8))));

        Assert.Equal(
            "wms-world-history-crossdomain: index=42;link=shipment;kind=wms-delivery-outbound;no=OB-DO-2026-00042;" +
            "expected=true;exists=true;quantity=80.5;amount=24000.25;timestamp=2026-01-19T01:21:00.0000000Z",
            line);
    }

    [Fact]
    public void FormatRow_MarksWitnessRowsAndAbsentMeasuresWithThePlaceholder()
    {
        var line = CrossServiceSampleProbe.FormatRow(
            "mes-world-history",
            new CrossServiceSampleProbeRow(
                Index: 7,
                Link: CrossServiceSampleProbe.Links.WorkOrder,
                Kind: "mes-shipment-witness",
                DocumentNo: "WO-2026-00007",
                Expected: false,
                Exists: null));

        Assert.Equal(
            "mes-world-history-crossdomain: index=7;link=work-order;kind=mes-shipment-witness;no=WO-2026-00007;" +
            "expected=false;exists=-;quantity=-;amount=-;timestamp=-",
            line);
    }

    [Fact]
    public async Task FormatRow_IsCultureInvariant()
    {
        // de-DE 用逗号作小数点：两侧文化不同就会把同一个数格式化成两个字符串，对账随即变成假红。
        await using var scope = await GlobalTestStateScope.CaptureAsync();
        scope.UseCulture("de-DE");

        var line = CrossServiceSampleProbe.FormatRow(
            "erp-world-history",
            new CrossServiceSampleProbeRow(1, "sales-order", "erp-sales-order", "SO-2026-00001", true, true, 80m, 24000.5m));

        Assert.Contains("quantity=80;amount=24000.5;", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("has;semicolon")]
    [InlineData("has=equals")]
    public void FormatRow_RejectsFieldValuesThatWouldCorruptTheKeyValueEncoding(string documentNo)
    {
        Assert.Throws<ArgumentException>(() => CrossServiceSampleProbe.FormatRow(
            "erp-world-history",
            new CrossServiceSampleProbeRow(1, "sales-order", "erp-sales-order", documentNo, true, true)));
    }
}
