using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingEngineTracePostgresTests(ITestOutputHelper output)
{
    private const string PreviousMigration = "20260722164839_AddOrderUrgencyArchiveMembership";
    private const string TraceMigration = "AddSchedulingEngineProviderTrace";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostgreSql18_migration_backfills_legacy_trace_and_round_trips_exact_new_engine_input()
    {
        var dockerAvailability = await RunDockerAsync(["info", "--format", "{{.ServerVersion}}"], TimeSpan.FromSeconds(15));
        if (dockerAvailability.StartError is not null || dockerAvailability.ExitCode != 0)
        {
            throw SkipException.ForSkip("Docker daemon is unavailable; PostgreSQL 18 scheduling trace verification was skipped.");
        }

        var containerName = $"nerv-man422-postgres-{Guid.NewGuid():N}";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Exception? testFailure = null;
        var containerMayExist = false;
        output.WriteLine($"PostgreSQL 18 container: {containerName}");

        try
        {
            containerMayExist = true;
            var run = await RunDockerAsync(
                [
                    "run",
                    "-d",
                    "--name",
                    containerName,
                    "-e",
                    $"POSTGRES_PASSWORD={password}",
                    "-p",
                    "127.0.0.1::5432",
                    "postgres:18",
                ],
                TimeSpan.FromMinutes(2));
            Assert.True(run.ExitCode == 0, "Unable to start the isolated PostgreSQL 18 container.");

            var portResult = await RunDockerAsync(
                ["port", containerName, "5432/tcp"],
                TimeSpan.FromSeconds(15));
            Assert.True(portResult.ExitCode == 0, "Unable to discover the isolated PostgreSQL 18 host port.");
            var port = ParsePort(portResult.StandardOutput);
            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Database = "postgres",
                Username = "postgres",
                Password = password,
                Pooling = false,
                Timeout = 3,
                CommandTimeout = 30,
            }.ConnectionString;
            await WaitUntilReadyAsync(connectionString, TimeSpan.FromSeconds(45));

            await using var context = CreateContext(connectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await SeedLegacyRowsAsync(connectionString);
            await migrator.MigrateAsync(TraceMigration);

            await AssertLegacyBackfillAsync(connectionString);

            const string constraintSourcesJson =
                """{"schemaVersion":1,"sources":[{"sourceId":"equipment-runtime-availability","sourceVersion":"v2","outcome":"applied","factCount":1,"factsFingerprint":"equipment-fingerprint","reasonCodes":["equipmentUnavailable"]}]}""";
            context.ScheduleProblems.Add(new ScheduleProblemSnapshot(
                problemId: "problem-new",
                contractVersion: 1,
                organizationId: "org-new",
                environmentId: "env-new",
                problemFingerprint: "base-fingerprint",
                problemJson: """{"problemId":"base-problem"}""",
                horizonStartUtc: FixedNow,
                horizonEndUtc: FixedNow.AddDays(1),
                capturedAtUtc: FixedNow,
                engineInputFingerprint: "effective-fingerprint",
                engineInputJson: """{"problemId":"effective-problem"}"""));
            context.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
                "org-new",
                "env-new",
                CreatePlanSnapshot(),
                new SchedulePlanExecutionTraceSnapshot(
                    EngineId: "finite-capacity",
                    RuleProviderId: "built-in",
                    RuleProfileId: "adr-0014-default",
                    RuleProfileVersion: "v1",
                    ConstraintSourcesJson: constraintSourcesJson,
                    TraceSchemaVersion: SchedulingExecutionTraceSchema.CurrentVersion,
                    ReplayStatus: SchedulingReplayStatuses.Available)));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var problem = await context.ScheduleProblems.AsNoTracking()
                .SingleAsync(x => x.ProblemId == "problem-new");
            Assert.Equal("base-fingerprint", problem.ProblemFingerprint);
            Assert.Equal("effective-fingerprint", problem.EngineInputFingerprint);
            Assert.Equal("base-problem", JsonDocument.Parse(problem.ProblemJson).RootElement.GetProperty("problemId").GetString());
            Assert.Equal("effective-problem", JsonDocument.Parse(problem.EngineInputJson!).RootElement.GetProperty("problemId").GetString());

            var plan = await context.SchedulePlans.AsNoTracking()
                .SingleAsync(x => x.PlanId == "plan-new");
            Assert.Equal("finite-capacity", plan.EngineId);
            Assert.Equal("aps-lite-v1", plan.AlgorithmVersion);
            Assert.Equal("built-in", plan.RuleProviderId);
            Assert.Equal("adr-0014-default", plan.RuleProfileId);
            Assert.Equal("v1", plan.RuleProfileVersion);
            Assert.Equal(SchedulingReplayStatuses.Available, plan.ReplayStatus);
            var constraintDocument = JsonDocument.Parse(plan.ConstraintSourcesJson);
            Assert.Equal(1, constraintDocument.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "equipment-runtime-availability",
                constraintDocument.RootElement.GetProperty("sources")[0].GetProperty("sourceId").GetString());
        }
        catch (Exception exception)
        {
            testFailure = exception;
        }
        finally
        {
            if (containerMayExist)
            {
                _ = await RunDockerAsync(["rm", "-f", containerName], TimeSpan.FromSeconds(30));
            }
        }

        var inspect = await RunDockerAsync(["inspect", containerName], TimeSpan.FromSeconds(15));
        Assert.NotEqual(0, inspect.ExitCode);
        var dockerAfterCleanup = await RunDockerAsync(
            ["info", "--format", "{{.ServerVersion}}"],
            TimeSpan.FromSeconds(15));
        Assert.Equal(0, dockerAfterCleanup.ExitCode);
        output.WriteLine($"Cleanup verified: docker inspect {containerName} returned not found.");
        if (testFailure is not null)
        {
            ExceptionDispatchInfo.Capture(testFailure).Throw();
        }
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "scheduling"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static GeneratedSchedulePlanSnapshot CreatePlanSnapshot()
    {
        return new GeneratedSchedulePlanSnapshot(
            PlanId: "plan-new",
            ProblemId: "problem-new",
            ProblemFingerprint: "base-fingerprint",
            AlgorithmVersion: "aps-lite-v1",
            ContractVersion: 1,
            GeneratedAtUtc: FixedNow,
            Status: SchedulePlanInputStatus.Generated,
            Metrics: new GeneratedSchedulePlanMetricsSnapshot(0, 0, 0, 0, 0, 0, 1m, 0m),
            Assignments: [],
            ResourceLoads: [],
            Conflicts: [],
            UnscheduledOperations: []);
    }

    private static async Task SeedLegacyRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO scheduling.schedule_problems (
                id, problem_id, contract_version, organization_id, environment_id,
                problem_fingerprint, problem_json, horizon_start_utc, horizon_end_utc, captured_at_utc)
            VALUES (
                @problem_row_id, 'problem-legacy', 1, 'org-legacy', 'env-legacy',
                'legacy-base-fingerprint', '{"problemId":"legacy-base"}'::jsonb,
                @horizon_start, @horizon_end, @captured_at);

            INSERT INTO scheduling.schedule_plans (
                id, organization_id, environment_id, plan_id, problem_id, problem_fingerprint,
                algorithm_version, contract_version, status, generated_at_utc,
                scheduled_operation_count, unscheduled_operation_count, locked_operation_count,
                optimizable_operation_count, assigned_minutes, makespan_minutes,
                total_tardiness_minutes, late_operation_count, on_time_rate,
                average_resource_utilization)
            VALUES (
                @plan_row_id, 'org-legacy', 'env-legacy', 'plan-legacy', 'problem-legacy',
                'legacy-base-fingerprint', 'aps-lite-v1', 1, 'Generated', @captured_at,
                0, 0, 0, 0, 0, 0, 0, 0, 1, 0);
            """,
            connection);
        command.Parameters.AddWithValue("problem_row_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("plan_row_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("horizon_start", FixedNow);
        command.Parameters.AddWithValue("horizon_end", FixedNow.AddDays(1));
        command.Parameters.AddWithValue("captured_at", FixedNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertLegacyBackfillAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT p.engine_input_json, p.engine_input_fingerprint,
                   h.engine_id, h.algorithm_version, h.rule_provider_id,
                   h.rule_profile_id, h.rule_profile_version,
                   h.constraint_sources_json, h.trace_schema_version, h.replay_status
            FROM scheduling.schedule_problems p
            JOIN scheduling.schedule_plans h
              ON h.organization_id = p.organization_id
             AND h.environment_id = p.environment_id
             AND h.problem_id = p.problem_id
            WHERE p.problem_id = 'problem-legacy'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.IsDBNullAsync(0));
        Assert.True(await reader.IsDBNullAsync(1));
        Assert.Equal("finite-capacity", reader.GetString(2));
        Assert.Equal("aps-lite-v1", reader.GetString(3));
        Assert.Equal("built-in", reader.GetString(4));
        Assert.Equal("adr-0014-default", reader.GetString(5));
        Assert.Equal("v1", reader.GetString(6));
        Assert.Equal(
            "legacy-unavailable",
            JsonDocument.Parse(reader.GetString(7)).RootElement.GetProperty("status").GetString());
        Assert.Equal(1, reader.GetInt32(8));
        Assert.Equal(SchedulingReplayStatuses.LegacyUnavailable, reader.GetString(9));
    }

    private static async Task WaitUntilReadyAsync(string connectionString, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException exception)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        throw new TimeoutException("The isolated PostgreSQL 18 container did not become ready in time.", lastError);
    }

    private static int ParsePort(string output)
    {
        var endpoint = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single();
        var separator = endpoint.LastIndexOf(':');
        return int.Parse(endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<ProcessResult> RunDockerAsync(
        IReadOnlyCollection<string> arguments,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                return new ProcessResult(-1, await standardOutput, await standardError, null);
            }

            return new ProcessResult(
                process.ExitCode,
                await standardOutput,
                await standardError,
                null);
        }
        catch (Win32Exception exception)
        {
            return new ProcessResult(-1, string.Empty, string.Empty, exception);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        Exception? StartError);

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(
            object notification,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;
    }
}
