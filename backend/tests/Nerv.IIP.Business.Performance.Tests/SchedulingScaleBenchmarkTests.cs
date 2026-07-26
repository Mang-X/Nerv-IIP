using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using SchedulingInfrastructure = Nerv.IIP.Business.Scheduling.Infrastructure;

namespace Nerv.IIP.Business.Performance.Tests;

public sealed class SchedulingScaleBenchmarkTests(ITestOutputHelper output)
{
    private const string EvidenceDirectoryEnvironmentVariable = "NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY";
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 8, 3, 7, 0, 0, TimeSpan.Zero);

    [Trait("Category", "scheduling")]
    [PerformanceBaselineFact("scheduling", EvidenceDirectoryEnvironmentVariable)]
    public async Task SchedulingScale_APS_Lite_100_500_1000_outputs_repeatable_PostgreSQL_evidence()
    {
        var settings = PerformanceBaselineSettings.FromEnvironment();
        var evidenceDirectory = Environment.GetEnvironmentVariable(EvidenceDirectoryEnvironmentVariable)!;
        await using var provider = BusinessPerformanceServiceProvider.CreateSchedulingProvider(settings);
        await BusinessPerformanceServiceProvider.MigrateSchedulingAsync(provider, CancellationToken.None);
        var scheduler = new FiniteCapacityScheduler();
        var profileEvidence = new List<SchedulingScaleProfileEvidence>();

        foreach (var profile in SchedulingScaleProfile.All)
        {
            var runs = new List<SchedulingScaleRunEvidence>();
            for (var repetition = 1; repetition <= 3; repetition++)
            {
                await CleanupAsync(provider, profile, CancellationToken.None);
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    await using var memory = ProcessMemorySampler.Start();
                    var total = Stopwatch.StartNew();

                    var input = Measure(() => SchedulingScaleProblemFactory.Create(profile));
                    var normalized = Measure(() => SchedulingProblemNormalizer.Normalize(input.Value));
                    var scheduled = Measure(() => scheduler.ScheduleNormalized(
                        normalized.Value,
                        $"plan-aps-scale-{profile.Name}",
                        GeneratedAtUtc));
                    var persistenceMilliseconds = await PersistAsync(
                        provider,
                        normalized.Value,
                        scheduled.Value,
                        CancellationToken.None);

                    total.Stop();
                    await memory.StopAsync();
                    var plan = scheduled.Value;
                    runs.Add(new SchedulingScaleRunEvidence(
                        Repetition: repetition,
                        TotalMilliseconds: total.ElapsedMilliseconds,
                        InputAssemblyMilliseconds: input.ElapsedMilliseconds,
                        NormalizationMilliseconds: normalized.ElapsedMilliseconds,
                        AlgorithmCalculationMilliseconds: scheduled.ElapsedMilliseconds,
                        PersistenceMilliseconds: persistenceMilliseconds,
                        PeakWorkingSetBytes: memory.PeakWorkingSetBytes,
                        PeakManagedHeapBytes: memory.PeakManagedHeapBytes,
                        WorkingSetIncreaseBytes: memory.WorkingSetIncreaseBytes,
                        ScheduledOperationCount: plan.Metrics.ScheduledOperationCount,
                        UnscheduledOperationCount: plan.Metrics.UnscheduledOperationCount,
                        OnTimeRate: plan.Metrics.OnTimeRate,
                        TotalTardinessMinutes: plan.Metrics.TotalTardinessMinutes,
                        AverageResourceUtilization: plan.Metrics.AverageResourceUtilization,
                        UnscheduledReasonDistribution: plan.UnscheduledOperations
                            .GroupBy(x => JsonNamingPolicy.CamelCase.ConvertName(x.ReasonCode.ToString()))
                            .OrderBy(x => x.Key, StringComparer.Ordinal)
                            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal),
                        OutputHash: CalculateOutputHash(plan)));
                }
                finally
                {
                    await CleanupAsync(provider, profile, CancellationToken.None);
                }
            }

            profileEvidence.Add(new SchedulingScaleProfileEvidence(
                Profile: profile.Name,
                OrderCount: profile.OrderCount,
                OperationCount: profile.OrderCount * SchedulingScaleProfile.OperationsPerOrder,
                ResourceCount: SchedulingScaleProfile.ResourceCount,
                Runs: runs));
        }

        var document = new SchedulingScaleEvidenceDocument(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Commit: Environment.GetEnvironmentVariable("NERV_IIP_APS_SCALE_COMMIT") ?? "unknown",
            OperatingSystem: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessorCount: Environment.ProcessorCount,
            Profile: settings.Profile,
            PersistenceProvider: "PostgreSQL",
            Profiles: profileEvidence);

        document.EnsureStable();
        var paths = document.Write(evidenceDirectory);
        output.WriteLine(document.ToJson());
        output.WriteLine($"JSON evidence: {paths.JsonPath}");
        output.WriteLine($"Markdown evidence: {paths.MarkdownPath}");
    }

    private static MeasuredValue<T> Measure<T>(Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var value = action();
        stopwatch.Stop();
        return new MeasuredValue<T>(value, stopwatch.ElapsedMilliseconds);
    }

    private static async Task<long> PersistAsync(
        IServiceProvider provider,
        SchedulingProblemContract problem,
        SchedulePlanContract plan,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingInfrastructure.ApplicationDbContext>();
        var generated = SchedulePlanContractMapper.WithStatus(plan, SchedulePlanStatusContract.Generated);
        var normalizedEngineInput = SchedulingProblemNormalizer.Normalize(problem);
        var engineInputJson = JsonSerializer.Serialize(
            normalizedEngineInput,
            SchedulingJson.Options);
        var engineInputFingerprint = CalculateFingerprint(engineInputJson);
        Assert.Equal(plan.ProblemFingerprint, engineInputFingerprint);
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId,
            problem.ContractVersion,
            problem.OrganizationId,
            problem.EnvironmentId,
            engineInputFingerprint,
            engineInputJson,
            problem.HorizonStartUtc,
            problem.HorizonEndUtc,
            GeneratedAtUtc,
            engineInputFingerprint,
            engineInputJson));
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            problem.OrganizationId,
            problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(generated),
            CreateDirectBenchmarkTrace()));
        await db.SaveChangesAsync(cancellationToken);
        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    private static async Task CleanupAsync(
        IServiceProvider provider,
        SchedulingScaleProfile profile,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingInfrastructure.ApplicationDbContext>();
        var problemId = $"aps-scale-{profile.Name}";
        await db.SchedulePlans
            .Where(x => x.OrganizationId == "org-aps-scale" &&
                x.EnvironmentId == "benchmark" && x.ProblemId == problemId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ScheduleProblems
            .Where(x => x.OrganizationId == "org-aps-scale" &&
                x.EnvironmentId == "benchmark" && x.ProblemId == problemId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string CalculateOutputHash(SchedulePlanContract plan)
    {
        var canonical = new
        {
            plan.Metrics,
            plan.Assignments,
            plan.ResourceLoads,
            plan.Conflicts,
            plan.UnscheduledOperations,
        };
        var json = JsonSerializer.Serialize(canonical, SchedulingJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string CalculateFingerprint(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static SchedulePlanExecutionTraceSnapshot CreateDirectBenchmarkTrace()
    {
        return new SchedulePlanExecutionTraceSnapshot(
            EngineId: "finite-capacity",
            EngineVersion: "aps-lite-v1",
            RuleProviderId: "direct-benchmark",
            RuleProfileId: "aps-scale-normalized-input",
            RuleProfileVersion: "v1",
            ConstraintSourcesJson: """{"schemaVersion":1,"sources":[]}""",
            TraceSchemaVersion: SchedulingExecutionTraceSchema.CurrentVersion,
            ReplayStatus: SchedulingReplayStatuses.Available);
    }

    private sealed record MeasuredValue<T>(T Value, long ElapsedMilliseconds);
}
