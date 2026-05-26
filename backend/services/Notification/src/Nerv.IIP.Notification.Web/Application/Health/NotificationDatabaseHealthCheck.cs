using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nerv.IIP.Notification.Infrastructure;

namespace Nerv.IIP.Notification.Web.Application.Health;

internal sealed class NotificationDatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Notification database is not reachable.");
    }
}
