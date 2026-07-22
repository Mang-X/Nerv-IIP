using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed record SchedulingScaleRunEvidence(
    int Repetition,
    long TotalMilliseconds,
    long InputAssemblyMilliseconds,
    long ConstraintCheckMilliseconds,
    long AlgorithmCalculationMilliseconds,
    long PersistenceMilliseconds,
    long PeakWorkingSetBytes,
    long PeakManagedHeapBytes,
    long WorkingSetIncreaseBytes,
    int ScheduledOperationCount,
    int UnscheduledOperationCount,
    decimal OnTimeRate,
    int TotalTardinessMinutes,
    decimal AverageResourceUtilization,
    IReadOnlyDictionary<string, int> UnscheduledReasonDistribution,
    string OutputHash);

public sealed record SchedulingScaleStatisticSummary(long Minimum, decimal Median, long Maximum)
{
    public static SchedulingScaleStatisticSummary From(IEnumerable<long> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(source));
        }

        var middle = values.Length / 2;
        var median = values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2m
            : values[middle];
        return new SchedulingScaleStatisticSummary(values[0], median, values[^1]);
    }
}

public sealed record SchedulingScaleProfileSummary(
    SchedulingScaleStatisticSummary TotalMilliseconds,
    SchedulingScaleStatisticSummary InputAssemblyMilliseconds,
    SchedulingScaleStatisticSummary ConstraintCheckMilliseconds,
    SchedulingScaleStatisticSummary AlgorithmCalculationMilliseconds,
    SchedulingScaleStatisticSummary PersistenceMilliseconds,
    SchedulingScaleStatisticSummary PeakWorkingSetBytes,
    SchedulingScaleStatisticSummary PeakManagedHeapBytes,
    SchedulingScaleStatisticSummary WorkingSetIncreaseBytes);

public sealed record SchedulingScaleProfileEvidence(
    string Profile,
    int OrderCount,
    int OperationCount,
    int ResourceCount,
    IReadOnlyList<SchedulingScaleRunEvidence> Runs)
{
    public bool Stable => Runs.Count > 0 &&
        Runs.Select(x => x.OutputHash).Distinct(StringComparer.Ordinal).Count() == 1;

    public string OutputHash => Stable ? Runs[0].OutputHash : string.Empty;

    public SchedulingScaleProfileSummary Summary => new(
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.TotalMilliseconds)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.InputAssemblyMilliseconds)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.ConstraintCheckMilliseconds)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.AlgorithmCalculationMilliseconds)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.PersistenceMilliseconds)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.PeakWorkingSetBytes)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.PeakManagedHeapBytes)),
        SchedulingScaleStatisticSummary.From(Runs.Select(x => x.WorkingSetIncreaseBytes)));
}

public sealed record SchedulingScaleEvidenceDocument(
    DateTimeOffset CapturedAtUtc,
    string Commit,
    string OperatingSystem,
    string ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount,
    string Profile,
    string PersistenceProvider,
    IReadOnlyList<SchedulingScaleProfileEvidence> Profiles)
{
    public const string CapabilityDisclaimer =
        "APS Lite deterministic finite-capacity heuristic; no global optimality claim.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string Disclaimer => CapabilityDisclaimer;

    public void EnsureStable()
    {
        foreach (var profile in Profiles)
        {
            if (!profile.Stable)
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.Profile}' produced unstable schedule output.");
            }
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# APS Lite Scale Benchmark Evidence");
        builder.AppendLine();
        builder.AppendLine(CapabilityDisclaimer);
        builder.AppendLine();
        builder.AppendLine($"- Captured at (UTC): {CapturedAtUtc:O}");
        builder.AppendLine($"- Commit: `{Commit}`");
        builder.AppendLine($"- Runtime: {FrameworkDescription}; {OperatingSystem}; {ProcessArchitecture}; {ProcessorCount} logical processors");
        builder.AppendLine($"- Profile: {Profile}; persistence: {PersistenceProvider}");
        builder.AppendLine();
        builder.AppendLine("| Profile | Orders | Operations | Resources | Runs | Stable |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");
        foreach (var profile in Profiles)
        {
            builder.AppendLine(
                $"| {profile.Profile} | {profile.OrderCount} | {profile.OperationCount} | {profile.ResourceCount} | {profile.Runs.Count} | {(profile.Stable ? "yes" : "no")} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Median results");
        builder.AppendLine();
        builder.AppendLine("| Profile | Total ms | Input ms | Constraint ms | Algorithm ms | Persistence ms | Peak working set MiB | On-time rate | Tardiness min | Utilization | Scheduled | Unscheduled |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var profile in Profiles)
        {
            var representative = profile.Runs[profile.Runs.Count / 2];
            builder.AppendLine(
                $"| {profile.Profile} | {Format(profile.Summary.TotalMilliseconds.Median)} | {Format(profile.Summary.InputAssemblyMilliseconds.Median)} | {Format(profile.Summary.ConstraintCheckMilliseconds.Median)} | {Format(profile.Summary.AlgorithmCalculationMilliseconds.Median)} | {Format(profile.Summary.PersistenceMilliseconds.Median)} | {Format(profile.Summary.PeakWorkingSetBytes.Median / 1024m / 1024m)} | {representative.OnTimeRate.ToString("0.####", CultureInfo.InvariantCulture)} | {representative.TotalTardinessMinutes} | {representative.AverageResourceUtilization.ToString("0.####", CultureInfo.InvariantCulture)} | {representative.ScheduledOperationCount} | {representative.UnscheduledOperationCount} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Unscheduled reason distribution");
        builder.AppendLine();
        foreach (var profile in Profiles)
        {
            var reasons = profile.Runs[0].UnscheduledReasonDistribution.Count == 0
                ? "none"
                : string.Join(", ", profile.Runs[0].UnscheduledReasonDistribution
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => $"{x.Key}={x.Value}"));
            builder.AppendLine($"- {profile.Profile}: {reasons}");
        }

        return builder.ToString();
    }

    public SchedulingScaleEvidencePaths Write(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        EnsureStable();
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, "aps-lite-scale-benchmark.json");
        var markdownPath = Path.Combine(directory, "aps-lite-scale-benchmark.md");
        File.WriteAllText(jsonPath, ToJson(), Encoding.UTF8);
        File.WriteAllText(markdownPath, ToMarkdown(), Encoding.UTF8);
        return new SchedulingScaleEvidencePaths(jsonPath, markdownPath);
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed record SchedulingScaleEvidencePaths(string JsonPath, string MarkdownPath);
