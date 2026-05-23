using System.Text.RegularExpressions;

namespace Nerv.IIP.Business.Inventory.Web.Application.Validation;

internal static partial class InventoryValidationRules
{
    public const int IdempotencyKeyMaxLength = 128;
    private const string CodePattern = "^[A-Za-z0-9_.:-]+$";

    public static IRuleBuilderOptions<T, string> RequiredInventoryCode<T>(this IRuleBuilder<T, string> rule, int maxLength)
    {
        return rule
            .NotEmpty()
            .MaximumLength(maxLength)
            .Matches(CodePattern)
            .WithMessage("{PropertyName} may only contain letters, digits, dot, colon, underscore, and hyphen.");
    }

    public static IRuleBuilderOptions<T, string?> OptionalInventoryCode<T>(this IRuleBuilder<T, string?> rule, int maxLength)
    {
        return rule
            .MaximumLength(maxLength)
            .Must(value => string.IsNullOrWhiteSpace(value) || InventoryCodeRegex().IsMatch(value))
            .WithMessage("{PropertyName} may only contain letters, digits, dot, colon, underscore, and hyphen.");
    }

    [GeneratedRegex(CodePattern, RegexOptions.CultureInvariant)]
    private static partial Regex InventoryCodeRegex();
}
