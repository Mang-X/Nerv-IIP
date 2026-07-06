using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Notification.Domain.ObservabilityAlerts;
using Npgsql;

namespace Nerv.IIP.Notification.Infrastructure;

public sealed class PostgreSqlDatabaseWatermarkReader(IConfiguration configuration) : IDatabaseWatermarkReader, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> dataSources = new(StringComparer.Ordinal);

    public async Task<double?> ReadPercentAsync(DatabaseWatermarkReadRequest request, CancellationToken cancellationToken)
    {
        var dataSource = GetDataSource(request.ConnectionStringName);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            if (string.Equals(request.MetricName, "database-size", StringComparison.OrdinalIgnoreCase))
            {
                if (request.CapacityMegabytes is null or <= 0)
                {
                    return null;
                }

                await using var command = new NpgsqlCommand("select pg_database_size(current_database())", connection);
                var bytes = Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                return bytes / 1024d / 1024d / request.CapacityMegabytes.Value * 100d;
            }

            await using var activeCommand = new NpgsqlCommand("select count(*) from pg_stat_activity", connection);
            var active = Convert.ToDouble(await activeCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            await using var maxCommand = new NpgsqlCommand("show max_connections", connection);
            var maxValue = Convert.ToDouble(await maxCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            return maxValue <= 0 ? null : active / maxValue * 100d;
        }
        catch (NpgsqlException exception)
        {
            throw new DatabaseWatermarkReadException(
                $"PostgreSQL watermark read failed for connection string '{request.ConnectionStringName}'.",
                exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in dataSources.Values)
        {
            await dataSource.DisposeAsync();
        }
    }

    private NpgsqlDataSource GetDataSource(string connectionStringName)
    {
        return dataSources.GetOrAdd(connectionStringName, static (name, configuration) =>
        {
            var connectionString = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{name}' is not configured.");
            }

            return NpgsqlDataSource.Create(connectionString);
        }, configuration);
    }
}
