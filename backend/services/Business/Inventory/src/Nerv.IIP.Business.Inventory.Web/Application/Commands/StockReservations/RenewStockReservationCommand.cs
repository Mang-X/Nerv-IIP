using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;

public sealed record RenewStockReservationCommand(StockReservationId ReservationId) : ICommand<RenewStockReservationResult>;

public sealed record RenewStockReservationResult(StockReservationId ReservationId, DateTime ExpiresAtUtc);

public sealed class RenewStockReservationCommandValidator : AbstractValidator<RenewStockReservationCommand>
{
    public RenewStockReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
    }
}

public sealed class RenewStockReservationCommandHandler(
    ApplicationDbContext dbContext,
    IOptions<StockReservationExpirationOptions>? options = null)
    : ICommandHandler<RenewStockReservationCommand, RenewStockReservationResult>
{
    private readonly StockReservationExpirationOptions expirationOptions = options?.Value ?? new StockReservationExpirationOptions();

    public async Task<RenewStockReservationResult> Handle(RenewStockReservationCommand request, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(x => x.Id == request.ReservationId, cancellationToken)
            ?? throw new KnownException($"Stock reservation '{request.ReservationId}' was not found.");
        var renewedAtUtc = DateTime.UtcNow;
        reservation.Renew(renewedAtUtc.Add(expirationOptions.ResolveLifetime(reservation.SourceService)), renewedAtUtc);
        return new RenewStockReservationResult(reservation.Id, reservation.ExpiresAtUtc);
    }
}
