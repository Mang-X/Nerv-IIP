using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record RunMrpCommand(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd) : ICommand<RunMrpCommandResult>;

public sealed record RunMrpCommandResult(MrpRunId RunId, int SuggestionCount);

public sealed class RunMrpCommandValidator : AbstractValidator<RunMrpCommand>
{
    public RunMrpCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.HorizonEnd).GreaterThanOrEqualTo(x => x.HorizonStart);
    }
}

public sealed class RunMrpCommandHandler(ApplicationDbContext dbContext, IPlanningInputSnapshotProvider snapshotProvider)
    : ICommandHandler<RunMrpCommand, RunMrpCommandResult>
{
    public async Task<RunMrpCommandResult> Handle(RunMrpCommand request, CancellationToken cancellationToken)
    {
        var snapshot = await snapshotProvider.GetSnapshotAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.HorizonStart,
            request.HorizonEnd,
            cancellationToken);
        var run = MrpRun.Create(request.OrganizationId, request.EnvironmentId, request.HorizonStart, request.HorizonEnd);
        dbContext.MrpRuns.Add(run);
        run.Start(new PlanningInputSnapshot(
            snapshot.ProductionEngineeringSnapshotSource,
            snapshot.InventorySnapshotSource,
            snapshot.Demands.Count,
            snapshot.Availability.Count));
        var calculated = MrpCalculator.Calculate(new MrpCalculationInput(
            request.OrganizationId,
            request.EnvironmentId,
            request.HorizonStart,
            request.HorizonEnd,
            snapshot.Demands,
            snapshot.Availability,
            snapshot.ProductionVersions,
            snapshot.BomComponents));

        foreach (var calculatedSuggestion in calculated)
        {
            var suggestion = PlanningSuggestion.Create(
                request.OrganizationId,
                request.EnvironmentId,
                run.Id,
                calculatedSuggestion.SuggestionType,
                calculatedSuggestion.SkuCode,
                calculatedSuggestion.UomCode,
                calculatedSuggestion.SiteCode,
                calculatedSuggestion.Quantity,
                calculatedSuggestion.RequiredDate,
                calculatedSuggestion.ReasonCode);
            foreach (var link in calculatedSuggestion.PeggingLinks)
            {
                suggestion.AddPeggingLink(
                    link.PeggingType,
                    link.DemandSourceReference,
                    link.ParentSkuCode,
                    link.ComponentSkuCode,
                    link.Quantity,
                    link.ProductionVersionReference,
                    link.ManufacturingBomReference,
                    link.RoutingReference);
            }

            dbContext.PlanningSuggestions.Add(suggestion);
        }

        run.Complete(calculated.Count);
        return new RunMrpCommandResult(run.Id, calculated.Count);
    }
}
