using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql;

/// <summary>
/// Builds PostgreSQL connection strings whose failure mode is decided by the fixture rather than by
/// the machine the test happens to run on. The endpoint comes from
/// <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/>, so a connect attempt is
/// refused immediately and classifies as <see cref="NetworkFailureKind.ConnectionRefused"/>.
///
/// The type is named for the outcome it guarantees — <em>refused</em> — and deliberately not
/// "unreachable": "unreachable" is the vague word this fixture exists to remove, because it lumps
/// DNS failure, refusal and timeout into one shrug.
/// </summary>
public static class RefusedPostgres
{
    /// <summary>
    /// Both budgets are required rather than defaulted. A default would let every call site inherit
    /// one number and quietly reproduce the "single fuzzy duration standing in for connect, DNS and
    /// response budgets" that docs/architecture/backend-test-determinism.md ("网络结果与预算")
    /// forbids — which is exactly what happens when an optional parameter exists and nobody passes it.
    /// </summary>
    /// <param name="connectBudget">
    /// Bounds establishing the TCP/startup connection. Against a refused loopback port the verdict
    /// arrives in microseconds, so this is a stall guard, not an expected wait.
    /// </param>
    /// <param name="requestBudget">
    /// Bounds an individual command once a connection exists. Against a refused endpoint no command
    /// can ever run, so this budget is unreachable by construction; it is still stated explicitly so
    /// that a regression which makes the host reachable is bounded by a real dependency's jitter
    /// rather than by whatever the connect budget happened to be.
    /// </param>
    public static string ConnectionString(
        RefusedTcpEndpoint endpoint,
        string database,
        string username,
        string password,
        TimeSpan connectBudget,
        TimeSpan requestBudget)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);

        return new NpgsqlConnectionStringBuilder
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            Database = database,
            Username = username,
            Password = password,
            Timeout = ToWholeSeconds(connectBudget, nameof(connectBudget)),
            CommandTimeout = ToWholeSeconds(requestBudget, nameof(requestBudget)),

            // A pooled connection to a dead endpoint would let one test's failure leak into the
            // next one's first open; each attempt must dial for itself.
            Pooling = false,
        }.ConnectionString;
    }

    private static int ToWholeSeconds(TimeSpan budget, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero, parameterName);
        if (budget.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Npgsql expresses connect and command budgets in whole seconds.");
        }

        return (int)budget.TotalSeconds;
    }
}
