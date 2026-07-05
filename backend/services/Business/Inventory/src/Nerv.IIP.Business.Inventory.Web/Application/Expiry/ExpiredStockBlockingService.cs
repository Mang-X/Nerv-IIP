using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class ExpiredStockBlockingOptions
{
    public bool Enabled { get; set; }

    public int BatchSize { get; set; } = 100;

    public string TargetQualityStatus { get; set; } = "blocked";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class ExpiredStockBlockingService(
    ApplicationDbContext dbContext,
    IOptions<ExpiredStockBlockingOptions> options)
{
    public async Task<int> BlockExpiredAvailableStockAsync(DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var targetStatus = StockQualityStatus.Normalize(options.Value.TargetQualityStatus);
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 1000);
        var expiredLedgers = await dbContext.StockLedgers
            .Where(x => x.ExpiryDate != null
                && x.ExpiryDate < asOfDate
                && x.QualityStatus != targetStatus
                && x.OnHandQuantity > x.ReservedQuantity)
            .OrderBy(x => x.ExpiryDate)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.LocationCode)
            .ThenBy(x => x.LotNo)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var blockedCount = 0;
        foreach (var source in expiredLedgers)
        {
            var quantity = source.AvailableQuantity;
            if (quantity <= 0)
            {
                continue;
            }

            var sourceDocumentId = $"{source.Id}:{asOfDate:yyyyMMdd}";
            var outboundKey = $"expiry-block:{source.Id}:{asOfDate:yyyyMMdd}:out";
            var inboundKey = $"expiry-block:{source.Id}:{asOfDate:yyyyMMdd}:in";
            if (await dbContext.StockMovements.AnyAsync(x =>
                    x.OrganizationId == source.OrganizationId
                    && x.EnvironmentId == source.EnvironmentId
                    && x.SourceService == "inventory-expiry"
                    && x.SourceDocumentId == sourceDocumentId
                    && x.IdempotencyKey == outboundKey,
                    cancellationToken))
            {
                continue;
            }

            var outbound = StockMovement.Post(
                source.OrganizationId,
                source.EnvironmentId,
                "status-transfer-out",
                "inventory-expiry",
                sourceDocumentId,
                null,
                outboundKey,
                source.SkuCode,
                source.UomCode,
                source.SiteCode,
                source.LocationCode,
                source.LotNo,
                source.SerialNo,
                source.QualityStatus,
                source.OwnerType,
                source.OwnerId,
                -quantity,
                ProductionDate: source.ProductionDate,
                ExpiryDate: source.ExpiryDate);
            source.ApplyMovement(outbound);
            dbContext.StockMovements.Add(outbound);

            var target = await FindTargetLedgerAsync(source, targetStatus, cancellationToken);
            if (target is null)
            {
                target = StockLedger.Create(
                    source.OrganizationId,
                    source.EnvironmentId,
                    source.SkuCode,
                    source.UomCode,
                    source.SiteCode,
                    source.LocationCode,
                    source.LotNo,
                    source.SerialNo,
                    targetStatus,
                    source.OwnerType,
                    source.OwnerId,
                    source.ProductionDate,
                    source.ExpiryDate);
                dbContext.StockLedgers.Add(target);
            }

            var inbound = StockMovement.Post(
                source.OrganizationId,
                source.EnvironmentId,
                "status-transfer-in",
                "inventory-expiry",
                sourceDocumentId,
                null,
                inboundKey,
                source.SkuCode,
                source.UomCode,
                source.SiteCode,
                source.LocationCode,
                source.LotNo,
                source.SerialNo,
                targetStatus,
                source.OwnerType,
                source.OwnerId,
                quantity,
                source.MovingAverageUnitCost,
                source.ProductionDate,
                source.ExpiryDate);
            target.ApplyMovement(inbound);
            dbContext.StockMovements.Add(inbound);
            blockedCount++;
        }

        if (blockedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return blockedCount;
    }

    private Task<StockLedger?> FindTargetLedgerAsync(StockLedger source, string targetStatus, CancellationToken cancellationToken)
    {
        return dbContext.StockLedgers.SingleOrDefaultAsync(
            x => x.OrganizationId == source.OrganizationId
                && x.EnvironmentId == source.EnvironmentId
                && x.SkuCode == source.SkuCode
                && x.UomCode == source.UomCode
                && x.SiteCode == source.SiteCode
                && x.LocationCode == source.LocationCode
                && x.LotNo == source.LotNo
                && x.SerialNo == source.SerialNo
                && x.ProductionDate == source.ProductionDate
                && x.ExpiryDate == source.ExpiryDate
                && x.QualityStatus == targetStatus
                && x.OwnerType == source.OwnerType
                && x.OwnerId == source.OwnerId,
            cancellationToken);
    }
}
