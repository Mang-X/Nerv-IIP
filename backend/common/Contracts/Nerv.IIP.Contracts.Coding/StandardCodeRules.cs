namespace Nerv.IIP.Contracts.Coding;

public static class StandardCodeRules
{
    private static readonly CodeRuleDefinition[] Rules =
    [
        Document("sku", "SKU", "SKU"),
        Document("demand", "Demand source", "DEMAND"),
        Document("work-order", "Work order", "WO"),
        Document("production-report", "Production report", "PRPT"),
        Document("finished-goods-receipt-request", "Finished goods receipt request", "FGR"),
        Document("material-issue-request", "Material issue request", "MIR"),
        Document("defect", "Defect", "DEF"),
        Document("downtime-event", "Downtime event", "DOWNTIME"),
        Document("shift-handover", "Shift handover", "SHO"),
        Document("opportunity", "Opportunity", "OPP"),
        Document("quotation", "Quotation", "QUO"),
        Document("sales-order", "Sales order", "SO"),
        Document("delivery-order", "Delivery order", "DO"),
        Document("purchase-requisition", "Purchase requisition", "PR"),
        Document("request-for-quotation", "Request for quotation", "RFQ"),
        Document("supplier-quotation", "Supplier quotation", "SQ"),
        Document("purchase-order", "Purchase order", "PO"),
        Document("purchase-receipt", "Purchase receipt", "GR"),
        Document("purchase-return", "Purchase return", "PRTN"),
        Document("supplier-invoice", "Supplier invoice", "SI"),
        Document("debit-note", "Supplier debit note", "DN"),
        Document("account-payable", "Account payable", "AP"),
        Document("account-receivable", "Account receivable", "AR"),
        Document("sales-return-authorization", "Sales return authorization", "RMA"),
        Document("credit-note", "Customer credit note", "CN"),
        Document("account-payable-payment", "Account payable payment", "APPAY"),
        Document("account-receivable-collection", "Account receivable collection", "ARCOL"),
        Document("cost-candidate", "Cost candidate", "COST"),
        Document("journal-voucher", "Journal voucher", "JV"),
        Document("engineering-document", "Engineering document", "EDOC"),
        Document("engineering-item", "Engineering item", "ITEM"),
        Document("engineering-bom", "Engineering BOM", "EBOM"),
        Document("manufacturing-bom", "Manufacturing BOM", "MBOM"),
        Document("routing", "Routing", "RTG"),
        Document("engineering-change", "Engineering change", "ECO"),
        Material(),
        SimpleResource("standard-operation", "Standard operation", "OP", 4, separator: "-"),
        SimpleResource("quality-reason", "Quality reason", "QR", 4, separator: "-"),
        SimpleResource("measuring-device", "Measuring device", "MD", 4, separator: "-"),
        SimpleResource("maintenance-plan", "Maintenance plan", "PM", 4, separator: "-"),
        SimpleResource("product-category", "Product category", "PCAT", 4, separator: "-"),
        SimpleResource("skill", "Skill", "SK", 4, separator: "-"),
        SimpleResource("unit-of-measure", "Unit of measure", "UOM", 4, separator: "-"),
        SimpleResource("site", "Site", "ST", 3),
        SimpleResource("workshop", "Workshop", "WS", 3),
        SimpleResource("production-line", "Production line", "PL", 3),
        SimpleResource("shift", "Shift", "SH", 2),
        SimpleResource("work-center", "Work center", "WC", 4),
        SimpleResource("device-asset", "Device asset", "EQ", 5),
        SimpleResource("tooling-asset", "Tooling asset", "TOOL", 5, separator: "-"),
        BusinessPartner(),
        SimpleResource("department", "Department", "DEPT", 4, separator: "-"),
        SimpleResource("team", "Team", "TEAM", 4, separator: "-"),
        SimpleResource("work-calendar", "Work calendar", "CAL", 4, separator: "-"),
    ];

    private static readonly IReadOnlyDictionary<string, CodeRuleDefinition> RuleByKey =
        Rules.ToDictionary(rule => rule.RuleKey, StringComparer.Ordinal);

    public static IReadOnlyList<CodeRuleDefinition> All => Rules;

    public static CodeRuleDefinition Get(string ruleKey)
    {
        return RuleByKey.TryGetValue(ruleKey, out var rule)
            ? rule
            : throw new KeyNotFoundException($"Standard code rule '{ruleKey}' is not registered.");
    }

    private static CodeRuleDefinition Document(string ruleKey, string displayName, string prefix) => new()
    {
        RuleKey = ruleKey,
        DisplayName = displayName,
        AppliesTo = displayName,
        Segments =
        [
            CodeRuleSegment.ConstantOf(prefix),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.DateOf("yyyyMMdd"),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.SequenceOf(6, ResetPeriod.Day),
        ],
    };

    private static CodeRuleDefinition Material() => new()
    {
        RuleKey = "material",
        DisplayName = "Material",
        AppliesTo = "SKU/material",
        Segments =
        [
            CodeRuleSegment.FieldOf("materialType", FieldTransform.Upper, maxLength: 3),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.SequenceOf(5),
        ],
    };

    private static CodeRuleDefinition BusinessPartner() => new()
    {
        RuleKey = "business-partner",
        DisplayName = "Business partner",
        AppliesTo = "Business partner",
        Segments =
        [
            CodeRuleSegment.FieldOf("partnerType", FieldTransform.Upper, maxLength: 4),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.SequenceOf(5),
        ],
    };

    private static CodeRuleDefinition SimpleResource(
        string ruleKey,
        string displayName,
        string prefix,
        int width,
        string separator = "") => new()
    {
        RuleKey = ruleKey,
        DisplayName = displayName,
        AppliesTo = displayName,
        Segments = string.IsNullOrEmpty(separator)
            ?
            [
                CodeRuleSegment.ConstantOf(prefix),
                CodeRuleSegment.SequenceOf(width),
            ]
            :
            [
                CodeRuleSegment.ConstantOf(prefix),
                CodeRuleSegment.ConstantOf(separator),
                CodeRuleSegment.SequenceOf(width),
            ],
    };
}
