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

        await Task.Delay(TimeSpan.FromSeconds(3));
        var belowThreshold = await ReadFactsAsync(maintenancePostgres, source);
        Assert.Equal(0, belowThreshold.WorkOrderCount);
        Assert.Equal(0m, belowThreshold.LastGeneratedRuntimeHours);
        Assert.Equal(1m, belowThreshold.NextDueRuntimeHours);

        await AppendThresholdCrossingRuntimeAsync(industrialTelemetryPostgres, source);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var facts = await ReadFactsAsync(maintenancePostgres, source);
            if (facts.WorkOrderCount == 1)
            {
                Assert.Equal(1.25m, facts.LastGeneratedRuntimeHours);
                Assert.Equal(2m, facts.NextDueRuntimeHours);
                Assert.Equal($"{source.PlanCode}:runtime:1:1", facts.SourceReferenceId);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        var finalFacts = await ReadFactsAsync(maintenancePostgres, source);
        throw new TimeoutException(
            "MAN-440 runtime-hour plan did not generate through the real scheduler within 30 seconds. " +
            $"WorkOrders={finalFacts.WorkOrderCount}, LastRuntime={finalFacts.LastGeneratedRuntimeHours}, " +
            $"NextRuntime={finalFacts.NextDueRuntimeHours}, SourceReference={finalFacts.SourceReferenceId}.");
    }

    private static async Task WaitForMaintenanceRedisConsumerAsync(string redisConnectionString, string capVersion)
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var groups = await database.StreamGroupInfoAsync("AlarmRaisedIntegrationEvent");
                if (groups.Any(group => group.Name == $"business-maintenance.alarm-raised.{capVersion}"))
                {
                    return;
                }
            }
            catch (RedisServerException exception) when (
                exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException("MAN-440 timed out waiting for the Maintenance Redis CAP consumer group.");
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

    private static async Task<MaintenanceFacts> ReadFactsAsync(string connectionString, ProbeSource source)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
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
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
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
