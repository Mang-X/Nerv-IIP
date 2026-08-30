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
            windowStart.AddHours(1).AddMinutes(30),
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
        Assert.Equal([new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 29)], days.Items.Select(x => x.BusinessDate));
        Assert.Equal([30m, 16m], days.Items.Select(x => x.TotalOutputQuantity));
        var day = days.Items.Single(x => x.BusinessDate == new DateOnly(2026, 8, 29));
        Assert.Equal(new DateOnly(2026, 8, 29), day.BusinessDate);
        Assert.Equal(12m, day.GoodQuantity);
        Assert.Equal(2m, day.ScrapQuantity);
        Assert.Equal(2m, day.ReworkQuantity);
        Assert.Equal(16m, day.TotalOutputQuantity);
        Assert.Equal(0.75m, day.GoodRate);
        Assert.Equal(0.125m, day.ScrapRate);
        Assert.Equal(0.125m, day.ReworkRate);

        var shifts = await Query(handler, ProductionStatisticsDimension.Shift, windowStart, windowEnd);
        Assert.Equal(
            ["2026-08-28/LATE", "2026-08-29/EARLY", "2026-08-29/LATE"],
            shifts.Items.Select(x => x.DimensionValue));
        Assert.Equal([30m, 14m, 2m], shifts.Items.Select(x => x.TotalOutputQuantity));

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
        var originalWindow = await Query(
            handler,
            ProductionStatisticsDimension.Day,
            originalAtUtc.AddMinutes(-30),
            originalAtUtc.AddMinutes(30));
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
            originalAtUtc.AddMinutes(-30),
            originalAtUtc.AddMinutes(30))).Items);
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
        Assert.Null(bucket.DimensionValue);
        Assert.Null(bucket.BusinessDate);
        Assert.Equal(5m, bucket.TotalOutputQuantity);
        Assert.Equal(ProductionStatisticsResolutionStatus.Degraded, bucket.ResolutionStatus);
        Assert.Contains(
            ProductionStatisticsDegradedReason.HistoricalDimensionLegacyUnresolved,
            bucket.DegradedReasons);
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
            null));
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
