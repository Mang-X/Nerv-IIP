namespace Nerv.IIP.Business.Performance.Tests;

public sealed record PerformanceMetric(
    string Scenario,
    string Profile,
    long ElapsedMilliseconds,
    int Rows,
    string Unit,
    DateTimeOffset CapturedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public void WriteTo(ITestOutputHelper output)
    {
        output.WriteLine(JsonSerializer.Serialize(this, JsonOptions));
    }
}
