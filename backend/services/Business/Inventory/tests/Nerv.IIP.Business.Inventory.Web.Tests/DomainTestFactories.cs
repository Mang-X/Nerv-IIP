using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

internal static class DomainLedgerFactory
{
    public static StockLedger NewLedger()
    {
        return StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
    }
}

internal static class DomainMovementFactory
{
    public static StockMovement Inbound(decimal quantity)
    {
        return InboundWithIdempotency("idem-in-001", quantity);
    }

    public static StockMovement InboundWithIdempotency(string idempotencyKey, decimal quantity)
    {
        return New("LOC-A-01", "LOT-001", idempotencyKey, quantity);
    }

    public static StockMovement InboundForLocation(string locationCode, string lotNo, decimal quantity)
    {
        return New(locationCode, lotNo, "idem-in-001", quantity);
    }

    private static StockMovement New(string locationCode, string lotNo, string idempotencyKey, decimal quantity)
    {
        return StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-001",
            "LINE-001",
            idempotencyKey,
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            locationCode,
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            quantity);
    }
}

internal static class DomainCountTaskFactory
{
    public static StockCountTask NewTask(StockLedger ledger)
    {
        return StockCountTask.Create(
            "org-001",
            "env-dev",
            "COUNT-001",
            "COUNT-001",
            ledger.OrganizationId,
            ledger.EnvironmentId,
            ledger.SkuCode,
            ledger.UomCode,
            ledger.SiteCode,
            ledger.LocationCode,
            ledger.LotNo,
            ledger.SerialNo,
            ledger.QualityStatus,
            ledger.OwnerType,
            ledger.OwnerId,
            ledger.LedgerVersion);
    }
}
