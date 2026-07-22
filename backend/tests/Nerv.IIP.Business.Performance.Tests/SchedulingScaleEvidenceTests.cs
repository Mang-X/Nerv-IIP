using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class SchedulingScaleEvidenceTests
{
    [Theory]
    [InlineData("demo", 100)]
    [InlineData("medium", 500)]
    [InlineData("stress", 1000)]
    public void SchedulingScale_factory_creates_fixed_deterministic_profiles(string name, int orderCount)
    {
        var profile = SchedulingScaleProfile.All.Single(x => x.Name == name);

        var first = SchedulingScaleProblemFactory.Create(profile);
        var second = SchedulingScaleProblemFactory.Create(profile);

        Assert.Equal(orderCount, first.Orders.Count);
        Assert.Equal(orderCount * 4, first.Orders.Sum(x => x.Operations.Count));
        Assert.Equal(24, first.Resources.Count);
        Assert.Equal(
            JsonSerializer.Serialize(first, SchedulingJson.Options),
            JsonSerializer.Serialize(second, SchedulingJson.Options));
    }

    [Fact]
    public void SchedulingScale_factory_uses_UTC_for_all_persisted_timestamps()
    {
        var problem = SchedulingScaleProblemFactory.Create(SchedulingScaleProfile.All[0]);
        var timestamps = new[] { problem.HorizonStartUtc, problem.HorizonEndUtc }
            .Concat(problem.Orders.Select(x => x.DueUtc))
            .Concat(problem.Orders.SelectMany(x => x.Operations)
                .SelectMany(x => new[] { x.EarliestStartUtc, x.DueUtc }))
            .Concat(problem.Calendars.SelectMany(x => x.ShiftWindows)
                .SelectMany(x => new[] { x.StartUtc, x.EndUtc }))
            .Concat(problem.UnavailabilityWindows
                .SelectMany(x => new[] { x.StartUtc, x.EndUtc }));

        Assert.All(timestamps, timestamp => Assert.Equal(TimeSpan.Zero, timestamp.Offset));
    }

    [Fact]
    public void SchedulingScale_evidence_contains_required_fields_and_capability_boundary()
    {
        var document = CreateEvidence("stable-hash", "stable-hash", "stable-hash");

        document.EnsureStable();
        var json = document.ToJson();
        var markdown = document.ToMarkdown();

        Assert.Contains("inputAssemblyMilliseconds", json, StringComparison.Ordinal);
        Assert.Contains("constraintCheckMilliseconds", json, StringComparison.Ordinal);
        Assert.Contains("algorithmCalculationMilliseconds", json, StringComparison.Ordinal);
        Assert.Contains("persistenceMilliseconds", json, StringComparison.Ordinal);
        Assert.Contains("peakWorkingSetBytes", json, StringComparison.Ordinal);
        Assert.Contains("onTimeRate", json, StringComparison.Ordinal);
        Assert.Contains("totalTardinessMinutes", json, StringComparison.Ordinal);
        Assert.Contains("averageResourceUtilization", json, StringComparison.Ordinal);
        Assert.Contains("unscheduledReasonDistribution", json, StringComparison.Ordinal);
        Assert.Contains(SchedulingScaleEvidenceDocument.CapabilityDisclaimer, json, StringComparison.Ordinal);
        Assert.Contains(SchedulingScaleEvidenceDocument.CapabilityDisclaimer, markdown, StringComparison.Ordinal);
        Assert.Contains("| demo | 100 | 400 | 24 | 3 | yes |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void SchedulingScale_evidence_rejects_unstable_output()
    {
        var document = CreateEvidence("hash-a", "hash-a", "hash-b");

        var exception = Assert.Throws<InvalidOperationException>(document.EnsureStable);

        Assert.Contains("demo", exception.Message, StringComparison.Ordinal);
    }

    private static SchedulingScaleEvidenceDocument CreateEvidence(params string[] hashes)
    {
        var runs = hashes.Select((hash, index) => new SchedulingScaleRunEvidence(
            Repetition: index + 1,
            TotalMilliseconds: 10 + index,
            InputAssemblyMilliseconds: 1,
            ConstraintCheckMilliseconds: 2,
            AlgorithmCalculationMilliseconds: 3,
            PersistenceMilliseconds: 4,
            PeakWorkingSetBytes: 100_000,
            PeakManagedHeapBytes: 50_000,
            WorkingSetIncreaseBytes: 10_000,
            ScheduledOperationCount: 390,
            UnscheduledOperationCount: 10,
            OnTimeRate: 0.95m,
            TotalTardinessMinutes: 120,
            AverageResourceUtilization: 0.60m,
            UnscheduledReasonDistribution: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["material"] = 10,
            },
            OutputHash: hash)).ToArray();

        return new SchedulingScaleEvidenceDocument(
            CapturedAtUtc: DateTimeOffset.UnixEpoch,
            Commit: "fb4bad1",
            OperatingSystem: "test-os",
            ProcessArchitecture: "X64",
            FrameworkDescription: ".NET 10 test",
            ProcessorCount: 8,
            Profile: "test-postgresql",
            PersistenceProvider: "PostgreSQL",
            Profiles:
            [
                new SchedulingScaleProfileEvidence(
                    Profile: "demo",
                    OrderCount: 100,
                    OperationCount: 400,
                    ResourceCount: 24,
                    Runs: runs)
            ]);
    }
}
