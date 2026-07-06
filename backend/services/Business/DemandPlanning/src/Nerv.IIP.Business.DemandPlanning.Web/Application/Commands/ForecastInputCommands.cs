using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record CreateOrUpdateForecastInputCommand(
    string OrganizationId,
    string EnvironmentId,
    string ForecastReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    int BackwardConsumptionDays = 0,
    int ForwardConsumptionDays = 0) : ICommand<ForecastInputId>;

public sealed class CreateOrUpdateForecastInputCommandValidator : AbstractValidator<CreateOrUpdateForecastInputCommand>
{
    public CreateOrUpdateForecastInputCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ForecastReference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PeriodEndDate).GreaterThanOrEqualTo(x => x.PeriodStartDate);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.BackwardConsumptionDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ForwardConsumptionDays).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateOrUpdateForecastInputCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateForecastInputCommand, ForecastInputId>
{
    public async Task<ForecastInputId> Handle(CreateOrUpdateForecastInputCommand request, CancellationToken cancellationToken)
    {
        var forecast = await dbContext.ForecastInputs.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ForecastReference == request.ForecastReference,
            cancellationToken);
        if (forecast is null)
        {
            forecast = ForecastInput.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.ForecastReference,
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.PeriodStartDate,
                request.PeriodEndDate,
                request.Quantity,
                request.BackwardConsumptionDays,
                request.ForwardConsumptionDays);
            dbContext.ForecastInputs.Add(forecast);
        }
        else
        {
            forecast.Update(
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.PeriodStartDate,
                request.PeriodEndDate,
                request.Quantity,
                request.BackwardConsumptionDays,
                request.ForwardConsumptionDays);
        }

        return forecast.Id;
    }
}
