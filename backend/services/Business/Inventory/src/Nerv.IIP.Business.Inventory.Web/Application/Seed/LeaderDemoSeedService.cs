using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string RawMaterialSkuCode = "SKU-DEMO-RM-001";
    public const string SiteCode = "SITE-001";
    public const string LocationCode = "DEMO-RAW-01";
    private const string SourceDocumentId = "LEADER-DEMO-RAW-STOCK";
    private const string IdempotencyKey = "leader-demo-raw-stock-v1";
    private const decimal Quantity = 100m;

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.StockLedgers
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.SkuCode == RawMaterialSkuCode && x.LocationCode == LocationCode)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length > 0)
        {
            var ledger = candidates.SingleOrDefault();
            var movement = await dbContext.StockMovements.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.SourceService == "leader-demo-seed" &&
                x.SourceDocumentId == SourceDocumentId && x.IdempotencyKey == IdempotencyKey, cancellationToken);
            if (ledger is null || ledger.UomCode != "pcs" || ledger.SiteCode != SiteCode || ledger.LotNo is not null || ledger.SerialNo is not null ||
                ledger.QualityStatus != StockQualityStatus.Unrestricted || ledger.OwnerType != StockOwnerType.Company || ledger.OwnerId is not null ||
                ledger.OnHandQuantity != Quantity || ledger.ReservedQuantity != 0m || movement is null || movement.Quantity != Quantity)
            {
                throw Collision();
            }

            return;
        }

        var stockLedger = StockLedger.Create(
            organizationId, environmentId, RawMaterialSkuCode, "pcs", SiteCode, LocationCode, null, null,
            StockQualityStatus.Unrestricted, StockOwnerType.Company, null);
        var stockMovement = StockMovement.Post(
            organizationId, environmentId, "inbound", "leader-demo-seed", SourceDocumentId, "10", IdempotencyKey,
            RawMaterialSkuCode, "pcs", SiteCode, LocationCode, null, null, StockQualityStatus.Unrestricted, StockOwnerType.Company, null, Quantity, 1m);
        stockLedger.ApplyMovement(stockMovement);
        dbContext.StockLedgers.Add(stockLedger);
        dbContext.StockMovements.Add(stockMovement);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InvalidOperationException Collision() =>
        new($"Reserved leader-demo stock fact '{RawMaterialSkuCode}' at '{LocationCode}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
