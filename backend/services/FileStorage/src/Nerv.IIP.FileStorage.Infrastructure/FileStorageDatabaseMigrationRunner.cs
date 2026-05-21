using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.FileStorage.Infrastructure;

public sealed class FileStorageDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
