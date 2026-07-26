namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// 领导演示「规模块」的订单分布口径。ERP 与 MES 按同一确定性公式从 1-based 序号派生 SKU、
/// 数量、交期梯度和优先级，因此第 i 张 <c>SO-SCALE-#####</c> 与第 i 张 <c>WO-SCALE-#####</c>
/// 天然一一对应。两侧各自持有黄金向量测试，防止跨服务漂移。
/// </summary>
public static class LeaderDemoScaleSpec
{
    public const string SiteCode = "SITE-001";

    public static readonly string[] FinishedSkuCodes =
    [
        "SKU-SCALE-001",
        "SKU-SCALE-002",
        "SKU-SCALE-003",
        "SKU-SCALE-004",
        "SKU-SCALE-005",
        "SKU-SCALE-006",
    ];

    public static readonly string[] CustomerCodes =
    [
        "CUST-SCALE-001",
        "CUST-SCALE-002",
        "CUST-SCALE-003",
        "CUST-SCALE-004",
    ];

    public static string SalesOrderNo(int index) => $"SO-SCALE-{index:D5}";

    public static string QuotationNo(int index) => $"QUO-SCALE-{index:D5}";

    public static string SkuCode(int index) => FinishedSkuCodes[(index - 1) % FinishedSkuCodes.Length];

    public static string CustomerCode(int index) => CustomerCodes[(index - 1) % CustomerCodes.Length];

    /// <summary>20 / 30 / 40 / 50 / 60 件循环，配合单件 1 分钟工时得到 20–60 分钟量级的工序。</summary>
    public static decimal Quantity(int index) => 20m + ((index - 1) % 5) * 10m;

    /// <summary>交期梯度：距锚定日 14–42 天（未来 2–6 周）。</summary>
    public static int DueDayOffset(int index) => 14 + ((index - 1) % 29);

    public static bool IsRush(int index) => index % 29 == 0;

    public static int Priority(int index) => IsRush(index) ? 100 : 1 + (index % 9);

    public static decimal UnitPrice(int index) => 180m + ((index - 1) % FinishedSkuCodes.Length) * 20m;
}
