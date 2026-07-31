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
}
