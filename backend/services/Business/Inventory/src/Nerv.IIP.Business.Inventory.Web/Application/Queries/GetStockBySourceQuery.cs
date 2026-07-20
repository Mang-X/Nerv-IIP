using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public sealed record GetStockBySourceQuery(
    string OrganizationId,
    string EnvironmentId,
    string SourceService,
    string? SourceDocumentId,
    string? SourceDocumentLineId) : IQuery<StockBySourceResponse>;

public sealed record StockBySourceResponse(
    string SourceService,
    string? SourceDocumentId,
    string? SourceDocumentLineId,
    bool IsEstablished,
    IReadOnlyCollection<SourceStockMovementFact> Movements,
    IReadOnlyCollection<SourceStockBalanceFact> Balances);

public sealed record SourceStockMovementFact(
    string MovementId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    decimal Quantity,
    DateTime PostedAtUtc);

public sealed record SourceStockBalanceFact(
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    long LedgerVersion,
    DateTime UpdatedAtUtc);

public sealed class GetStockBySourceQueryValidator : AbstractValidator<GetStockBySourceQuery>
{
    public GetStockBySourceQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SourceService).RequiredInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
        RuleFor(x => x.SourceDocumentLineId).MaximumLength(150);
        RuleFor(x => x).Must(x =>
                !string.IsNullOrWhiteSpace(x.SourceDocumentId) ||
                !string.IsNullOrWhiteSpace(x.SourceDocumentLineId))
            .WithMessage("Source document id or source document line id is required.");
    }
}

public sealed class GetStockBySourceQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetStockBySourceQuery, StockBySourceResponse>
{
    private const int MaxMovements = 100;

    public async Task<StockBySourceResponse> Handle(
        GetStockBySourceQuery request,
        CancellationToken cancellationToken)
    {
        var sourceDocumentId = NormalizeOptional(request.SourceDocumentId);
        var sourceDocumentLineId = NormalizeOptional(request.SourceDocumentLineId);
        var movementQuery = dbContext.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceService == request.SourceService);

        if (sourceDocumentId is not null)
        {
            movementQuery = movementQuery.Where(x => x.SourceDocumentId == sourceDocumentId);
        }

        if (sourceDocumentLineId is not null)
        {
            movementQuery = movementQuery.Where(x => x.SourceDocumentLineId == sourceDocumentLineId);
        }

        var movements = await movementQuery
            .OrderBy(x => x.PostedAtUtc)
            .ThenBy(x => x.Id)
            .Take(MaxMovements + 1)
            .Select(x => new SourceStockMovementFact(
                x.Id.ToString(),
                x.MovementType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.IdempotencyKey,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.ProductionDate,
                x.ExpiryDate,
                x.Quantity,
                x.PostedAtUtc))
            .ToArrayAsync(cancellationToken);

        if (movements.Length > MaxMovements)
        {
            throw new KnownException($"Inventory source query returned more than {MaxMovements} movements. Add both source document id and source document line id to narrow the request.");
        }

        if (movements.Length == 0)
        {
            return new StockBySourceResponse(
                request.SourceService,
                sourceDocumentId,
                sourceDocumentLineId,
                false,
                [],
                []);
        }

        var dimensions = movements.Select(StockDimension.From).ToHashSet();
        var skuCodes = dimensions.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).ToArray();
        var uomCodes = dimensions.Select(x => x.UomCode).Distinct(StringComparer.Ordinal).ToArray();
        var siteCodes = dimensions.Select(x => x.SiteCode).Distinct(StringComparer.Ordinal).ToArray();
        var locationCodes = dimensions.Select(x => x.LocationCode).Distinct(StringComparer.Ordinal).ToArray();
        var ledgerCandidates = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                skuCodes.Contains(x.SkuCode) &&
                uomCodes.Contains(x.UomCode) &&
                siteCodes.Contains(x.SiteCode) &&
                locationCodes.Contains(x.LocationCode))
            .ToArrayAsync(cancellationToken);

        var balances = ledgerCandidates
            .Where(x => dimensions.Contains(StockDimension.From(x)))
            .OrderBy(x => x.SkuCode, StringComparer.Ordinal)
            .ThenBy(x => x.LocationCode, StringComparer.Ordinal)
            .ThenBy(x => x.LotNo, StringComparer.Ordinal)
            .ThenBy(x => x.SerialNo, StringComparer.Ordinal)
            .ThenBy(x => x.QualityStatus, StringComparer.Ordinal)
            .Select(x => new SourceStockBalanceFact(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.ProductionDate,
                x.ExpiryDate,
                x.OnHandQuantity,
                x.ReservedQuantity,
                x.AvailableQuantity,
                x.LedgerVersion,
                x.UpdatedAtUtc))
            .ToArray();

        return new StockBySourceResponse(
            request.SourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            true,
            movements,
            balances);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record StockDimension(
        string SkuCode,
        string UomCode,
        string SiteCode,
        string LocationCode,
        string? LotNo,
        string? SerialNo,
        string OwnerType,
        string? OwnerId,
        DateOnly? ProductionDate,
        DateOnly? ExpiryDate)
    {
        public static StockDimension From(SourceStockMovementFact movement) =>
            new(
                movement.SkuCode,
                movement.UomCode,
                movement.SiteCode,
                movement.LocationCode,
                movement.LotNo,
                movement.SerialNo,
                movement.OwnerType,
                movement.OwnerId,
                movement.ProductionDate,
                movement.ExpiryDate);

        public static StockDimension From(global::Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate.StockLedger ledger) =>
            new(
                ledger.SkuCode,
                ledger.UomCode,
                ledger.SiteCode,
                ledger.LocationCode,
                ledger.LotNo,
                ledger.SerialNo,
                ledger.OwnerType,
                ledger.OwnerId,
                ledger.ProductionDate,
                ledger.ExpiryDate);
    }
}
