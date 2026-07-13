using Npgsql;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// Each conditional PostgreSQL profile test gets its own freshly-created database and drops it on dispose,
// so the destructive tests never delete each other's database when xUnit runs them in parallel (the previous
// shared NERV_IIP_TEST_POSTGRES + EnsureDeleted/Migrate approach could race).
internal sealed class SchedulingTemporaryDatabase(
    string adminConnectionString,
    string databaseName,
    string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<SchedulingTemporaryDatabase> CreateAsync(string baseConnectionString)
    {
        var name = $"nerv_scheduling_test_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new SchedulingTemporaryDatabase(
            admin,
            name,
            new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = name }.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
