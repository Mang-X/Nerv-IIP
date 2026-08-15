using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class InventoryReservationMetrics(TimeProvider timeProvider, CollectorRegistry registry)
{
    private readonly Gauge hangingReservations = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_inventory_hanging_stock_reservations",
        "Number of Inventory stock reservations that remain open after their expiration deadline.");

    private readonly Counter expiredReservations = Metrics.WithCustomRegistry(registry).CreateCounter(
        "nerv_iip_inventory_stock_reservations_expired_total",
        "Number of Inventory stock reservations automatically released after expiration.");

    public async Task RefreshHangingReservationsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var count = await dbContext.StockReservations.CountAsync(
            x => x.OpenQuantity > 0m && x.ExpiresAtUtc <= now,
            cancellationToken);
        hangingReservations.Set(count);
    }

    public void RecordExpiration(int expiredCount)
    {
        if (expiredCount > 0)
        {
            expiredReservations.Inc(expiredCount);
        }
    }
}
