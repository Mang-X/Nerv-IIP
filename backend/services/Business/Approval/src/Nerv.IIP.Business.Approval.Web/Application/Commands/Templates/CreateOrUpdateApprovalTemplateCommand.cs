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
            // 校验消息一律中文领域文案：前端分层透传（#1298）只原样上屏中文短消息，
            // 英文 problem 文案会被兜底吞成「请稍后重试」，用户看不到到底哪儿不满足（台账 #33）。
            // 取值判定复用领域权威集合 ApprovalCompletionPolicies.Supported（大小写不敏感），
            // 避免校验器比领域更严——那正是 #1313 裁决取值必 400 的成因。
            step.RuleFor(x => x.CompletionPolicy)
                .Must(x => string.IsNullOrWhiteSpace(x) || ApprovalCompletionPolicies.Supported.Contains(x.Trim()))
                .WithMessage("步骤完成策略只能是 all（会签，全部通过）或 any（或签，任一通过）。");
            step.RuleFor(x => x.ConditionExpression)
                .MaximumLength(200)
                .Must(ApprovalConditionMatcher.IsValid)
                .WithMessage("步骤条件只能留空，或写成 documentType=<值> / sourceService=<值>。");
            step.RuleFor(x => x).Must(x => x.Condition is null || string.IsNullOrWhiteSpace(x.ConditionExpression))
                .WithMessage("结构化条件与条件表达式只能二选一，不能同时填写。");
            step.RuleFor(x => x.Condition).Must(condition =>
            {
                if (condition is null) return true;
                try { condition.ToDomain().Validate(); return true; }
                catch (InvalidOperationException) { return false; }
            }).WithMessage("结构化审批条件的金额区间非法，或维度取值里含空值。");
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
