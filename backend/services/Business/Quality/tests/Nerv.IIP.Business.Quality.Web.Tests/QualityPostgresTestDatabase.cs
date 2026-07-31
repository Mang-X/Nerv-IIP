using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

internal sealed class QualityPostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private QualityPostgresTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<QualityPostgresTestDatabase> CreateAsync(
        string testName,
        CancellationToken cancellationToken = default)
    {
        var configured = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "NERV_IIP_TEST_POSTGRES must target the isolated Docker PostgreSQL instance.");
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var safePrefix = new string(testName
            .ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character))
            .Take(24)
            .ToArray());
        var databaseName = $"quality_{safePrefix}_{suffix}";
        var targetBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = databaseName,
        };
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
        };

        await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new QualityPostgresTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            targetBuilder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
