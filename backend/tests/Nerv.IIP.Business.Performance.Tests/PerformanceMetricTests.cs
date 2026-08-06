using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class PerformanceMetricTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WriteTo_writes_machine_readable_metric_when_path_is_configured()
    {
        var metricsPath = Path.Combine(
            Path.GetTempPath(),
            $"nerv-iip-performance-metrics-{Guid.NewGuid():N}.jsonl");

        // The scope serialises every process-global mutator in the assembly and restores the exact
        // prior value (including "was never set") on dispose, so the metrics path cannot outlive this
        // test. It is still process-global while the scope is open: a test that never takes a scope
        // can observe it.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();

        try
        {
            globalState.SetEnvironmentVariable(
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
            if (File.Exists(metricsPath))
            {
                File.Delete(metricsPath);
            }
        }
    }
}
