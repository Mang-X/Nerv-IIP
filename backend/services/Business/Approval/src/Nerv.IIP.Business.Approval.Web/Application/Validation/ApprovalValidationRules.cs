using System.Linq.Expressions;

namespace Nerv.IIP.Business.Approval.Web.Application.Validation;

public static class ApprovalValidationRules
{
    public const string CodePatternMessage = "只能使用字母、数字与点、冒号、下划线、连字符。";

    public static IRuleBuilderOptions<T, string> RequiredApprovalCode<T>(this IRuleBuilder<T, string> rule, int maxLength)
    {
        return rule.NotEmpty().MaximumLength(maxLength).Matches("^[A-Za-z0-9_.:-]+$").WithMessage(CodePatternMessage);
    }

    public static IRuleBuilderOptions<T, string?> OptionalApprovalCode<T>(this IRuleBuilder<T, string?> rule, int maxLength)
    {
        return rule
            .MaximumLength(maxLength)
            .Must(value => string.IsNullOrWhiteSpace(value) || System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_.:-]+$"))
            .WithMessage(CodePatternMessage);
    }

    public static void AddRequiredTenantRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> organizationId,
        Expression<Func<T, string>> environmentId)
    {
        validator.RuleFor(organizationId).RequiredApprovalCode(100);
        validator.RuleFor(environmentId).RequiredApprovalCode(100);
    }

    public static void AddOptionalTenantRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> organizationId,
        Expression<Func<T, string?>> environmentId)
    {
        validator.RuleFor(organizationId).OptionalApprovalCode(100);
        validator.RuleFor(environmentId).OptionalApprovalCode(100);
    }

    public static void AddOffsetPageRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> skip,
        Expression<Func<T, int>> take)
    {
        validator.RuleFor(skip).GreaterThanOrEqualTo(0);
        validator.RuleFor(take).InclusiveBetween(1, 500);
    }
}
