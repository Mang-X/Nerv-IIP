using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public static class ErpPersistenceConflicts
{
    public static bool IsCodeIdempotencyKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_code_idempotency_keys_scope",
        };
}
