using Npgsql;

namespace Nerv.IIP.Business.Scheduling.Infrastructure;

public static class ScheduleReleaseUniqueConflictClassifier
{
    private static readonly HashSet<string> ConstraintNames =
    [
        "ux_schedule_plans_scope_active_release",
        "ux_schedule_plans_scope_release_revision"
    ];

    public static bool IsReleaseGovernanceConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                ((postgresException.ConstraintName is not null && ConstraintNames.Contains(postgresException.ConstraintName)) ||
                    ConstraintNames.Any(name => postgresException.Message.Contains(name, StringComparison.Ordinal))))
            {
                return true;
            }
        }

        return false;
    }
}
