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
/// happened within some sleep. Each test runs against its own temporary database, so
/// <c>datname = current_database()</c> is a sufficient discriminator between concurrent test classes.
/// </remarks>
internal static class MesPostgresAdvisoryLockProbe
{
    public static async Task WaitForWaitersAsync(
        string connectionString,
        int expectedWaiters,
        string scopeDescription)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
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
                SensitiveValues: [connectionString]));
    }
}
