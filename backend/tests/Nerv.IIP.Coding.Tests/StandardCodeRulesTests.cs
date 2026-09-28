using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Coding.Tests;

public sealed class StandardCodeRulesTests
{
    [Theory]
    [InlineData("sku", "SKU")]
    [InlineData("demand", "DEMAND")]
    [InlineData("forecast", "FC")]
    [InlineData("work-order", "WO")]
    [InlineData("production-report", "PRPT")]
    [InlineData("finished-goods-receipt-request", "FGR")]
    [InlineData("material-issue-request", "MIR")]
    [InlineData("defect", "DEF")]
    [InlineData("downtime-event", "DOWNTIME")]
    [InlineData("changeover-record", "CHANGEOVER")]
    [InlineData("shift-handover", "SHO")]
    [InlineData("opportunity", "OPP")]
    [InlineData("quotation", "QUO")]
    [InlineData("sales-order", "SO")]
    [InlineData("delivery-order", "DO")]
    [InlineData("purchase-requisition", "PR")]
    [InlineData("request-for-quotation", "RFQ")]
    [InlineData("supplier-quotation", "SQ")]
    [InlineData("purchase-order", "PO")]
    [InlineData("purchase-receipt", "GR")]
    [InlineData("account-payable", "AP")]
    [InlineData("account-receivable", "AR")]
    [InlineData("account-payable-payment", "APPAY")]
    [InlineData("account-receivable-collection", "ARCOL")]
    [InlineData("cost-candidate", "COST")]
    [InlineData("journal-voucher", "JV")]
    [InlineData("engineering-document", "EDOC")]
    [InlineData("engineering-item", "ITEM")]
    [InlineData("engineering-bom", "EBOM")]
    [InlineData("manufacturing-bom", "MBOM")]
    [InlineData("routing", "RTG")]
    [InlineData("engineering-change", "ECO")]
    [InlineData("wms-count-execution", "CNT")]
    [InlineData("inventory-stock-count-task", "SCT")]
    public void Document_rules_preserve_existing_prefixes(string ruleKey, string prefix)
    {
        var rule = StandardCodeRules.Get(ruleKey);

        Assert.Equal(prefix, rule.Segments[0].Value);
        Assert.Equal(ResetPeriod.Day, rule.Segments.Single(segment => segment.Type == SegmentType.Sequence).Reset);
        rule.Validate();
    }

    [Theory]
    [InlineData("unit-of-measure", "UOM")]
    [InlineData("site", "ST")]
    [InlineData("workshop", "WS")]
    [InlineData("production-line", "PL")]
    [InlineData("shift", "SH")]
    [InlineData("work-center", "WC")]
    [InlineData("device-asset", "EQ")]
    [InlineData("department", "DEPT")]
    [InlineData("team", "TEAM")]
    [InlineData("work-calendar", "CAL")]
    [InlineData("standard-operation", "OP")]
    [InlineData("quality-reason", "QR")]
    [InlineData("maintenance-plan", "PM")]
    public void Simple_resource_rules_are_registered(string ruleKey, string prefix)
    {
        var rule = StandardCodeRules.Get(ruleKey);

        Assert.Equal(prefix, rule.Segments[0].Value);
        Assert.Contains(rule.Segments, segment => segment.Type == SegmentType.Sequence);
        rule.Validate();
    }

    [Theory]
    [InlineData("material", "materialType")]
    [InlineData("business-partner", "partnerType")]
    public void MasterData_field_based_rules_are_registered(string ruleKey, string source)
    {
        var rule = StandardCodeRules.Get(ruleKey);

        Assert.Contains(rule.Segments, segment => segment.Type == SegmentType.Field && segment.Source == source);
        rule.Validate();
    }

    [Fact]
    public void All_rules_have_unique_keys()
    {
        var duplicates = StandardCodeRules.All
            .GroupBy(rule => rule.RuleKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// #3918：WMS 盘点把自己按 <c>wms-count-execution</c> 分到的号原样写进库存盘点任务表，
    /// 库存手工新建的盘点任务按 <c>inventory-stock-count-task</c> 在库存自己的计数器里分号。
    /// 两个计数器互不相知、同一天都从 1 数起，同序号必然同时出现，而那张表按单号唯一——
    /// 只有前缀不同才能保证两套号永不相撞。
    /// </summary>
    [Fact]
    public async Task Inventory_count_task_and_wms_count_numbers_never_collide_at_the_same_sequence()
    {
        var sameDay = new DateTimeOffset(2026, 9, 28, 1, 0, 0, TimeSpan.Zero);
        var inventoryCounter = new CodeAllocator(timeProvider: new FrozenTimeProvider(sameDay));
        var wmsCounter = new CodeAllocator(timeProvider: new FrozenTimeProvider(sameDay));

        var inventoryNumbers = new List<string>();
        var wmsNumbers = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            inventoryNumbers.Add((await inventoryCounter.AllocateAsync(
                Request("inventory-stock-count-task"),
                CancellationToken.None)).Code);
            wmsNumbers.Add((await wmsCounter.AllocateAsync(
                Request("wms-count-execution"),
                CancellationToken.None)).Code);
        }

        Assert.Equal(["SCT-20260928-000001", "SCT-20260928-000002", "SCT-20260928-000003"], inventoryNumbers);
        Assert.Equal(["CNT-20260928-000001", "CNT-20260928-000002", "CNT-20260928-000003"], wmsNumbers);
        Assert.Empty(inventoryNumbers.Intersect(wmsNumbers, StringComparer.Ordinal));

        static CodeAllocationRequest Request(string ruleKey) =>
            new("org", "env", StandardCodeRules.Get(ruleKey), null, null, null, "payload", ruleKey);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
