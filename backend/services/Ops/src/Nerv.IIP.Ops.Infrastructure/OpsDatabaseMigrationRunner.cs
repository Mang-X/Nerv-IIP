using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure;

public sealed class OpsDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
