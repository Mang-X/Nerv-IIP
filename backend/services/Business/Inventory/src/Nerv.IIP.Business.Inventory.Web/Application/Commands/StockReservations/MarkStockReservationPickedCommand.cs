using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;

/// <summary>
/// WMS 拣货完成后调用：预留从此保持到出库过账核销，不再因超时过期（#3836）。
/// </summary>
public sealed record MarkStockReservationPickedCommand(StockReservationId ReservationId) : ICommand<MarkStockReservationPickedResult>;

public sealed record MarkStockReservationPickedResult(StockReservationId ReservationId, string Status, decimal OpenQuantity);

public sealed class MarkStockReservationPickedCommandValidator : AbstractValidator<MarkStockReservationPickedCommand>
{
    public MarkStockReservationPickedCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
    }
}

public sealed class MarkStockReservationPickedCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkStockReservationPickedCommand, MarkStockReservationPickedResult>
{
    public async Task<MarkStockReservationPickedResult> Handle(MarkStockReservationPickedCommand request, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(x => x.Id == request.ReservationId, cancellationToken)
            ?? throw new KnownException($"Stock reservation '{request.ReservationId}' was not found.");
        reservation.MarkPicked();
        return new MarkStockReservationPickedResult(reservation.Id, reservation.Status, reservation.OpenQuantity);
    }
}
