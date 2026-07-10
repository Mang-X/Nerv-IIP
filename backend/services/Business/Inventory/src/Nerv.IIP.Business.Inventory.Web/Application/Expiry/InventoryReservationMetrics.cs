using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class InventoryReservationMetrics
{
    private static readonly Gauge OpenReservations = Metrics.CreateGauge(
        "nerv_iip_inventory_open_stock_reservations",
        "Number of Inventory stock reservations with a remaining open quantity.");

    private static readonly Counter ExpiredReservations = Metrics.CreateCounter(
        "nerv_iip_inventory_stock_reservations_expired_total",
        "Number of Inventory stock reservations automatically released after expiration.");

    public async Task RefreshOpenReservationsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var count = await dbContext.StockReservations.CountAsync(x => x.OpenQuantity > 0m, cancellationToken);
        OpenReservations.Set(count);
    }

    public void RecordExpiration(int expiredCount)
    {
        if (expiredCount > 0)
        {
            ExpiredReservations.Inc(expiredCount);
        }
    }
}
