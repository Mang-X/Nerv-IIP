using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql;

public sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly Func<string, string, Action, CancellationToken, Task> _executeAdminCommandAsync;
    private readonly string?[] _sensitiveValues;
    private readonly object _lifecycleLock = new();
    private Task? _dropTask;
    private Task? _disposeTask;
    private bool _dropped;

    private PostgreSqlTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString,
        string host,
        int port,
        bool usernameConfigured,
        Func<string, string, Action, CancellationToken, Task> executeAdminCommandAsync,
        string?[] sensitiveValues)
    {
        _adminConnectionString = adminConnectionString;
        _executeAdminCommandAsync = executeAdminCommandAsync;
        _sensitiveValues = sensitiveValues;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        Host = host;
        Port = port;
        UsernameConfigured = usernameConfigured;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    private string Host { get; }

    private int Port { get; }

    private bool UsernameConfigured { get; }

    public static async Task<PostgreSqlTestDatabase> CreateAsync(
        string baseConnectionString,
        string databaseNamePrefix,
        Func<string, CancellationToken, Task>? initializeAsync = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateAsync(
            baseConnectionString,
            databaseNamePrefix,
            initializeAsync,
            ExecuteAdminCommandCoreAsync,
            cancellationToken);
    }

    internal static async Task<PostgreSqlTestDatabase> CreateAsync(
        string baseConnectionString,
        string databaseNamePrefix,
        Func<string, CancellationToken, Task>? initializeAsync,
        Func<string, string, Action, CancellationToken, Task> executeAdminCommandAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new ArgumentException("A PostgreSQL base connection string is required.", nameof(baseConnectionString));
        }

        NpgsqlConnectionStringBuilder baseBuilder;
        try
        {
            baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"PostgreSQL test database operation=parse failed: connectionConfigured=True; detail={exception.GetType().Name}; credentials redacted.");
        }

        var databaseName = PostgreSqlTestDatabaseName.Create(databaseNamePrefix);
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var testBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = databaseName
        };
        var sensitiveValues = new[]
        {
            baseConnectionString,
            baseBuilder.ConnectionString,
            adminBuilder.ConnectionString,
            testBuilder.ConnectionString,
            baseBuilder.Username,
            baseBuilder.Password
        };
        var database = new PostgreSqlTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            testBuilder.ConnectionString,
            baseBuilder.Host ?? "<missing>",
            baseBuilder.Port,
            !string.IsNullOrWhiteSpace(baseBuilder.Username),
            executeAdminCommandAsync,
            sensitiveValues);

        var createConnectionOpened = false;
        try
        {
            await database.ExecuteAdminCommandAsync(
                $"CREATE DATABASE \"{databaseName}\"",
                cancellationToken,
                () => createConnectionOpened = true);
        }
        catch (OperationCanceledException)
        {
            if (createConnectionOpened)
            {
                await database.TryDropAfterFailureAsync();
            }

            throw;
        }
        catch (Exception exception)
        {
            var cleanupFailure = createConnectionOpened
                ? await database.TryDropAfterFailureAsync()
                : null;
            throw database.CreateFailure("create", exception, cleanupFailure);
        }

        if (initializeAsync is null)
        {
            return database;
        }

        try
        {
            await initializeAsync(database.ConnectionString, cancellationToken);
            return database;
        }
        catch (OperationCanceledException)
        {
            await database.TryDropAfterFailureAsync();
            throw;
        }
        catch (Exception exception)
        {
            var cleanupFailure = await database.TryDropAfterFailureAsync();
            throw database.CreateFailure("initialize", exception, cleanupFailure);
        }
    }

    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dropTask = GetOrStartDropTask(cancellationToken);
        await dropTask.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        TaskCompletionSource? disposeCompletion = null;
        lock (_lifecycleLock)
        {
            if (_disposeTask is null)
            {
                disposeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = disposeCompletion.Task;
            }

            disposeTask = _disposeTask;
        }

        if (disposeCompletion is not null)
        {
            _ = CompleteDisposeAsync(disposeCompletion);
        }

        return new ValueTask(disposeTask);
    }

    private Task GetOrStartDropTask(CancellationToken cancellationToken)
    {
        Task dropTask;
        TaskCompletionSource? dropCompletion = null;
        lock (_lifecycleLock)
        {
            if (_dropped)
            {
                return Task.CompletedTask;
            }

            if (_dropTask is null)
            {
                dropCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _dropTask = dropCompletion.Task;
            }

            dropTask = _dropTask;
        }

        if (dropCompletion is not null)
        {
            _ = CompleteDropAsync(dropCompletion, cancellationToken);
        }

        return dropTask;
    }

    private async Task CompleteDropAsync(
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        OperationCanceledException? cancellation = null;
        try
        {
            await ExecuteAdminCommandAsync(
                $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)",
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            cancellation = exception;
        }
        catch (Exception exception)
        {
            failure = CreateFailure("drop", exception);
        }

        lock (_lifecycleLock)
        {
            if (failure is null && cancellation is null)
            {
                _dropped = true;
            }
        }

        if (cancellation is not null)
        {
            completion.TrySetCanceled(cancellation.CancellationToken);
        }
        else if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else
        {
            completion.TrySetResult();
        }

        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_dropTask, completion.Task))
            {
                _dropTask = null;
            }
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await GetOrStartDropTask(CancellationToken.None);
        }
        catch (Exception)
        {
            // Dispose is best-effort; explicit DropAsync preserves cleanup failure evidence.
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task ExecuteAdminCommandAsync(
        string commandText,
        CancellationToken cancellationToken,
        Action? onConnectionOpened = null)
    {
        await _executeAdminCommandAsync(
            _adminConnectionString,
            commandText,
            onConnectionOpened ?? (() => { }),
            cancellationToken);
    }

    private static async Task ExecuteAdminCommandCoreAsync(
        string connectionString,
        string commandText,
        Action onConnectionOpened,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        onConnectionOpened();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> TryDropAfterFailureAsync()
    {
        try
        {
            await DropAsync();
            return null;
        }
        catch (Exception exception)
        {
            return Sanitize(exception.Message);
        }
    }

    private InvalidOperationException CreateFailure(
        string operation,
        Exception exception,
        string? cleanupFailure = null)
    {
        var cleanupStatus = cleanupFailure is null
            ? string.Empty
            : $" cleanupFailure={cleanupFailure}";
        return new InvalidOperationException(
            $"PostgreSQL test database operation={operation} failed: host={Host}; port={Port}; database={DatabaseName}; usernameConfigured={UsernameConfigured}; detail={Sanitize(exception.GetType().Name + ": " + exception.Message)}; credentials redacted.{cleanupStatus}");
    }

    private string Sanitize(string value)
    {
        return SanitizeDiagnostic(value, _sensitiveValues);
    }

    internal static string SanitizeDiagnostic(string value, string?[] sensitiveValues)
    {
        var sanitized = value;
        foreach (var sensitiveValue in sensitiveValues)
        {
            if (!string.IsNullOrEmpty(sensitiveValue))
            {
                sanitized = Regex.Replace(
                    sanitized,
                    $@"(?<![A-Za-z0-9_]){Regex.Escape(sensitiveValue)}(?![A-Za-z0-9_])",
                    "<redacted>",
                    RegexOptions.CultureInvariant);
            }
        }

        return sanitized;
    }
}

internal static class PostgreSqlTestDatabaseName
{
    public const int MaximumIdentifierLength = 63;
    private const int SuffixLength = 33;

    public static string Create(string prefix)
    {
        var normalized = Normalize(prefix);
        var maximumPrefixLength = MaximumIdentifierLength - SuffixLength;
        if (normalized.Length > maximumPrefixLength)
        {
            normalized = normalized[..maximumPrefixLength].TrimEnd('_');
        }

        return $"{normalized}_{Guid.CreateVersion7():N}";
    }

    private static string Normalize(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "nerv_test";
        }

        var builder = new StringBuilder(prefix.Length);
        var previousWasUnderscore = false;
        foreach (var character in prefix.Trim().ToLowerInvariant())
        {
            var normalizedCharacter = character is >= 'a' and <= 'z' or >= '0' and <= '9'
                ? character
                : '_';
            if (normalizedCharacter == '_')
            {
                if (previousWasUnderscore)
                {
                    continue;
                }

                previousWasUnderscore = true;
            }
            else
            {
                previousWasUnderscore = false;
            }

            builder.Append(normalizedCharacter);
        }

        var normalized = builder.ToString().Trim('_');
        return normalized.Length == 0 ? "nerv_test" : normalized;
    }
}
