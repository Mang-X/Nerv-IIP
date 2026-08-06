using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// Bounded wait for the real edge behind "the competing operation is still blocked": a backend of the
/// run-scoped database parked on <c>pg_advisory_xact_lock</c>.
/// </summary>
/// <remarks>
/// The MES scope coordinators serialize competing writers with <c>pg_advisory_xact_lock</c>, and
/// <c>pg_stat_activity</c> exposes that wait as an observable fact. Waiting for that edge is strictly
/// stronger than a settle window: the concurrency tests observe the blocked state instead of assuming it
/// happened within some sleep.
/// </remarks>
/// <remarks>
/// <para>
/// "Any advisory-lock waiter in this database" is a sufficient discriminator only because the database is
/// per-test-invocation, not per-class and not per-assembly: every caller opens its own
/// <c>TemporaryDatabase</c>/<c>DisposablePostgresDatabase</c> whose name carries a fresh
/// <c>Guid.CreateVersion7()</c> and is dropped <c>WITH (FORCE)</c> on disposal
/// (<c>MesSchedulePlanProvenancePostgresTests</c>, <c>SkuDisabledConsumerTests</c> and
/// <c>WorkOrderCapitalizationConcurrencyPostgresTests</c> each do this inside the test method). A waiter
/// leaked by an earlier test therefore cannot be counted here: it is parked on a database this connection
/// cannot even see. If a caller ever shares a database across tests, this predicate has to be narrowed to
/// the exact <c>classid</c>/<c>objid</c> of the lock the test asked for, because "some advisory lock is
/// contended" would no longer imply "this test's lock is contended".
/// </para>
/// </remarks>
internal static class MesPostgresAdvisoryLockProbe
{
    public static async Task WaitForWaitersAsync(
        string connectionString,
        int expectedWaiters,
        string scopeDescription,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        // Explicit budget for the single hangable operation; caller cancellation propagates unchanged.
        await TestTimeout.RunAsync(
            operation: $"open the advisory-lock probe connection for {scopeDescription}",
            action: async token => await connection.OpenAsync(token),
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken,
            sensitiveValues: [connectionString]);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND wait_event = 'advisory'
            """, connection);

        await Eventually.WaitAsync(
            condition: $"{expectedWaiters} PostgreSQL advisory-lock waiter(s) for {scopeDescription}",
            observe: async token => Convert.ToInt32(await command.ExecuteScalarAsync(token)),
            isSatisfied: waiters => waiters >= expectedWaiters,
            describe: waiters => $"advisoryLockWaiters={waiters}; expected>={expectedWaiters}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
    }
}
