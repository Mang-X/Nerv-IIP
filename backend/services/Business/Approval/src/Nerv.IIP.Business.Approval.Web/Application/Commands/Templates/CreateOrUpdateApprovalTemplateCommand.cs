using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Templates;

public sealed record CreateOrUpdateApprovalTemplateCommand(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<ApprovalTemplateStepInput> Steps) : ICommand<ApprovalTemplateId>;

public sealed record ApprovalTemplateStepInput(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours);

public sealed class CreateOrUpdateApprovalTemplateCommandValidator : AbstractValidator<CreateOrUpdateApprovalTemplateCommand>
{
    public CreateOrUpdateApprovalTemplateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.TemplateCode).RequiredApprovalCode(100);
        RuleFor(x => x.DocumentType).RequiredApprovalCode(100);
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Steps).NotEmpty();
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(x => x.StepNo).GreaterThan(0);
            step.RuleFor(x => x.StepName).NotEmpty().MaximumLength(100);
            step.RuleFor(x => x.ParallelGroupKey).OptionalApprovalCode(100);
            step.RuleFor(x => x.ApproverType).RequiredApprovalCode(50);
            step.RuleFor(x => x.ApproverRef).RequiredApprovalCode(150);
            step.RuleFor(x => x.DueInHours).GreaterThan(0).When(x => x.DueInHours.HasValue);
        });
    }
}

public sealed class CreateOrUpdateApprovalTemplateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateApprovalTemplateCommand, ApprovalTemplateId>
{
    public async Task<ApprovalTemplateId> Handle(CreateOrUpdateApprovalTemplateCommand request, CancellationToken cancellationToken)
    {
        var steps = request.Steps
            .Select(x => new ApprovalTemplateStepDefinition(
                x.StepNo,
                x.StepName,
                x.ParallelGroupKey,
                x.ApproverType,
                x.ApproverRef,
                x.DueInHours))
            .ToArray();
        var template = await dbContext.ApprovalTemplates
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TemplateCode == request.TemplateCode,
                cancellationToken);
        if (template is null)
        {
            template = ApprovalTemplate.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.TemplateCode,
                request.DocumentType,
                request.Version,
                request.IsActive,
                steps);
            dbContext.ApprovalTemplates.Add(template);
            return template.Id;
        }

        template.ReplaceDefinition(request.DocumentType, request.Version, request.IsActive, steps);
        return template.Id;
    }
}
