using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class ExpiredStockReservationService(
    ApplicationDbContext dbContext,
    IOptions<StockReservationExpirationOptions> options)
{
    public async Task<int> ExpireOpenReservationsAsync(DateTime expiredAtUtc, CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 1000);
        var reservations = await dbContext.StockReservations
            .Where(IsDue(expiredAtUtc))
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

    /// <summary>
    /// 到期待回收的预留：仍有 open 数量、已过失效时间、且货物尚未拣出。
    /// 已拣预留保持到过账核销，不参与超时回收，也不算挂起（#3836）。
    /// </summary>
    internal static Expression<Func<StockReservation, bool>> IsDue(DateTime asOfUtc) =>
        x => x.OpenQuantity > 0m && x.ExpiresAtUtc <= asOfUtc && x.Status != StockReservation.PickedStatus;
}
