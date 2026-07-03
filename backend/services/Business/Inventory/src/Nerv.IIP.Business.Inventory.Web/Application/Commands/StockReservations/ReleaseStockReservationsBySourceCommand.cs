using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;

public sealed record ReleaseStockReservationsBySourceCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceService,
    string SourceDocumentId,
    IReadOnlyCollection<string> SourceDocumentLineIds) : ICommand<ReleaseStockReservationsBySourceResult>;

public sealed record ReleaseStockReservationsBySourceResult(int ReleasedReservationCount, decimal ReleasedQuantity);

public sealed class ReleaseStockReservationsBySourceCommandValidator : AbstractValidator<ReleaseStockReservationsBySourceCommand>
{
    public ReleaseStockReservationsBySourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SourceService).RequiredInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
    }
}

public sealed class ReleaseStockReservationsBySourceCommandHandler(
    ApplicationDbContext dbContext,
    ILogger<ReleaseStockReservationsBySourceCommandHandler> logger)
    : ICommandHandler<ReleaseStockReservationsBySourceCommand, ReleaseStockReservationsBySourceResult>
{
    public async Task<ReleaseStockReservationsBySourceResult> Handle(
        ReleaseStockReservationsBySourceCommand request,
        CancellationToken cancellationToken)
    {
        var sourceServices = ExpandSourceServices(request.SourceService);
        var sourceDocumentIds = new[] { request.SourceDocumentId }
            .Concat(request.SourceDocumentLineIds)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reservations = await dbContext.StockReservations
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && sourceServices.Contains(x.SourceService)
                && x.OpenQuantity > 0m
                && (sourceDocumentIds.Contains(x.SourceDocumentId)
                    || (x.SourceDocumentLineId != null && sourceDocumentIds.Contains(x.SourceDocumentLineId))))
            .ToListAsync(cancellationToken);

        var releasedCount = 0;
        var releasedQuantity = 0m;
        foreach (var reservation in reservations)
        {
            var ledger = await FindLedgerAsync(reservation, cancellationToken);
            if (ledger is null)
            {
                logger.LogWarning(
                    "Skipped Inventory reservation release because the matching stock ledger does not exist. ReservationId={ReservationId}, SourceService={SourceService}, SourceDocumentId={SourceDocumentId}, SourceDocumentLineId={SourceDocumentLineId}, SkuCode={SkuCode}, SiteCode={SiteCode}, LocationCode={LocationCode}, LotNo={LotNo}, QualityStatus={QualityStatus}.",
                    reservation.Id,
                    reservation.SourceService,
                    reservation.SourceDocumentId,
                    reservation.SourceDocumentLineId,
                    reservation.SkuCode,
                    reservation.SiteCode,
                    reservation.LocationCode,
                    reservation.LotNo,
                    reservation.QualityStatus);
                continue;
            }

            var openQuantity = reservation.OpenQuantity;
            ledger.ReleaseReservation(reservation, openQuantity);
            releasedCount++;
            releasedQuantity += openQuantity;
        }

        return new ReleaseStockReservationsBySourceResult(releasedCount, releasedQuantity);
    }

    private Task<Domain.AggregatesModel.StockLedgerAggregate.StockLedger?> FindLedgerAsync(
        StockReservation reservation,
        CancellationToken cancellationToken)
    {
        return dbContext.StockLedgers.SingleOrDefaultAsync(
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
    }

    private static string[] ExpandSourceServices(string sourceService)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceService)
            ? throw new ArgumentException("Source service is required.", nameof(sourceService))
            : sourceService.Trim();
        return string.Equals(normalized, InventoryIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase)
            ? [InventoryIntegrationEventSources.BusinessMes, "mes"]
            : [normalized];
    }
}
