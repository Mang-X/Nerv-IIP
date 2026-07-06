using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MediatR;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;

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
    IOptions<ExpiredStockBlockingOptions> options,
    ISender sender)
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
            var idempotencyKey = $"expiry-block:{source.Id}:{asOfDate:yyyyMMdd}";
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

            await sender.Send(new PostStockStatusTransferCommand(
                source.OrganizationId,
                source.EnvironmentId,
                source.QualityStatus,
                targetStatus,
                "inventory-expiry",
                sourceDocumentId,
                null,
                idempotencyKey,
                source.SkuCode,
                source.UomCode,
                source.SiteCode,
                source.LocationCode,
                source.LotNo,
                source.SerialNo,
                source.OwnerType,
                source.OwnerId,
                quantity,
                source.ProductionDate,
                source.ExpiryDate), cancellationToken);
            blockedCount++;
        }

        return blockedCount;
    }
}
