namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>
/// 人工走查最小目录。所有事实均从 <see cref="WorldBibleSpec"/> 的既有编码显式投影，
/// 不按数组位置截断，避免全量设定调整时悄悄改变走查数据。
/// </summary>
public static class WalkthroughSeedSpec
{
    public const string FinishedSkuCode = "FG-QJ-P1-L";

    public static IReadOnlyList<string> SkuCodes { get; } =
    [
        FinishedSkuCode,
        "SF-ROD-01", "SF-TUB-01", "SF-VLV-01",
        "RM-SPR-01", "RM-SPR-05", "RM-SEL-01", "RM-SEL-02",
        "RM-OIL-01", "RM-OIL-02", "RM-ACC-01", "RM-ACC-02",
        "RM-ACC-04", "RM-ACC-07", "RM-BAR-01", "RM-TUB-01",
        "PK-BOX-01", "PK-BOX-02", "PK-LBL-01", "PK-LBL-03",
    ];

    public static IReadOnlyList<string> CustomerCodes { get; } =
    [
        "CUST-WB-001", "CUST-WB-002", "CUST-WB-003", "CUST-WB-004", "CUST-WB-005",
    ];

    public static IReadOnlyList<string> SupplierCodes { get; } =
    [
        "SUP-WB-BAR-01", "SUP-WB-TUB-01", "SUP-WB-SPR-02", "SUP-WB-SEL-01", "SUP-WB-OIL-01",
    ];

    public static IReadOnlyList<WorldBibleSku> Skus { get; } = Resolve(
        WorldBibleSpec.AllSkus,
        SkuCodes,
        item => item.Code,
        "SKU");

    public static IReadOnlyList<WorldBiblePartner> Customers { get; } = Resolve(
        WorldBibleSpec.Customers,
        CustomerCodes,
        item => item.Code,
        "客户");

    public static IReadOnlyList<WorldBiblePartner> Suppliers { get; } = Resolve(
        WorldBibleSpec.Suppliers,
        SupplierCodes,
        item => item.Code,
        "供应商");

    private static T[] Resolve<T>(
        IEnumerable<T> source,
        IEnumerable<string> codes,
        Func<T, string> codeSelector,
        string kind)
    {
        var byCode = source.ToDictionary(codeSelector, StringComparer.Ordinal);
        return
        [
            .. codes.Select(code => byCode.TryGetValue(code, out var value)
                ? value
                : throw new InvalidOperationException($"走查最小数据引用的{kind} '{code}' 不在工厂设定集中。")),
        ];
    }
}
