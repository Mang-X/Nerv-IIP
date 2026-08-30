using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

public sealed record SubmitNonconformanceReportDispositionCommand(
    NonconformanceReportId NcrId,
    string OrganizationId,
    string EnvironmentId,
    string DispositionType,
    string? DispositionApprovalChainId,
    IReadOnlyCollection<string> AttachmentFileIds,
    IReadOnlyCollection<MrbReviewInput> MrbReviews,
    string? IdempotencyKey = null) : ICommand;

public sealed class SubmitNonconformanceReportDispositionCommandLock
    : ICommandLock<SubmitNonconformanceReportDispositionCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        SubmitNonconformanceReportDispositionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-quality:ncr-disposition:{command.OrganizationId}:{command.EnvironmentId}:{command.NcrId}",
            30));
    }
}

public sealed class SubmitNonconformanceReportDispositionCommandValidator : AbstractValidator<SubmitNonconformanceReportDispositionCommand>
{
    public SubmitNonconformanceReportDispositionCommandValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DispositionType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DispositionApprovalChainId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(150)
            .When(x => string.Equals(
                x.DispositionType,
                QualityNcrDispositionTypes.Rework,
                StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class SubmitNonconformanceReportDispositionCommandHandler(
    INonconformanceReportRepository repository,
    IApprovalChainStatusClient approvalChainStatusClient,
    ICapaAutomationService capaAutomationService,
    ApplicationDbContext dbContext)
    : ICommandHandler<SubmitNonconformanceReportDispositionCommand>
{
    private const string ReworkDispositionRuleKey = "ncr-rework-disposition";

    public async Task Handle(SubmitNonconformanceReportDispositionCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetScopedAsync(
                request.NcrId,
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken)
            ?? throw QualityAuthorizationException.Forbidden("ncr-tenant-mismatch");
        var isRework = string.Equals(
            request.DispositionType,
            QualityNcrDispositionTypes.Rework,
            StringComparison.OrdinalIgnoreCase);
        var idempotencyKey = isRework ? request.IdempotencyKey!.Trim() : null;
        var requestFingerprint = isRework ? Fingerprint(request) : null;
        if (isRework && await IsReplayAsync(
                ncr,
                idempotencyKey!,
                requestFingerprint!,
                cancellationToken))
        {
            return;
        }

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
        if (isRework)
        {
            dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ReworkDispositionRuleKey,
                idempotencyKey!,
                ncr.Id.ToString(),
                requestFingerprint!,
                DateTimeOffset.UtcNow));
        }
    }

    private async Task<bool> IsReplayAsync(
        NonconformanceReport ncr,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var existing = dbContext.CodeIdempotencyKeys.Local.FirstOrDefault(x =>
            x.OrganizationId == ncr.OrganizationId &&
            x.EnvironmentId == ncr.EnvironmentId &&
            x.RuleKey == ReworkDispositionRuleKey &&
            x.IdempotencyKey == idempotencyKey)
            ?? await dbContext.CodeIdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrganizationId == ncr.OrganizationId &&
                    x.EnvironmentId == ncr.EnvironmentId &&
                    x.RuleKey == ReworkDispositionRuleKey &&
                    x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is null)
        {
            return false;
        }

        if (!string.Equals(existing.Code, ncr.Id.ToString(), StringComparison.Ordinal)
            || !string.Equals(existing.PayloadFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new QualityIdempotencyConflictException();
        }

        return true;
    }

    internal static string Fingerprint(SubmitNonconformanceReportDispositionCommand request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ncrId = request.NcrId.ToString(),
            organizationId = request.OrganizationId,
            environmentId = request.EnvironmentId,
            dispositionType = request.DispositionType.Trim().ToLowerInvariant(),
            dispositionApprovalChainId = Normalize(request.DispositionApprovalChainId),
            attachmentFileIds = request.AttachmentFileIds
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            mrbReviews = request.MrbReviews
                .Select(review => new
                {
                    reviewerId = review.ReviewerId.Trim(),
                    decision = review.Decision.Trim().ToLowerInvariant(),
                    comment = Normalize(review.Comment),
                    reviewedAtUtc = review.ReviewedAtUtc.ToUniversalTime().ToString("O"),
                })
                .OrderBy(review => review.reviewerId, StringComparer.Ordinal)
                .ThenBy(review => review.decision, StringComparer.Ordinal)
                .ThenBy(review => review.comment, StringComparer.Ordinal)
                .ThenBy(review => review.reviewedAtUtc, StringComparer.Ordinal)
                .ToArray(),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
