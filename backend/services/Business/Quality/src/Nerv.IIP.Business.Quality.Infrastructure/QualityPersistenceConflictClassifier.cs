using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Infrastructure;

public interface IQualityPersistenceConflictClassifier
{
    bool IsReinspectionConflict(DbUpdateException exception);
}

public sealed class QualityPersistenceConflictClassifier(ApplicationDbContext dbContext)
    : IQualityPersistenceConflictClassifier
{
    private static readonly HashSet<string> ReinspectionConstraintNames =
    [
        "ux_inspection_records_reinspection_predecessor",
        "ux_inspection_records_source_attempt",
    ];

    private static readonly string[] SourceAttemptSqliteColumns =
    [
        "inspection_records.organization_id",
        "inspection_records.environment_id",
        "inspection_records.source_type",
        "inspection_records.source_service",
        "inspection_records.source_document_id",
        "inspection_records.sku_code",
        "inspection_records.attempt_number",
    ];

    public bool IsReinspectionConflict(DbUpdateException exception)
    {
        if (!exception.Entries.Any(entry => entry.Entity is InspectionRecord))
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsPostgreSqlReinspectionConflict(current)
                || IsSqliteReinspectionConflict(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostgreSqlReinspectionConflict(Exception exception) =>
        exception is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && ((postgresException.ConstraintName is not null
                && ReinspectionConstraintNames.Contains(postgresException.ConstraintName))
            || ReinspectionConstraintNames.Any(name =>
                postgresException.Message.Contains(name, StringComparison.Ordinal)));

    private bool IsSqliteReinspectionConflict(Exception exception)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            && !typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var errorCode = GetIntProperty(exception, "SqliteErrorCode");
        var extendedErrorCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
        if (errorCode != 19 && extendedErrorCode is not (1555 or 2067))
        {
            return false;
        }

        return exception.Message.Contains(
                "inspection_records.reinspection_of_inspection_record_id",
                StringComparison.OrdinalIgnoreCase)
            || SourceAttemptSqliteColumns.All(column =>
                exception.Message.Contains(column, StringComparison.OrdinalIgnoreCase));
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        var value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
        return value switch
        {
            int intValue => intValue,
            uint uintValue => unchecked((int)uintValue),
            _ => null,
        };
    }
}
