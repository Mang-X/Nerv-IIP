using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure;

public sealed class IamDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
