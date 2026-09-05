using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;

public sealed record CreateTemplateAssetRetirementDecisionCommand(
    string OrganizationId,
    string EnvironmentId,
    LabelTemplateId LabelTemplateId,
    string TemplateFileId,
    string TemplateAssetSha256,
    string IdempotencyKey,
    string RequesterSubject,
    string Permission,
    string Reason,
    string CorrelationId) : ICommand<TemplateAssetRetirementDecisionId>;

public sealed class CreateTemplateAssetRetirementDecisionCommandValidator
    : AbstractValidator<CreateTemplateAssetRetirementDecisionCommand>
{
    public CreateTemplateAssetRetirementDecisionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LabelTemplateId).NotEmpty();
        RuleFor(x => x.TemplateFileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TemplateAssetSha256).Matches("^sha256:[0-9a-f]{64}$");
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.RequesterSubject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Permission).Equal(TemplateAssetRetirementDecision.RequiredPermission, StringComparer.Ordinal);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateTemplateAssetRetirementDecisionCommandHandler(
    ApplicationDbContext dbContext,
    ITemplateAssetRetirementFence retirementFence)
    : ICommandHandler<CreateTemplateAssetRetirementDecisionCommand, TemplateAssetRetirementDecisionId>
{
    public async Task<TemplateAssetRetirementDecisionId> Handle(
        CreateTemplateAssetRetirementDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var replay = await FindByIdempotencyKeyAsync(request, cancellationToken);
        if (replay is not null)
        {
            return EnsureSameRequest(replay, request);
        }

        await retirementFence.AcquireAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.TemplateFileId,
            cancellationToken);

        replay = await FindByIdempotencyKeyAsync(request, cancellationToken);
        if (replay is not null)
        {
            return EnsureSameRequest(replay, request);
        }

        if (await dbContext.TemplateAssetRetirementDecisions.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.TemplateFileId == request.TemplateFileId,
                cancellationToken))
        {
            throw new KnownException("模板资产已存在退役裁决，不能创建第二条记录。");
        }

        var template = await dbContext.LabelTemplates.SingleOrDefaultAsync(
                x => x.Id == request.LabelTemplateId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException("标签模板 owner 事实缺失，模板资产退役已安全拒绝。");

        var currentReferences = await dbContext.LabelTemplates
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TemplateFileId == request.TemplateFileId)
            .ToListAsync(cancellationToken);
        if (currentReferences.Any(x => x.RetiredCurrentFileByDecisionId is not null))
        {
            throw new KnownException("模板资产引用事实不完整，退役已安全拒绝。");
        }
        if (currentReferences.Any(x => x.Id != template.Id || x.Status == LabelTemplate.ActiveStatus))
        {
            throw new KnownException("模板资产仍可被标签模板引用，不能退役。");
        }

        if (currentReferences.Any(x => x.Status != LabelTemplate.InactiveStatus))
        {
            throw new KnownException("模板资产引用事实不完整，退役已安全拒绝。");
        }

        var scopedBatches = await dbContext.LabelPrintBatches
            .Include(x => x.Items)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId)
            .ToListAsync(cancellationToken);
        var batchDispositions = scopedBatches
            .Select(batch => batch.GetTemplateAssetReferenceDisposition(
                template.Id,
                request.TemplateFileId,
                request.TemplateAssetSha256))
            .ToArray();
        if (batchDispositions.Contains(TemplateAssetReferenceDisposition.Unknown))
        {
            throw new KnownException("模板资产引用事实不完整，退役已安全拒绝。");
        }

        if (batchDispositions.Contains(TemplateAssetReferenceDisposition.Hold))
        {
            throw new KnownException("模板资产存在未封闭的交付事实，退役已安全拒绝。");
        }

        if (batchDispositions.Contains(TemplateAssetReferenceDisposition.Reachable))
        {
            throw new KnownException("模板资产仍可被打印批次引用，不能退役。");
        }

        var decision = TemplateAssetRetirementDecision.Create(
            request.OrganizationId,
            request.EnvironmentId,
            template.Id,
            template.TemplateCode,
            request.TemplateFileId,
            request.TemplateAssetSha256,
            request.IdempotencyKey,
            request.RequesterSubject,
            request.Permission,
            request.Reason,
            request.CorrelationId);
        if (currentReferences.Count == 1)
        {
            template.FreezeCurrentFileForRetirement(request.TemplateFileId, decision.Id);
        }

        dbContext.TemplateAssetRetirementDecisions.Add(decision);
        return decision.Id;
    }

    private Task<TemplateAssetRetirementDecision?> FindByIdempotencyKeyAsync(
        CreateTemplateAssetRetirementDecisionCommand request,
        CancellationToken cancellationToken) =>
        dbContext.TemplateAssetRetirementDecisions.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);

    private static TemplateAssetRetirementDecisionId EnsureSameRequest(
        TemplateAssetRetirementDecision existing,
        CreateTemplateAssetRetirementDecisionCommand request)
    {
        if (!existing.HasSameRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.TemplateFileId,
                request.TemplateAssetSha256,
                request.Reason))
        {
            throw new KnownException("模板资产退役幂等键与已有记录不一致，请检查提交内容。");
        }

        return existing.Id;
    }
}
