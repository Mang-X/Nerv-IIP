using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Coding.Tests;

public sealed class StandardCodeRulesTests
{
    [Theory]
    [InlineData("sku", "SKU")]
    [InlineData("demand", "DEMAND")]
    [InlineData("work-order", "WO")]
    [InlineData("production-report", "PRPT")]
    [InlineData("finished-goods-receipt-request", "FGR")]
    [InlineData("material-issue-request", "MIR")]
    [InlineData("defect", "DEF")]
    [InlineData("downtime-event", "DOWNTIME")]
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
    [InlineData("cost-candidate", "COST")]
    [InlineData("journal-voucher", "JV")]
    [InlineData("engineering-document", "EDOC")]
    [InlineData("engineering-item", "ITEM")]
    [InlineData("engineering-bom", "EBOM")]
    [InlineData("manufacturing-bom", "MBOM")]
    [InlineData("routing", "RTG")]
    [InlineData("engineering-change", "ECO")]
    public void Document_rules_preserve_existing_prefixes(string ruleKey, string prefix)
    {
        var rule = StandardCodeRules.Get(ruleKey);

        Assert.Equal(prefix, rule.Segments[0].Value);
        Assert.Equal(ResetPeriod.Day, rule.Segments.Single(segment => segment.Type == SegmentType.Sequence).Reset);
        rule.Validate();
    }

    [Theory]
    [InlineData("unit-of-measure")]
    [InlineData("site")]
    [InlineData("workshop")]
    [InlineData("production-line")]
    [InlineData("shift")]
    [InlineData("work-center")]
    [InlineData("device-asset")]
    [InlineData("department")]
    [InlineData("team")]
    [InlineData("work-calendar")]
    public void MasterData_simple_resource_rules_are_registered(string ruleKey)
    {
        var rule = StandardCodeRules.Get(ruleKey);

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
}
