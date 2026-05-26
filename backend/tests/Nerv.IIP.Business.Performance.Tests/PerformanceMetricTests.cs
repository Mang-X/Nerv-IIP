namespace Nerv.IIP.Business.Performance.Tests;

public sealed class PerformanceMetricTests(ITestOutputHelper output)
{
    [Fact]
    public void WriteTo_writes_machine_readable_metric_when_path_is_configured()
    {
        var metricsPath = Path.Combine(
            Path.GetTempPath(),
            $"nerv-iip-performance-metrics-{Guid.NewGuid():N}.jsonl");
        var originalMetricsPath = Environment.GetEnvironmentVariable(
            PerformanceBaselineSettings.MetricsPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                PerformanceBaselineSettings.MetricsPathEnvironmentVariable,
                metricsPath);

            new PerformanceMetric(
                "inventory-high-write",
                "local-baseline",
                123,
                25,
                "stock-movements",
                DateTimeOffset.UnixEpoch).WriteTo(output);

            var line = Assert.Single(File.ReadAllLines(metricsPath));
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            Assert.Equal("inventory-high-write", root.GetProperty("scenario").GetString());
            Assert.Equal(123, root.GetProperty("elapsedMilliseconds").GetInt64());
            Assert.Equal(25, root.GetProperty("rows").GetInt32());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                PerformanceBaselineSettings.MetricsPathEnvironmentVariable,
                originalMetricsPath);

            if (File.Exists(metricsPath))
            {
                File.Delete(metricsPath);
            }
        }
    }
}
