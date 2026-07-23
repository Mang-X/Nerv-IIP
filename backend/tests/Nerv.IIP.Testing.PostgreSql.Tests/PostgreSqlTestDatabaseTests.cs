using Nerv.IIP.Testing.PostgreSql;
using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql.Tests;

public sealed class PostgreSqlTestDatabaseTests
{
    private const string Secret = "postgres-test-diagnostic-secret";

    [Fact]
    public void Database_names_are_bounded_safe_and_unique_for_parallel_tests()
    {
        var names = Enumerable.Range(0, 256)
            .AsParallel()
            .Select(_ => PostgreSqlTestDatabaseName.Create("Nerv Scheduling / Release Governance With A Very Long Prefix"))
            .ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name =>
        {
            Assert.InRange(name.Length, 1, PostgreSqlTestDatabaseName.MaximumIdentifierLength);
            Assert.Matches("^[a-z0-9_]+$", name);
            Assert.StartsWith("nerv_scheduling_release_gover", name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Connection_failures_are_diagnostic_without_leaking_credentials()
    {
        var connectionString =
            $"Host=127.0.0.1;Port=1;Timeout=1;Database=postgres;Username=test-user;Password={Secret}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlTestDatabase.CreateAsync(connectionString, "redaction"));

        Assert.Contains("operation=create", exception.Message, StringComparison.Ordinal);
        Assert.Contains("host=127.0.0.1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("usernameConfigured=True", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-user", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Precancelled_creation_stops_before_connecting()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PostgreSqlTestDatabase.CreateAsync(
                $"Host=127.0.0.1;Port=1;Database=postgres;Username=test-user;Password={Secret}",
                "cancelled",
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Create_failure_before_admin_connection_opens_does_not_attempt_drop()
    {
        var commands = new List<string>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateWithExecutorAsync(
            (_, commandText, _, _) =>
            {
                commands.Add(commandText);
                throw new TimeoutException("admin connection did not open");
            }));

        Assert.Contains("operation=create", exception.Message, StringComparison.Ordinal);
        Assert.Single(commands);
        Assert.StartsWith("CREATE DATABASE", commands[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_failure_after_admin_connection_opens_attempts_drop()
    {
        var commands = new List<string>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateWithExecutorAsync(
            (_, commandText, onConnectionOpened, _) =>
            {
                commands.Add(commandText);
                onConnectionOpened();
                if (commandText.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
                {
                    throw new TimeoutException("create outcome is ambiguous");
                }

                return Task.CompletedTask;
            }));

        Assert.Contains("operation=create", exception.Message, StringComparison.Ordinal);
        Assert.Collection(
            commands,
            command => Assert.StartsWith("CREATE DATABASE", command, StringComparison.Ordinal),
            command => Assert.StartsWith("DROP DATABASE", command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposeAsync_suppresses_cleanup_failure_and_always_disposes_cleanup_gate()
    {
        var database = await CreateWithExecutorAsync((_, commandText, onConnectionOpened, _) =>
        {
            onConnectionOpened();
            if (commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                throw new TimeoutException("drop failed");
            }

            return Task.CompletedTask;
        });

        await database.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => database.DropAsync());
    }

    [Fact]
    public async Task DropAsync_preserves_cleanup_failure_for_explicit_callers()
    {
        var database = await CreateWithExecutorAsync((_, commandText, onConnectionOpened, _) =>
        {
            onConnectionOpened();
            if (commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                throw new TimeoutException("drop failed");
            }

            return Task.CompletedTask;
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.DropAsync());

        Assert.Contains("operation=drop", exception.Message, StringComparison.Ordinal);
        await database.DisposeAsync();
    }

    [Fact]
    public void Sanitizer_redacts_credentials_without_rewriting_database_name_substrings()
    {
        const string connectionString =
            "Host=localhost;Database=nerv_scheduling_test_001;Username=test;Password=secret";
        const string diagnostic =
            "connection Host=localhost;Database=nerv_scheduling_test_001;Username=test;Password=secret rejected user \"test\" password \"secret\" for database nerv_scheduling_test_001";

        var sanitized = SanitizeDiagnostic(
            diagnostic,
            [connectionString, "test", "secret"]);

        Assert.Contains("database nerv_scheduling_test_001", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("user \"test\"", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("password \"secret\"", sanitized, StringComparison.Ordinal);
    }

    [PostgreSqlTestFact]
    public async Task Parallel_databases_are_isolated_initialized_and_removed()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var createTasks = Enumerable.Range(0, 2)
            .Select(_ => PostgreSqlTestDatabase.CreateAsync(
                baseConnectionString,
                "nerv_parallel_contract",
                InitializeMarkerAsync,
                CancellationToken.None))
            .ToArray();
        var databases = await Task.WhenAll(createTasks);
        var databaseNames = databases.Select(database => database.DatabaseName).ToArray();

        try
        {
            Assert.Equal(2, databaseNames.Distinct(StringComparer.Ordinal).Count());
            foreach (var database in databases)
            {
                await using var connection = new NpgsqlConnection(database.ConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "SELECT current_database(), marker FROM test_marker",
                    connection);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(database.DatabaseName, reader.GetString(0));
                Assert.Equal("initialized", reader.GetString(1));
            }
        }
        finally
        {
            await Task.WhenAll(databases.Select(database => database.DropAsync()).ToArray());
        }

        Assert.Empty(await FindDatabasesAsync(baseConnectionString, databaseNames));
    }

    [PostgreSqlTestFact]
    public async Task Initializer_failure_drops_database_and_redacts_diagnostics()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        string? createdDatabaseName = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlTestDatabase.CreateAsync(
                baseConnectionString,
                "nerv_initializer_failure",
                (connectionString, _) =>
                {
                    createdDatabaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
                    throw new InvalidOperationException($"initializer rejected {connectionString}");
                },
                CancellationToken.None));

        Assert.NotNull(createdDatabaseName);
        Assert.Contains("operation=initialize", exception.Message, StringComparison.Ordinal);
        var password = new NpgsqlConnectionStringBuilder(baseConnectionString).Password;
        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
        }
        Assert.Empty(await FindDatabasesAsync(baseConnectionString, [createdDatabaseName]));
    }

    private static async Task InitializeMarkerAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "CREATE TABLE test_marker (marker text NOT NULL); INSERT INTO test_marker (marker) VALUES ('initialized');",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PostgreSqlTestDatabase> CreateWithExecutorAsync(
        Func<string, string, Action, CancellationToken, Task> executor)
    {
        return await PostgreSqlTestDatabase.CreateAsync(
            "Host=localhost;Database=postgres;Username=test;Password=secret",
            "nerv_scheduling_test",
            initializeAsync: null,
            executeAdminCommandAsync: executor,
            cancellationToken: CancellationToken.None);
    }

    private static string SanitizeDiagnostic(string value, string?[] sensitiveValues)
    {
        return PostgreSqlTestDatabase.SanitizeDiagnostic(value, sensitiveValues);
    }

    private static async Task<IReadOnlyList<string>> FindDatabasesAsync(
        string baseConnectionString,
        IReadOnlyCollection<string> databaseNames)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE datname = ANY ($1)",
            connection);
        command.Parameters.AddWithValue(databaseNames.ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var found = new List<string>();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }
}

internal sealed class PostgreSqlTestFactAttribute : FactAttribute
{
    public PostgreSqlTestFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run PostgreSQL test database lifecycle tests.";
        }
    }
}
