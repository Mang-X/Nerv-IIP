namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

public static class WalkthroughSeedSpec
{
    public const string SiteCode = "SITE-001";
    public const string FinishedSkuCode = "FG-QJ-P1-L";
    public const string CustomerCode = "CUST-WB-001";
    public const string SalesQuotationNo = "QUO-WALK-001";
    public const string RfqNo = "RFQ-WALK-001";
    public const decimal SalesUnitPrice = 980m;
    public static readonly DateOnly ValidUntil = new(2099, 12, 31);

    public static IReadOnlyList<WalkthroughPurchasePrice> PurchasePrices { get; } =
    [
        new("SUP-WB-BAR-01", "SQ-WALK-001", "RM-BAR-01", "kg", 1.4m, 8m),
        new("SUP-WB-TUB-01", "SQ-WALK-002", "RM-TUB-01", "kg", 1.1m, 10m),
        new("SUP-WB-SPR-02", "SQ-WALK-003", "RM-SPR-05", "pcs", 1m, 120m),
        new("SUP-WB-SEL-01", "SQ-WALK-004", "RM-SEL-01", "pcs", 2m, 15m),
        new("SUP-WB-OIL-01", "SQ-WALK-005", "RM-OIL-01", "l", 0.65m, 20m),
    ];

    public static decimal AuditablePurchaseCost => PurchasePrices.Sum(price => price.Quantity * price.UnitPrice);

    public const string MaterialSupplyRfqNo = "RFQ-WALK-002";

    // 参考询价量不是生产定额；实际采购量由公开工程需求决定。
    public static IReadOnlyList<WalkthroughPurchasePrice> MaterialSupplyPrices { get; } =
    [
        new("SUP-WALK-MET-01", "SQ-WALK-006", "SF-ROD-01", "pcs", 1m, 58m),
        new("SUP-WALK-MET-01", "SQ-WALK-007", "SF-TUB-01", "pcs", 1m, 42m),
        new("SUP-WALK-VLV-01", "SQ-WALK-008", "SF-VLV-01", "pcs", 1m, 36m),
        new("SUP-WALK-ACC-01", "SQ-WALK-009", "RM-ACC-01", "pcs", 1m, 6m),
        new("SUP-WALK-ACC-01", "SQ-WALK-010", "RM-ACC-04", "pcs", 1m, 4m),
        new("SUP-WALK-ACC-01", "SQ-WALK-011", "RM-ACC-07", "pcs", 1m, 3m),
        new("SUP-WB-PKG-01", "SQ-WALK-012", "PK-BOX-01", "pcs", 1m, 5m),
        new("SUP-WB-PKG-02", "SQ-WALK-013", "PK-LBL-03", "pcs", 1m, 0.3m),
    ];
}

public sealed record WalkthroughPurchasePrice(
    string SupplierCode,
    string QuotationNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice);
