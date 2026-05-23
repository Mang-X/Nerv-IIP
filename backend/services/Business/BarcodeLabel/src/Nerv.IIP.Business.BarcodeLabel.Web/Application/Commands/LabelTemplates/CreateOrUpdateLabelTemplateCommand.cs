using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

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

public sealed class CreateOrUpdateLabelTemplateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateLabelTemplateCommand, LabelTemplateId>
{
    public async Task<LabelTemplateId> Handle(CreateOrUpdateLabelTemplateCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.LabelTemplates.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.TemplateCode == request.TemplateCode,
            cancellationToken);
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
