using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;

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

public sealed class SubmitNonconformanceReportDispositionCommandHandler(INonconformanceReportRepository repository)
    : ICommandHandler<SubmitNonconformanceReportDispositionCommand>
{
    public async Task Handle(SubmitNonconformanceReportDispositionCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken)
            ?? throw new KnownException($"NCR '{request.NcrId}' was not found.");
        ncr.SubmitDisposition(
            request.DispositionType,
            request.DispositionApprovalChainId,
            request.AttachmentFileIds,
            request.MrbReviews);
    }
}

public sealed record CompleteNonconformanceReportInventoryDispositionCommand(
    NonconformanceReportId NcrId,
    string InventoryMovementId,
    string MovementType,
    string QualityStatus,
    decimal Quantity) : ICommand;

public sealed class CompleteNonconformanceReportInventoryDispositionCommandValidator
    : AbstractValidator<CompleteNonconformanceReportInventoryDispositionCommand>
{
    public CompleteNonconformanceReportInventoryDispositionCommandValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.InventoryMovementId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.MovementType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(50);
    }
}

public sealed class CompleteNonconformanceReportInventoryDispositionCommandHandler(INonconformanceReportRepository repository)
    : ICommandHandler<CompleteNonconformanceReportInventoryDispositionCommand>
{
    public async Task Handle(CompleteNonconformanceReportInventoryDispositionCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken);
        if (ncr is null)
        {
            return;
        }

        if (ncr.DispositionType == QualityNcrDispositionTypes.Scrap)
        {
            if (IsPostedScrapAdjustment(request))
            {
                ncr.CompleteScrapDisposition(request.InventoryMovementId);
            }

            return;
        }

        if (ncr.DispositionType == QualityNcrDispositionTypes.ConditionalRelease && IsPostedConditionalReleaseInbound(request))
        {
            ncr.CompleteConditionalReleaseDisposition();
        }
    }

    private static bool IsPostedScrapAdjustment(CompleteNonconformanceReportInventoryDispositionCommand request)
    {
        return string.Equals(request.MovementType, InventoryMovementTypes.Adjustment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.QualityStatus, InventoryQualityStatuses.Blocked, StringComparison.OrdinalIgnoreCase)
            && request.Quantity < 0;
    }

    private static bool IsPostedConditionalReleaseInbound(CompleteNonconformanceReportInventoryDispositionCommand request)
    {
        return string.Equals(request.MovementType, InventoryMovementTypes.StatusTransferIn, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.QualityStatus, InventoryQualityStatuses.Restricted, StringComparison.OrdinalIgnoreCase)
            && request.Quantity > 0;
    }
}
