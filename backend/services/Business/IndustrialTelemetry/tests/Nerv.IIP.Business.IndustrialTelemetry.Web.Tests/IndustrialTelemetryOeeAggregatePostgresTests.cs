using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryOeeAggregatePostgresTests
{
    [RealPostgresFact]
    public async Task Six_dimensions_and_exact_filters_share_weighted_apq_and_scope_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = start.AddHours(2);
        var businessDate = new DateOnly(2026, 7, 10);

        db.DeviceStateSnapshots.AddRange(
            State("org-001", "DEV-A", "running", start, "a-1"),
            State("org-001", "DEV-A", "idle", start.AddHours(1), "a-2"),
            State("org-001", "DEV-B", "running", start, "b-1"),
            State("org-other", "DEV-A", "planned-down", start.AddMinutes(30), "other-1"));
        db.OeeProductionFacts.AddRange(
            Fact("A", start.AddMinutes(20), "DEV-A", "WC-01", 70m, 20m, 10m, "PCS", 100m, businessDate),
            Fact("B", start.AddMinutes(40), "DEV-B", "WC-01", 40m, 20m, 20m, "PCS", 50m, businessDate),
            Fact("OTHER", start.AddMinutes(30), "DEV-A", "WC-01", 900m, 0m, 0m, "PCS", 1m, businessDate, "org-other"));
        await db.SaveChangesAsync();

        var handler = new QueryOeeAggregateBucketsQueryHandler(db);
        var workCenter = await handler.Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.WorkCenter, start, end,
            WorkCenterId: "WC-01"), CancellationToken.None);
        var weighted = Assert.Single(workCenter.Buckets);
        Assert.Equal(2, weighted.DeviceCount);
        Assert.Equal(2, weighted.ProductionFactCount);
        Assert.Equal(0.75m, weighted.AvailabilityRate);
        Assert.Equal(0.9m, weighted.PerformanceRate);
        Assert.Equal(0.611111m, weighted.QualityRate);
        Assert.Equal(0.4125m, weighted.OeeRate);
        Assert.Equal(200m, weighted.ExpectedOutputQuantity);
        Assert.False(weighted.IsDegraded);

        var requests = new[]
        {
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Device, start, end, DeviceAssetId: "DEV-A"),
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Line, start, end, LineCode: "LINE-01"),
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Workshop, start, end, WorkshopCode: "WORKSHOP-01"),
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Shift, start, end, ShiftCode: "SHIFT-01"),
            new QueryOeeAggregateBucketsQuery("org-001", "env-dev", OeeAggregateDimensions.Day, start, end, BusinessDate: businessDate),
        };
        foreach (var request in requests)
        {
            var result = await handler.Handle(request, CancellationToken.None);
            Assert.NotEmpty(result.Buckets);
            Assert.All(result.Buckets, bucket => Assert.Equal(request.Dimension, bucket.Dimension));
        }
        Assert.Equal("DEV-A", Assert.Single((await handler.Handle(requests[0], CancellationToken.None)).Buckets).DimensionValue);
        Assert.Equal("LINE-01", Assert.Single((await handler.Handle(requests[1], CancellationToken.None)).Buckets).DimensionValue);
        Assert.Equal("WORKSHOP-01", Assert.Single((await handler.Handle(requests[2], CancellationToken.None)).Buckets).DimensionValue);
        Assert.Equal("SHIFT-01", Assert.Single((await handler.Handle(requests[3], CancellationToken.None)).Buckets).DimensionValue);
        Assert.Equal(businessDate, Assert.Single((await handler.Handle(requests[4], CancellationToken.None)).Buckets).BusinessDate);

        var secondDevicePage = await handler.Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.Device, start, end,
            Skip: 1,
            Take: 1), CancellationToken.None);
        Assert.Equal(2, secondDevicePage.TotalCount);
        Assert.Equal(1, secondDevicePage.Skip);
        Assert.Equal(1, secondDevicePage.Take);
        Assert.Equal("DEV-B", Assert.Single(secondDevicePage.Buckets).DimensionValue);
    }

    [RealPostgresFact]
    public async Task Scope_and_six_filter_predicates_reject_single_dimension_interference_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateLaneDbContext();
        await db.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var day = new DateOnly(2026, 7, 10);
        db.DeviceStateSnapshots.AddRange(
            State("org-001", "DEV-TARGET", "running", start, "target-state"),
            State("org-001", "DEV-OTHER", "running", start, "other-state"),
            State("org-other", "DEV-TARGET", "planned-down", start.AddMinutes(20), "other-org-state"),
            State("org-001", "DEV-TARGET", "planned-down", start.AddMinutes(30), "other-env-state", "env-other"));
        db.OeeProductionFacts.AddRange(
            Fact("TARGET", start.AddMinutes(1), "DEV-TARGET", "WC-01", 1m, 0m, 0m, "PCS", 1m, day),
            Fact("DEVICE", start.AddMinutes(2), "DEV-OTHER", "WC-01", 10m, 0m, 0m, "PCS", 1m, day),
            Fact("WORK-CENTER", start.AddMinutes(3), "DEV-TARGET", "WC-02", 10m, 0m, 0m, "PCS", 1m, day),
            Fact("LINE", start.AddMinutes(4), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day, line: "LINE-02"),
            Fact("WORKSHOP", start.AddMinutes(5), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day, workshop: "WORKSHOP-02"),
            Fact("SHIFT", start.AddMinutes(6), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day, shift: "SHIFT-02"),
            Fact("DAY", start.AddMinutes(7), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day.AddDays(1)),
            Fact("ORGANIZATION", start.AddMinutes(8), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day, organization: "org-other"),
            Fact("ENVIRONMENT", start.AddMinutes(9), "DEV-TARGET", "WC-01", 10m, 0m, 0m, "PCS", 1m, day, environment: "env-other"));
        await db.SaveChangesAsync();

        var result = await new QueryOeeAggregateBucketsQueryHandler(db).Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.Device, start, start.AddHours(1),
            DeviceAssetId: "DEV-TARGET",
            WorkCenterId: "WC-01",
            ShiftCode: "SHIFT-01",
            LineCode: "LINE-01",
            WorkshopCode: "WORKSHOP-01",
            BusinessDate: day), CancellationToken.None);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(1, bucket.ProductionFactCount);
        Assert.Equal(1m, bucket.GoodQuantity);
        Assert.Equal(1m, bucket.AvailabilityRate);
        Assert.Equal(1m, bucket.PerformanceRate);
        Assert.Equal(1m, bucket.QualityRate);
        Assert.Equal(1m, bucket.OeeRate);
    }

    [RealPostgresFact]
    public async Task Hierarchy_and_business_day_changes_do_not_duplicate_device_runtime_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateLaneDbContext();
        await db.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        db.DeviceStateSnapshots.Add(State("org-001", "DEV-MOVE", "running", start, "move-1"));
        db.OeeProductionFacts.AddRange(
            Fact("MOVE-A", start.AddHours(1), "DEV-MOVE", "WC-A", 10m, 0m, 0m, "PCS", 10m, new(2026, 7, 10), line: "LINE-A", workshop: "WS-A"),
            Fact("MOVE-B", start.AddDays(1).AddHours(1), "DEV-MOVE", "WC-B", 10m, 0m, 0m, "PCS", 10m, new(2026, 7, 11), line: "LINE-B", workshop: "WS-B"));
        await db.SaveChangesAsync();

        var handler = new QueryOeeAggregateBucketsQueryHandler(db);
        foreach (var dimension in new[] { OeeAggregateDimensions.WorkCenter, OeeAggregateDimensions.Line, OeeAggregateDimensions.Workshop, OeeAggregateDimensions.Day })
        {
            var result = await handler.Handle(new(
                "org-001", "env-dev", dimension, start, start.AddDays(2)), CancellationToken.None);

            Assert.Equal(2, result.Buckets.Count);
            Assert.Equal(20m, result.Buckets.Sum(x => x.GoodQuantity));
            Assert.All(result.Buckets, bucket =>
            {
                Assert.Equal(1m, bucket.AvailabilityRate);
                Assert.Equal(1m, bucket.QualityRate);
                Assert.NotNull(bucket.PerformanceRate);
                Assert.NotNull(bucket.OeeRate);
                Assert.NotNull(bucket.ExpectedOutputQuantity);
                Assert.Empty(bucket.DegradedReasons);
            });
            Assert.Equal(
                dimension == OeeAggregateDimensions.Day ? 40m : 48m,
                result.Buckets.Sum(x => x.ExpectedOutputQuantity!.Value) / 10m);

            if (dimension == OeeAggregateDimensions.Day)
            {
                var ordered = result.Buckets.OrderBy(x => x.BusinessDate).ToArray();
                Assert.Equal(start, ordered[0].BucketStartUtc);
                Assert.Equal(DateTimeOffset.Parse("2026-07-11T00:00:00Z"), ordered[0].BucketEndUtc);
                Assert.Equal(DateTimeOffset.Parse("2026-07-11T00:00:00Z"), ordered[1].BucketStartUtc);
                Assert.Equal(DateTimeOffset.Parse("2026-07-12T00:00:00Z"), ordered[1].BucketEndUtc);
            }
        }
    }

    [RealPostgresFact]
    public async Task Reversal_uses_aggregation_time_and_degraded_inputs_never_fabricate_factors_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateLaneDbContext();
        await db.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var snapshot = Snapshot(new(2026, 7, 10));
        var original = Fact("ORIGINAL", start.AddMinutes(30), "DEV-REV", "WC-REV", 10m, 0m, 0m, "PCS", 10m, new(2026, 7, 10));
        var reversal = original.ProjectReversal("REVERSAL", -10m, 0m, 0m, start.AddDays(1));
        db.DeviceStateSnapshots.AddRange(
            State("org-001", "DEV-REV", "running", start, "rev-1"),
            State("org-001", "DEV-MIX", "running", start, "mix-1"),
            State("org-001", "DEV-RATE", "running", start, "rate-1"),
            State("org-001", "DEV-GAP", "running", start.AddMinutes(30), "gap-1"),
            State("org-001", "DEV-DST", "running", start, "dst-1"));
        db.OeeProductionFacts.AddRange(
            original,
            reversal,
            Fact("MIX-A", start.AddMinutes(10), "DEV-MIX", "WC-MIX", 5m, 0m, 0m, "PCS", 10m, new(2026, 7, 10)),
            Fact("MIX-B", start.AddMinutes(20), "DEV-MIX", "WC-MIX", 1m, 0m, 0m, "KG", 10m, new(2026, 7, 10)),
            Fact("RATE", start.AddMinutes(10), "DEV-RATE", "WC-RATE", 5m, 0m, 0m, "PCS", null, new(2026, 7, 10)),
            Fact("GAP", start.AddMinutes(40), "DEV-GAP", "WC-GAP", 5m, 0m, 0m, "PCS", 10m, new(2026, 7, 10)),
            OeeProductionFact.Project("org-001", "env-dev", "DST", "WC-DST", "DEV-DST", 5m, 0m, 0m, "PCS", 10m, start.AddMinutes(10),
                snapshot with { BusinessDate = null, ShiftBucketStartUtc = null, ShiftBucketEndUtc = null, Status = OeeHistoricalDimensionStatus.AmbiguousLocalTime }));
        await db.SaveChangesAsync();

        var handler = new QueryOeeAggregateBucketsQueryHandler(db);
        var reversalBucket = Assert.Single((await handler.Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.Device, start, start.AddHours(1), DeviceAssetId: "DEV-REV"), CancellationToken.None)).Buckets);
        Assert.Equal(2, reversalBucket.ProductionFactCount);
        Assert.Equal(0m, reversalBucket.GoodQuantity);
        Assert.Empty((await handler.Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.Device, start.AddDays(1), start.AddDays(1).AddHours(1), DeviceAssetId: "DEV-REV"), CancellationToken.None)).Buckets);

        var mixed = await BucketFor("DEV-MIX");
        Assert.Null(mixed.PerformanceRate);
        Assert.Null(mixed.QualityRate);
        Assert.Null(mixed.OeeRate);
        Assert.Contains(OeeAggregateDegradedReason.ProductionUomAmbiguous, mixed.DegradedReasons);
        var missingRate = await BucketFor("DEV-RATE");
        Assert.Equal(1m, missingRate.AvailabilityRate);
        Assert.Null(missingRate.PerformanceRate);
        Assert.Null(missingRate.ExpectedOutputQuantity);
        Assert.Contains(OeeAggregateDegradedReason.TheoreticalRateMissingOrAmbiguous, missingRate.DegradedReasons);
        var gap = await BucketFor("DEV-GAP");
        Assert.Null(gap.AvailabilityRate);
        Assert.Null(gap.PerformanceRate);
        Assert.Null(gap.QualityRate);
        Assert.Null(gap.OeeRate);
        Assert.Contains(OeeAggregateDegradedReason.RuntimeStateCoverageIncomplete, gap.DegradedReasons);
        var dst = await BucketFor("DEV-DST");
        Assert.Null(dst.AvailabilityRate);
        Assert.Null(dst.PerformanceRate);
        Assert.Null(dst.QualityRate);
        Assert.Null(dst.OeeRate);
        Assert.Null(dst.ExpectedOutputQuantity);
        Assert.Contains(OeeAggregateDegradedReason.HistoricalLocalTimeAmbiguous, dst.DegradedReasons);

        Task<OeeAggregateBucket> BucketFor(string device) => GetBucketAsync(device);
        async Task<OeeAggregateBucket> GetBucketAsync(string device) => Assert.Single((await handler.Handle(new(
            "org-001", "env-dev", OeeAggregateDimensions.Device, start, start.AddHours(1), DeviceAssetId: device), CancellationToken.None)).Buckets);
    }

    [RealPostgresFact]
    public async Task State_budget_is_combined_and_bounded_in_executed_postgres_sql()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using (var setup = CreateLaneDbContext())
        {
            await setup.Database.MigrateAsync();
            var start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            setup.OeeProductionFacts.Add(Fact("BOUNDARY", start.AddMinutes(1), "DEV-BOUNDARY", "WC-BOUNDARY", 10m, 0m, 0m, "PCS", 10m, new(2026, 7, 1)));
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO industrial_telemetry.device_state_snapshots
                    ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                     occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                     recorded_at_utc, recorded_at_unix_time_milliseconds)
                SELECT (md5('oee-state-' || sample::text))::uuid,
                       'org-001', 'env-dev', 'DEV-BOUNDARY', 'running',
                       TIMESTAMPTZ '2026-07-01T00:00:00Z' + sample * INTERVAL '1 second',
                       (EXTRACT(EPOCH FROM (TIMESTAMPTZ '2026-07-01T00:00:00Z' + sample * INTERVAL '1 second')) * 1000)::bigint,
                       'oee-state-' || sample::text, NULL, NULL,
                       TIMESTAMPTZ '2026-07-02T00:00:00Z', 1782864000000
                FROM generate_series(-1, 9998) AS sample;
                """);
        }

        var interceptor = new LimitCaptureInterceptor();
        await using var db = CreateLaneDbContext(interceptor);
        var startUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var request = new QueryOeeAggregateBucketsQuery(
            "org-001", "env-dev", OeeAggregateDimensions.WorkCenter, startUtc, startUtc.AddDays(1), WorkCenterId: "WC-BOUNDARY");
        var accepted = await new QueryOeeAggregateBucketsQueryHandler(db).Handle(request, CancellationToken.None);
        Assert.Equal(OeeAggregateMaterializationLimits.MaximumStateSampleCount, Assert.Single(accepted.Buckets).StateSampleCount);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumProductionFactCount + 1, interceptor.FactLimits);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount + 1, interceptor.StateLimits);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount, interceptor.StateLimits);
        Assert.All(interceptor.FactCommands, command => Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase));
        Assert.All(interceptor.StateCommands, command => Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase));

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO industrial_telemetry.device_state_snapshots
                ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                 occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                 recorded_at_utc, recorded_at_unix_time_milliseconds)
            VALUES ((md5('oee-state-9999'))::uuid, 'org-001', 'env-dev', 'DEV-BOUNDARY', 'running',
                    TIMESTAMPTZ '2026-07-01T02:46:39Z', 1782873999000, 'oee-state-9999', NULL, NULL,
                    TIMESTAMPTZ '2026-07-02T00:00:00Z', 1782864000000);
            """);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new QueryOeeAggregateBucketsQueryHandler(db).Handle(request, CancellationToken.None));
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [RealPostgresFact]
    public async Task Production_fact_budget_accepts_boundary_and_rejects_one_more_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        var interceptor = new LimitCaptureInterceptor();
        await using var db = CreateLaneDbContext(interceptor);
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO industrial_telemetry.oee_production_facts
                (id, organization_id, environment_id, source_report_no, work_center_id, device_asset_id,
                 good_quantity, scrap_quantity, rework_quantity, uom_code, theoretical_rate_per_hour,
                 reported_at_utc, aggregation_occurred_at_utc, historical_dimension_status)
            SELECT (md5('oee-fact-' || sample::text))::uuid, 'org-001', 'env-dev',
                   'OEE-FACT-' || sample::text, 'WC-LIMIT', 'DEV-' || sample::text,
                   1, 0, 0, 'PCS', 1,
                   TIMESTAMPTZ '2026-07-01T00:01:00Z', TIMESTAMPTZ '2026-07-01T00:01:00Z', 'LegacyUnresolved'
            FROM generate_series(1, 10000) AS sample;
            """);
        var request = new QueryOeeAggregateBucketsQuery(
            "org-001", "env-dev", OeeAggregateDimensions.WorkCenter,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-02T00:00:00Z"), WorkCenterId: "WC-LIMIT");
        var accepted = await new QueryOeeAggregateBucketsQueryHandler(db).Handle(request, CancellationToken.None);
        Assert.Equal(10000, Assert.Single(accepted.Buckets).ProductionFactCount);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumProductionFactCount + 1, interceptor.FactLimits);
        Assert.All(interceptor.FactCommands, command => Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase));

        db.OeeProductionFacts.Add(Fact("OVER", DateTimeOffset.Parse("2026-07-01T00:02:00Z"), "DEV-OVER", "WC-LIMIT", 1m, 0m, 0m, "PCS", 1m, new(2026, 7, 1)));
        await db.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new QueryOeeAggregateBucketsQueryHandler(db).Handle(request, CancellationToken.None));
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumProductionFactCount.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateLaneDbContext(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }
        return new ApplicationDbContext(options.Options, new NoopMediator());
    }

    private static DeviceStateSnapshot State(
        string organization,
        string device,
        string state,
        DateTimeOffset occurredAt,
        string sequence,
        string environment = "env-dev") =>
        DeviceStateSnapshot.Record(organization, environment, device, state, occurredAt, sequence, raiseChangedEvent: false);

    private static OeeProductionFact Fact(
        string report,
        DateTimeOffset occurredAt,
        string device,
        string workCenter,
        decimal good,
        decimal scrap,
        decimal rework,
        string uom,
        decimal? rate,
        DateOnly businessDate,
        string organization = "org-001",
        string environment = "env-dev",
        string line = "LINE-01",
        string workshop = "WORKSHOP-01",
        string shift = "SHIFT-01") =>
        OeeProductionFact.Project(
            organization,
            environment,
            report,
            workCenter,
            device,
            good,
            scrap,
            rework,
            uom,
            rate,
            occurredAt,
            Snapshot(businessDate, line, workshop, shift));

    private static OeeHistoricalDimensionSnapshot Snapshot(
        DateOnly businessDate,
        string line = "LINE-01",
        string workshop = "WORKSHOP-01",
        string shift = "SHIFT-01") => new(
            "SITE-01",
            workshop,
            line,
            shift,
            "UTC",
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            false,
            450,
            30,
            businessDate,
            new DateTimeOffset(businessDate.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
            new DateTimeOffset(businessDate.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero),
            OeeHistoricalDimensionStatus.Resolved);

    private sealed class LimitCaptureInterceptor : DbCommandInterceptor
    {
        public List<int> Limits { get; } = [];
        public List<int> FactLimits { get; } = [];
        public List<int> StateLimits { get; } = [];
        public List<string> FactCommands { get; } = [];
        public List<string> StateCommands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var limits = command.Parameters.Cast<DbParameter>()
                .Where(x => x.Value is int)
                .Select(x => (int)x.Value!)
                .ToArray();
            Limits.AddRange(limits);
            if (command.CommandText.Contains("oee_production_facts", StringComparison.Ordinal))
            {
                FactLimits.AddRange(limits);
                FactCommands.Add(command.CommandText);
            }
            if (command.CommandText.Contains("device_state_snapshots", StringComparison.Ordinal))
            {
                StateLimits.AddRange(limits);
                StateCommands.Add(command.CommandText);
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoopMediator : IMediator
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
