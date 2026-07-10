using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class ExpiredStockReservationService(
    ApplicationDbContext dbContext,
    IOptions<StockReservationExpirationOptions> options)
{
    public async Task<int> ExpireOpenReservationsAsync(DateTime expiredAtUtc, CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 1000);
        var reservations = await dbContext.StockReservations
            .Where(x => x.OpenQuantity > 0m && x.ExpiresAtUtc <= expiredAtUtc)
            .OrderBy(x => x.ExpiresAtUtc)
            .ThenBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;
        foreach (var reservation in reservations)
        {
            var ledger = await dbContext.StockLedgers.SingleOrDefaultAsync(
                x => x.OrganizationId == reservation.OrganizationId
                    && x.EnvironmentId == reservation.EnvironmentId
                    && x.SkuCode == reservation.SkuCode
                    && x.UomCode == reservation.UomCode
                    && x.SiteCode == reservation.SiteCode
                    && x.LocationCode == reservation.LocationCode
                    && x.LotNo == reservation.LotNo
                    && x.SerialNo == reservation.SerialNo
                    && x.QualityStatus == reservation.QualityStatus
                    && x.OwnerType == reservation.OwnerType
                    && x.OwnerId == reservation.OwnerId,
                cancellationToken);
            if (ledger is not null && ledger.ExpireReservation(reservation, expiredAtUtc) > 0m)
            {
                expiredCount++;
            }
        }

        if (expiredCount > 0)
        {
            await dbContext.SaveEntitiesAsync(cancellationToken);
        }

        return expiredCount;
    }
}
