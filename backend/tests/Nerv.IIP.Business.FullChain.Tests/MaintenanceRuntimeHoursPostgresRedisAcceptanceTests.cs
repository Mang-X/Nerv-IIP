using Nerv.IIP.Testing;
using Npgsql;
using StackExchange.Redis;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MaintenanceRuntimeHoursPostgresRedisAcceptanceTests
{
    [RealPostgresRedisMaintenanceRuntimeHoursFact]
    public async Task Scheduler_generates_plan_work_order_only_after_real_runtime_crosses_threshold()
    {
        var maintenancePostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_MAINTENANCE_POSTGRES")!;
        var industrialTelemetryPostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_INDUSTRIAL_TELEMETRY_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION")!;
        var probeRunId = Environment.GetEnvironmentVariable("NERV_IIP_TEST_PROBE_RUN_ID")!;
        var source = ProbeSource.Create(probeRunId);

        await WaitForMaintenanceRedisConsumerAsync(redis, capVersion);
        await SeedPlanAndInitialRuntimeAsync(maintenancePostgres, industrialTelemetryPostgres, source);

        // The scheduler runs in a separate process on the wall clock, so "nothing was generated below the
        // threshold" has no observable edge: it is a bounded stability window that keeps observing the same
        // fact and fails on the first violation instead of sleeping once and asserting once.
        var belowThreshold = await Consistently.StaysAsync(
            condition: $"no runtime-hour work order is generated for {source.PlanCode} while runtime stays below the threshold",
            observe: token => new ValueTask<MaintenanceFacts>(ReadFactsAsync(maintenancePostgres, source, token)),
            isSatisfied: facts => facts.WorkOrderCount == 0
                && facts.LastGeneratedRuntimeHours == 0m
                && facts.NextDueRuntimeHours == 1m,
            describe: DescribeFacts,
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(3),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [maintenancePostgres, industrialTelemetryPostgres, redis]));
        Assert.Equal(0, belowThreshold.WorkOrderCount);
        Assert.Equal(0m, belowThreshold.LastGeneratedRuntimeHours);
        Assert.Equal(1m, belowThreshold.NextDueRuntimeHours);

        await AppendThresholdCrossingRuntimeAsync(industrialTelemetryPostgres, source);

        // Real external scheduler process: the only observable fact is the Maintenance database, so poll it
        // on a bounded budget and report the last sanitized observation on timeout.
        var facts = await Eventually.WaitAsync(
            condition: $"the real scheduler generated the runtime-hour work order for {source.PlanCode}",
            observe: token => new ValueTask<MaintenanceFacts>(ReadFactsAsync(maintenancePostgres, source, token)),
            isSatisfied: observed => observed.WorkOrderCount == 1,
            describe: DescribeFacts,
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(30),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [maintenancePostgres, industrialTelemetryPostgres, redis]));
        Assert.Equal(1.25m, facts.LastGeneratedRuntimeHours);
        Assert.Equal(2m, facts.NextDueRuntimeHours);
        Assert.Equal($"{source.PlanCode}:runtime:1:1", facts.SourceReferenceId);

        // "Later scheduler ticks do not generate a second work order" is again a negative assertion about a
        // process the test cannot signal, so it stays a bounded stability window rather than a single sleep.
        var afterMoreSchedulerTicks = await Consistently.StaysAsync(
            condition: $"later scheduler ticks do not regenerate the runtime-hour work order for {source.PlanCode}",
            observe: token => new ValueTask<MaintenanceFacts>(ReadFactsAsync(maintenancePostgres, source, token)),
            isSatisfied: observed => observed.WorkOrderCount == 1
                && observed.LastGeneratedRuntimeHours == 1.25m
                && observed.NextDueRuntimeHours == 2m
                && observed.SourceReferenceId == facts.SourceReferenceId,
            describe: DescribeFacts,
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(2),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [maintenancePostgres, industrialTelemetryPostgres, redis]));
        Assert.Equal(1, afterMoreSchedulerTicks.WorkOrderCount);
        Assert.Equal(1.25m, afterMoreSchedulerTicks.LastGeneratedRuntimeHours);
        Assert.Equal(2m, afterMoreSchedulerTicks.NextDueRuntimeHours);
        Assert.Equal(facts.SourceReferenceId, afterMoreSchedulerTicks.SourceReferenceId);
    }

    private static string DescribeFacts(MaintenanceFacts facts) =>
        $"workOrders={facts.WorkOrderCount}; lastRuntime={facts.LastGeneratedRuntimeHours}; " +
        $"nextRuntime={facts.NextDueRuntimeHours}; sourceReference={facts.SourceReferenceId ?? "none"}";

    private static async Task WaitForMaintenanceRedisConsumerAsync(string redisConnectionString, string capVersion)
    {
        // The CAP consumer group proves that Maintenance is ready on Redis; runtime-hour scheduling itself pulls telemetry over HTTP.
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var expectedGroup = $"business-maintenance.alarm-raised.{capVersion}";

        // Real external process on Redis: bounded polling of an observable fact (the consumer group exists).
        // StackExchange.Redis exposes no CancellationToken overloads (its budget is the multiplexer's own
        // SyncTimeout/AsyncTimeout), so the token is genuinely unusable here rather than dropped; the
        // observation is still bounded because Eventually abandons an observation when the window closes.
        await Eventually.WaitAsync(
            condition: $"the Maintenance Redis CAP consumer group {expectedGroup} exists",
            observe: async _ =>
            {
                try
                {
                    var groups = await database.StreamGroupInfoAsync("AlarmRaisedIntegrationEvent");
                    return groups.Any(group => group.Name == expectedGroup)
                        ? "registered"
                        : $"stream present without {expectedGroup}";
                }
                catch (RedisServerException exception) when (
                    exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
                {
                    return "stream not created yet";
                }
            },
            isSatisfied: observation => observation == "registered",
            describe: observation => observation,
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromMinutes(2),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [redisConnectionString]));
    }

    private static async Task SeedPlanAndInitialRuntimeAsync(
        string maintenanceConnectionString,
        string industrialTelemetryConnectionString,
        ProbeSource source)
    {
        await using (var connection = new NpgsqlConnection(maintenanceConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO maintenance.maintenance_plans
                    (id, organization_id, environment_id, device_asset_id, plan_code, interval, starts_on,
                     last_generated_on, next_due_on, owner, window_start_utc, window_end_utc,
                     runtime_hour_interval, last_generated_runtime_hours, next_due_runtime_hours, paused, created_at_utc)
                VALUES
                    (@id, @organization_id, @environment_id, @device_asset_id, @plan_code, NULL, @starts_on,
                     NULL, NULL, 'system:man-440-acceptance', NULL, NULL, 1, 0, 1, FALSE, @created_at_utc);
                """;
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("organization_id", source.OrganizationId);
            command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
            command.Parameters.AddWithValue("device_asset_id", source.DeviceAssetId);
            command.Parameters.AddWithValue("plan_code", source.PlanCode);
            command.Parameters.AddWithValue("starts_on", source.StartsOn);
            command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        await InsertStatesAsync(
            industrialTelemetryConnectionString,
            source,
            ("running", source.WindowStartUtc, "initial-running"),
            ("stopped", source.WindowStartUtc.AddMinutes(30), "initial-stopped"));
    }

    private static Task AppendThresholdCrossingRuntimeAsync(string connectionString, ProbeSource source)
    {
        return InsertStatesAsync(
            connectionString,
            source,
            ("running", source.WindowStartUtc.AddHours(1), "threshold-running"),
            ("stopped", source.WindowStartUtc.AddHours(1).AddMinutes(45), "threshold-stopped"));
    }

    private static async Task InsertStatesAsync(
        string connectionString,
        ProbeSource source,
        params (string State, DateTimeOffset OccurredAtUtc, string Sequence)[] states)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var state in states)
        {
            var recordedAtUtc = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO industrial_telemetry.device_state_snapshots
                    ("Id", organization_id, environment_id, device_asset_id, state, occurred_at_utc,
                     occurred_at_unix_time_milliseconds, source_sequence, source_system, source_connector,
                     recorded_at_utc, recorded_at_unix_time_milliseconds)
                VALUES
                    (@id, @organization_id, @environment_id, @device_asset_id, @state, @occurred_at_utc,
                     @occurred_at_unix_time_milliseconds, @source_sequence, 'man-440-acceptance', 'external-probe',
                     @recorded_at_utc, @recorded_at_unix_time_milliseconds);
                """;
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("organization_id", source.OrganizationId);
            command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
            command.Parameters.AddWithValue("device_asset_id", source.DeviceAssetId);
            command.Parameters.AddWithValue("state", state.State);
            command.Parameters.AddWithValue("occurred_at_utc", state.OccurredAtUtc);
            command.Parameters.AddWithValue("occurred_at_unix_time_milliseconds", state.OccurredAtUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("source_sequence", $"{source.ProbeRunId}:{state.Sequence}");
            command.Parameters.AddWithValue("recorded_at_utc", recordedAtUtc);
            command.Parameters.AddWithValue("recorded_at_unix_time_milliseconds", recordedAtUtc.ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Reads the observable Maintenance facts. The <see cref="CancellationToken"/> is required rather than
    /// optional on purpose: this runs inside an <c>Eventually</c>/<c>Consistently</c> window, and every
    /// step of it (opening the connection, executing the query, reading the row) can block. A discarded
    /// token here would give the query no budget at all and would hold the surrounding window open.
    /// </summary>
    private static async Task<MaintenanceFacts> ReadFactsAsync(
        string connectionString,
        ProbeSource source,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.last_generated_runtime_hours, p.next_due_runtime_hours,
                   COUNT(w.id), MAX(w.source_reference_id)
            FROM maintenance.maintenance_plans p
            LEFT JOIN maintenance.maintenance_work_orders w
              ON w.organization_id = p.organization_id
             AND w.environment_id = p.environment_id
             AND w.source_type = 'plan'
             AND w.source_plan_code = p.plan_code
            WHERE p.organization_id = @organization_id
              AND p.environment_id = @environment_id
              AND p.plan_code = @plan_code
            GROUP BY p.last_generated_runtime_hours, p.next_due_runtime_hours;
            """;
        command.Parameters.AddWithValue("organization_id", source.OrganizationId);
        command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        command.Parameters.AddWithValue("plan_code", source.PlanCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("MAN-440 acceptance plan disappeared from PostgreSQL.");
        }

        return new MaintenanceFacts(
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private sealed record ProbeSource(
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string PlanCode,
        string ProbeRunId,
        DateOnly StartsOn,
        DateTimeOffset WindowStartUtc)
    {
        public static ProbeSource Create(string probeRunId)
        {
            var suffix = probeRunId.Replace("-", string.Empty, StringComparison.Ordinal);
            suffix = suffix.Length <= 20 ? suffix : suffix[^20..];
            var startsOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            return new ProbeSource(
                "org-man440",
                "env-man440",
                $"DEV-MAN440-{suffix}",
                $"PM-MAN440-{suffix}",
                probeRunId,
                startsOn,
                new DateTimeOffset(startsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        }
    }

    private sealed record MaintenanceFacts(
        decimal LastGeneratedRuntimeHours,
        decimal NextDueRuntimeHours,
        int WorkOrderCount,
        string? SourceReferenceId);
}

internal sealed class RealPostgresRedisMaintenanceRuntimeHoursFactAttribute : FactAttribute
{
    public RealPostgresRedisMaintenanceRuntimeHoursFactAttribute()
    {
        var required = new[]
        {
            "NERV_IIP_TEST_MAINTENANCE_POSTGRES",
            "NERV_IIP_TEST_INDUSTRIAL_TELEMETRY_POSTGRES",
            "NERV_IIP_TEST_REDIS",
            "NERV_IIP_TEST_CAP_VERSION",
            "NERV_IIP_TEST_PROBE_RUN_ID",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-440 Maintenance/IndustrialTelemetry PostgreSQL, Redis, CAP version, and probe-run variables to run the external-process PM probe.";
        }
    }
}
