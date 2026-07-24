using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Infrastructure;

public interface IQualityPersistenceConflictClassifier
{
    bool IsReinspectionConflict(DbUpdateException exception);
}

public sealed class QualityPersistenceConflictClassifier
    : IQualityPersistenceConflictClassifier
{
    private static readonly HashSet<string> ReinspectionConstraintNames =
    [
        "ux_inspection_records_reinspection_predecessor",
        "ux_inspection_records_source_attempt",
    ];

    public bool IsReinspectionConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsPostgreSqlReinspectionConflict(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostgreSqlReinspectionConflict(Exception exception) =>
        exception is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && postgresException.ConstraintName is not null
        && ReinspectionConstraintNames.Contains(postgresException.ConstraintName);
}
