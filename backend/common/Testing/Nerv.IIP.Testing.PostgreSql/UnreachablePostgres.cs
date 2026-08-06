using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql;

/// <summary>
/// Builds PostgreSQL connection strings whose failure mode is decided by the fixture rather than by
/// the machine the test happens to run on. The endpoint comes from
/// <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/>, so a connect attempt is
/// refused immediately and classifies as <see cref="NetworkFailureKind.ConnectionRefused"/>.
/// </summary>
public static class UnreachablePostgres
{
    /// <summary>Budget for opening the TCP/startup connection. Never elapses against a refused port.</summary>
    public static TimeSpan DefaultConnectBudget { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Budget for an individual command once a connection exists — deliberately separate.</summary>
    public static TimeSpan DefaultRequestBudget { get; } = TimeSpan.FromSeconds(5);

    public static string ConnectionRefusedConnectionString(
        RefusedTcpEndpoint endpoint,
        string database,
        string username,
        string password,
        TimeSpan? connectBudget = null,
        TimeSpan? requestBudget = null)
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
            Timeout = ToWholeSeconds(connectBudget ?? DefaultConnectBudget, nameof(connectBudget)),
            CommandTimeout = ToWholeSeconds(requestBudget ?? DefaultRequestBudget, nameof(requestBudget)),

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
