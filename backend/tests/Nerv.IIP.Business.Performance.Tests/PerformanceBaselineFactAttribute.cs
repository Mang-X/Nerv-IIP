namespace Nerv.IIP.Business.Performance.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PerformanceBaselineFactAttribute : FactAttribute
{
    public PerformanceBaselineFactAttribute(string scenario, string? requiredEnvironmentVariable = null)
    {
        Skip = GetSkipReason(
            scenario,
            Environment.GetEnvironmentVariable(PerformanceBaselineSettings.ConnectionStringEnvironmentVariable),
            Environment.GetEnvironmentVariable(PerformanceBaselineSettings.ScenarioEnvironmentVariable),
            requiredEnvironmentVariable,
            string.IsNullOrWhiteSpace(requiredEnvironmentVariable)
                ? null
                : Environment.GetEnvironmentVariable(requiredEnvironmentVariable));
    }

    internal static string? GetSkipReason(
        string scenario,
        string? connectionString,
        string? requestedScenario,
        string? requiredEnvironmentVariable,
        string? requiredEnvironmentValue)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return $"Set {PerformanceBaselineSettings.ConnectionStringEnvironmentVariable} to run PostgreSQL performance baseline tests.";
        }

        if (!string.IsNullOrWhiteSpace(requestedScenario)
            && !string.Equals(requestedScenario, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedScenario, scenario, StringComparison.OrdinalIgnoreCase))
        {
            return $"Scenario '{scenario}' is disabled by {PerformanceBaselineSettings.ScenarioEnvironmentVariable}={requestedScenario}.";
        }

        return !string.IsNullOrWhiteSpace(requiredEnvironmentVariable)
            && string.IsNullOrWhiteSpace(requiredEnvironmentValue)
                ? $"Set {requiredEnvironmentVariable} to run the '{scenario}' performance baseline."
                : null;
    }
}
