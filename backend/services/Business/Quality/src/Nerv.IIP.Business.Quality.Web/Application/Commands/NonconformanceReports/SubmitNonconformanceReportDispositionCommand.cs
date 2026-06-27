using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

public sealed record SubmitNonconformanceReportDispositionCommand(
    NonconformanceReportId NcrId,
    string DispositionType,
    string? DispositionApprovalChainId,
    IReadOnlyCollection<string> AttachmentFileIds,
    IReadOnlyCollection<MrbReviewInput> MrbReviews) : ICommand;

public sealed class SubmitNonconformanceReportDispositionCommandValidator : AbstractValidator<SubmitNonconformanceReportDispositionCommand>
{
    public SubmitNonconformanceReportDispositionCommandValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.DispositionType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DispositionApprovalChainId).MaximumLength(150);
    }
}

public sealed class SubmitNonconformanceReportDispositionCommandHandler(
    INonconformanceReportRepository repository,
    IApprovalChainStatusClient approvalChainStatusClient)
    : ICommandHandler<SubmitNonconformanceReportDispositionCommand>
{
    public async Task Handle(SubmitNonconformanceReportDispositionCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken)
            ?? throw new KnownException($"NCR '{request.NcrId}' was not found.");
        if (NonconformanceReport.RequiresCentralApproval(request.DispositionType))
        {
            if (string.IsNullOrWhiteSpace(request.DispositionApprovalChainId))
            {
                throw new KnownException("NCR disposition requires an approved central approval chain.");
            }

            var isApproved = await approvalChainStatusClient.IsApprovedForNcrDispositionAsync(
                request.DispositionApprovalChainId,
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ncr.NcrCode,
                cancellationToken);
            if (!isApproved)
            {
                throw new KnownException("NCR disposition approval chain is not approved.");
            }
        }

        ncr.SubmitDisposition(
            request.DispositionType,
            request.DispositionApprovalChainId,
            request.AttachmentFileIds,
            request.MrbReviews);
    }

}
