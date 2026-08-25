using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryOeePostgresQueryTests
{
    [RealPostgresFact]
    public async Task V1_optional_snapshot_projection_resolves_site_day_and_cross_midnight_shift_windows_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var reportedAtUtc = DateTimeOffset.Parse("2026-07-10T17:30:00Z");
        var integrationEvent = new ProductionReportRecordedIntegrationEvent(
            "evt-oee-v2-pg-001",
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            "PRPT-OEE-V2-PG-001",
            "PRPT-OEE-V2-PG-001",
            "org-001",
            "env-dev",
            "system:mes",
            "production-report-recorded:org-001:env-dev:PRPT-OEE-V2-PG-001",
            new ProductionReportRecordedPayload(
                "PRPT-OEE-V2-PG-001",
                "WO-001",
                "OP-10",
                "WC-01",
                "DEV-01",
                10m,
                0m,
                0m,
                "PCS",
                100m,
                reportedAtUtc,
                false,
                SiteCode: "SITE-SH",
                WorkshopCode: "WS-01",
                LineCode: "LINE-01",
                ShiftCode: "NIGHT",
                SiteTimezone: "Asia/Shanghai",
                ShiftStartsAt: new TimeOnly(20, 0),
                ShiftEndsAt: new TimeOnly(4, 0),
                ShiftCrossesMidnight: true,
                ShiftPaidMinutes: 450,
                ShiftBreakMinutes: 30));

        await new ProductionReportOeeProjectionHandler(dbContext, new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(integrationEvent, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var fact = await dbContext.OeeProductionFacts.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 7, 11), fact.BusinessDate);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T16:00:00Z"), fact.DayBucketStartUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T16:00:00Z"), fact.DayBucketEndUtc);
        Assert.Equal(new DateOnly(2026, 7, 10), fact.ShiftBusinessDate);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T12:00:00Z"), fact.ShiftBucketStartUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T20:00:00Z"), fact.ShiftBucketEndUtc);
        Assert.Equal("SITE-SH", fact.SiteCode);
        Assert.Equal("WS-01", fact.WorkshopCode);
        Assert.Equal("LINE-01", fact.LineCode);
    }

    [RealPostgresFact]
    public async Task Work_center_bucket_uses_weighted_apq_and_isolates_scope_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        dbContext.DeviceStateSnapshots.AddRange(
            State("org-001", "DEV-01", "running", start, "state-01"),
            State("org-001", "DEV-01", "stopped", start.AddHours(1), "state-02"),
            State("org-001", "DEV-02", "running", start, "state-03"));
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-WEIGHTED-01", start.AddMinutes(30), "DEV-01", "WC-WEIGHTED", 80m, 20m, "PCS", 100m),
            Fact("PRPT-WEIGHTED-02", start.AddMinutes(45), "DEV-02", "WC-WEIGHTED", 180m, 20m, "PCS", 200m),
            Fact("PRPT-OTHER-TENANT", start.AddMinutes(30), "DEV-01", "WC-WEIGHTED", 999m, 1m, "PCS", 100m, organizationId: "org-other"));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001",
                "env-dev",
                OeeAggregateDimensions.WorkCenter,
                start,
                end,
                WorkCenterId: "WC-WEIGHTED"),
            CancellationToken.None);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal("WC-WEIGHTED", bucket.DimensionValue);
        Assert.Equal(2, bucket.DeviceCount);
        Assert.Equal(2, bucket.ProductionFactCount);
        Assert.Equal(0.75m, bucket.AvailabilityRate);
        Assert.Equal(0.6m, bucket.PerformanceRate);
        Assert.Equal(0.866667m, bucket.QualityRate);
        Assert.Equal(0.39m, bucket.OeeRate);
        Assert.False(bucket.IsDegraded);
    }

    [RealPostgresFact]
    public async Task Mixed_uom_bucket_is_explicitly_degraded_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        dbContext.DeviceStateSnapshots.Add(State("org-001", "DEV-MIX", "running", start, "state-mix"));
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-MIX-01", start.AddMinutes(30), "DEV-MIX", "WC-MIX", 10m, 0m, "PCS", 10m),
            Fact("PRPT-MIX-02", start.AddMinutes(45), "DEV-MIX", "WC-MIX", 1m, 0m, "KG", 1m));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001",
                "env-dev",
                OeeAggregateDimensions.WorkCenter,
                start,
                end,
                WorkCenterId: "WC-MIX"),
            CancellationToken.None);

        var bucket = Assert.Single(result.Buckets);
        Assert.Null(bucket.QualityRate);
        Assert.Null(bucket.PerformanceRate);
        Assert.Null(bucket.OeeRate);
        Assert.True(bucket.IsDegraded);
        Assert.Contains("production-uom-ambiguous", bucket.DegradedReasons);
    }

    [RealPostgresFact]
    public async Task Device_shift_and_day_buckets_use_snapshots_and_degrade_missing_boundaries_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = DateTimeOffset.Parse("2026-07-11T08:00:00Z");
        dbContext.DeviceStateSnapshots.AddRange(
            State("org-001", "DEV-BUCKET", "running", start, "state-bucket"),
            State("org-001", "DEV-LEGACY", "running", start, "state-legacy"));
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-BUCKET", start.AddHours(1), "DEV-BUCKET", "WC-BUCKET", 8m, 2m, "PCS", 10m),
            OeeProductionFact.Project(
                "org-001",
                "env-dev",
                "PRPT-LEGACY",
                "WC-BUCKET",
                "DEV-LEGACY",
                5m,
                0m,
                0m,
                "PCS",
                5m,
                start.AddHours(2)));
        await dbContext.SaveChangesAsync();

        var handler = new QueryOeeAggregateBucketsQueryHandler(dbContext);
        var device = await handler.Handle(
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Device, start, end),
            CancellationToken.None);
        var shift = await handler.Handle(
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Shift, start, end),
            CancellationToken.None);
        var day = await handler.Handle(
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Day, start, end),
            CancellationToken.None);
        var line = await handler.Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Line, start, end,
                LineCode: "LINE-01"),
            CancellationToken.None);
        var workshop = await handler.Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Workshop, start, end,
                WorkshopCode: "WORKSHOP-01"),
            CancellationToken.None);

        Assert.Equal(
            ["DEV-BUCKET", "DEV-LEGACY"],
            device.Buckets.Select(x => Assert.IsType<string>(x.DimensionValue)).Order().ToArray());
        var resolvedShift = Assert.Single(shift.Buckets, x => x.DimensionValue == "SHIFT-01");
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T08:00:00Z"), resolvedShift.BucketStartUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T16:00:00Z"), resolvedShift.BucketEndUtc);
        Assert.False(resolvedShift.IsDegraded);
        var unresolvedShift = Assert.Single(shift.Buckets, x => x.DimensionValue is null);
        Assert.Contains("shift-definition-or-boundary-missing", unresolvedShift.DegradedReasons);
        var resolvedDay = Assert.Single(day.Buckets, x => x.DimensionValue == "SITE-01");
        Assert.Equal(new DateOnly(2026, 7, 10), resolvedDay.BusinessDate);
        Assert.Equal(start, resolvedDay.BucketStartUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T00:00:00Z"), resolvedDay.BucketEndUtc);
        var unresolvedDay = Assert.Single(day.Buckets, x => x.DimensionValue is null);
        Assert.Contains("site-timezone-or-day-boundary-missing", unresolvedDay.DegradedReasons);
        Assert.Equal("LINE-01", Assert.Single(line.Buckets).DimensionValue);
        Assert.Equal("WORKSHOP-01", Assert.Single(workshop.Buckets).DimensionValue);
    }

    [RealPostgresFact]
    public async Task Cross_day_reversal_reuses_original_historical_buckets_and_nets_original_bucket_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var originalAtUtc = DateTimeOffset.Parse("2026-07-10T17:30:00Z");
        var reversalAtUtc = DateTimeOffset.Parse("2026-07-11T17:30:00Z");
        var handler = new ProductionReportOeeProjectionHandler(dbContext, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(ProductionReportEvent(
            "PRPT-ORIGINAL", originalAtUtc, 10m, false, null), CancellationToken.None);
        await handler.HandleAsync(ProductionReportEvent(
            "PRPT-REVERSAL", reversalAtUtc, -10m, true, "PRPT-ORIGINAL"), CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var facts = await dbContext.OeeProductionFacts.AsNoTracking()
            .OrderBy(x => x.SourceReportNo)
            .ToArrayAsync();
        Assert.Equal(2, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.Equal(new DateOnly(2026, 7, 11), fact.BusinessDate);
            Assert.Equal(DateTimeOffset.Parse("2026-07-10T16:00:00Z"), fact.DayBucketStartUtc);
            Assert.Equal(new DateOnly(2026, 7, 10), fact.ShiftBusinessDate);
            Assert.Equal(DateTimeOffset.Parse("2026-07-10T12:00:00Z"), fact.ShiftBucketStartUtc);
        });

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Day,
                DateTimeOffset.Parse("2026-07-10T16:00:00Z"),
                DateTimeOffset.Parse("2026-07-12T16:00:00Z")),
            CancellationToken.None);
        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(2, bucket.ProductionFactCount);
        Assert.Equal(0m, bucket.GoodQuantity);
    }

    [RealPostgresFact]
    public async Task Oee_query_filters_production_facts_by_datetimeoffset_window_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-OEE-PG-BEFORE", "2026-07-10T07:59:59Z"),
            Fact("PRPT-OEE-PG-IN", "2026-07-10T08:30:00Z"),
            Fact("PRPT-OEE-PG-END", "2026-07-10T10:00:00Z"));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeQueryHandler(dbContext).Handle(
            new QueryOeeQuery(
                "org-001",
                "env-dev",
                "DEV-OEE-PG-01",
                DateTimeOffset.Parse("2026-07-10T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-10T10:00:00Z")),
            CancellationToken.None);

        Assert.Equal(1, result.ProductionFactCount);
        Assert.Equal(10m, result.GoodQuantity);
    }

    private static ApplicationDbContext CreateLaneDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static OeeProductionFact Fact(string reportNo, string reportedAtUtc) =>
        Fact(reportNo, DateTimeOffset.Parse(reportedAtUtc), "DEV-OEE-PG-01", "WC-OEE-PG-01", 10m, 0m, "PCS", 10m);

    private static OeeProductionFact Fact(
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string deviceAssetId,
        string workCenterId,
        decimal goodQuantity,
        decimal scrapQuantity,
        string uomCode,
        decimal theoreticalRatePerHour,
        string organizationId = "org-001") =>
        OeeProductionFact.Project(
            organizationId,
            "env-dev",
            reportNo,
            workCenterId,
            deviceAssetId,
            goodQuantity,
            scrapQuantity,
            0m,
            uomCode,
            theoreticalRatePerHour,
            reportedAtUtc,
            new OeeHistoricalDimensionSnapshot(
                "SITE-01",
                "WORKSHOP-01",
                "LINE-01",
                "SHIFT-01",
                "UTC",
                new TimeOnly(8, 0),
                new TimeOnly(16, 0),
                false,
                450,
                30,
                DateOnly.FromDateTime(reportedAtUtc.UtcDateTime),
                new DateTimeOffset(reportedAtUtc.UtcDateTime.Date, TimeSpan.Zero),
                new DateTimeOffset(reportedAtUtc.UtcDateTime.Date.AddDays(1), TimeSpan.Zero),
                DateOnly.FromDateTime(reportedAtUtc.UtcDateTime),
                new DateTimeOffset(reportedAtUtc.UtcDateTime.Date.AddHours(8), TimeSpan.Zero),
                new DateTimeOffset(reportedAtUtc.UtcDateTime.Date.AddHours(16), TimeSpan.Zero)));

    private static ProductionReportRecordedIntegrationEvent ProductionReportEvent(
        string reportNo,
        DateTimeOffset reportedAtUtc,
        decimal goodQuantity,
        bool isReversal,
        string? reversedReportNo) =>
        new(
            $"evt-{reportNo}",
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            reportNo,
            reportNo,
            "org-001",
            "env-dev",
            "system:mes",
            $"production-report-recorded:org-001:env-dev:{reportNo}",
            new ProductionReportRecordedPayload(
                reportNo,
                "WO-001",
                "OP-10",
                "WC-01",
                "DEV-01",
                goodQuantity,
                0m,
                0m,
                "PCS",
                100m,
                reportedAtUtc,
                isReversal,
                reversedReportNo,
                SiteCode: "SITE-SH",
                WorkshopCode: "WS-01",
                LineCode: "LINE-01",
                ShiftCode: "NIGHT",
                SiteTimezone: "Asia/Shanghai",
                ShiftStartsAt: new TimeOnly(20, 0),
                ShiftEndsAt: new TimeOnly(4, 0),
                ShiftCrossesMidnight: true,
                ShiftPaidMinutes: 450,
                ShiftBreakMinutes: 30));

    private static DeviceStateSnapshot State(
        string organizationId,
        string deviceAssetId,
        string state,
        DateTimeOffset occurredAtUtc,
        string sequence) =>
        DeviceStateSnapshot.Record(
            organizationId,
            "env-dev",
            deviceAssetId,
            state,
            occurredAtUtc,
            sequence,
            raiseChangedEvent: false);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
