using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCorePal.Extensions.Primitives;
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
    public async Task Same_occurred_at_states_use_recorded_at_and_source_sequence_for_carry_in_and_window_tail_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        var interceptor = new StateQueryLimitCaptureInterceptor();
        await using var dbContext = CreateLaneDbContext(interceptor);
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var windowStart = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        dbContext.OeeProductionFacts.Add(Fact(
            "PRPT-STATE-TIE", windowStart.AddMinutes(1), "DEV-STATE-TIE", "WC-STATE-TIE",
            5m, 0m, "PCS", 10m));
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO industrial_telemetry.device_state_snapshots
                ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                 occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                 recorded_at_utc, recorded_at_unix_time_milliseconds)
            VALUES
                ((md5('state-tie-carry-wrong'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'idle',
                 TIMESTAMPTZ '2026-06-30T23:59:00Z', 1782863940000, 'Z-carry', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:01:00Z', 1782864060000),
                ((md5('state-tie-carry-correct'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'planned-down',
                 TIMESTAMPTZ '2026-06-30T23:59:00Z', 1782863940000, 'A-carry', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:02:00Z', 1782864120000),
                ((md5('state-tie-window-recorded-correct'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'running',
                 TIMESTAMPTZ '2026-07-01T00:30:00Z', 1782865800000, 'A-window', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:04:00Z', 1782864240000),
                ((md5('state-tie-window-recorded-wrong'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'idle',
                 TIMESTAMPTZ '2026-07-01T00:30:00Z', 1782865800000, 'Z-window', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:03:00Z', 1782864180000),
                ((md5('state-tie-window-sequence-correct'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'running',
                 TIMESTAMPTZ '2026-07-01T00:45:00Z', 1782866700000, 'Z-sequence', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:05:00Z', 1782864300000),
                ((md5('state-tie-window-sequence-wrong'))::uuid, 'org-001', 'env-dev', 'DEV-STATE-TIE', 'idle',
                 TIMESTAMPTZ '2026-07-01T00:45:00Z', 1782866700000, 'A-sequence', NULL, NULL,
                 TIMESTAMPTZ '2026-07-01T00:05:00Z', 1782864300000);
            """);

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.WorkCenter,
                windowStart, windowStart.AddHours(1), WorkCenterId: "WC-STATE-TIE"),
            CancellationToken.None);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(5, bucket.StateSampleCount);
        Assert.Equal(1m, bucket.AvailabilityRate);
        Assert.Equal(1m, bucket.PerformanceRate);
        Assert.Equal(5m, bucket.ExpectedOutputQuantity);
        Assert.Equal(1m, bucket.OeeRate);
        var stateCommands = interceptor.ExecutedStateCommands
            .Select(command => string.Join(' ', command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace("\"", string.Empty, StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(stateCommands, command => command.Contains(
            "ORDER BY d.occurred_at_utc, d.recorded_at_utc, d.source_sequence",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stateCommands, command => command.Contains(
            "ORDER BY d0.occurred_at_utc DESC, d0.recorded_at_utc DESC, d0.source_sequence DESC",
            StringComparison.OrdinalIgnoreCase));
    }

    [RealPostgresFact]
    public async Task Combined_carry_in_and_window_state_limit_executes_exact_boundary_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using (var migrationContext = CreateLaneDbContext())
        {
            IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(migrationContext);
            await migrationContext.Database.MigrateAsync();
            var start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            migrationContext.OeeProductionFacts.Add(Fact(
                "PRPT-STATE-BOUNDARY", start.AddMinutes(1), "DEV-STATE-BOUNDARY", "WC-STATE-BOUNDARY",
                10m, 0m, "PCS", 10m));
            await migrationContext.SaveChangesAsync();
            await migrationContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO industrial_telemetry.device_state_snapshots
                    ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                     occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                     recorded_at_utc, recorded_at_unix_time_milliseconds)
                SELECT (md5('state-boundary-' || sample::text))::uuid,
                       'org-001', 'env-dev', 'DEV-STATE-BOUNDARY', 'running',
                       TIMESTAMPTZ '2026-07-01T00:00:00Z' + sample * INTERVAL '1 second',
                       (EXTRACT(EPOCH FROM (TIMESTAMPTZ '2026-07-01T00:00:00Z' + sample * INTERVAL '1 second')) * 1000)::bigint,
                       'state-boundary-' || sample::text, NULL, NULL,
                       TIMESTAMPTZ '2026-07-02T00:00:00Z', 1782864000000
                FROM generate_series(-1, 9998) AS sample;
                """);
        }

        var interceptor = new StateQueryLimitCaptureInterceptor();
        await using var dbContext = CreateLaneDbContext(interceptor);
        var windowStart = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var request = new QueryOeeAggregateBucketsQuery(
            "org-001", "env-dev", OeeAggregateDimensions.WorkCenter,
            windowStart, windowStart.AddDays(1), WorkCenterId: "WC-STATE-BOUNDARY");

        var accepted = await new QueryOeeAggregateBucketsQueryHandler(dbContext)
            .Handle(request, CancellationToken.None);

        Assert.Equal(OeeAggregateMaterializationLimits.MaximumStateSampleCount, Assert.Single(accepted.Buckets).StateSampleCount);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount + 1, interceptor.ExecutedLimits);
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount, interceptor.ExecutedLimits);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO industrial_telemetry.device_state_snapshots
                ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                 occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                 recorded_at_utc, recorded_at_unix_time_milliseconds)
            VALUES ((md5('state-boundary-9999'))::uuid,
                    'org-001', 'env-dev', 'DEV-STATE-BOUNDARY', 'running',
                    TIMESTAMPTZ '2026-07-01T02:46:39Z', 1782873999000,
                    'state-boundary-9999', NULL, NULL,
                    TIMESTAMPTZ '2026-07-02T00:00:00Z', 1782864000000);
            """);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(request, CancellationToken.None));
        Assert.Contains(OeeAggregateMaterializationLimits.MaximumStateSampleCount.ToString(), exception.Message, StringComparison.Ordinal);
    }

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
            State("org-001", "DEV-02", "running", start, "state-03"),
            State("org-001", "DEV-01", "stopped", start, "state-other-environment", environmentId: "env-other"));
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-WEIGHTED-01", start.AddMinutes(30), "DEV-01", "WC-WEIGHTED", 80m, 20m, "PCS", 100m),
            Fact("PRPT-WEIGHTED-02", start.AddMinutes(45), "DEV-02", "WC-WEIGHTED", 180m, 20m, "PCS", 200m),
            Fact("PRPT-OTHER-TENANT", start.AddMinutes(30), "DEV-01", "WC-WEIGHTED", 999m, 1m, "PCS", 100m, organizationId: "org-other"),
            Fact("PRPT-OTHER-ENVIRONMENT", start.AddMinutes(30), "DEV-01", "WC-WEIGHTED", 777m, 223m, "PCS", 100m, environmentId: "env-other"));
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
    public async Task Device_hierarchy_migration_degrades_runtime_metrics_instead_of_reusing_the_full_window_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = start.AddHours(2);
        dbContext.DeviceStateSnapshots.Add(State("org-001", "DEV-MOVED", "running", start, "state-moved"));
        dbContext.OeeProductionFacts.AddRange(
            FactWithHierarchy("PRPT-MOVED-A", start.AddMinutes(30), "DEV-MOVED", "WC-A", "LINE-A", "WORKSHOP-A", 10m, 10m),
            FactWithHierarchy("PRPT-MOVED-B", start.AddMinutes(90), "DEV-MOVED", "WC-B", "LINE-B", "WORKSHOP-B", 10m, 10m));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Line, start, end),
            CancellationToken.None);

        Assert.Equal(
            ["LINE-A", "LINE-B"],
            result.Buckets.Select(x => Assert.IsType<string>(x.DimensionValue)).Order().ToArray());
        Assert.All(result.Buckets, bucket =>
        {
            Assert.Equal(10m, bucket.GoodQuantity);
            Assert.Null(bucket.AvailabilityRate);
            Assert.Null(bucket.PerformanceRate);
            Assert.Null(bucket.QualityRate);
            Assert.Null(bucket.OeeRate);
            Assert.Null(bucket.ExpectedOutputQuantity);
            Assert.True(bucket.IsDegraded);
            Assert.Contains("historical-dimension-effective-range-ambiguous", bucket.DegradedReasons);
        });
    }

    [RealPostgresFact]
    public async Task State_queries_ignore_same_device_and_environment_from_other_organizations_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = start.AddHours(1);
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-ORG-IN", start.AddMinutes(45), "DEV-ORG-IN", "WC-ORG-IN", 10m, 0m, "PCS", 10m),
            Fact("PRPT-ORG-CARRY", start.AddMinutes(45), "DEV-ORG-CARRY", "WC-ORG-CARRY", 10m, 0m, "PCS", 10m));
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO industrial_telemetry.device_state_snapshots
                ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                 occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                 recorded_at_utc, recorded_at_unix_time_milliseconds)
            VALUES
                ((md5('org-in-target'))::uuid, 'org-001', 'env-dev', 'DEV-ORG-IN', 'running',
                 TIMESTAMPTZ '2026-07-10T08:00:00Z', 1783670400000, 'target-in', NULL, NULL,
                 TIMESTAMPTZ '2026-07-10T08:00:01Z', 1783670401000),
                ((md5('org-in-interference'))::uuid, 'org-other', 'env-dev', 'DEV-ORG-IN', 'planned-down',
                 TIMESTAMPTZ '2026-07-10T08:30:00Z', 1783672200000, 'other-in', NULL, NULL,
                 TIMESTAMPTZ '2026-07-10T08:30:01Z', 1783672201000),
                ((md5('org-carry-target'))::uuid, 'org-001', 'env-dev', 'DEV-ORG-CARRY', 'running',
                 TIMESTAMPTZ '2026-07-10T07:50:00Z', 1783669800000, 'target-carry', NULL, NULL,
                 TIMESTAMPTZ '2026-07-10T07:50:01Z', 1783669801000),
                ((md5('org-carry-interference'))::uuid, 'org-other', 'env-dev', 'DEV-ORG-CARRY', 'planned-down',
                 TIMESTAMPTZ '2026-07-10T07:59:00Z', 1783670340000, 'other-carry', NULL, NULL,
                 TIMESTAMPTZ '2026-07-10T07:59:01Z', 1783670341000);
            """);

        var handler = new QueryOeeAggregateBucketsQueryHandler(dbContext);
        var inWindow = await handler.Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Device, start, end, DeviceAssetId: "DEV-ORG-IN"),
            CancellationToken.None);
        var carryIn = await handler.Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Device, start, end, DeviceAssetId: "DEV-ORG-CARRY"),
            CancellationToken.None);

        var inWindowBucket = Assert.Single(inWindow.Buckets);
        Assert.Equal(1m, inWindowBucket.AvailabilityRate);
        Assert.Equal(1m, inWindowBucket.PerformanceRate);
        Assert.Equal(10m, inWindowBucket.ExpectedOutputQuantity);
        var carryInBucket = Assert.Single(carryIn.Buckets);
        Assert.Equal(1m, carryInBucket.AvailabilityRate);
        Assert.Equal(1m, carryInBucket.PerformanceRate);
        Assert.Equal(10m, carryInBucket.ExpectedOutputQuantity);
    }

    [RealPostgresFact]
    public async Task State_starting_after_the_window_start_degrades_apq_instead_of_assuming_prefix_coverage_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var end = start.AddHours(1);
        dbContext.DeviceStateSnapshots.Add(
            State("org-001", "DEV-PREFIX-GAP", "running", start.AddMinutes(30), "state-prefix-gap"));
        dbContext.OeeProductionFacts.Add(
            Fact("PRPT-PREFIX-GAP", start.AddMinutes(45), "DEV-PREFIX-GAP", "WC-PREFIX-GAP", 5m, 0m, "PCS", 10m));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.WorkCenter,
                start, end, WorkCenterId: "WC-PREFIX-GAP"),
            CancellationToken.None);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(1, bucket.StateSampleCount);
        Assert.Null(bucket.AvailabilityRate);
        Assert.Null(bucket.PerformanceRate);
        Assert.Null(bucket.QualityRate);
        Assert.Null(bucket.OeeRate);
        Assert.True(bucket.IsDegraded);
        Assert.Contains("runtime-state-coverage-incomplete", bucket.DegradedReasons);
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
                DateTimeOffset.Parse("2026-07-11T16:00:00Z")),
            CancellationToken.None);
        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(2, bucket.ProductionFactCount);
        Assert.Equal(0m, bucket.GoodQuantity);

        var reversalWindow = await new QueryOeeAggregateBucketsQueryHandler(dbContext).Handle(
            new QueryOeeAggregateBucketsQuery(
                "org-001", "env-dev", OeeAggregateDimensions.Day,
                DateTimeOffset.Parse("2026-07-11T16:00:00Z"),
                DateTimeOffset.Parse("2026-07-12T16:00:00Z")),
            CancellationToken.None);
        Assert.Empty(reversalWindow.Buckets);
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

    private static ApplicationDbContext CreateLaneDbContext(DbCommandInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"));
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }
        return new ApplicationDbContext(optionsBuilder.Options, new NoopMediator());
    }

    private sealed class StateQueryLimitCaptureInterceptor : DbCommandInterceptor
    {
        public List<int> ExecutedLimits { get; } = [];
        public List<string> ExecutedStateCommands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("device_state_snapshots", StringComparison.Ordinal))
            {
                ExecutedStateCommands.Add(command.CommandText);
                ExecutedLimits.AddRange(command.Parameters.Cast<DbParameter>()
                    .Where(parameter => parameter.Value is int)
                    .Select(parameter => (int)parameter.Value!));
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
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
        string organizationId = "org-001",
        string environmentId = "env-dev") =>
        OeeProductionFact.Project(
            organizationId,
            environmentId,
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

    private static OeeProductionFact FactWithHierarchy(
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string deviceAssetId,
        string workCenterId,
        string lineCode,
        string workshopCode,
        decimal goodQuantity,
        decimal theoreticalRatePerHour) =>
        OeeProductionFact.Project(
            "org-001",
            "env-dev",
            reportNo,
            workCenterId,
            deviceAssetId,
            goodQuantity,
            0m,
            0m,
            "PCS",
            theoreticalRatePerHour,
            reportedAtUtc,
            new OeeHistoricalDimensionSnapshot(
                "SITE-01",
                workshopCode,
                lineCode,
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
        string sequence,
        string environmentId = "env-dev") =>
        DeviceStateSnapshot.Record(
            organizationId,
            environmentId,
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
