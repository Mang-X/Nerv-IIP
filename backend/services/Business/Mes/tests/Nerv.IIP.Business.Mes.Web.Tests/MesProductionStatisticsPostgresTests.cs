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
        AddReport(db, "org-other", "env-dev", "A", "SKU-A", "WC-A", "EARLY", windowStart.AddHours(1), 900m, 0m, 0m);
        AddReport(db, "org-001", "env-other", "A", "SKU-A", "WC-A", "EARLY", windowStart.AddHours(1), 800m, 0m, 0m);
        await db.SaveChangesAsync();

        var handler = new QueryProductionStatisticsQueryHandler(db);
        var day = Assert.Single((await Query(handler, ProductionStatisticsDimension.Day, windowStart, windowEnd)).Items);
        Assert.Equal(new DateOnly(2026, 8, 29), day.BusinessDate);
        Assert.Equal(11m, day.GoodQuantity);
        Assert.Equal(2m, day.ScrapQuantity);
        Assert.Equal(1m, day.ReworkQuantity);
        Assert.Equal(14m, day.TotalOutputQuantity);
        Assert.Equal(0.785714m, day.GoodRate);
        Assert.Equal(0.142857m, day.ScrapRate);
        Assert.Equal(0.071429m, day.ReworkRate);

        var shift = Assert.Single((await Query(handler, ProductionStatisticsDimension.Shift, windowStart, windowEnd)).Items);
        Assert.Equal("EARLY", shift.ShiftCode);
        Assert.Equal(new DateOnly(2026, 8, 29), shift.BusinessDate);
        Assert.Equal(14m, shift.TotalOutputQuantity);

        var workCenters = await Query(handler, ProductionStatisticsDimension.WorkCenter, windowStart, windowEnd);
        Assert.Equal(2, workCenters.TotalCount);
        Assert.Equal(["WC-A", "WC-B"], workCenters.Items.Select(x => x.WorkCenterId));
        Assert.Equal([10m, 4m], workCenters.Items.Select(x => x.TotalOutputQuantity));

        var skus = await Query(handler, ProductionStatisticsDimension.Sku, windowStart, windowEnd);
        Assert.Equal(["SKU-A", "SKU-B"], skus.Items.Select(x => x.SkuId));
        Assert.Equal([10m, 4m], skus.Items.Select(x => x.TotalOutputQuantity));

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

        var secondPage = await handler.Handle(new QueryProductionStatisticsQuery(
            "org-001",
            "env-dev",
            ProductionStatisticsDimension.WorkCenter,
            windowStart,
            windowEnd,
            Skip: 1,
            Take: 1), CancellationToken.None);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Equal(1, secondPage.Skip);
        Assert.Equal(1, secondPage.Take);
        Assert.Equal("WC-B", Assert.Single(secondPage.Items).WorkCenterId);
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
            DateTimeOffset.Parse("2026-08-30T08:30:00Z"),
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

        var reversalWindow = await Query(
            handler,
            ProductionStatisticsDimension.Day,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            DateTimeOffset.Parse("2026-08-30T09:00:00Z"));
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
