using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
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
    IApprovalChainStatusClient approvalChainStatusClient,
    ICapaAutomationService capaAutomationService)
    : ICommandHandler<SubmitNonconformanceReportDispositionCommand>
{
    public async Task Handle(SubmitNonconformanceReportDispositionCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken)
            ?? throw new KnownException($"找不到不合格报告 {request.NcrId}，请在不合格报告页确认单据存在后重试。");
        if (ncr.Status != "open")
        {
            throw new QualityLifecycleConflictException("submit-ncr-disposition", ncr.Status);
        }

        if (NonconformanceReport.RequiresCentralApproval(request.DispositionType))
        {
            if (string.IsNullOrWhiteSpace(request.DispositionApprovalChainId))
            {
                throw new KnownException($"不合格报告 {ncr.NcrCode} 的处置需要已批准的中央审批链，请在审批页面提交并批准后再提交处置。");
            }

            var isApproved = await approvalChainStatusClient.IsApprovedForNcrDispositionAsync(
                request.DispositionApprovalChainId,
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ncr.NcrCode,
                cancellationToken);
            if (!isApproved)
            {
                throw new KnownException($"不合格报告 {ncr.NcrCode} 的处置审批链 {request.DispositionApprovalChainId} 尚未批准，请在审批页面完成审批后再提交处置。");
            }
        }

        try
        {
            ncr.SubmitDisposition(
                request.DispositionType,
                request.DispositionApprovalChainId,
                request.AttachmentFileIds,
                request.MrbReviews);
        }
        catch (InvalidOperationException)
        {
            throw new KnownException($"不合格报告 {ncr.NcrCode} 的处置条件未满足，请检查 MRB 审批、附件和处置类型后重试。");
        }
        catch (ArgumentException)
        {
            throw new KnownException($"不合格报告 {ncr.NcrCode} 的处置参数无效，请检查处置类型、审批链和附件后重试。");
        }

        await capaAutomationService.OpenForDispositionIfRequiredAsync(ncr, cancellationToken);
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
            if (IsPostedScrapAdjustment(request) && IsFullDispositionQuantity(ncr, request.Quantity))
            {
                if (NonconformanceReport.RequiresEffectiveCapa(ncr.SourceType, ncr.DispositionType)
                    && !await correctiveActionRepository.HasEffectiveCapaForNcrAsync(
                        ncr.OrganizationId,
                        ncr.EnvironmentId,
                        ncr.Id.ToString(),
                        cancellationToken))
                {
                    ncr.RecordScrapDispositionMovement(request.InventoryMovementId, request.Quantity);
                    return;
                }

                ncr.CompleteScrapDisposition(request.InventoryMovementId, request.Quantity);
            }

            return;
        }

        if (ncr.DispositionType == QualityNcrDispositionTypes.ConditionalRelease
            && IsPostedConditionalReleaseInbound(request)
            && IsFullDispositionQuantity(ncr, request.Quantity))
        {
            ncr.CompleteConditionalReleaseDisposition(request.Quantity);
        }
    }

    private static bool IsFullDispositionQuantity(NonconformanceReport ncr, decimal quantity) => Math.Abs(quantity) == ncr.DefectQuantity;

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
