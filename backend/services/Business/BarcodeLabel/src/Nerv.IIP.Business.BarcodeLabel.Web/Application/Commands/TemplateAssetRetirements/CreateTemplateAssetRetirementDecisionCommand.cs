using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

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

public sealed class CreateTemplateAssetRetirementDecisionCommandHandler(ApplicationDbContext dbContext)
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

        await TemplateAssetRetirementFence.AcquireAsync(
            dbContext,
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
                && x.TemplateFileId == request.TemplateFileId
                && x.RetiredCurrentFileByDecisionId == null)
            .ToListAsync(cancellationToken);
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
        var ambiguousPartialSnapshot = scopedBatches.Any(batch =>
            !batch.HasCompleteReplaySnapshot
            && (batch.TemplateFileIdSnapshot is not null
                || batch.TemplateAssetSha256 is not null
                || batch.VariableSchemaJsonSnapshot is not null
                || batch.BarcodeTypeSnapshot is not null
                || batch.RendererContractVersion is not null)
            && (batch.TemplateFileIdSnapshot is null
                || batch.TemplateFileIdSnapshot == request.TemplateFileId));
        var matchingBatches = scopedBatches
            .Where(batch => batch.TemplateFileIdSnapshot == request.TemplateFileId)
            .ToArray();
        var unknownBatchFact = ambiguousPartialSnapshot || matchingBatches.Any(batch =>
            !batch.HasCompleteReplaySnapshot
            || batch.LabelTemplateId != template.Id
            || batch.TemplateAssetSha256 != request.TemplateAssetSha256
            || batch.Status is not ("pending" or "failed" or "sent-to-printer" or "printed" or "delivery-unknown")
            || (batch.Status is "sent-to-printer" or "printed"
                && (batch.Items.Count == 0
                    || batch.Items.Any(item => item.Status is not ("created" or "printed" or "reprinted" or "voided" or "consumed")))));
        if (unknownBatchFact)
        {
            throw new KnownException("模板资产引用事实不完整，退役已安全拒绝。");
        }

        if (matchingBatches.Any(batch => batch.Status == "delivery-unknown"))
        {
            throw new KnownException("模板资产存在未封闭的交付事实，退役已安全拒绝。");
        }

        if (matchingBatches.Any(batch =>
                batch.Status is "pending" or "failed"
                || (batch.Status is "sent-to-printer" or "printed"
                    && batch.Items.Any(item => item.Status is not ("voided" or "consumed")))))
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
                request.LabelTemplateId,
                request.TemplateFileId,
                request.TemplateAssetSha256,
                request.RequesterSubject,
                request.Permission,
                request.Reason,
                request.CorrelationId))
        {
            throw new KnownException("模板资产退役幂等键与已有记录不一致，请检查提交内容。");
        }

        return existing.Id;
    }
}
