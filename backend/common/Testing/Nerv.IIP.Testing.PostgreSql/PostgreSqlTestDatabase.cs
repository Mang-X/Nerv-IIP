using System.Text;
using Npgsql;

namespace Nerv.IIP.Testing.PostgreSql;

public sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string?[] _sensitiveValues;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private bool _dropped;

    private PostgreSqlTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString,
        string host,
        int port,
        bool usernameConfigured,
        string?[] sensitiveValues)
    {
        _adminConnectionString = adminConnectionString;
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
            sensitiveValues);

        try
        {
            await database.ExecuteAdminCommandAsync(
                $"CREATE DATABASE \"{databaseName}\"",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await database.TryDropAfterFailureAsync();
            throw;
        }
        catch (Exception exception)
        {
            var cleanupFailure = await database.TryDropAfterFailureAsync();
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
        await _cleanupGate.WaitAsync(cancellationToken);
        try
        {
            if (_dropped)
            {
                return;
            }

            try
            {
                await ExecuteAdminCommandAsync(
                    $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)",
                    cancellationToken);
                _dropped = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw CreateFailure("drop", exception);
            }
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync();
        _cleanupGate.Dispose();
    }

    private async Task ExecuteAdminCommandAsync(string commandText, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);
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
        var sanitized = value;
        foreach (var sensitiveValue in _sensitiveValues)
        {
            if (!string.IsNullOrEmpty(sensitiveValue))
            {
                sanitized = sanitized.Replace(sensitiveValue, "<redacted>", StringComparison.Ordinal);
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
