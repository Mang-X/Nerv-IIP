using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure;

public sealed class NotificationDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
