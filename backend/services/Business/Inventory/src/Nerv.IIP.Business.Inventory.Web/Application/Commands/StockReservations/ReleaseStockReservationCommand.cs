using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;

public sealed record ReleaseStockReservationCommand(
    StockReservationId ReservationId,
    decimal Quantity) : ICommand<ReleaseStockReservationResult>;

public sealed record ReleaseStockReservationResult(StockReservationId ReservationId, decimal OpenQuantity, decimal AvailableQuantity);

public sealed class ReleaseStockReservationCommandValidator : AbstractValidator<ReleaseStockReservationCommand>
{
    public ReleaseStockReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class ReleaseStockReservationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReleaseStockReservationCommand, ReleaseStockReservationResult>
{
    public async Task<ReleaseStockReservationResult> Handle(ReleaseStockReservationCommand request, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(x => x.Id == request.ReservationId, cancellationToken)
            ?? throw new KnownException($"Stock reservation '{request.ReservationId}' was not found.");
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
            cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested reservation release.");

        ledger.ReleaseReservation(reservation, request.Quantity);
        return new ReleaseStockReservationResult(reservation.Id, reservation.OpenQuantity, ledger.AvailableQuantity);
    }
}
