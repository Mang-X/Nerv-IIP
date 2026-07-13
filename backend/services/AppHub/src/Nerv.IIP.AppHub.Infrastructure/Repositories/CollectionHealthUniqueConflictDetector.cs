using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.AppHub.Infrastructure.Repositories;

public interface ICollectionHealthUniqueConstraintViolation;

public static class CollectionHealthUniqueConflictDetector
{
    private const string InstanceConstraint = "ux_connector_collection_health_instance";
    private const string ScopeConstraint = "ux_connector_collection_health_scope";

    public static bool IsUniqueConflict(DbUpdateException exception)
    {
        return exception.InnerException switch
        {
            ICollectionHealthUniqueConstraintViolation => true,
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && IsCollectionHealthConstraint(postgres.ConstraintName),
            _ => false
        };
    }

    private static bool IsCollectionHealthConstraint(string? constraintName) =>
        constraintName is InstanceConstraint or ScopeConstraint;
}
