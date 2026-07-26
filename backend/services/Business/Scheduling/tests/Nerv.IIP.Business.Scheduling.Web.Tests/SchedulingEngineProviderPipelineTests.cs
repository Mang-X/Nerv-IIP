using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingEngineProviderPipelineTests
{
    private static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FiniteCapacityScheduler_implements_engine_boundary_with_stable_identity()
    {
        ISchedulingEngine engine = new FiniteCapacityScheduler();

        Assert.Equal("finite-capacity", engine.EngineId);
        Assert.Equal("aps-lite-v1", engine.Version);
    }

    [Fact]
    public async Task Default_DI_resolves_one_engine_rule_provider_constraint_provider_and_generator()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting(
                    "ConnectionStrings:PostgreSQL",
                    "Host=unused;Database=nerv_iip_scheduling_pipeline;Username=nerv;Password=nerv");
            });
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<FiniteCapacityScheduler>(Assert.Single(services.GetServices<ISchedulingEngine>()));
        Assert.IsType<DefaultSchedulingRuleProvider>(Assert.Single(services.GetServices<ISchedulingRuleProvider>()));
        Assert.IsType<DefaultSchedulingConstraintProvider>(
            Assert.Single(services.GetServices<ISchedulingConstraintProvider>()));
        Assert.NotNull(services.GetRequiredService<SchedulingPlanGenerator>());
    }

    [Fact]
    public async Task Generate_runs_rule_then_constraint_and_sends_effective_problem_to_engine()
    {
        var calls = new List<string>();
        var original = ShockAbsorberSchedulingFixture.CreateProblem();
        var ruled = original with { ProblemId = "problem-after-rules" };
        var effective = ruled with
        {
            MaterialReadiness =
            [
                new SchedulingMaterialReadinessContract(
                    "order",
                    ruled.Orders.First().OrderId,
                    null,
                    false,
                    ["material.test-block"])
            ]
        };
        var engine = new RecordingEngine(calls);
        var generator = new SchedulingPlanGenerator(
            new StubRuleProvider(async (problem, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                calls.Add("rule");
                Assert.Same(original, problem);
                return new SchedulingRuleProviderResult(
                    ruled,
                    [new SchedulingProviderSummary("test-rule", SchedulingProviderOutcome.Applied, 1, [])]);
            }),
            new StubConstraintProvider(async (problem, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                calls.Add("constraint");
                Assert.Same(ruled, problem);
                return new SchedulingConstraintProviderResult(
                    problem,
                    effective,
                    [new SchedulingProviderSummary("test-constraint", SchedulingProviderOutcome.Applied, 1, [])]);
            }),
            engine);

        var result = await generator.GenerateAsync(
            original,
            "plan-provider-order",
            GeneratedAtUtc,
            CancellationToken.None);

        Assert.Equal(["rule", "constraint", "engine"], calls);
        Assert.Same(effective, engine.ReceivedProblem);
        Assert.Same(ruled, result.Constraints.BaseProblem);
        Assert.Same(effective, result.Constraints.EffectiveProblem);
    }

    [Fact]
    public async Task Generate_rejects_duplicate_constraint_source_ids_before_engine_execution()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var engine = new RecordingEngine([]);
        var generator = new SchedulingPlanGenerator(
            new StubRuleProvider((value, _) => Task.FromResult(
                new SchedulingRuleProviderResult(value, []))),
            new StubConstraintProvider((value, _) => Task.FromResult(
                new SchedulingConstraintProviderResult(
                    value,
                    value,
                    [
                        new SchedulingProviderSummary(
                            "duplicate-source",
                            SchedulingProviderOutcome.NoData,
                            0,
                            []),
                        new SchedulingProviderSummary(
                            "duplicate-source",
                            SchedulingProviderOutcome.Degraded,
                            1,
                            ["source.unavailable"])
                    ]))),
            engine);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(problem, "plan-duplicate-source", GeneratedAtUtc, CancellationToken.None));

        Assert.Equal(
            "Duplicate scheduling constraint source IDs are not allowed: duplicate-source.",
            exception.Message);
        Assert.Null(engine.ReceivedProblem);
    }

    [Fact]
    public async Task Default_constraint_provider_reports_no_data_and_degraded_sources_explicitly()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var noDataProvider = new DefaultSchedulingConstraintProvider(
            new StubEquipmentAvailabilityProvider(CreateAvailability(problem, [])),
            new StubMaterialReadinessProvider([]));

        var noData = await noDataProvider.ApplyAsync(problem, CancellationToken.None);

        Assert.Collection(
            noData.Summaries,
            summary =>
            {
                Assert.Equal(DefaultSchedulingConstraintProvider.EquipmentSourceId, summary.SourceId);
                Assert.Equal(SchedulingProviderOutcome.NoData, summary.Outcome);
                Assert.Equal(0, summary.FactCount);
            },
            summary =>
            {
                Assert.Equal(DefaultSchedulingConstraintProvider.MaterialSourceId, summary.SourceId);
                Assert.Equal(SchedulingProviderOutcome.NoData, summary.Outcome);
                Assert.Equal(0, summary.FactCount);
            });

        var resource = problem.Resources.First();
        var degradedProvider = new DefaultSchedulingConstraintProvider(
            new StubEquipmentAvailabilityProvider(CreateAvailability(
                problem,
                [
                    new EquipmentRuntimeAvailabilityWindowContract(
                        resource.ResourceId,
                        resource.WorkCenterId,
                        EquipmentRuntimeAvailabilityStatus.Unknown,
                        HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode,
                        EquipmentRuntimeSeverity.Blocked,
                        problem.HorizonStartUtc,
                        problem.HorizonEndUtc,
                        EquipmentRuntimeSourceType.StaleSource,
                        "equipment-source",
                        "equipment.availability.sourceUnavailable",
                        [])
                ])),
            new StubMaterialReadinessProvider(
                [
                    new SchedulingMaterialReadinessContract(
                        "order",
                        problem.Orders.First().OrderId,
                        null,
                        false,
                        [HttpSchedulingMaterialReadinessProvider.SourceUnavailableReasonCode])
                ]));

        var degraded = await degradedProvider.ApplyAsync(problem, CancellationToken.None);

        Assert.All(
            degraded.Summaries,
            summary => Assert.Equal(SchedulingProviderOutcome.Degraded, summary.Outcome));
        Assert.Equal(
            [
                HttpSchedulingEquipmentAvailabilityProvider.SourceUnavailableReasonCode
            ],
            degraded.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.EquipmentSourceId).ReasonCodes);
        Assert.Equal(
            [
                HttpSchedulingMaterialReadinessProvider.SourceUnavailableReasonCode
            ],
            degraded.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.MaterialSourceId).ReasonCodes);
    }

    [Fact]
    public async Task Default_constraint_result_separates_post_override_base_from_effective_problem()
    {
        var original = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = original.Orders.First();
        var operation = order.Operations.First();
        var locked = new SchedulingLockedAssignmentContract(
            "post-override-lock",
            order.OrderId,
            operation.OperationId,
            operation.OperationSequence,
            operation.EligibleResourceIds.First(),
            original.Resources.First(x => x.ResourceId == operation.EligibleResourceIds.First()).WorkCenterId,
            original.HorizonStartUtc,
            original.HorizonStartUtc.AddMinutes(operation.DurationMinutes),
            "manual-override");
        var postOverride = original with
        {
            LockedAssignments =
            [
                locked
            ]
        };
        var resource = postOverride.Resources.First();
        var provider = new DefaultSchedulingConstraintProvider(
            new StubEquipmentAvailabilityProvider(CreateAvailability(
                postOverride,
                [
                    new EquipmentRuntimeAvailabilityWindowContract(
                        resource.ResourceId,
                        resource.WorkCenterId,
                        EquipmentRuntimeAvailabilityStatus.Unavailable,
                        "equipment.test-window",
                        EquipmentRuntimeSeverity.Blocked,
                        postOverride.HorizonStartUtc,
                        postOverride.HorizonStartUtc.AddHours(1),
                        EquipmentRuntimeSourceType.ManualBlock,
                        "equipment-test-window",
                        "equipment.test-window",
                        [])
                ])),
            new StubMaterialReadinessProvider(
                [
                    new SchedulingMaterialReadinessContract(
                        "order",
                        postOverride.Orders.First().OrderId,
                        null,
                        false,
                        ["material.test-block"])
                ]));

        var result = await provider.ApplyAsync(postOverride, CancellationToken.None);

        Assert.Same(postOverride, result.BaseProblem);
        Assert.DoesNotContain(
            result.BaseProblem.UnavailabilityWindows,
            x => x.ReasonCode == "equipment.test-window");
        Assert.DoesNotContain(
            result.BaseProblem.MaterialReadiness,
            x => x.ReasonCodes.Contains("material.test-block", StringComparer.Ordinal));
        Assert.Contains(
            result.EffectiveProblem.UnavailabilityWindows,
            x => x.ReasonCode == "equipment.test-window");
        Assert.Contains(
            result.EffectiveProblem.MaterialReadiness,
            x => x.ReasonCodes.Contains("material.test-block", StringComparer.Ordinal));
        Assert.Contains(
            result.BaseProblem.LockedAssignments,
            x => x.AssignmentId == "post-override-lock");
        Assert.Contains(
            result.EffectiveProblem.LockedAssignments,
            x => x.AssignmentId == "post-override-lock");
    }

    [Fact]
    public async Task Default_pipeline_matches_direct_finite_capacity_scheduler_business_output()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var postOverride = problem with { ProblemId = "problem-default-parity-after-override" };
        var scheduler = new FiniteCapacityScheduler();
        var generator = new SchedulingPlanGenerator(
            new DefaultSchedulingRuleProvider(new FixedOverrideOverlay(postOverride)),
            new DefaultSchedulingConstraintProvider(
                new NoopSchedulingEquipmentAvailabilityProvider(),
                new NoopSchedulingMaterialReadinessProvider()),
            scheduler);

        var direct = scheduler.Schedule(postOverride, "plan-default-parity", GeneratedAtUtc);
        var generated = await generator.GenerateAsync(
            problem,
            "plan-default-parity",
            GeneratedAtUtc,
            CancellationToken.None);

        Assert.Equal(
            JsonSerializer.Serialize(direct, SchedulingJson.Options),
            JsonSerializer.Serialize(generated.Plan, SchedulingJson.Options));
        Assert.Same(postOverride, generated.Constraints.BaseProblem);
        Assert.Equal("finite-capacity", generated.EngineId);
        Assert.Equal("aps-lite-v1", generated.EngineVersion);
    }

    private static EquipmentRuntimeAvailabilityResponse CreateAvailability(
        SchedulingProblemContract problem,
        IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> items)
    {
        return new EquipmentRuntimeAvailabilityResponse(
            1,
            problem.OrganizationId,
            problem.EnvironmentId,
            problem.HorizonStartUtc,
            problem.HorizonEndUtc,
            items);
    }

    private sealed class StubRuleProvider(
        Func<SchedulingProblemContract, CancellationToken, Task<SchedulingRuleProviderResult>> apply)
        : ISchedulingRuleProvider
    {
        public Task<SchedulingRuleProviderResult> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return apply(problem, cancellationToken);
        }
    }

    private sealed class StubConstraintProvider(
        Func<SchedulingProblemContract, CancellationToken, Task<SchedulingConstraintProviderResult>> apply)
        : ISchedulingConstraintProvider
    {
        public Task<SchedulingConstraintProviderResult> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return apply(problem, cancellationToken);
        }
    }

    private sealed class RecordingEngine(List<string> calls) : ISchedulingEngine
    {
        public string EngineId => "recording";

        public string Version => "test-v1";

        public SchedulingProblemContract? ReceivedProblem { get; private set; }

        public SchedulePlanContract Schedule(
            SchedulingProblemContract problem,
            string planId,
            DateTimeOffset generatedAtUtc)
        {
            calls.Add("engine");
            ReceivedProblem = problem;
            return new FiniteCapacityScheduler().Schedule(problem, planId, generatedAtUtc);
        }
    }

    private sealed class StubEquipmentAvailabilityProvider(EquipmentRuntimeAvailabilityResponse response)
        : ISchedulingEquipmentAvailabilityProvider
    {
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class StubMaterialReadinessProvider(
        IReadOnlyCollection<SchedulingMaterialReadinessContract> readiness)
        : ISchedulingMaterialReadinessProvider
    {
        public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(readiness);
        }
    }

    private sealed class FixedOverrideOverlay(SchedulingProblemContract result)
        : ISchedulingOperationOverrideOverlay
    {
        public Task<SchedulingProblemContract> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
