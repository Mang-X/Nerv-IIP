using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
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
    int? DueInHours,
    string? CompletionPolicy = null,
    string? ConditionExpression = null,
    ApprovalRoutingConditionInput? Condition = null);

public sealed record ApprovalRoutingConditionInput(
    decimal? MinimumAmount = null,
    decimal? MaximumAmount = null,
    IReadOnlyCollection<string>? DocumentTypes = null,
    IReadOnlyCollection<string>? OrganizationIds = null,
    IReadOnlyCollection<string>? DepartmentIds = null)
{
    public ApprovalRoutingCondition ToDomain() => new(MinimumAmount, MaximumAmount, DocumentTypes, OrganizationIds, DepartmentIds);
}

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
            step.RuleFor(x => x.CompletionPolicy).Must(x => string.IsNullOrWhiteSpace(x) || x is "all" or "any").WithMessage("CompletionPolicy must be all or any.");
            step.RuleFor(x => x.ConditionExpression)
                .MaximumLength(200)
                .Must(ApprovalConditionMatcher.IsValid)
                .WithMessage("ConditionExpression must be empty or use supported key=value syntax: documentType=<value> or sourceService=<value>.");
            step.RuleFor(x => x).Must(x => x.Condition is null || string.IsNullOrWhiteSpace(x.ConditionExpression))
                .WithMessage("Use either structured Condition or legacy ConditionExpression, not both.");
            step.RuleFor(x => x.Condition).Must(condition =>
            {
                if (condition is null) return true;
                try { condition.ToDomain().Validate(); return true; }
                catch (InvalidOperationException) { return false; }
            }).WithMessage("Structured approval condition contains an invalid amount range or empty dimension value.");
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
                x.DueInHours,
                x.CompletionPolicy ?? ApprovalCompletionPolicies.All,
                x.ConditionExpression,
                x.Condition?.ToDomain()))
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
