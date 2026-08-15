using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「生产准备底座」块（设备 ↔ 工作中心映射 / SKU 停用投影）的形状与幂等性证据。
///
/// 最关键的一条断言在 <see cref="Disabled_skus_never_block_the_demo_chain"/>：
/// <c>mes_sku_availabilities</c> 是**黑名单**，写错一行就当场废掉某个 SKU 的建工单能力。
/// </summary>
public sealed class WorldHistoryFoundationSeedServiceTests
{
    private const double TestScale = 0.02d;

    /// <summary>5 个 asOfDate 边界：底座块与日期无关，但仍逐个跑一遍防止将来引入日期耦合。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new()
        {
            { 2026, 1, 5 },
            { 2026, 1, 6 },
            { 2026, 4, 15 },
            { 2026, 7, 27 },
            { 2026, 12, 31 },
        };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Foundation_seed_fills_both_tables_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);

        var report = await new WorldHistoryFoundationSeedService(dbContext).SeedAsync("org-001", "env-dev");

        Assert.Equal(WorldHistoryFoundationSpec.DeviceAssetMappings.Count, report.DeviceAssetMappingsWritten);
        Assert.Equal(WorldHistoryFoundationSpec.DisabledSkus.Count, report.DisabledSkusWritten);
        Assert.Equal(report.DeviceAssetMappingsWritten, await dbContext.DeviceAssetWorkCenterMappings.CountAsync());
        Assert.Equal(report.DisabledSkusWritten, await dbContext.MesSkuAvailabilities.CountAsync());

        Assert.Equal(report.DeviceAssetMappingsWritten, report.Validation.DeviceAssetMappingsChecked);
        Assert.Equal(report.DisabledSkusWritten, report.Validation.DisabledSkusChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Foundation_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        var seed = new WorldHistoryFoundationSeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev");
        var mappingCount = await dbContext.DeviceAssetWorkCenterMappings.CountAsync();
        var disabledCount = await dbContext.MesSkuAvailabilities.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(0, second.DeviceAssetMappingsWritten);
        Assert.Equal(0, second.DisabledSkusWritten);
        Assert.Equal(mappingCount, await dbContext.DeviceAssetWorkCenterMappings.CountAsync());
        Assert.Equal(disabledCount, await dbContext.MesSkuAvailabilities.CountAsync());
        Assert.True(first.DeviceAssetMappingsWritten > 0);
    }

    /// <summary>
    /// **演示主链安全性**：停用清单里的 SKU 一个都不能命中演示主链。
    ///
    /// <c>MesSkuAvailabilityGate</c> 是建工单的前置闸门（含急件工单与「计划建议转工单」），
    /// 命中即抛 <c>DisabledMesSkuException</c>。这条测试逐个成品与逐个用料过闸门，
    /// 任何一个被挡住都会失败。
    /// </summary>
    [Fact]
    public async Task Disabled_skus_never_block_the_demo_chain()
    {
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await new WorldHistoryFoundationSeedService(dbContext).SeedAsync("org-001", "env-dev");

        Assert.NotEmpty(await dbContext.MesSkuAvailabilities.ToArrayAsync());

        foreach (var skuCode in WorldHistorySpec.FinishedGoodSkus)
        {
            Assert.False(
                await MesSkuAvailabilityGate.IsDisabledAsync(dbContext, "org-001", "env-dev", skuCode, default),
                $"成品 {skuCode} 被停用，会挡住建工单。");

            // 用料被停用不会挡建单，但会与历史消耗自相矛盾，同样不允许。
            foreach (var component in WorldHistoryMesSpec.Components(skuCode))
            {
                Assert.False(
                    await MesSkuAvailabilityGate.IsDisabledAsync(dbContext, "org-001", "env-dev", component.SkuCode, default),
                    $"用料 {component.SkuCode} 被停用，与历史物料消耗矛盾。");
            }
        }

        // 反向证明闸门确实生效：清单里的 SKU 建单会被挡。
        foreach (var disabled in WorldHistoryFoundationSpec.DisabledSkus)
        {
            await Assert.ThrowsAsync<DisabledMesSkuException>(() =>
                MesSkuAvailabilityGate.EnsureActiveAsync(dbContext, "org-001", "env-dev", disabled.SkuCode, default));
        }
    }

    /// <summary>停用清单本身的口径：只挑非成品、非用料、非二供弹簧的原材料，且行数受控。</summary>
    [Fact]
    public void Disabled_sku_list_stays_off_the_demo_chain_by_construction()
    {
        var disabled = WorldHistoryFoundationSpec.DisabledSkus;
        Assert.InRange(disabled.Count, 1, 2);

        var finishedGoods = WorldHistorySpec.FinishedGoodSkus.ToHashSet(StringComparer.Ordinal);
        var components = WorldHistorySpec.FinishedGoodSkus
            .SelectMany(sku => WorldHistoryMesSpec.Components(sku).Select(component => component.SkuCode))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(disabled, sku =>
        {
            Assert.DoesNotContain(sku.SkuCode, finishedGoods);
            Assert.DoesNotContain(sku.SkuCode, components);
            // 工程版本演进故事（V2 换弹簧供应商）用的二供弹簧不得停用。
            Assert.NotEqual("RM-SPR-05", sku.SkuCode);
            Assert.NotEqual("RM-SPR-06", sku.SkuCode);
            Assert.StartsWith("RM-", sku.SkuCode, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(sku.DisabledReason));
        });

        Assert.Equal(disabled.Count, disabled.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>设备映射必须符合设定集 §2/§3 的车间归属，且辅助设备段刻意不映射。</summary>
    [Fact]
    public void Device_asset_mappings_follow_the_world_bible_workshop_layout()
    {
        var mappings = WorldHistoryFoundationSpec.DeviceAssetMappings;
        Assert.Equal(38, mappings.Count);
        Assert.Equal(mappings.Count, mappings.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).Count());

        Assert.All(mappings, mapping =>
        {
            Assert.Matches(@"^DEV-(CNC|GRD|ASM|WLD|CTG|TST|PKG)-\d{2}$", mapping.DeviceAssetId);
            Assert.Contains(mapping.WorkCenterId, WorldHistoryFloorEventsSpec.WorkCenterIds);
            Assert.DoesNotContain("-DEMO", mapping.DeviceAssetId, StringComparison.Ordinal);
        });

        // 辅助设备服务全厂，不绑产线——否则其压力/温度遥测会被当成产量计数。
        Assert.DoesNotContain(
            mappings,
            mapping => mapping.DeviceAssetId.StartsWith(
                WorldHistoryFoundationSpec.UnmappedAuxiliaryDevicePrefix, StringComparison.Ordinal));

        // 机加设备只挂机加工作中心，装配设备只挂装配工作中心，表面/包装同理。
        var machining = new[] { "WC-ROD-01", "WC-ROD-02", "WC-TUB-01", "WC-TUB-02", "WC-GRD-01" };
        var assembly = new[] { "WC-FA-01", "WC-FA-02", "WC-FA-03", "WC-RA-01", "WC-RA-02", "WC-VA-01" };
        var surface = new[] { "WC-CT-01", "WC-TS-01", "WC-PK-01" };

        Assert.All(
            mappings.Where(x => x.DeviceAssetId.StartsWith("DEV-CNC-", StringComparison.Ordinal)
                || x.DeviceAssetId.StartsWith("DEV-GRD-", StringComparison.Ordinal)),
            mapping => Assert.Contains(mapping.WorkCenterId, machining));
        Assert.All(
            mappings.Where(x => x.DeviceAssetId.StartsWith("DEV-ASM-", StringComparison.Ordinal)
                || x.DeviceAssetId.StartsWith("DEV-WLD-", StringComparison.Ordinal)),
            mapping => Assert.Contains(mapping.WorkCenterId, assembly));
        Assert.All(
            mappings.Where(x => x.DeviceAssetId.StartsWith("DEV-CTG-", StringComparison.Ordinal)
                || x.DeviceAssetId.StartsWith("DEV-TST-", StringComparison.Ordinal)
                || x.DeviceAssetId.StartsWith("DEV-PKG-", StringComparison.Ordinal)),
            mapping => Assert.Contains(mapping.WorkCenterId, surface));

        // 14 个生产工作中心中，除阀系/涂装/检测/包装外的产线都至少有一台设备。
        Assert.Equal(14, mappings.Select(x => x.WorkCenterId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>停用时点必须落在上线日之后的生产日上（历史时间线自洽）。</summary>
    [Fact]
    public void Disabled_at_utc_is_a_historical_working_day()
    {
        for (var index = 0; index < WorldHistoryFoundationSpec.DisabledSkus.Count; index++)
        {
            var moment = WorldHistoryFoundationSpec.DisabledAtUtc(index);
            var day = DateOnly.FromDateTime(moment.UtcDateTime);
            Assert.True(day > WorldHistoryCalendar.GoLiveDate);
            Assert.True(WorldHistoryCalendar.IsWorkingDay(day));
        }
    }

    /// <summary>停用行的状态必须是 disabled——这张表按设计只记录停用事实。</summary>
    [Fact]
    public async Task Disabled_rows_carry_the_disabled_status_and_traceable_source()
    {
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await new WorldHistoryFoundationSeedService(dbContext).SeedAsync("org-001", "env-dev");

        var rows = await dbContext.MesSkuAvailabilities.ToArrayAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(MesSkuAvailabilityStatuses.Disabled, row.Status);
            Assert.True(row.IsDisabled);
            Assert.Equal(WorldHistoryFoundationSpec.DisabledSourceEventId(row.SkuCode), row.SourceEventId);
            Assert.False(string.IsNullOrWhiteSpace(row.DisabledReason));
        });
    }
}
