using Nerv.IIP.Testing.PostgreSql;
using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql.Tests;

public sealed class PostgreSqlTestDatabaseTests
{
    private const string Secret = "postgres-test-diagnostic-secret";

    // connect 预算按「本地 loopback 立即 RST」取小 —— 它是停滞上限而非预期等待；request 预算取秒级
    // 且大于 connect 预算 —— 它约束连接建立之后的单条命令，对着被拒端点不可能被触发，但一旦这些用例
    // 被指向真库，命令就该按真实依赖的正常抖动兜底，而不是继承为 loopback 挑的小数字。
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(10);

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
        var refused = NetworkFailureFixture.ReserveRefusedLoopbackEndpoint();
        var connectionString = RefusedPostgres.ConnectionString(
            refused,
            database: "postgres",
            username: "test-user",
            password: Secret,
            connectBudget: ConnectBudget,
            requestBudget: RequestBudget);

        // 前提固定：这条用例断言的是「连接失败时的诊断脱敏」，它只有在失败确实是**连接被拒**时才有
        // 意义。CreateAsync 按契约不保留 InnerException（见末行断言），分类只能在它之外做一次，
        // 因此这里直接对同一端点拨号并用四分法钉死前提，而不是把「反正会失败」当默认。
        var probe = await Record.ExceptionAsync(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
        });
        Assert.NotNull(probe);
        Assert.Equal(
            NetworkFailureKind.ConnectionRefused,
            NetworkFailureClassifier.FromException(probe!, CancellationToken.None).Kind);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlTestDatabase.CreateAsync(connectionString, "redaction"));

        Assert.Contains("operation=create", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"host={refused.Host}", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"port={refused.Port}", exception.Message, StringComparison.Ordinal);
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

        // 这条用例的被测意图是「取消发生在拨号之前」，因此它**不会**产生任何连接尝试，也就没有
        // 网络失败可供分类；套一句 ConnectionRefused 断言会断言一个不存在的事件。连接串仍指向被拒
        // 端点，纯粹作为护栏：一旦回归让它先拨号再看取消，会立刻失败而不是挂住。
        var refused = NetworkFailureFixture.ReserveRefusedLoopbackEndpoint();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PostgreSqlTestDatabase.CreateAsync(
                RefusedPostgres.ConnectionString(
                    refused,
                    database: "postgres",
                    username: "test-user",
                    password: Secret,
                    connectBudget: ConnectBudget,
                    requestBudget: RequestBudget),
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
    public async Task DisposeAsync_suppresses_cleanup_failure_while_DropAsync_remains_strict()
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.DropAsync());
        Assert.Contains("operation=drop", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_DisposeAsync_and_DropAsync_share_the_same_strict_cleanup_result()
    {
        var firstDropEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDropEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropAttempts = 0;
        var database = await CreateWithExecutorAsync(async (_, commandText, onConnectionOpened, cancellationToken) =>
        {
            onConnectionOpened();
            if (!commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                return;
            }

            var attempt = Interlocked.Increment(ref dropAttempts);
            if (attempt == 1)
            {
                firstDropEntered.TrySetResult();
                await releaseFirstDrop.Task.WaitAsync(cancellationToken);
                throw new TimeoutException("dispose cleanup failed");
            }

            secondDropEntered.TrySetResult();
            await releaseSecondDrop.Task.WaitAsync(cancellationToken);
        });

        var disposeTask = database.DisposeAsync().AsTask();
        await firstDropEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var dropTask = database.DropAsync();
        releaseFirstDrop.TrySetResult();

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
        var nextCompletion = await Task.WhenAny(dropTask, secondDropEntered.Task)
            .WaitAsync(TimeSpan.FromSeconds(1));
        if (nextCompletion == secondDropEntered.Task)
        {
            releaseSecondDrop.TrySetResult();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dropTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("operation=drop", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, dropAttempts);
    }

    [Fact]
    public async Task Repeated_DisposeAsync_is_idempotent()
    {
        var dropAttempts = 0;
        var database = await CreateWithExecutorAsync((_, commandText, onConnectionOpened, _) =>
        {
            onConnectionOpened();
            if (commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref dropAttempts);
            }

            return Task.CompletedTask;
        });

        await database.DisposeAsync();
        await database.DisposeAsync();

        Assert.Equal(1, dropAttempts);
    }

    [Fact]
    public async Task Cancelled_DropAsync_observer_does_not_cancel_physical_cleanup_joined_by_DisposeAsync()
    {
        var dropEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropAttempts = 0;
        var database = await CreateWithExecutorAsync(async (_, commandText, onConnectionOpened, cancellationToken) =>
        {
            onConnectionOpened();
            if (!commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.Increment(ref dropAttempts);
            dropEntered.TrySetResult();
            await releaseDrop.Task.WaitAsync(cancellationToken);
        });
        using var observerCancellation = new CancellationTokenSource();

        var dropObserver = database.DropAsync(observerCancellation.Token);
        await dropEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var disposeTask = database.DisposeAsync().AsTask();
        await observerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dropObserver);
        Assert.False(disposeTask.IsCompleted);
        Assert.Equal(1, dropAttempts);

        releaseDrop.TrySetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, dropAttempts);
    }

    [Fact]
    public async Task Precancelled_DropAsync_does_not_start_physical_cleanup()
    {
        var dropAttempts = 0;
        var database = await CreateWithExecutorAsync((_, commandText, onConnectionOpened, _) =>
        {
            onConnectionOpened();
            if (commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref dropAttempts);
            }

            return Task.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.DropAsync(cancellation.Token));
        Assert.Equal(0, dropAttempts);

        await database.DisposeAsync();
        Assert.Equal(1, dropAttempts);
    }

    [Fact]
    public async Task Failed_DropAsync_can_be_retried_immediately_and_success_is_idempotent()
    {
        var dropAttempts = 0;
        var database = await CreateWithExecutorAsync((_, commandText, onConnectionOpened, _) =>
        {
            onConnectionOpened();
            if (!commandText.StartsWith("DROP DATABASE", StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            if (Interlocked.Increment(ref dropAttempts) == 1)
            {
                throw new TimeoutException("first drop failed");
            }

            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.DropAsync());
        await database.DropAsync();
        await database.DropAsync();

        Assert.Equal(2, dropAttempts);
        await database.DisposeAsync();
        Assert.Equal(2, dropAttempts);
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
