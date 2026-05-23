using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockLocations;

public sealed record CreateStockLocationCommand(
    string OrganizationId,
    string EnvironmentId,
    string LocationCode,
    string LocationType,
    string SiteCode,
    string? ParentLocationCode,
    string Status) : ICommand<CreateStockLocationResult>;

public sealed record CreateStockLocationResult(StockLocationId LocationId);

public sealed class CreateStockLocationCommandValidator : AbstractValidator<CreateStockLocationCommand>
{
    public CreateStockLocationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).NotEmpty().MaximumLength(30);
    }
}

public sealed class CreateStockLocationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateStockLocationCommand, CreateStockLocationResult>
{
    public async Task<CreateStockLocationResult> Handle(CreateStockLocationCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.StockLocations.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.LocationCode == request.LocationCode,
            cancellationToken);
        var location = StockLocation.CreateOrUpdate(
            existing,
            request.OrganizationId,
            request.EnvironmentId,
            request.LocationCode,
            request.LocationType,
            request.SiteCode,
            request.ParentLocationCode,
            request.Status);
        if (existing is null)
        {
            dbContext.StockLocations.Add(location);
        }

        return new CreateStockLocationResult(location.Id);
    }
}
