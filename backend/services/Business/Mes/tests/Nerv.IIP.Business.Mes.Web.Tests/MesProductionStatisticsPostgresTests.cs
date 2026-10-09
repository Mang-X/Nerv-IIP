using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesProductionStatisticsPostgresTests
{
    [MesRealPostgresFact]
    public async Task Four_dimensions_filters_and_second_page_use_scoped_production_totals_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var windowStart = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        var windowEnd = windowStart.AddDays(1);

        AddReport(db, "org-001", "env-dev", "A", "SKU-A", "WC-A", "EARLY", windowStart.AddHours(1), 8m, 1m, 1m);
        AddReport(db, "org-001", "env-dev", "B", "SKU-B", "WC-B", "EARLY", windowStart.AddHours(2), 3m, 1m, 0m);
        AddReportWithSnapshot(
            db,
            "org-001",
            "env-dev",
            "C",
            "SKU-C",
            // 洛杉矶工厂 08-29 18:30（晚于窗口末端的 UTC 时刻，但窗口按工厂本地取整后就是它的 08-29）。
            windowStart.AddDays(1).AddHours(1).AddMinutes(30),
            20m,
            5m,
            5m,
            ProductionReportOeeDimensionSnapshot.Resolved(
                "DEV-C",
                "WC-Z",
                "SITE-LA",
                "WS-02",
                "LINE-02",
                "America/Los_Angeles",
                "LATE",
                new TimeOnly(18, 0),
                new TimeOnly(20, 0),
                false,
                120,
                0));
        AddReportWithSnapshot(
            db,
            "org-001",
            "env-dev",
            "D",
            "SKU-D",
            windowStart.AddHours(7),
            1m,
            0m,
            1m,
            ProductionReportOeeDimensionSnapshot.Resolved(
                "DEV-D",
                "WC-C",
                "SITE-SH",
                "WS-03",
                "LINE-03",
                "Asia/Shanghai",
                "LATE",
                new TimeOnly(14, 0),
                new TimeOnly(18, 0),
                false,
                240,
                0));
        AddReport(db, "org-other", "env-dev", "A", "SKU-A", "WC-A", "EARLY", windowStart.AddHours(1), 900m, 0m, 0m);
        AddReport(db, "org-001", "env-other", "A", "SKU-A", "WC-A", "EARLY", windowStart.AddHours(1), 800m, 0m, 0m);
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        var days = await Query(handler, ProductionStatisticsDimension.Day, windowStart, windowEnd);
        var day = Assert.Single(days.Items);
        Assert.Equal(new DateOnly(2026, 8, 29), day.BusinessDate);
        Assert.Equal(32m, day.GoodQuantity);
        Assert.Equal(7m, day.ScrapQuantity);
        Assert.Equal(7m, day.ReworkQuantity);
        Assert.Equal(46m, day.TotalOutputQuantity);
        Assert.Equal(0.695652m, day.GoodRate);
        Assert.Equal(0.152174m, day.ScrapRate);
        Assert.Equal(0.152174m, day.ReworkRate);

        var shifts = await Query(handler, ProductionStatisticsDimension.Shift, windowStart, windowEnd);
        Assert.Equal(
            ["2026-08-29/EARLY", "2026-08-29/LATE"],
            shifts.Items.Select(x => x.DimensionValue));
        Assert.Equal([14m, 32m], shifts.Items.Select(x => x.TotalOutputQuantity));

        var workCenters = await Query(handler, ProductionStatisticsDimension.WorkCenter, windowStart, windowEnd);
        Assert.Equal(4, workCenters.TotalCount);
        Assert.Equal(["WC-A", "WC-B", "WC-C", "WC-Z"], workCenters.Items.Select(x => x.WorkCenterId));
        Assert.Equal([10m, 4m, 2m, 30m], workCenters.Items.Select(x => x.TotalOutputQuantity));

        var skus = await Query(handler, ProductionStatisticsDimension.Sku, windowStart, windowEnd);
        Assert.Equal(["SKU-A", "SKU-B", "SKU-C", "SKU-D"], skus.Items.Select(x => x.SkuId));
        Assert.Equal([10m, 4m, 30m, 2m], skus.Items.Select(x => x.TotalOutputQuantity));

        var filtered = await handler.Handle(new QueryProductionStatisticsQuery(
            "org-001",
            "env-dev",
            ProductionStatisticsDimension.WorkCenter,
            windowStart,
            windowEnd,
            BusinessDate: new DateOnly(2026, 8, 29),
            ShiftCode: "EARLY",
            WorkCenterId: "WC-A",
            SkuId: "SKU-A"), CancellationToken.None);
        Assert.Equal("WC-A", Assert.Single(filtered.Items).WorkCenterId);

        var firstPage = await handler.Handle(new QueryProductionStatisticsQuery(
            "org-001",
            "env-dev",
            ProductionStatisticsDimension.WorkCenter,
            windowStart,
            windowEnd,
            Take: 2), CancellationToken.None);
        var secondPage = await handler.Handle(new QueryProductionStatisticsQuery(
            "org-001",
            "env-dev",
            ProductionStatisticsDimension.WorkCenter,
            windowStart,
            windowEnd,
            Skip: 2,
            Take: 2), CancellationToken.None);
        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(4, secondPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(0, firstPage.Skip);
        Assert.Equal(2, secondPage.Skip);
        Assert.Equal(2, firstPage.Take);
        Assert.Equal(2, secondPage.Take);
        Assert.Equal(
            workCenters.Items.Select(x => x.WorkCenterId),
            firstPage.Items.Concat(secondPage.Items).Select(x => x.WorkCenterId));
    }

    [MesRealPostgresFact]
    public async Task Reversal_offsets_the_original_business_bucket_and_non_positive_totals_have_no_rates()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var originalAtUtc = DateTimeOffset.Parse("2026-08-29T16:30:00Z");
        var original = AddReportWithSnapshot(
            db,
            "org-001",
            "env-dev",
            "NIGHT",
            "SKU-NIGHT",
            originalAtUtc,
            6m,
            1m,
            1m,
            ProductionReportOeeDimensionSnapshot.Resolved(
                "DEV-NIGHT",
                "WC-NIGHT",
                "SITE-SH",
                "WS-01",
                "LINE-01",
                "Asia/Shanghai",
                "NIGHT",
                new TimeOnly(20, 0),
                new TimeOnly(4, 0),
                true,
                450,
                30));
        db.ProductionReports.Add(ProductionReport.Reverse(
            original,
            "PR-NIGHT-REV",
            DateTimeOffset.Parse("2026-08-30T16:30:00Z"),
            "incorrect report",
            "operator-001"));
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        // 原报工在上海 08-30 00:30（夜班 20:00–04:00），业务日是 08-29；按 08-29 这一业务日的窗口查询。
        var businessDayStartUtc = DateTimeOffset.Parse("2026-08-28T16:00:00Z");
        var businessDayEndUtc = DateTimeOffset.Parse("2026-08-29T16:00:00Z");
        var originalWindow = await Query(
            handler,
            ProductionStatisticsDimension.Day,
            businessDayStartUtc,
            businessDayEndUtc);
        var bucket = Assert.Single(originalWindow.Items);
        Assert.Equal(new DateOnly(2026, 8, 29), bucket.BusinessDate);
        Assert.Equal(0m, bucket.TotalOutputQuantity);
        Assert.Null(bucket.GoodRate);
        Assert.Null(bucket.ScrapRate);
        Assert.Null(bucket.ReworkRate);
        Assert.Equal(ProductionStatisticsResolutionStatus.Degraded, bucket.ResolutionStatus);
        Assert.Contains(ProductionStatisticsDegradedReason.NonPositiveTotalOutput, bucket.DegradedReasons);

        var shiftBucket = Assert.Single((await Query(
            handler,
            ProductionStatisticsDimension.Shift,
            businessDayStartUtc,
            businessDayEndUtc)).Items);
        Assert.Equal("2026-08-29/NIGHT", shiftBucket.DimensionValue);
        Assert.Equal(0m, shiftBucket.TotalOutputQuantity);

        var reversalWindow = await Query(
            handler,
            ProductionStatisticsDimension.Day,
            DateTimeOffset.Parse("2026-08-30T16:00:00Z"),
            DateTimeOffset.Parse("2026-08-30T17:00:00Z"));
        Assert.Empty(reversalWindow.Items);
    }

    [MesRealPostgresFact]
    public async Task Legacy_reports_keep_quantities_in_an_explicitly_degraded_dimension_bucket()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-29T02:00:00Z");
        AddReportWithSnapshot(
            db,
            "org-001",
            "env-dev",
            "LEGACY",
            "SKU-LEGACY",
            reportedAtUtc,
            5m,
            0m,
            0m,
            null);
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        var response = await Query(
            handler,
            ProductionStatisticsDimension.Day,
            reportedAtUtc.AddMinutes(-30),
            reportedAtUtc.AddMinutes(30));
        var bucket = Assert.Single(response.Items);
        // 没有工厂时区与班次快照：按默认工厂时区 Asia/Shanghai 的 00:00 起算，02:00Z 即上海 08-29 10:00。
        Assert.Equal("2026-08-29", bucket.DimensionValue);
        Assert.Equal(new DateOnly(2026, 8, 29), bucket.BusinessDate);
        Assert.Equal(5m, bucket.TotalOutputQuantity);
        Assert.Equal(ProductionStatisticsResolutionStatus.Degraded, bucket.ResolutionStatus);
        Assert.Contains(
            ProductionStatisticsDegradedReason.HistoricalDimensionLegacyUnresolved,
            bucket.DegradedReasons);
    }

    [MesRealPostgresFact]
    public async Task Night_shift_reports_after_midnight_count_on_the_shift_start_day_in_day_and_shift_views()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        // 上海 09-26 这一天：浏览器按本地 00:00–次日 00:00 发来的窗口。
        var windowStart = DateTimeOffset.Parse("2026-09-26T00:00:00+08:00").ToUniversalTime();
        var windowEnd = DateTimeOffset.Parse("2026-09-27T00:00:00+08:00").ToUniversalTime();

        AddShanghaiReport(db, "DAY-1", "DAY", DateTimeOffset.Parse("2026-09-26T10:00:00+08:00"), 1m);
        AddShanghaiReport(db, "NIGHT-EVE", "NIGHT", DateTimeOffset.Parse("2026-09-26T22:00:00+08:00"), 10m);
        // 跨午夜那段：发生在窗口末端之后，仍属于 09-26 开班的夜班。
        AddShanghaiReport(db, "NIGHT-TAIL", "NIGHT", DateTimeOffset.Parse("2026-09-27T01:20:00+08:00"), 100m);
        // 窗口开头那段夜班属于 09-25 开班，不算进 09-26。
        AddShanghaiReport(db, "NIGHT-PREV", "NIGHT", DateTimeOffset.Parse("2026-09-26T01:00:00+08:00"), 1000m);
        // 没有班次快照：按工厂时区 00:00 起算。
        AddShanghaiReport(db, "NO-SHIFT", null, DateTimeOffset.Parse("2026-09-26T23:30:00+08:00"), 10000m);
        AddShanghaiReport(db, "NO-SHIFT-NEXT", null, DateTimeOffset.Parse("2026-09-27T00:30:00+08:00"), 100000m);
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        var day = Assert.Single((await Query(handler, ProductionStatisticsDimension.Day, windowStart, windowEnd)).Items);
        Assert.Equal(new DateOnly(2026, 9, 26), day.BusinessDate);
        Assert.Equal(10111m, day.GoodQuantity);

        var shifts = await Query(handler, ProductionStatisticsDimension.Shift, windowStart, windowEnd);
        Assert.Equal(["2026-09-26/DAY", "2026-09-26/NIGHT", null], shifts.Items.Select(x => x.DimensionValue));
        Assert.Equal([1m, 110m, 10000m], shifts.Items.Select(x => x.GoodQuantity));
        Assert.All(shifts.Items, x => Assert.Equal(new DateOnly(2026, 9, 26), x.BusinessDate));
    }

    [MesRealPostgresFact]
    public async Task Single_day_window_counts_each_factorys_own_complete_business_day_across_timezones()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        // 浏览器在上海、选 09-26：窗口是上海 09-26 00:00 至 09-27 00:00。三个工厂都只该计入各自的 09-26 整个业务日。
        var windowStart = DateTimeOffset.Parse("2026-09-26T00:00:00+08:00").ToUniversalTime();
        var windowEnd = DateTimeOffset.Parse("2026-09-27T00:00:00+08:00").ToUniversalTime();
        var day = (new TimeOnly(8, 0), new TimeOnly(20, 0));
        var night = (new TimeOnly(20, 0), new TimeOnly(8, 0));
        // 跨午夜的长班（20:00–次日 14:00）：西半球工厂的尾段会落在窗口末端 24 小时之后，用来钉住末端放宽量。
        var longNight = (new TimeOnly(20, 0), new TimeOnly(14, 0));

        AddSiteReport(db, "BKK-D25", "Asia/Bangkok", "DAY", day, "2026-09-25T16:00:00+07:00", 1m);
        AddSiteReport(db, "BKK-N25-TAIL", "Asia/Bangkok", "NIGHT", night, "2026-09-26T00:30:00+07:00", 2m);
        AddSiteReport(db, "BKK-D26", "Asia/Bangkok", "DAY", day, "2026-09-26T10:00:00+07:00", 10m);
        AddSiteReport(db, "BKK-N26-TAIL", "Asia/Bangkok", "NIGHT", night, "2026-09-27T02:00:00+07:00", 20m);
        AddSiteReport(db, "TYO-D26", "Asia/Tokyo", "DAY", day, "2026-09-26T10:00:00+09:00", 100m);
        AddSiteReport(db, "TYO-N26-TAIL", "Asia/Tokyo", "NIGHT", night, "2026-09-27T02:00:00+09:00", 200m);
        AddSiteReport(db, "TYO-D27", "Asia/Tokyo", "DAY", day, "2026-09-27T10:00:00+09:00", 1000m);
        AddSiteReport(db, "TYO-N27-TAIL", "Asia/Tokyo", "NIGHT", night, "2026-09-28T02:00:00+09:00", 2000m);
        AddSiteReport(db, "BUE-D26", "America/Argentina/Buenos_Aires", "DAY", day, "2026-09-26T10:00:00-03:00", 10000m);
        AddSiteReport(db, "BUE-L26-TAIL", "America/Argentina/Buenos_Aires", "LONG", longNight, "2026-09-27T13:30:00-03:00", 20000m);
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        var skus = await Query(handler, ProductionStatisticsDimension.Sku, windowStart, windowEnd);
        Assert.Equal(
            ["BKK-D26", "BKK-N26-TAIL", "BUE-D26", "BUE-L26-TAIL", "TYO-D26", "TYO-N26-TAIL"],
            skus.Items.Select(x => x.SkuId));
        Assert.Equal([10m, 20m, 10000m, 20000m, 100m, 200m], skus.Items.Select(x => x.GoodQuantity));

        var days = Assert.Single((await Query(handler, ProductionStatisticsDimension.Day, windowStart, windowEnd)).Items);
        Assert.Equal(new DateOnly(2026, 9, 26), days.BusinessDate);
        Assert.Equal(30330m, days.GoodQuantity);
    }

    private static void AddShanghaiReport(
        ApplicationDbContext db,
        string suffix,
        string? shiftCode,
        DateTimeOffset reportedAt,
        decimal goodQuantity)
    {
        var shift = shiftCode == "NIGHT"
            ? (new TimeOnly(20, 0), new TimeOnly(8, 0))
            : (new TimeOnly(8, 0), new TimeOnly(20, 0));
        AddSiteReport(db, suffix, "Asia/Shanghai", shiftCode, shift, reportedAt, goodQuantity, "SKU-001");
    }

    private static void AddSiteReport(
        ApplicationDbContext db,
        string suffix,
        string timezone,
        string? shiftCode,
        (TimeOnly StartsAt, TimeOnly EndsAt) shift,
        string reportedAt,
        decimal goodQuantity) =>
        AddSiteReport(db, suffix, timezone, shiftCode, shift, DateTimeOffset.Parse(reportedAt), goodQuantity, suffix);

    private static void AddSiteReport(
        ApplicationDbContext db,
        string suffix,
        string timezone,
        string? shiftCode,
        (TimeOnly StartsAt, TimeOnly EndsAt) shift,
        DateTimeOffset reportedAt,
        decimal goodQuantity,
        string skuId)
    {
        var (startsAt, endsAt) = shift;
        AddReportWithSnapshot(
            db,
            "org-001",
            "env-dev",
            suffix,
            skuId,
            reportedAt.ToUniversalTime(),
            goodQuantity,
            0m,
            0m,
            ProductionReportOeeDimensionSnapshot.Resolved(
                $"DEV-{suffix}",
                "WC-A",
                "SITE-" + timezone,
                "WS-01",
                "LINE-01",
                timezone,
                shiftCode,
                shiftCode is null ? null : startsAt,
                shiftCode is null ? null : endsAt,
                shiftCode is null ? null : endsAt <= startsAt,
                shiftCode is null ? null : 720,
                shiftCode is null ? null : 60));
    }

    private static Task<ProductionStatisticsResponse> Query(
        QueryProductionStatisticsQueryHandler handler,
        ProductionStatisticsDimension dimension,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd) =>
        handler.Handle(new QueryProductionStatisticsQuery(
            "org-001",
            "env-dev",
            dimension,
            windowStart,
            windowEnd), CancellationToken.None);

    private static ProductionReport AddReport(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string suffix,
        string skuId,
        string workCenterId,
        string shiftCode,
        DateTimeOffset reportedAtUtc,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity)
    {
        return AddReportWithSnapshot(
            db,
            organizationId,
            environmentId,
            suffix,
            skuId,
            reportedAtUtc,
            goodQuantity,
            scrapQuantity,
            reworkQuantity,
            ProductionReportOeeDimensionSnapshot.Resolved(
                $"DEV-{suffix}",
                workCenterId,
                "SITE-SH",
                "WS-01",
                "LINE-01",
                "Asia/Shanghai",
                shiftCode,
                new TimeOnly(8, 0),
                new TimeOnly(16, 0),
                false,
                450,
                30));
    }

    private static ProductionReport AddReportWithSnapshot(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string suffix,
        string skuId,
        DateTimeOffset reportedAtUtc,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        ProductionReportOeeDimensionSnapshot? oeeDimensionSnapshot)
    {
        var workOrderId = $"WO-{suffix}";
        var operationTaskId = $"OP-{suffix}";
        db.WorkOrders.Add(WorkOrder.Create(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            "PV-001",
            100m,
            10,
            reportedAtUtc.AddDays(1),
            "PCS"));
        db.OperationTasks.Add(OperationTask.Create(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.InProgress,
            10,
            oeeDimensionSnapshot?.WorkCenterId ?? $"WC-{suffix}",
            [],
            reportedAtUtc.AddHours(-1),
            TimeSpan.FromHours(8),
            reportedAtUtc.AddHours(-1),
            null,
            "SKU-001"));
        var report = ProductionReport.Record(
            organizationId,
            environmentId,
            $"PR-{suffix}",
            workOrderId,
            operationTaskId,
            goodQuantity,
            scrapQuantity,
            false,
            reportedAtUtc,
            reworkQuantity: reworkQuantity,
            oeeDimensionSnapshot: oeeDimensionSnapshot);
        db.ProductionReports.Add(report);
        return report;
    }
}

public sealed class QueryProductionStatisticsQueryValidatorTests
{
    [Fact]
    public void Rejects_non_increasing_windows_and_take_above_limit()
    {
        var validator = new QueryProductionStatisticsQueryValidator();
        var windowStart = DateTimeOffset.Parse("2026-08-29T00:00:00Z");

        validator.TestValidate(Query(windowStart, windowStart))
            .ShouldHaveValidationErrorFor(x => x.WindowEndUtc);
        validator.TestValidate(Query(windowStart, windowStart.AddTicks(-1)))
            .ShouldHaveValidationErrorFor(x => x.WindowEndUtc);
        validator.TestValidate(Query(windowStart, windowStart.AddHours(1)) with { Take = 501 })
            .ShouldHaveValidationErrorFor(x => x.Take);
    }

    private static QueryProductionStatisticsQuery Query(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd) =>
        new(
            "org-001",
            "env-dev",
            ProductionStatisticsDimension.Day,
            windowStart,
            windowEnd);
}
