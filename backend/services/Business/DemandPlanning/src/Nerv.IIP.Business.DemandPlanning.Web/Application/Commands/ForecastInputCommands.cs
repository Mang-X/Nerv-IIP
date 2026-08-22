using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record CreateOrUpdateForecastInputResult(
    ForecastInputId ForecastInputId,
    string ForecastReference);

public sealed record CreateOrUpdateForecastInputCommand(
    string OrganizationId,
    string EnvironmentId,
    string? ForecastReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    int BackwardConsumptionDays = 0,
    int ForwardConsumptionDays = 0,
    string? IdempotencyKey = null) : ICommand<CreateOrUpdateForecastInputResult>;

public sealed class CreateOrUpdateForecastInputCommandValidator : AbstractValidator<CreateOrUpdateForecastInputCommand>
{
    public CreateOrUpdateForecastInputCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ForecastReference).MaximumLength(128);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PeriodEndDate).GreaterThanOrEqualTo(x => x.PeriodStartDate);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.BackwardConsumptionDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ForwardConsumptionDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).MaximumLength(128);
    }
}

public sealed class CreateOrUpdateForecastInputCommandHandler(
    ApplicationDbContext dbContext,
    DemandPlanningCodingService? codingService = null)
    : ICommandHandler<CreateOrUpdateForecastInputCommand, CreateOrUpdateForecastInputResult>
{
    private readonly DemandPlanningCodingService _codingService = codingService ?? new DemandPlanningCodingService();

    public async Task<CreateOrUpdateForecastInputResult> Handle(
        CreateOrUpdateForecastInputCommand request,
        CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "forecast",
            request.ForecastReference,
            request.IdempotencyKey,
            DemandPlanningCodingService.Fingerprint(
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.PeriodStartDate,
                request.PeriodEndDate,
                request.Quantity,
                request.BackwardConsumptionDays,
                request.ForwardConsumptionDays),
            cancellationToken);
        var forecast = await dbContext.ForecastInputs.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ForecastReference == allocation.Code,
            cancellationToken);
        if (forecast is null)
        {
            forecast = ForecastInput.Create(
                request.OrganizationId,
                request.EnvironmentId,
                allocation.Code,
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

        return new CreateOrUpdateForecastInputResult(forecast.Id, allocation.Code);
    }
}
