namespace Nerv.IIP.Contracts.Coding;

public static class StandardCodeRules
{
    private static readonly CodeRuleDefinition[] Rules =
    [
        Document("sku", "物料编码", "SKU"),
        Document("demand", "需求来源", "DEMAND"),
        Document("work-order", "生产工单", "WO"),
        Document("production-report", "生产报工单", "PRPT"),
        Document("finished-goods-receipt-request", "成品入库申请", "FGR"),
        Document("material-issue-request", "领料申请", "MIR"),
        Document("defect", "不良记录", "DEF"),
        Document("downtime-event", "停机事件", "DOWNTIME"),
        Document("shift-handover", "交接班记录", "SHO"),
        Document("opportunity", "销售商机", "OPP"),
        Document("quotation", "客户报价单", "QUO"),
        Document("sales-order", "销售订单", "SO"),
        Document("delivery-order", "发货单", "DO"),
        Document("purchase-requisition", "采购申请", "PR"),
        Document("request-for-quotation", "询价单", "RFQ"),
        Document("supplier-quotation", "供应商报价单", "SQ"),
        Document("purchase-order", "采购订单", "PO"),
        Document("purchase-receipt", "采购收货单", "GR"),
        Document("purchase-return", "采购退货单", "PRTN"),
        Document("supplier-invoice", "供应商发票", "SI"),
        Document("debit-note", "供应商扣款单", "DN"),
        Document("account-payable", "应付单", "AP"),
        Document("account-receivable", "应收单", "AR"),
        Document("sales-return-authorization", "销售退货授权单", "RMA"),
        Document("credit-note", "客户红字通知单", "CN"),
        Document("account-payable-payment", "应付付款单", "APPAY"),
        Document("account-receivable-collection", "应收收款单", "ARCOL"),
        Document("cost-candidate", "成本待定档", "COST"),
        Document("journal-voucher", "记账凭证", "JV"),
        Document("engineering-document", "工程文档", "EDOC"),
        Document("engineering-item", "工程物料", "ITEM"),
        Document("engineering-bom", "设计 BOM", "EBOM"),
        Document("manufacturing-bom", "制造 BOM", "MBOM"),
        Document("routing", "工艺路线", "RTG"),
        Document("engineering-change", "工程变更", "ECO"),
        Material(),
        SimpleResource("standard-operation", "标准工序", "OP", 4, separator: "-"),
        SimpleResource("quality-reason", "质量原因", "QR", 4, separator: "-"),
        SimpleResource("measuring-device", "计量器具", "MD", 4, separator: "-"),
        SimpleResource("maintenance-plan", "保养计划", "PM", 4, separator: "-"),
        SimpleResource("product-category", "产品分类", "PCAT", 4, separator: "-"),
        SimpleResource("skill", "技能", "SK", 4, separator: "-"),
        SimpleResource("unit-of-measure", "计量单位", "UOM", 4, separator: "-"),
        SimpleResource("site", "工厂", "ST", 3),
        SimpleResource("workshop", "车间", "WS", 3),
        SimpleResource("production-line", "生产线", "PL", 3),
        SimpleResource("shift", "班次", "SH", 2),
        SimpleResource("work-center", "工作中心", "WC", 4),
        SimpleResource("device-asset", "设备资产", "EQ", 5),
        SimpleResource("tooling-asset", "工装资产", "TOOL", 5, separator: "-"),
        BusinessPartner(),
        SimpleResource("department", "部门", "DEPT", 4, separator: "-"),
        SimpleResource("team", "班组", "TEAM", 4, separator: "-"),
        SimpleResource("worker", "员工", "EMP", 4, separator: "-"),
        SimpleResource("work-calendar", "工作日历", "CAL", 4, separator: "-"),
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
        DisplayName = "物料",
        AppliesTo = "物料/SKU",
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
        DisplayName = "业务伙伴",
        AppliesTo = "业务伙伴",
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
