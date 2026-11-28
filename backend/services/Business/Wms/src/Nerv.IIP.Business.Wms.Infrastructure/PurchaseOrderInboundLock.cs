using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Wms.Infrastructure;

public static class PurchaseOrderInboundLock
{
    public static async Task AcquireAsync(
        ApplicationDbContext db, string organizationId, string environmentId, string purchaseOrderNo,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Purchase order inbound lock requires an active transaction.");
        }

        var key = JsonSerializer.Serialize(new[] { "wms-purchase-order-inbound", organizationId, environmentId, purchaseOrderNo });
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}
