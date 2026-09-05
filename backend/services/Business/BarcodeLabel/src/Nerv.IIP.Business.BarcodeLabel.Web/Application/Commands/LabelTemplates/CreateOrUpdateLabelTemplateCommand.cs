using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.LabelTemplates;

public sealed record CreateOrUpdateLabelTemplateCommand(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string Status) : ICommand<LabelTemplateId>;

public sealed class CreateOrUpdateLabelTemplateCommandValidator : AbstractValidator<CreateOrUpdateLabelTemplateCommand>
{
    public CreateOrUpdateLabelTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TemplateCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TemplateName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TemplateFileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TemplateFileId).Must(x =>
            !x.Contains("objectKey", StringComparison.OrdinalIgnoreCase)
            && !x.Contains("object_key", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.VariableSchemaJson).NotEmpty();
        RuleFor(x => x.Status).NotEmpty().MaximumLength(30);
    }
}

public sealed class CreateOrUpdateLabelTemplateCommandHandler(
    ApplicationDbContext dbContext,
    ITemplateAssetRetirementFence retirementFence)
    : ICommandHandler<CreateOrUpdateLabelTemplateCommand, LabelTemplateId>
{
    public async Task<LabelTemplateId> Handle(CreateOrUpdateLabelTemplateCommand request, CancellationToken cancellationToken)
    {
        var observed = await dbContext.LabelTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TemplateCode == request.TemplateCode,
                cancellationToken);
        var fileIdsToFence = observed is null
            ? [request.TemplateFileId]
            : new[] { observed.TemplateFileId, request.TemplateFileId };
        foreach (var fileId in fileIdsToFence.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await retirementFence.AcquireAsync(
                request.OrganizationId,
                request.EnvironmentId,
                fileId,
                cancellationToken);
        }

        if (await dbContext.TemplateAssetRetirementDecisions.AnyAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.TemplateFileId == request.TemplateFileId,
                cancellationToken))
        {
            throw new KnownException("模板资产已经退役，不能重新用于标签模板。");
        }

        var existing = await dbContext.LabelTemplates.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.TemplateCode == request.TemplateCode,
            cancellationToken);
        if (observed is null
            ? existing is not null
            : existing is null
                || existing.Id != observed.Id
                || !string.Equals(existing.TemplateFileId, observed.TemplateFileId, StringComparison.Ordinal))
        {
            throw new KnownException("标签模板当前文件已发生并发变化，请重试。");
        }

        if (existing is not null)
        {
            existing.Update(request.TemplateName, request.TemplateFileId, request.VariableSchemaJson, request.Status);
            return existing.Id;
        }

        var template = LabelTemplate.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.TemplateCode,
            request.TemplateName,
            request.TemplateFileId,
            request.VariableSchemaJson,
            request.Status);
        dbContext.LabelTemplates.Add(template);
        return template.Id;
    }
}
