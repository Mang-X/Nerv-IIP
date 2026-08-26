using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.DependencyInjection;
using Npgsql;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class WorkCenterMachineOverheadRatePostgresAcceptanceTests
{
    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task Concurrent_commands_serialize_revision_allocation_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-machine-rate-concurrency-{Guid.CreateVersion7():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);
        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-concurrent", "env-concurrent", "2026-06",
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(ConfigureWorkCenterMachineOverheadRateCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddErpPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlWorkCenterMachineOverheadRateRevisionLock(gateDb).AcquireAsync(
            "org-concurrent", "env-concurrent", "WC-CONCURRENT", CancellationToken.None);

        var firstSend = firstScope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(30_000m, "user:first"));
        var secondSend = secondScope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(31_000m, "user:second"));
        var gateReleased = false;
        try
        {
            await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 2);
            Assert.False(firstSend.IsCompleted);
            Assert.False(secondSend.IsCompleted);
            await gateTransaction.CommitAsync();
            gateReleased = true;

            var ids = await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, ids.Distinct().Count());
        }
        finally
        {
            if (!gateReleased) await gateTransaction.RollbackAsync();
            try
            {
                await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Preserve the primary assertion or command failure while observing both tasks.
            }
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var revisions = await assertDb.WorkCenterMachineOverheadRates
            .Where(x => x.OrganizationId == "org-concurrent"
                && x.EnvironmentId == "env-concurrent"
                && x.WorkCenterId == "WC-CONCURRENT"
                && x.AccountingPeriodCode == "2026-06")
            .OrderBy(x => x.Revision)
            .Select(x => x.Revision)
            .ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, revisions);
    }

    [ErpCostPostgresFact]
    public async Task Migration_persists_monthly_rate_and_enforces_scope_period_revision_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();

        db.AccountingPeriods.Add(AccountingPeriod.Open(
            "org-pg", "env-pg", "2026-06", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        db.WorkCenterMachineOverheadRates.Add(
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-06",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "initial rate", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.WorkCenterMachineOverheadRates.SingleAsync();
        Assert.Equal(30m, persisted.FixedHourlyRate);
        Assert.Equal(10m, persisted.VariableHourlyRate);
        Assert.Equal(40m, persisted.TotalHourlyRate);

        var audit = await new ListWorkCenterMachineOverheadRatesQueryHandler(db).Handle(
            new ListWorkCenterMachineOverheadRatesQuery("org-pg", "env-pg", "WC-PG", "2026-06"),
            CancellationToken.None);
        Assert.Equal(1, audit.CurrentRevision);
        Assert.Equal("Applicable", Assert.Single(audit.Items).Applicability);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using (var invalidCostBasis = new NpgsqlCommand("""
            INSERT INTO erp.work_center_machine_overhead_rates
                (id, organization_id, environment_id, work_center_id, accounting_period_code,
                 applicability, fixed_overhead_budget, variable_overhead_budget,
                 normal_capacity_machine_hours, fixed_hourly_rate, variable_hourly_rate,
                 total_hourly_rate, currency_code, revision, changed_by, reason, changed_at_utc)
            VALUES
                (@id, 'org-pg', 'env-pg', 'WC-PG', '2026-06',
                 'Applicable', -1, 10000, 1000, -0.001, 10, 9.999,
                 'CNY', 2, 'system:test', 'invalid negative budget', now())
            """, connection))
        {
            invalidCostBasis.Parameters.AddWithValue("id", Guid.CreateVersion7());
            var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidCostBasis.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        db.WorkCenterMachineOverheadRates.Add(
            WorkCenterMachineOverheadRate.DefineApplicable(
                "org-pg", "env-pg", "WC-PG", "2026-06",
                32_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "duplicate revision", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var indexCommand = new NpgsqlCommand("""
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND tablename = 'work_center_machine_overhead_rates'
              AND indexname = 'ux_wc_machine_overhead_rates_scope_period_revision'
            """, connection);
        var indexDefinition = Assert.IsType<string>(await indexCommand.ExecuteScalarAsync());
        Assert.Contains("UNIQUE", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accounting_period_code", indexDefinition, StringComparison.Ordinal);
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

    private static ConfigureWorkCenterMachineOverheadRateCommand Command(decimal fixedBudget, string actor) =>
        new(
            "org-concurrent",
            "env-concurrent",
            "WC-CONCURRENT",
            "2026-06",
            MachineOverheadApplicability.Applicable,
            fixedBudget,
            10_000m,
            1_000m,
            "CNY",
            actor,
            "concurrent rate",
            new DateTimeOffset(2026, 5, 25, 8, 0, 0, TimeSpan.Zero));

    private static async Task WaitForAdvisoryLockWaitersAsync(
        string connectionString,
        string applicationName,
        int expectedCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await TestTimeout.RunAsync(
            operation: $"open the machine-rate advisory-lock probe connection for {applicationName}",
            action: async token => await connection.OpenAsync(token),
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken,
            sensitiveValues: [connectionString]);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE application_name = @application_name
              AND wait_event_type = 'Lock'
              AND query LIKE 'SELECT pg_advisory_xact_lock%'
            """, connection);
        command.Parameters.AddWithValue("application_name", applicationName);
        await Eventually.WaitAsync(
            condition: $"{expectedCount} PostgreSQL machine-rate advisory-lock waiters for {applicationName}",
            observe: async token => Convert.ToInt32(await command.ExecuteScalarAsync(token)),
            isSatisfied: waitingCount => waitingCount >= expectedCount,
            describe: waitingCount => $"waiters={waitingCount}; expected>={expectedCount}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(10),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
    }
}
