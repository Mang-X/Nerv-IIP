using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

public sealed record CloseNonconformanceReportCommand(
    NonconformanceReportId NcrId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    string Reason) : ICommand;

public sealed class CloseNonconformanceReportCommandValidator : AbstractValidator<CloseNonconformanceReportCommand>
{
    public CloseNonconformanceReportCommandValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.ReworkWorkOrderId).MaximumLength(150);
        RuleFor(x => x.ScrapMovementId).MaximumLength(150);
        RuleFor(x => x.ReturnDocumentId).MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CloseNonconformanceReportCommandHandler(
    INonconformanceReportRepository repository,
    ICorrectiveActionRepository correctiveActionRepository)
    : ICommandHandler<CloseNonconformanceReportCommand>
{
    public async Task Handle(CloseNonconformanceReportCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken)
            ?? throw new KnownException($"NCR '{request.NcrId}' was not found.");
        if (NonconformanceReport.RequiresEffectiveCapa(ncr.SourceType, ncr.DispositionType)
            && !await correctiveActionRepository.HasEffectiveCapaForNcrAsync(
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ncr.Id.ToString(),
                cancellationToken))
        {
            throw new KnownException("NCR requires a linked effective CAPA before closure.");
        }

        ncr.Close(request.ReworkWorkOrderId, request.ScrapMovementId, request.ReturnDocumentId, request.Reason);
    }
}
