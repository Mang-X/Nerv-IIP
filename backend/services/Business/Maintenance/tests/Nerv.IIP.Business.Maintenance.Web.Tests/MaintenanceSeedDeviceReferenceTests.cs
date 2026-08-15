using Nerv.IIP.Business.Maintenance.Web.Application.Seed;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// 「引用了不存在的主数据」的 fail-closed 门禁。
///
/// 回归背景：点检保养计划 seed 硬编码了 <c>DEV-PUMP-02</c> / <c>DEV-COMP-03</c> 两台设备，
/// 而设定集 §3 的设备台账只有 CNC/GRD/ASM/WLD/CTG/TST/PKG/AUX 八类共 46 台，根本没有这两台。
/// 后果是 <c>/maintenance/plans</c> 只能显示裸编码——名录里查不到就没有中文名可显，
/// 前端「中文名 + 编码」的正确写法反而背了锅。
///
/// 这类缺陷不该等真机走查才暴露：种子引用的设备编码必须落在设备台账内，否则直接失败。
/// </summary>
public sealed class MaintenanceSeedDeviceReferenceTests
{
    [Fact]
    public void Seeded_inspection_plans_only_reference_devices_in_the_world_bible_ledger()
    {
        var ledger = WorldHistoryDeviceSpec.Devices
            .Select(x => x.DeviceAssetId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(MaintenanceSeedService.SeededDeviceAssetIds);

        var dangling = MaintenanceSeedService.SeededDeviceAssetIds
            .Where(x => !ledger.Contains(x))
            .ToArray();

        Assert.True(
            dangling.Length == 0,
            $"点检保养计划 seed 引用了设备台账里不存在的设备：{string.Join("、", dangling)}。"
                + "设备编码必须取自设定集 §3 的 46 台设备（CNC/GRD/ASM/WLD/CTG/TST/PKG/AUX）。");
    }

    /// <summary>三档周期各绑不同设备——同一台设备上挂三份计划就讲不出「日检/周检/月检」这条线。</summary>
    [Fact]
    public void Seeded_inspection_plans_spread_across_distinct_devices()
    {
        var deviceIds = MaintenanceSeedService.SeededDeviceAssetIds;
        Assert.Equal(deviceIds.Count, deviceIds.Distinct(StringComparer.Ordinal).Count());
    }
}
