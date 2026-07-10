using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class InventoryReservationMetrics
{
    private static readonly Gauge HangingReservations = Metrics.CreateGauge(
        "nerv_iip_inventory_hanging_stock_reservations",
        "Number of Inventory stock reservations that remain open after their expiration deadline.");

    private static readonly Counter ExpiredReservations = Metrics.CreateCounter(
        "nerv_iip_inventory_stock_reservations_expired_total",
        "Number of Inventory stock reservations automatically released after expiration.");

    public async Task RefreshHangingReservationsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var count = await dbContext.StockReservations.CountAsync(
            x => x.OpenQuantity > 0m && x.ExpiresAtUtc <= now,
            cancellationToken);
        HangingReservations.Set(count);
    }

    public void RecordExpiration(int expiredCount)
    {
        if (expiredCount > 0)
        {
            ExpiredReservations.Inc(expiredCount);
        }
    }
}
