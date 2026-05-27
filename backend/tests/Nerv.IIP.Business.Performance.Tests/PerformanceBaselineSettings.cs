namespace Nerv.IIP.Business.Performance.Tests;

public sealed record PerformanceBaselineSettings(
    string ConnectionString,
    string Profile,
    string OrganizationId,
    string EnvironmentId,
    int SampleRows)
{
    public const string ConnectionStringEnvironmentVariable = "NERV_IIP_PERF_POSTGRES";
    public const string ScenarioEnvironmentVariable = "NERV_IIP_PERF_SCENARIO";
    public const string MetricsPathEnvironmentVariable = "NERV_IIP_PERF_METRICS_PATH";

    public static PerformanceBaselineSettings FromEnvironment()
    {
        return new PerformanceBaselineSettings(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException($"{ConnectionStringEnvironmentVariable} is required."),
            ReadOptional("NERV_IIP_PERF_PROFILE", "local-baseline"),
            ReadOptional("NERV_IIP_PERF_ORG", "org-001"),
            ReadOptional("NERV_IIP_PERF_ENV", "env-dev"),
            ReadInt("NERV_IIP_PERF_ROWS", 25));
    }

    private static string ReadOptional(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static int ReadInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? Math.Min(parsed, 500)
            : defaultValue;
    }
}
