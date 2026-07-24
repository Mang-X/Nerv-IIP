using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityPersistenceConflictClassifierTests
{
    [Theory]
    [InlineData("ux_inspection_records_reinspection_predecessor", true)]
    [InlineData("ux_inspection_records_source_attempt", true)]
    [InlineData("ux_other_quality_constraint", false)]
    public void Reinspection_conflicts_are_classified_by_structured_postgres_constraint_name(
        string constraintName,
        bool expected)
    {
        var postgresException = new PostgresException(
            "message text deliberately contains ux_inspection_records_reinspection_predecessor",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            detail: string.Empty,
            hint: string.Empty,
            position: 0,
            internalPosition: 0,
            internalQuery: string.Empty,
            where: string.Empty,
            schemaName: "quality",
            tableName: "inspection_records",
            columnName: string.Empty,
            dataTypeName: string.Empty,
            constraintName,
            file: string.Empty,
            line: string.Empty,
            routine: string.Empty);
        var exception = new DbUpdateException("reinspection conflict", postgresException);

        Assert.Equal(
            expected,
            new QualityPersistenceConflictClassifier().IsReinspectionConflict(exception));
    }
}
