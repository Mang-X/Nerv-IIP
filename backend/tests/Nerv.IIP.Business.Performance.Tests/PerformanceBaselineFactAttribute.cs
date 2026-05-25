namespace Nerv.IIP.Business.Performance.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PerformanceBaselineFactAttribute : FactAttribute
{
    public PerformanceBaselineFactAttribute(string scenario)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PerformanceBaselineSettings.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {PerformanceBaselineSettings.ConnectionStringEnvironmentVariable} to run PostgreSQL performance baseline tests.";
            return;
        }

        var requestedScenario = Environment.GetEnvironmentVariable(PerformanceBaselineSettings.ScenarioEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requestedScenario)
            && !string.Equals(requestedScenario, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedScenario, scenario, StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Scenario '{scenario}' is disabled by {PerformanceBaselineSettings.ScenarioEnvironmentVariable}={requestedScenario}.";
        }
    }
}
