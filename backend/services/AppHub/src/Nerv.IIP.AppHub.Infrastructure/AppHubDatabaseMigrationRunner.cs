using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure;

public sealed class AppHubDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
