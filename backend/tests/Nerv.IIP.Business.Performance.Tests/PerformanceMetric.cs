namespace Nerv.IIP.Business.Performance.Tests;

public sealed record PerformanceMetric(
    string Scenario,
    string Profile,
    long ElapsedMilliseconds,
    int Rows,
    string Unit,
    DateTimeOffset CapturedAtUtc)
{
    private static readonly object MetricsFileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public void WriteTo(ITestOutputHelper output)
    {
        var json = ToJson();
        output.WriteLine(json);

        var metricsPath = Environment.GetEnvironmentVariable(PerformanceBaselineSettings.MetricsPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(metricsPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(metricsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (MetricsFileLock)
        {
            File.AppendAllText(metricsPath, json + Environment.NewLine);
        }
    }
}
