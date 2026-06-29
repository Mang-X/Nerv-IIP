using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
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

public sealed class CompleteNonconformanceReportInventoryDispositionCommandHandler(
    INonconformanceReportRepository repository,
    ICorrectiveActionRepository correctiveActionRepository)
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
                EnsureQuantityBalanced(ncr, request.Quantity);
                await EnsureEffectiveCapaAsync(ncr, correctiveActionRepository, cancellationToken);
                ncr.CompleteScrapDisposition(request.InventoryMovementId, request.Quantity);
            }

            return;
        }

        if (ncr.DispositionType == QualityNcrDispositionTypes.ConditionalRelease && IsPostedConditionalReleaseInbound(request))
        {
            EnsureQuantityBalanced(ncr, request.Quantity);
            ncr.CompleteConditionalReleaseDisposition(request.Quantity);
        }
    }

    private static void EnsureQuantityBalanced(NonconformanceReport ncr, decimal quantity)
    {
        if (Math.Abs(quantity) != ncr.DefectQuantity)
        {
            throw new KnownException("NCR disposition quantity must balance the full defect quantity before closing.");
        }
    }

    private static async Task EnsureEffectiveCapaAsync(
        NonconformanceReport ncr,
        ICorrectiveActionRepository correctiveActionRepository,
        CancellationToken cancellationToken)
    {
        if (!await correctiveActionRepository.HasEffectiveCapaForNcrAsync(ncr.Id.ToString(), cancellationToken))
        {
            throw new KnownException("NCR requires a linked effective CAPA before closure.");
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
