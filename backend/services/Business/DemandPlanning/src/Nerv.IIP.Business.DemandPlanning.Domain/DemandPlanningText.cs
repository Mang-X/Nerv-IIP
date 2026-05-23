namespace Nerv.IIP.Business.DemandPlanning.Domain;

internal static class DemandPlanningText
{
    public static string Required(string value, string? parameterName = null)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName ?? nameof(value))
            : value.Trim();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Quantity must be positive.") : value;
    }
}
