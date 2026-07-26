namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>
/// 领导演示「规模块」的 MasterData 侧固定形状。号段与固定演示事实完全隔离；
/// 同名规格在 ProductEngineering / MES / ERP 各自的 seed 内按同一字面量重复声明，
/// 每个服务都有针对该字面量的黄金向量测试，避免跨服务漂移。
/// </summary>
public static class LeaderDemoScaleSpec
{
    public const string SiteCode = "SITE-001";
    public const string LineCode = "LINE-SCALE-01";
    public const string LineName = "减振器规模示范线";
    public const string CalendarCode = "STANDARD";
    public const int DevicesPerWorkCenter = 6;
    public const int CapacityMinutesPerDay = 1440;
    public const string RawMaterialSkuCode = "SKU-SCALE-RM-001";
    public const string RawMaterialSkuName = "减振器筒体棒料";

    public static readonly LeaderDemoScaleWorkCenter[] WorkCenters =
    [
        new("WC-SCALE-WELD", "筒体焊接中心", "WELD", "焊接机器人 RW", "welding-robot"),
        new("WC-SCALE-ROD", "活塞杆装配中心", "ROD", "活塞杆装配台 RA", "assembly-station"),
        new("WC-SCALE-SEAL", "油封压装中心", "SEAL", "油封压装机 SP", "press-machine"),
        new("WC-SCALE-TEST", "阻尼性能检测中心", "TEST", "阻尼试验台 DT", "test-bench"),
    ];

    public static readonly LeaderDemoScaleSku[] FinishedSkus =
    [
        new("SKU-SCALE-001", "前减振器总成-A型"),
        new("SKU-SCALE-002", "前减振器总成-B型"),
        new("SKU-SCALE-003", "后减振器总成-A型"),
        new("SKU-SCALE-004", "后减振器总成-B型"),
        new("SKU-SCALE-005", "高性能减振器总成"),
        new("SKU-SCALE-006", "商用车减振器总成"),
    ];

    /// <summary>与 ERP 规模块 <c>LeaderDemoScaleSpec.CustomerCodes</c> 字面量一致。</summary>
    public static readonly LeaderDemoScaleSku[] Customers =
    [
        new("CUST-SCALE-001", "华东汽车底盘系统"),
        new("CUST-SCALE-002", "华南商用车集团"),
        new("CUST-SCALE-003", "西南新能源整车"),
        new("CUST-SCALE-004", "北方重型车辆"),
    ];

    public static string DeviceCode(LeaderDemoScaleWorkCenter workCenter, int index)
    {
        ArgumentNullException.ThrowIfNull(workCenter);
        return $"DEV-SCALE-{workCenter.Suffix}-{index:D2}";
    }
}

public sealed record LeaderDemoScaleWorkCenter(
    string Code,
    string Name,
    string Suffix,
    string DeviceModel,
    string AssetClassCode);

public sealed record LeaderDemoScaleSku(string Code, string Name);
