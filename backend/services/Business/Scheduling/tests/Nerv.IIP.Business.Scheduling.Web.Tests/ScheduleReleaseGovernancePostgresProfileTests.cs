using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class ScheduleReleaseGovernancePostgresProfileTests
{
    private const string PreviousMigration = "20260715095540_AddSchedulingOverrideRevocationTombstones";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 18, 2, 0, 0, TimeSpan.Zero);

    [SchedulingPostgresFact]
    public async Task Migration_normalizes_historical_duplicate_releases_with_exact_timestamp_tie()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(adminConnectionString, "nerv_scheduling_test");
        await using var context = CreateContext(database.ConnectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await SeedHistoricalReleasedRowsAsync(database.ConnectionString);

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT plan_id, status, release_revision, superseded_by_plan_id
            FROM scheduling.schedule_plans
            ORDER BY plan_id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("plan-a", reader.GetString(0));
        Assert.Equal("Superseded", reader.GetString(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal("plan-b", reader.GetString(3));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("plan-b", reader.GetString(0));
        Assert.Equal("Released", reader.GetString(1));
        Assert.Equal(2, reader.GetInt64(2));
        Assert.True(await reader.IsDBNullAsync(3));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();

        await using var indexCommand = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'scheduling' AND tablename = 'schedule_plans' AND indexname LIKE 'ux_schedule_plans_scope_%' ORDER BY indexname",
            connection);
        await using var indexReader = await indexCommand.ExecuteReaderAsync();
        var indexNames = new List<string>();
        while (await indexReader.ReadAsync())
        {
            indexNames.Add(indexReader.GetString(0));
        }

        Assert.Equal([
            "ux_schedule_plans_scope_active_release",
            "ux_schedule_plans_scope_release_revision",
        ], indexNames);
    }

    [SchedulingPostgresFact]
    public async Task Concurrent_releases_converge_to_one_active_plan_with_monotonic_revisions()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(adminConnectionString, "nerv_scheduling_test");
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.SchedulePlans.AddRange(CreatePlan("plan-a"), CreatePlan("plan-b"));
            await setup.SaveChangesAsync();
        }

        await Task.WhenAll(
            ReleaseAsync(database.ConnectionString, "plan-a"),
            ReleaseAsync(database.ConnectionString, "plan-b"));

        await using var assertion = CreateContext(database.ConnectionString);
        var plans = await assertion.SchedulePlans.OrderBy(x => x.ReleaseRevision).ToArrayAsync();
        Assert.Equal(new long?[] { 1, 2 }, plans.Select(x => x.ReleaseRevision));
        Assert.Single(plans, x => x.Status == SchedulePlanLifecycleStatus.Released);
        Assert.Single(plans, x => x.Status == SchedulePlanLifecycleStatus.Superseded);
        Assert.Equal(
            plans.Single(x => x.Status == SchedulePlanLifecycleStatus.Released).PlanId,
            plans.Single(x => x.Status == SchedulePlanLifecycleStatus.Superseded).SupersededByPlanId);
    }

    private static async Task ReleaseAsync(string connectionString, string planId)
    {
        await using var context = CreateContext(connectionString);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var handler = new ReleaseSchedulePlanCommandHandler(
            context,
            new FixedTimeProvider(FixedNow),
            new PostgreSqlScheduleReleaseScopeLock(context));
        await handler.Handle(
            new ReleaseSchedulePlanCommand(planId, "org-001", "env-dev"),
            CancellationToken.None);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "scheduling"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static SchedulePlan CreatePlan(string planId)
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                1,
                planId,
                $"problem-{planId}",
                $"fingerprint-{planId}",
                "aps-lite-v1",
                SchedulePlanStatusContract.Generated,
                FixedNow.AddHours(-2),
                new SchedulePlanMetricsContract(1, 0, 60, 60, 0, 0, 1m, 0m),
                [new ScheduleAssignmentContract(
                    $"assignment-{planId}",
                    "WO-001",
                    "OP-001",
                    10,
                    "ASSET-001",
                    "WC-001",
                    FixedNow,
                    FixedNow.AddHours(1),
                    false,
                    "scheduled")],
                [],
                [],
                [],
                [],
                [])));
    }

    private static async Task SeedHistoricalReleasedRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO scheduling.schedule_plans (
                id, organization_id, environment_id, plan_id, problem_id, problem_fingerprint,
                algorithm_version, contract_version, status, generated_at_utc, released_at_utc,
                scheduled_operation_count, unscheduled_operation_count, locked_operation_count,
                optimizable_operation_count, assigned_minutes, makespan_minutes,
                total_tardiness_minutes, late_operation_count, on_time_rate,
                average_resource_utilization)
            VALUES
                (@id_a, 'org-001', 'env-dev', 'plan-a', 'problem-a', 'fingerprint-a',
                 'aps-lite-v1', 1, 'Released', @occurred_at, @occurred_at,
                 0, 0, 0, 0, 0, 0, 0, 0, 1, 0),
                (@id_b, 'org-001', 'env-dev', 'plan-b', 'problem-b', 'fingerprint-b',
                 'aps-lite-v1', 1, 'Released', @occurred_at, @occurred_at,
                 0, 0, 0, 0, 0, 0, 0, 0, 1, 0)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id_a", Guid.CreateVersion7());
        command.Parameters.AddWithValue("id_b", Guid.CreateVersion7());
        command.Parameters.AddWithValue("occurred_at", FixedNow);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
