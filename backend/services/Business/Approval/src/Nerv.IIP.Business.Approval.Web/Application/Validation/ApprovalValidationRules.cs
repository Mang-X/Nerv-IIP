namespace Nerv.IIP.Business.Approval.Web.Application.Validation;

public static class ApprovalValidationRules
{
    public const string CodePatternMessage = "Use letters, numbers, dot, colon, underscore or hyphen only.";

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
