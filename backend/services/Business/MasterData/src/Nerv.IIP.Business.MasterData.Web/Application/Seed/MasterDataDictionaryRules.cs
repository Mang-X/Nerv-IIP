namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public enum ReferenceDataCodeSetKind
{
    SystemEnum,
    PlatformPresetMaintained,
    FactoryCustom
}

public sealed record ReferenceDataDictionaryEntry(
    string CodeSet,
    string Code,
    string Name,
    ReferenceDataCodeSetKind Kind = ReferenceDataCodeSetKind.PlatformPresetMaintained);

public sealed record SkuControlledReference(string CodeSet, string Code, string Field);

public static class MasterDataDictionaryRules
{
    public static readonly IReadOnlyCollection<ReferenceDataDictionaryEntry> StandardReferenceData =
    [
        new("material-type", "raw-material", "原材料", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "semi-finished", "半成品", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "finished-goods", "成品", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "packaging", "包装物", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "consumable", "辅料消耗品", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "spare-part", "备品备件", ReferenceDataCodeSetKind.SystemEnum),
        new("material-type", "tooling", "工装刀具", ReferenceDataCodeSetKind.SystemEnum),

        new("product-category", "electronic", "电子料"),
        new("product-category", "mechanical", "机械件"),
        new("product-category", "plastic", "塑胶件"),
        new("product-category", "hardware", "五金件"),
        new("product-category", "chemical", "化学品"),
        new("product-category", "assembly", "组装件"),

        new("batch-tracking-policy", "none", "不管理", ReferenceDataCodeSetKind.SystemEnum),
        new("batch-tracking-policy", "optional", "可选记录", ReferenceDataCodeSetKind.SystemEnum),
        new("batch-tracking-policy", "mandatory", "强制批次", ReferenceDataCodeSetKind.SystemEnum),

        new("serial-tracking-policy", "none", "不管理", ReferenceDataCodeSetKind.SystemEnum),
        new("serial-tracking-policy", "on-receipt", "入库赋序", ReferenceDataCodeSetKind.SystemEnum),
        new("serial-tracking-policy", "on-production", "生产赋序", ReferenceDataCodeSetKind.SystemEnum),
        new("serial-tracking-policy", "on-shipment", "出货赋序", ReferenceDataCodeSetKind.SystemEnum),

        new("shelf-life-policy", "none", "无保质期", ReferenceDataCodeSetKind.SystemEnum),
        new("shelf-life-policy", "fifo", "先进先出", ReferenceDataCodeSetKind.SystemEnum),
        new("shelf-life-policy", "fefo", "先到期先出", ReferenceDataCodeSetKind.SystemEnum),
        new("shelf-life-policy", "expiry-controlled", "到期管控", ReferenceDataCodeSetKind.SystemEnum),

        new("storage-condition", "ambient", "常温"),
        new("storage-condition", "refrigerated", "冷藏"),
        new("storage-condition", "frozen", "冷冻"),
        new("storage-condition", "dry", "干燥防潮"),
        new("storage-condition", "esd", "防静电"),
        new("storage-condition", "hazardous", "危化品"),

        new("barcode-rule", "code128", "Code128"),
        new("barcode-rule", "ean13", "EAN-13"),
        new("barcode-rule", "gs1-128", "GS1-128"),
        new("barcode-rule", "qr", "二维码"),
        new("barcode-rule", "customer-spec", "客户指定"),

        new("uom-dimension", "count", "计数", ReferenceDataCodeSetKind.SystemEnum),
        new("uom-dimension", "length", "长度", ReferenceDataCodeSetKind.SystemEnum),
        new("uom-dimension", "area", "面积", ReferenceDataCodeSetKind.SystemEnum),
        new("uom-dimension", "volume", "体积", ReferenceDataCodeSetKind.SystemEnum),
        new("uom-dimension", "weight", "重量", ReferenceDataCodeSetKind.SystemEnum),
        new("uom-dimension", "time", "时间", ReferenceDataCodeSetKind.SystemEnum),

        new("partner-type", "customer", "客户", ReferenceDataCodeSetKind.SystemEnum),
        new("partner-type", "supplier", "供应商", ReferenceDataCodeSetKind.SystemEnum),
        new("partner-type", "carrier", "承运商", ReferenceDataCodeSetKind.SystemEnum),

        new("skill-level", "junior", "初级", ReferenceDataCodeSetKind.SystemEnum),
        new("skill-level", "intermediate", "中级", ReferenceDataCodeSetKind.SystemEnum),
        new("skill-level", "senior", "高级", ReferenceDataCodeSetKind.SystemEnum),
        new("skill-level", "expert", "专家", ReferenceDataCodeSetKind.SystemEnum),

        new("quality-reason", "scratch", "划伤", ReferenceDataCodeSetKind.FactoryCustom),
        new("quality-reason", "dimension-ng", "尺寸不良", ReferenceDataCodeSetKind.FactoryCustom),
        new("quality-reason", "missing-part", "缺件", ReferenceDataCodeSetKind.FactoryCustom),
        new("quality-reason", "solder-defect", "焊接不良", ReferenceDataCodeSetKind.FactoryCustom),

        new("compliance-tag", "rohs", "RoHS"),
        new("compliance-tag", "reach", "REACH"),
        new("compliance-tag", "msd", "湿敏元件"),
        new("compliance-tag", "ul", "UL认证"),

        new("device-status", "running", "运行", ReferenceDataCodeSetKind.SystemEnum),
        new("device-status", "idle", "待机", ReferenceDataCodeSetKind.SystemEnum),
        new("device-status", "maintenance", "保养", ReferenceDataCodeSetKind.SystemEnum),
        new("device-status", "fault", "故障", ReferenceDataCodeSetKind.SystemEnum),
        new("device-status", "scrapped", "报废", ReferenceDataCodeSetKind.SystemEnum),

        new("line-type", "flow", "流水线", ReferenceDataCodeSetKind.SystemEnum),
        new("line-type", "cell", "单元线", ReferenceDataCodeSetKind.SystemEnum),
        new("line-type", "discrete", "离散", ReferenceDataCodeSetKind.SystemEnum),

        new("work-center-type", "work-center", "工作中心", ReferenceDataCodeSetKind.SystemEnum),
        new("work-center-type", "section", "工段", ReferenceDataCodeSetKind.SystemEnum),
        new("work-center-type", "station-group", "工位组", ReferenceDataCodeSetKind.SystemEnum)
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ObsoleteSeedCodes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["batch-tracking-policy"] = new HashSet<string>(["lot"], StringComparer.Ordinal),
            ["product-category"] = new HashSet<string>(["finished-good", "raw-material", "packaging", "spare-part"], StringComparer.Ordinal),
            ["material-type"] = new HashSet<string>(["material", "service"], StringComparer.Ordinal),
            ["serial-tracking-policy"] = new HashSet<string>(["serial"], StringComparer.Ordinal),
            ["shelf-life-policy"] = new HashSet<string>(["180d", "365d"], StringComparer.Ordinal),
            ["uom-dimension"] = new HashSet<string>(["mass", "quantity"], StringComparer.Ordinal)
        };

    public static IEnumerable<SkuControlledReference> GetCreateSkuReferences(
        string category,
        string materialType,
        string batchTrackingPolicy,
        string serialTrackingPolicy,
        string shelfLifePolicyCode,
        string storageConditionCode,
        string defaultBarcodeRuleCode,
        IEnumerable<string> complianceTags)
    {
        yield return new("product-category", category, "Category");
        yield return new("material-type", materialType, "MaterialType");
        yield return new("batch-tracking-policy", batchTrackingPolicy, "BatchTrackingPolicy");
        yield return new("serial-tracking-policy", serialTrackingPolicy, "SerialTrackingPolicy");
        yield return new("shelf-life-policy", shelfLifePolicyCode, "ShelfLifePolicyCode");
        yield return new("storage-condition", storageConditionCode, "StorageConditionCode");
        yield return new("barcode-rule", defaultBarcodeRuleCode, "DefaultBarcodeRuleCode");

        foreach (var tag in complianceTags)
        {
            yield return new("compliance-tag", tag, "ComplianceTags");
        }
    }

    public static IEnumerable<SkuControlledReference> GetUpdateSkuReferences(
        string? category,
        string? materialType,
        string? batchTrackingPolicy,
        string? serialTrackingPolicy,
        string? shelfLifePolicyCode,
        string? storageConditionCode,
        string? defaultBarcodeRuleCode)
    {
        if (category is not null) yield return new("product-category", category, "Category");
        if (materialType is not null) yield return new("material-type", materialType, "MaterialType");
        if (batchTrackingPolicy is not null) yield return new("batch-tracking-policy", batchTrackingPolicy, "BatchTrackingPolicy");
        if (serialTrackingPolicy is not null) yield return new("serial-tracking-policy", serialTrackingPolicy, "SerialTrackingPolicy");
        if (shelfLifePolicyCode is not null) yield return new("shelf-life-policy", shelfLifePolicyCode, "ShelfLifePolicyCode");
        if (storageConditionCode is not null) yield return new("storage-condition", storageConditionCode, "StorageConditionCode");
        if (defaultBarcodeRuleCode is not null) yield return new("barcode-rule", defaultBarcodeRuleCode, "DefaultBarcodeRuleCode");
    }

    public static bool IsSystemManagedReferenceData(string codeSet, string code)
    {
        return StandardReferenceData.Any(x =>
            x.Kind == ReferenceDataCodeSetKind.SystemEnum &&
            string.Equals(x.CodeSet, codeSet, StringComparison.Ordinal) &&
            string.Equals(x.Code, code, StringComparison.Ordinal));
    }

    public static bool IsStandardCodeSet(string codeSet)
    {
        return StandardReferenceData.Any(x => string.Equals(x.CodeSet, codeSet, StringComparison.Ordinal));
    }

    public static bool IsSystemEnumCodeSet(string codeSet)
    {
        return StandardReferenceData.Any(x =>
            x.Kind == ReferenceDataCodeSetKind.SystemEnum &&
            string.Equals(x.CodeSet, codeSet, StringComparison.Ordinal));
    }
}
