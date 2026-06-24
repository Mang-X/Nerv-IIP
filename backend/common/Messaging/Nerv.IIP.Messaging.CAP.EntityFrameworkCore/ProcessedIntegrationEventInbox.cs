using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Messaging.CAP;

public sealed record ProcessedIntegrationEventInboxRecord(
    string ConsumerName,
    string EventId,
    string EventType,
    int EventVersion,
    string SourceService,
    string IdempotencyKey,
    DateTimeOffset ProcessedAtUtc);

public static class ProcessedIntegrationEventInbox
{
    public const string UniqueIndexName = "ux_processed_integration_events_consumer_idempotency_key";

    private const string ConsumerNameProperty = nameof(ProcessedIntegrationEventInboxRecord.ConsumerName);
    private const string IdempotencyKeyProperty = nameof(ProcessedIntegrationEventInboxRecord.IdempotencyKey);

    public static async Task<bool> TryRecordAsync<TEntity>(
        DbContext dbContext,
        DbSet<TEntity> dbSet,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        Func<ProcessedIntegrationEventInboxRecord, TEntity> entityFactory,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(entityFactory);

        var eventId = Required(integrationEvent.EventId, "Integration event id is required.");
        var eventType = Required(integrationEvent.EventType, "Integration event type is required.");
        var sourceService = Required(integrationEvent.SourceService, "Integration event source service is required.");
        var idempotencyKey = Required(integrationEvent.IdempotencyKey, "Integration event idempotency key is required.");

        if (dbSet.Local.Any(entity => HasProcessedKey(entity, consumerName, idempotencyKey)))
        {
            return false;
        }

        if (await dbSet.AnyAsync(
            entity =>
                EF.Property<string>(entity, ConsumerNameProperty) == consumerName &&
                EF.Property<string>(entity, IdempotencyKeyProperty) == idempotencyKey,
            cancellationToken))
        {
            return false;
        }

        dbSet.Add(entityFactory(new ProcessedIntegrationEventInboxRecord(
            consumerName,
            eventId,
            eventType,
            integrationEvent.EventVersion,
            sourceService,
            idempotencyKey,
            DateTimeOffset.UtcNow)));
        return true;
    }

    public static bool IsUniqueConflict(
        Exception exception,
        DbContext dbContext,
        string? constraintOrIndexName = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(dbContext);

        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        return EnumerateExceptions(exception).Any(inner =>
            IsPostgreSqlUniqueConflict(inner, constraintOrIndexName) ||
            IsSqliteUniqueConflict(providerName, inner) ||
            IsSqlServerUniqueConflict(providerName, inner) ||
            IsMySqlUniqueConflict(providerName, inner));
    }

    private static bool HasProcessedKey<TEntity>(TEntity entity, string consumerName, string idempotencyKey)
        where TEntity : class
    {
        var type = entity.GetType();
        return string.Equals(GetStringProperty(type, entity, ConsumerNameProperty), consumerName, StringComparison.Ordinal) &&
            string.Equals(GetStringProperty(type, entity, IdempotencyKeyProperty), idempotencyKey, StringComparison.Ordinal);
    }

    private static string? GetStringProperty(Type type, object instance, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance) as string;
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value;
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool IsPostgreSqlUniqueConflict(Exception exception, string? constraintOrIndexName)
    {
        if (!string.Equals(exception.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        if (sqlState != "23505")
        {
            return false;
        }

        return MatchesConstraintName(exception, constraintOrIndexName);
    }

    private static bool IsSqliteUniqueConflict(string providerName, Exception exception)
    {
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var errorCode = GetIntProperty(exception, "SqliteErrorCode");
        var extendedErrorCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
        return errorCode == 19 || extendedErrorCode is 1555 or 2067;
    }

    private static bool IsSqlServerUniqueConflict(string providerName, Exception exception)
    {
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("SqlException", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetIntProperty(exception, "Number") is 2601 or 2627;
    }

    private static bool IsMySqlUniqueConflict(string providerName, Exception exception)
    {
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetIntProperty(exception, "Number") == 1062;
    }

    private static bool MatchesConstraintName(Exception exception, string? constraintOrIndexName)
    {
        if (string.IsNullOrWhiteSpace(constraintOrIndexName))
        {
            return true;
        }

        var constraintName = exception.GetType().GetProperty("ConstraintName")?.GetValue(exception) as string;
        return string.Equals(constraintName, constraintOrIndexName, StringComparison.Ordinal);
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        var value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            _ => null
        };
    }
}
