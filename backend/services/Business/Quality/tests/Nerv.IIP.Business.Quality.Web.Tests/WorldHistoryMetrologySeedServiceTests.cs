using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// L1 背景历史 **三期**（计量 / SPC / CAPA）的门禁测试。
///
/// 关键约束（与二期同）：任意 asOfDate 都必须成立——演示日期一改，
/// 校准状态分布、CAPA 阶段分布、时间窗都不能塌。因此规模 / 分布类断言一律走 5 日期 <c>[Theory]</c>。
/// </summary>
public sealed class WorldHistoryMetrologySeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>
    /// 库写入类用例的规模。
    ///
    /// 比二期的 0.05 大一档：CAPA 只在「报废处置」或「同物料同原因第 4 次」时才升单，
    /// 0.05 下整个历史只有个位数 NCR，一张 CAPA 都升不出来——那样的用例等于没测 CAPA。
    /// </summary>
    private const double SmallScale = 0.25d;

    /// <summary>五个演示候选日期：周日后首日 / 常规日 / 月初 / 春节段 / 月末。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new() { { 2026, 7, 27 }, { 2026, 7, 26 }, { 2026, 8, 2 }, { 2026, 2, 16 }, { 2026, 7, 31 } };

    #region 计量器具台账

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Measuring_device_ledger_keeps_its_shape_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var facts = WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(asOfDate);

        var expected = WorldHistoryMetrologySpec.DeviceCategories.Sum(x => x.Count);
        output.WriteLine($"metrology-devices={facts.Count} (expected {expected})");
        foreach (var group in facts.GroupBy(x => x.Lifecycle).OrderBy(x => x.Key))
        {
            output.WriteLine($"metrology-device-lifecycle-{group.Key}={group.Count()}");
        }

        // 台账是资产清单，不随 scale 波动：44 台，规模区间即精确值。
        Assert.Equal(44, expected);
        Assert.Equal(expected, facts.Count);
        Assert.Equal(
            WorldHistoryMetrologySpec.OverdueDeviceCount,
            facts.Count(x => x.Lifecycle == WorldHistoryMeasuringDeviceLifecycle.Overdue));
        Assert.Equal(
            WorldHistoryMetrologySpec.WarningDeviceCount,
            facts.Count(x => x.Lifecycle == WorldHistoryMeasuringDeviceLifecycle.Warning));
        Assert.Equal(
            WorldHistoryMetrologySpec.DisabledDeviceCount,
            facts.Count(x => x.Lifecycle == WorldHistoryMeasuringDeviceLifecycle.Disabled));
        Assert.Equal(
            WorldHistoryMetrologySpec.RetiredDeviceCount,
            facts.Count(x => x.Lifecycle == WorldHistoryMeasuringDeviceLifecycle.Retired));

        // 号段格式 + 中文类别名 + 唯一性。
        Assert.Equal(facts.Count, facts.Select(x => x.DeviceCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(facts, fact =>
        {
            Assert.Matches(@"^MD-[A-Z]{3}-\d{2}$", fact.DeviceCode);
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", fact.DeviceType);
            Assert.False(string.IsNullOrWhiteSpace(fact.Accuracy));
            Assert.True(fact.CalibrationIntervalDays is 180 or 365);
        });
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Calibration_records_stay_inside_the_electronic_history_window(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var goLiveUtc = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var asOfUtc = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var facts = WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(asOfDate);
        var calibrations = facts.SelectMany(x => x.Calibrations).ToArray();

        output.WriteLine($"metrology-calibrations={calibrations.Length}");

        Assert.NotEmpty(calibrations);
        Assert.Equal(
            calibrations.Length,
            calibrations.Select(x => x.CalibrationNo).Distinct(StringComparer.Ordinal).Count());
        Assert.All(calibrations, calibration =>
        {
            Assert.Matches(@"^CAL-2026-\d{4}$", calibration.CalibrationNo);
            Assert.InRange(calibration.CalibratedAtUtc, goLiveUtc, asOfUtc);
            Assert.True(
                WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(calibration.CalibratedAtUtc.UtcDateTime)),
                $"{calibration.CalibrationNo} 出具在周日。");
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", calibration.CalibrationProvider);
            Assert.Equal("合格", calibration.Conclusion);
        });

        // 每台器具的校准链严格递增，且末次校准 + 周期 = 台账上的到期日。
        foreach (var fact in facts.Where(x => x.Calibrations.Count > 0))
        {
            for (var index = 1; index < fact.Calibrations.Count; index++)
            {
                Assert.True(fact.Calibrations[index].CalibratedAtUtc > fact.Calibrations[index - 1].CalibratedAtUtc);
            }

            Assert.Equal(
                fact.CalibrationDueAtUtc,
                fact.Calibrations[^1].CalibratedAtUtc.AddDays(fact.CalibrationIntervalDays));
        }
    }

    #endregion

    #region SPC 控制图

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Spc_series_are_derived_from_real_variable_characteristics(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var series = WorldHistoryMetrologySpec.BuildSpcSeries(asOfDate, 1.0d);
        var plan = WorldHistoryQualitySpec.PlanFor("operation");

        output.WriteLine($"metrology-spc-series={series.Count}");
        foreach (var group in series.GroupBy(x => x.CharacteristicCode).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"metrology-spc-series-{group.Key}={group.Count()}");
        }

        // 2026-02-16 这类早期 asOfDate 只有 6 周历史，单个 (SKU, 特性) 根本攒不够 50 次实测，
        // 立不出图是**正确行为**而不是缺陷——绝对规模只在满纵深下断言（见下一个 Fact）。
        Assert.All(series, item =>
        {
            // 只能挂在真实存在的计量型特性上。
            var characteristic = plan.Characteristics.Single(x =>
                string.Equals(x.Code, item.CharacteristicCode, StringComparison.Ordinal));
            Assert.True(characteristic.IsVariable);
            Assert.Equal(WorldHistoryMetrologySpec.SpcWorkCenterId, item.WorkCenterId);
            Assert.Equal(WorldHistoryMetrologySpec.SpcSubgroupSize, item.SubgroupSize);
            Assert.True(item.Measurements.Count >= WorldHistoryMetrologySpec.SpcMinimumMeasurements);

            // 实测值必须落在「合格带内抖动」或「越界不合格」两种真实形态之一，不能是凭空造的数。
            var nominal = characteristic.NominalValue!.Value;
            var upper = characteristic.UpperSpecLimit!.Value;
            var defectValue = decimal.Round(upper * 1.02m, 2);
            Assert.All(item.Measurements, measurement =>
                Assert.True(
                    measurement.MeasuredValue == defectValue
                    || (measurement.MeasuredValue >= nominal - ((upper - nominal) * 0.8m)
                        && measurement.MeasuredValue <= nominal + ((upper - nominal) * 0.8m)),
                    $"{item.SkuCode}/{item.CharacteristicCode} 出现了既不在公差带内也不是不合格值的实测值。"));
        });

        // 立得出图的话，两条计量型特性必须成对出现（同一 SKU 的实测次数对两条特性完全相同）。
        Assert.True(
            series.Count == 0
            || series.Select(x => x.CharacteristicCode).Distinct(StringComparer.Ordinal).Count()
                == WorldHistoryMetrologySpec.SpcCharacteristicCodes.Count,
            "立出了图却只覆盖一条计量型特性，SPC 页会缺一半内容。");
    }

    /// <summary>满纵深（约 29 周）下的绝对规模：24 个成品 × 2 条计量型特性，扣掉样本不够的。</summary>
    [Fact]
    public void Spc_series_reach_demo_scale_at_full_history_depth()
    {
        var series = WorldHistoryMetrologySpec.BuildSpcSeries(AsOfDate, 1.0d);
        output.WriteLine($"metrology-spc-series-full={series.Count}");

        Assert.InRange(series.Count, 20, 60);
    }

    #endregion

    #region CAPA

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Capa_stream_only_escalates_major_or_repeated_nonconformances(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var ncrs = WorldHistoryQualitySpec.BuildInspectionFacts(asOfDate, 1.0d)
            .Where(x => x.HasNonconformance)
            .ToArray();
        var capas = WorldHistoryMetrologySpec.BuildCapaFacts(asOfDate, 1.0d);

        output.WriteLine($"metrology-ncrs={ncrs.Length}");
        output.WriteLine($"metrology-capas={capas.Count}");
        foreach (var group in capas.GroupBy(x => x.Stage).OrderBy(x => x.Key))
        {
            output.WriteLine($"metrology-capa-stage-{group.Key}={group.Count()}");
        }

        output.WriteLine($"metrology-capa-items={capas.Sum(x => x.Actions.Count)}");

        // 只有重大 / 重复才升 CAPA：规模必须远小于 NCR 总数。这条对任意 asOfDate 都成立。
        Assert.True(
            capas.Count <= ncrs.Length / 3,
            $"CAPA 数 {capas.Count} 相对 NCR 数 {ncrs.Length} 太多，「重大/重复才开单」的口径失效。");

        // 号段格式与唯一性。
        Assert.Equal(capas.Count, capas.Select(x => x.CapaCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(capas, capa =>
        {
            Assert.Matches(@"^CAPA-2026-\d{3}$", capa.CapaCode);
            Assert.StartsWith("NCR-2026-", capa.NcrCode, StringComparison.Ordinal);
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", capa.RootCause);
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", capa.ContainmentAction);
            Assert.Contains(capa.Trigger, new[] { "重大", "重复" });
        });

        // 每张 CAPA 的措施集合必须是 8D 形状：临时 + 纠正 + 预防齐全。
        Assert.All(capas, capa =>
        {
            Assert.InRange(capa.Actions.Count, 3, 4);
            Assert.Contains(capa.Actions, x => x.ActionType == "containment");
            Assert.Contains(capa.Actions, x => x.ActionType == "corrective");
            Assert.Contains(capa.Actions, x => x.ActionType == "preventive");
            Assert.All(capa.Actions, action => Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", action.Description));
        });
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Capa_stage_distribution_stays_demo_shaped_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var capas = WorldHistoryMetrologySpec.BuildCapaFacts(asOfDate, 1.0d);
        var closed = capas.Count(x => x.Stage == WorldHistoryCapaStage.Closed);
        var overdue = capas.Count(x => x.Stage == WorldHistoryCapaStage.Overdue);

        output.WriteLine($"metrology-capa-closed={closed}/{capas.Count}");
        output.WriteLine($"metrology-capa-overdue={overdue}/{capas.Count}");

        // 分布类断言需要足够样本：早期 asOfDate（如春节段只有 6 周历史）连 10 张 CAPA 都攒不出，
        // 那时谈「多数已关闭」没有意义。满纵深下的绝对分布见下一个 Fact。
        if (capas.Count >= 10)
        {
            Assert.True(closed * 2 > capas.Count, $"已关闭 CAPA 只有 {closed}/{capas.Count}，不是「多数已关闭」。");
            Assert.True(overdue * 4 < capas.Count, $"逾期 CAPA {overdue}/{capas.Count} 太多，不像一家在管的工厂。");
        }

        // 已关闭的必须走完效果验证；进行中的不得凭空带出关单人。
        Assert.All(capas, capa =>
        {
            if (capa.Stage == WorldHistoryCapaStage.Closed)
            {
                Assert.NotNull(capa.EffectivenessVerifiedAtUtc);
                Assert.NotNull(capa.ClosedAtUtc);
                Assert.True(capa.ClosedAtUtc > capa.EffectivenessVerifiedAtUtc);
                Assert.All(capa.Actions, action => Assert.NotNull(action.CompletedAtUtc));
            }
            else
            {
                Assert.Null(capa.ClosedAtUtc);
                Assert.Null(capa.ClosedByUserId);
            }

            if (capa.Stage == WorldHistoryCapaStage.Overdue)
            {
                // 逾期 = 到期日已过且没关单，卡在最后一步。
                Assert.Contains(capa.Actions, action => action.CompletedAtUtc is null);
            }
        });
    }

    /// <summary>满纵深下的 CAPA 绝对规模与阶段分布：十几到几十张，多数已关闭，个别逾期。</summary>
    [Fact]
    public void Capa_reaches_demo_scale_and_stage_mix_at_full_history_depth()
    {
        var capas = WorldHistoryMetrologySpec.BuildCapaFacts(AsOfDate, 1.0d);
        var closed = capas.Count(x => x.Stage == WorldHistoryCapaStage.Closed);
        var overdue = capas.Count(x => x.Stage == WorldHistoryCapaStage.Overdue);
        var open = capas.Count(x => x.Stage == WorldHistoryCapaStage.Open);
        var verified = capas.Count(x => x.Stage == WorldHistoryCapaStage.EffectivenessVerified);

        output.WriteLine($"metrology-capa-full={capas.Count} closed={closed} verified={verified} open={open} overdue={overdue}");
        output.WriteLine($"metrology-capa-items-full={capas.Sum(x => x.Actions.Count)}");

        Assert.InRange(capas.Count, 10, 80);
        Assert.True(closed * 2 > capas.Count, $"已关闭 CAPA 只有 {closed}/{capas.Count}，不是「多数已关闭」。");
        Assert.True(overdue >= 1, "一张逾期 CAPA 都没有，管理抓手这条演示线讲不出来。");
        Assert.True(overdue * 4 < capas.Count, $"逾期 CAPA {overdue}/{capas.Count} 太多，不像一家在管的工厂。");
    }

    #endregion

    #region 确定性 / 幂等 / 落库

    [Fact]
    public void Fact_streams_are_deterministic_for_the_same_inputs()
    {
        // 事实 record 里挂的是 List<>，结构相等不会逐元素比——渲染成串再比，才真的比到了内容。
        static string RenderDevices(DateOnly asOfDate) => string.Join(
            "\n",
            WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(asOfDate).Select(fact => (
                $"{fact.DeviceCode}|{fact.DeviceType}|{fact.Lifecycle}|{fact.CalibrationDueAtUtc:O}|"
                + $"{string.Join(",", fact.Calibrations.Select(x => $"{x.CalibrationNo}@{x.CalibratedAtUtc:O}"))}")));

        static string RenderCapas(DateOnly asOfDate, double scale) => string.Join(
            "\n",
            WorldHistoryMetrologySpec.BuildCapaFacts(asOfDate, scale).Select(fact => (
                $"{fact.CapaCode}|{fact.NcrCode}|{fact.Stage}|{fact.OpenedAtUtc:O}|"
                + $"{string.Join(",", fact.Actions.Select(x => $"{x.ActionType}@{x.DueAtUtc:O}"))}")));

        static string RenderSeries(DateOnly asOfDate, double scale) => string.Join(
            "\n",
            WorldHistoryMetrologySpec.BuildSpcSeries(asOfDate, scale).Select(series => (
                $"{series.SkuCode}|{series.CharacteristicCode}|{series.Measurements.Count}|"
                + $"{series.Measurements.Sum(x => x.MeasuredValue)}")));

        Assert.Equal(RenderDevices(AsOfDate), RenderDevices(AsOfDate));
        Assert.Equal(RenderCapas(AsOfDate, 0.2d), RenderCapas(AsOfDate, 0.2d));
        Assert.Equal(RenderSeries(AsOfDate, 0.5d), RenderSeries(AsOfDate, 0.5d));
    }

    [Fact]
    public async Task Seed_writes_the_ledger_and_reruns_without_writing_anything()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        db.ChangeTracker.Clear();

        var seed = new WorldHistoryMetrologySeedService(db);
        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        output.WriteLine($"small-scale-devices={first.MeasuringDevicesWritten}");
        output.WriteLine($"small-scale-calibrations={first.CalibrationRecordsWritten}");
        output.WriteLine($"small-scale-charts={first.SpcControlChartsWritten}");
        output.WriteLine($"small-scale-capas={first.CorrectiveActionsWritten}");
        output.WriteLine($"small-scale-capa-items={first.CorrectiveActionItemsWritten}");

        var expectedDevices = WorldHistoryMetrologySpec.BuildMeasuringDeviceFacts(AsOfDate);
        Assert.Equal(expectedDevices.Count, first.MeasuringDevicesWritten);
        Assert.Equal(expectedDevices.Sum(x => x.Calibrations.Count), first.CalibrationRecordsWritten);
        Assert.True(first.CorrectiveActionsWritten > 0, "小规模下也应至少升出一张 CAPA。");

        // 每张 CAPA 3–4 条措施（临时 / 纠正 / 预防，约四成多一条培训类纠正）。
        Assert.InRange(
            first.CorrectiveActionItemsWritten,
            first.CorrectiveActionsWritten * 3,
            first.CorrectiveActionsWritten * 4);

        // 重跑：写入量全 0，终态不变。
        Assert.Equal(0, second.MeasuringDevicesWritten);
        Assert.Equal(0, second.CalibrationRecordsWritten);
        Assert.Equal(0, second.SpcControlChartsWritten);
        Assert.Equal(0, second.CorrectiveActionsWritten);
        Assert.Equal(0, second.CorrectiveActionItemsWritten);

        Assert.Equal(expectedDevices.Count, await db.MeasuringDevices.CountAsync());
        Assert.Equal(first.CalibrationRecordsWritten, await db.CalibrationRecords.CountAsync());
        Assert.Equal(first.CorrectiveActionsWritten, await db.CorrectiveActions.CountAsync());
    }

    [Fact]
    public async Task Seeded_capas_reference_real_ncrs_and_real_verification_records()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        db.ChangeTracker.Clear();
        await new WorldHistoryMetrologySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var ncrIds = (await db.NonconformanceReports.Select(x => x.Id).ToArrayAsync())
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var recordIds = (await db.InspectionRecords.Select(x => x.Id).ToArrayAsync())
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var capas = await db.CorrectiveActions.Include(x => x.Actions).ToArrayAsync();

        Assert.NotEmpty(capas);
        Assert.All(capas, capa =>
        {
            Assert.NotNull(capa.SourceNcrId);
            Assert.Contains(capa.SourceNcrId!, ncrIds);
            Assert.NotEmpty(capa.Actions);
            if (capa.Status == "closed")
            {
                Assert.NotNull(capa.EffectivenessInspectionRecordId);
                Assert.Contains(capa.EffectivenessInspectionRecordId!.ToString(), recordIds);
                Assert.All(capa.Actions, action => Assert.Equal("completed", action.Status));
            }
        });
    }

    [Fact]
    public async Task Seeded_devices_carry_the_designed_calibration_state_mix()
    {
        await using var db = CreateDbContext();
        await new WorldHistoryMetrologySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);

        var nowUtc = new DateTimeOffset(AsOfDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc));
        var devices = await db.MeasuringDevices.ToArrayAsync();
        var states = devices
            .GroupBy(x => x.ComputeCalibrationState(nowUtc, WorldHistoryMetrologySpec.WarningDays))
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        foreach (var (state, count) in states.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"metrology-db-state-{state}={count}");
        }

        Assert.Equal(
            WorldHistoryMetrologySpec.OverdueDeviceCount,
            states.GetValueOrDefault(MeasuringDeviceCalibrationStates.Overdue));
        Assert.Equal(
            WorldHistoryMetrologySpec.WarningDeviceCount,
            states.GetValueOrDefault(MeasuringDeviceCalibrationStates.Warning));
        Assert.Equal(
            WorldHistoryMetrologySpec.DisabledDeviceCount + WorldHistoryMetrologySpec.RetiredDeviceCount,
            states.GetValueOrDefault(MeasuringDeviceCalibrationStates.Unavailable));
        Assert.True(states.GetValueOrDefault(MeasuringDeviceCalibrationStates.Current) > devices.Length / 2);
    }

    /// <summary>
    /// SPC 控制图不依赖库里的检验数据（控制限由纯函数从同一实测值公式推出），
    /// 因此可以在空库上按全量规模落图——这也是本仓能在 InMemory 上验证全量图形状的唯一路径。
    /// </summary>
    [Fact]
    public async Task Seed_locks_control_limits_that_bracket_the_center_line()
    {
        await using var db = CreateDbContext();
        var report = await new WorldHistoryMetrologySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, 1.0d);

        output.WriteLine($"metrology-db-charts={report.SpcControlChartsWritten}");
        var charts = await db.SpcControlCharts.ToArrayAsync();

        Assert.Equal(WorldHistoryMetrologySpec.BuildSpcSeries(AsOfDate, 1.0d).Count, charts.Length);
        Assert.InRange(charts.Length, 20, 60);
        Assert.All(charts, chart =>
        {
            Assert.True(chart.Locked);
            Assert.Equal(WorldHistoryMetrologySpec.SpcWorkCenterId, chart.WorkCenterId);
            Assert.Equal(WorldHistoryMetrologySpec.SpcSubgroupSize, chart.SubgroupSize);
            Assert.True(chart.XbarUpperControlLimit > chart.CenterLine);
            Assert.True(chart.XbarLowerControlLimit < chart.CenterLine);
            Assert.True(chart.RangeUpperControlLimit > chart.AverageRange);
            Assert.NotNull(chart.LimitsCalculatedAtUtc);
            // 控制限锁在试运行期，因此锁定时刻必须落在历史窗口内而不是「今天」。
            Assert.True(chart.LimitsCalculatedAtUtc!.Value < AsOfDate.ToDateTime(TimeOnly.MaxValue));
        });
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_device_loses_its_calibration_anchor()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistoryMetrologySeedService(db);
        await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        // 把一台器具的下次到期日改歪，「末次校准 + 检定周期 = 到期日」这条恒等式立刻不成立。
        var device = await db.MeasuringDevices.FirstAsync();
        db.Entry(device).Property(x => x.CalibrationDueAtUtc).CurrentValue =
            device.CalibrationDueAtUtc.AddDays(3);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorldHistoryConsistencyException>(() =>
            seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale));
    }

    [Fact]
    public async Task Seeded_records_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        db.ChangeTracker.Clear();
        await new WorldHistoryMetrologySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var deviceCodes = await db.MeasuringDevices.Select(x => x.DeviceCode).ToArrayAsync();
        var calibrationNos = await db.CalibrationRecords.Select(x => x.CalibrationNo).ToArrayAsync();
        var capaCodes = await db.CorrectiveActions.Select(x => x.CapaCode).ToArrayAsync();

        Assert.NotEmpty(deviceCodes);
        Assert.NotEmpty(capaCodes);
        foreach (var value in deviceCodes.Concat(calibrationNos).Concat(capaCodes))
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }

        Assert.All(deviceCodes, code => Assert.StartsWith("MD-", code, StringComparison.Ordinal));
        Assert.All(calibrationNos, code => Assert.StartsWith("CAL-2026-", code, StringComparison.Ordinal));
        Assert.All(capaCodes, code => Assert.StartsWith("CAPA-2026-", code, StringComparison.Ordinal));
    }

    #endregion

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-world-history-metrology-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new MetrologyTestMediator());
    }

    private sealed class MetrologyTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
