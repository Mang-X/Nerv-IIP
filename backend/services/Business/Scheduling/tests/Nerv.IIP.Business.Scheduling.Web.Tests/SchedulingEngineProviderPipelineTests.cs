using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
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
    public async Task Default_rule_provider_exposes_stable_built_in_profile_without_transforming_problem()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var provider = new DefaultSchedulingRuleProvider();

        var result = await provider.ApplyAsync(problem, CancellationToken.None);

        Assert.Equal("built-in", result.ProviderId);
        Assert.Equal("adr-0014-default", result.ProfileId);
        Assert.Equal("v1", result.ProfileVersion);
        Assert.Same(problem, result.EffectiveProblem);
        Assert.Empty(result.Summaries);
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
    public async Task Create_and_preview_use_the_same_generation_pipeline_once_with_matching_trace_and_distinct_statuses()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"scheduling-pipeline-handlers-{Guid.NewGuid():N}"));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var calls = new List<string>();
        var ruleProvider = new CountingRuleProvider(calls);
        var constraintProvider = new CountingConstraintProvider(calls);
        var engine = new CountingEngine(calls);
        var generator = new SchedulingPlanGenerator(ruleProvider, constraintProvider, engine);
        var clock = new FixedTimeProvider(GeneratedAtUtc);
        var urgencyService = new OrderUrgencyService(dbContext, clock);
        var createHandler = new CreateSchedulePlanCommandHandler(
            dbContext,
            generator,
            clock,
            urgencyService);
        var previewHandler = new PreviewSchedulePlanCommandHandler(
            generator,
            clock);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();

        var created = await createHandler.Handle(
            new CreateSchedulePlanCommand(problem),
            CancellationToken.None);
        var previewed = await previewHandler.Handle(
            new PreviewSchedulePlanCommand(problem),
            CancellationToken.None);

        Assert.Equal(2, ruleProvider.CallCount);
        Assert.Equal(2, constraintProvider.CallCount);
        Assert.Equal(2, engine.CallCount);
        Assert.Equal(
            [
                "rule:problem-shock-absorber-001",
                "constraint:problem-shock-absorber-001",
                "engine:problem-shock-absorber-001:2026-06-01T07:00:00.0000000+00:00",
                "rule:problem-shock-absorber-001",
                "constraint:problem-shock-absorber-001",
                "engine:problem-shock-absorber-001:2026-06-01T07:00:00.0000000+00:00"
            ],
            calls);
        Assert.Equal(SchedulePlanStatusContract.Generated, created.Status);
        Assert.Equal(SchedulePlanStatusContract.Preview, previewed.Status);
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
                    ProviderId: "test-rule-provider",
                    ProfileId: "test-rule-profile",
                    ProfileVersion: "test-v1",
                    EffectiveProblem: ruled,
                    Summaries:
                    [
                        new SchedulingProviderSummary(
                            SourceId: "test-rule",
                            SourceVersion: "test-v1",
                            Outcome: SchedulingProviderOutcome.Applied,
                            FactCount: 1,
                            FactsFingerprint: new string('0', 64),
                            ReasonCodes: ["facts-applied"])
                    ]);
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
                    [
                        new SchedulingProviderSummary(
                            SourceId: "test-constraint",
                            SourceVersion: "test-v1",
                            Outcome: SchedulingProviderOutcome.Applied,
                            FactCount: 1,
                            FactsFingerprint: new string('1', 64),
                            ReasonCodes: ["facts-applied"])
                    ]);
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
                new SchedulingRuleProviderResult(
                    "test-rule-provider",
                    "test-rule-profile",
                    "test-v1",
                    value,
                    []))),
            new StubConstraintProvider((value, _) => Task.FromResult(
                new SchedulingConstraintProviderResult(
                    value,
                    value,
                    [
                        new SchedulingProviderSummary(
                            SourceId: "duplicate-source",
                            SourceVersion: "test-v1",
                            Outcome: SchedulingProviderOutcome.NoData,
                            FactCount: 0,
                            FactsFingerprint: new string('2', 64),
                            ReasonCodes: ["no-data"]),
                        new SchedulingProviderSummary(
                            SourceId: "duplicate-source",
                            SourceVersion: "test-v2",
                            Outcome: SchedulingProviderOutcome.Degraded,
                            FactCount: 1,
                            FactsFingerprint: new string('3', 64),
                            ReasonCodes: ["source-unavailable"])
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
            new PassthroughOverrideOverlay(),
            new StubEquipmentAvailabilityProvider(CreateAvailability(problem, [])),
            new StubMaterialReadinessProvider([]));

        var noData = await noDataProvider.ApplyAsync(problem, CancellationToken.None);

        Assert.Collection(
            noData.Summaries,
            summary =>
            {
                Assert.Equal(DefaultSchedulingConstraintProvider.OperationOverrideSourceId, summary.SourceId);
                Assert.Equal(SchedulingProviderOutcome.NoData, summary.Outcome);
                Assert.Equal(0, summary.FactCount);
            },
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
        Assert.All(noData.Summaries, summary =>
        {
            Assert.Equal("v1", summary.SourceVersion);
            Assert.Matches("^[0-9a-f]{64}$", summary.FactsFingerprint);
            Assert.Equal(["no-data"], summary.ReasonCodes);
        });

        var resource = problem.Resources.First();
        var degradedProvider = new DefaultSchedulingConstraintProvider(
            new PassthroughOverrideOverlay(),
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
            degraded.Summaries.Where(x =>
                x.SourceId != DefaultSchedulingConstraintProvider.OperationOverrideSourceId),
            summary => Assert.Equal(SchedulingProviderOutcome.Degraded, summary.Outcome));
        Assert.Equal(
            [
                "source-unavailable"
            ],
            degraded.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.EquipmentSourceId).ReasonCodes);
        Assert.Equal(
            [
                "source-unavailable"
            ],
            degraded.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.MaterialSourceId).ReasonCodes);
    }

    [Fact]
    public async Task Default_provider_summaries_do_not_copy_raw_lock_equipment_or_material_reasons()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = problem.Orders.First();
        var operation = order.Operations.First();
        var resource = problem.Resources.First(x => x.ResourceId == operation.EligibleResourceIds.First());
        var lockReason = $"planner:{new string('l', 4096)}:employee-042";
        var equipmentReason = $"alarm:{new string('e', 4096)}:device-secret-042";
        var materialReason = $"shortage:{new string('m', 4096)}:lot-sensitive-042";
        var postOverride = problem with
        {
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    "lock-sensitive-001",
                    order.OrderId,
                    operation.OperationId,
                    operation.OperationSequence,
                    resource.ResourceId,
                    resource.WorkCenterId,
                    problem.HorizonStartUtc,
                    problem.HorizonStartUtc.AddMinutes(operation.DurationMinutes),
                    lockReason)
            ]
        };
        var rules = await new DefaultSchedulingRuleProvider()
            .ApplyAsync(problem, CancellationToken.None);
        var constraints = await new DefaultSchedulingConstraintProvider(
                new FixedOverrideOverlay(postOverride),
                new StubEquipmentAvailabilityProvider(CreateAvailability(
                    postOverride,
                    [
                        new EquipmentRuntimeAvailabilityWindowContract(
                            resource.ResourceId,
                            resource.WorkCenterId,
                            EquipmentRuntimeAvailabilityStatus.Unavailable,
                            equipmentReason,
                            EquipmentRuntimeSeverity.Blocked,
                            postOverride.HorizonStartUtc,
                            postOverride.HorizonStartUtc.AddHours(1),
                            EquipmentRuntimeSourceType.Alarm,
                            "alarm-sensitive-042",
                            "equipment.dynamic",
                            [])
                    ])),
                new StubMaterialReadinessProvider(
                    [
                        new SchedulingMaterialReadinessContract(
                            "order",
                            order.OrderId,
                            null,
                            false,
                            [materialReason])
                    ]))
            .ApplyAsync(rules.EffectiveProblem, CancellationToken.None);
        var summaries = rules.Summaries.Concat(constraints.Summaries).ToArray();
        var summaryReasonCodes = summaries.SelectMany(x => x.ReasonCodes).ToArray();

        Assert.Contains(
            constraints.BaseProblem.LockedAssignments,
            x => x.LockReasonCode == lockReason);
        Assert.Contains(
            constraints.EffectiveProblem.UnavailabilityWindows,
            x => x.ReasonCode == equipmentReason);
        Assert.Contains(
            constraints.EffectiveProblem.MaterialReadiness,
            x => x.ReasonCodes.Contains(materialReason, StringComparer.Ordinal));
        Assert.DoesNotContain(lockReason, summaryReasonCodes);
        Assert.DoesNotContain(equipmentReason, summaryReasonCodes);
        Assert.DoesNotContain(materialReason, summaryReasonCodes);
        Assert.All(
            summaryReasonCodes,
            code => Assert.Contains(code, new[] { "facts-applied", "no-data", "source-unavailable" }));
        Assert.All(summaries, summary => Assert.InRange(summary.ReasonCodes.Count, 0, 1));
        Assert.InRange(
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(summaries, SchedulingJson.Options)),
            1,
            2048);
    }

    [Fact]
    public async Task Constraint_fact_fingerprints_are_order_invariant_and_content_sensitive()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = problem.Orders.First();
        var operation = order.Operations.First();
        var resource = problem.Resources.First(x => x.ResourceId == operation.EligibleResourceIds.First());
        var firstLock = new SchedulingLockedAssignmentContract(
            "lock-fingerprint-a",
            order.OrderId,
            operation.OperationId,
            operation.OperationSequence,
            resource.ResourceId,
            resource.WorkCenterId,
            problem.HorizonStartUtc,
            problem.HorizonStartUtc.AddMinutes(operation.DurationMinutes),
            "lock.dynamic-a");
        var secondLock = firstLock with
        {
            AssignmentId = "lock-fingerprint-b",
            LockReasonCode = "lock.dynamic-b"
        };
        var firstEquipment = new EquipmentRuntimeAvailabilityWindowContract(
            resource.ResourceId,
            resource.WorkCenterId,
            EquipmentRuntimeAvailabilityStatus.Unavailable,
            "equipment.dynamic-a",
            EquipmentRuntimeSeverity.Blocked,
            problem.HorizonStartUtc,
            problem.HorizonStartUtc.AddHours(1),
            EquipmentRuntimeSourceType.Alarm,
            "alarm-fingerprint-a",
            "equipment.dynamic-a",
            ["substitute-b", "substitute-a"]);
        var secondEquipment = firstEquipment with
        {
            ReasonCode = "equipment.dynamic-b",
            SourceReferenceId = "alarm-fingerprint-b",
            SubstituteDeviceAssetIds = ["substitute-c"]
        };
        var firstMaterial = new SchedulingMaterialReadinessContract(
            "order",
            order.OrderId,
            null,
            false,
            ["material.dynamic-b", "material.dynamic-a"]);
        var secondMaterial = firstMaterial with
        {
            ScopeType = "operation",
            ScopeId = operation.OperationId,
            ReasonCodes = ["material.dynamic-c"]
        };
        var first = await new DefaultSchedulingConstraintProvider(
                new FixedOverrideOverlay(problem with
                {
                    LockedAssignments = [firstLock, firstLock, secondLock]
                }),
                new StubEquipmentAvailabilityProvider(CreateAvailability(
                    problem,
                    [firstEquipment, firstEquipment, secondEquipment])),
                new StubMaterialReadinessProvider([firstMaterial, firstMaterial, secondMaterial]))
            .ApplyAsync(problem, CancellationToken.None);
        var reordered = await new DefaultSchedulingConstraintProvider(
                new FixedOverrideOverlay(problem with
                {
                    LockedAssignments = [secondLock, firstLock, firstLock]
                }),
                new StubEquipmentAvailabilityProvider(CreateAvailability(
                    problem,
                    [secondEquipment, firstEquipment, firstEquipment])),
                new StubMaterialReadinessProvider(
                    [
                        secondMaterial,
                        firstMaterial with
                        {
                            ReasonCodes = ["material.dynamic-a", "material.dynamic-b"]
                        },
                        firstMaterial
                    ]))
            .ApplyAsync(problem, CancellationToken.None);
        var changed = await new DefaultSchedulingConstraintProvider(
                new FixedOverrideOverlay(problem with
                {
                    LockedAssignments = [secondLock, firstLock, firstLock]
                }),
                new StubEquipmentAvailabilityProvider(CreateAvailability(
                    problem,
                    [secondEquipment, firstEquipment, firstEquipment])),
                new StubMaterialReadinessProvider(
                    [
                        secondMaterial with { ReasonCodes = ["material.changed"] },
                        firstMaterial,
                        firstMaterial
                    ]))
            .ApplyAsync(problem, CancellationToken.None);

        Assert.Equal(
            first.Summaries.Select(x => (x.SourceId, x.FactsFingerprint)),
            reordered.Summaries.Select(x => (x.SourceId, x.FactsFingerprint)));
        Assert.All(first.Summaries, summary => Assert.Matches("^[0-9a-f]{64}$", summary.FactsFingerprint));
        Assert.NotEqual(
            reordered.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.MaterialSourceId).FactsFingerprint,
            changed.Summaries.Single(x =>
                x.SourceId == DefaultSchedulingConstraintProvider.MaterialSourceId).FactsFingerprint);
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
        var calls = new List<string>();
        var resource = postOverride.Resources.First();
        var provider = new DefaultSchedulingConstraintProvider(
            new FixedOverrideOverlay(postOverride, calls),
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
                ]),
                queriedProblem =>
                {
                    Assert.Same(postOverride, queriedProblem);
                    calls.Add("equipment");
                }),
            new StubMaterialReadinessProvider(
                [
                    new SchedulingMaterialReadinessContract(
                        "order",
                        postOverride.Orders.First().OrderId,
                        null,
                        false,
                        ["material.test-block"])
                ],
                queriedProblem =>
                {
                    Assert.Same(postOverride, queriedProblem);
                    calls.Add("material");
                }));

        var result = await provider.ApplyAsync(original, CancellationToken.None);

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
        Assert.Equal(["override", "equipment", "material"], calls);
    }

    [Fact]
    public async Task Default_pipeline_matches_direct_finite_capacity_scheduler_business_output()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var postOverride = problem with { ProblemId = "problem-default-parity-after-override" };
        var scheduler = new FiniteCapacityScheduler();
        var generator = new SchedulingPlanGenerator(
            new DefaultSchedulingRuleProvider(),
            new DefaultSchedulingConstraintProvider(
                new FixedOverrideOverlay(postOverride),
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

    private sealed class CountingRuleProvider(List<string> calls) : ISchedulingRuleProvider
    {
        public int CallCount { get; private set; }

        public Task<SchedulingRuleProviderResult> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            CallCount++;
            calls.Add($"rule:{problem.ProblemId}");
            return Task.FromResult(new SchedulingRuleProviderResult(
                "counting-rule-provider",
                "counting-profile",
                "test-v1",
                problem,
                []));
        }
    }

    private sealed class CountingConstraintProvider(List<string> calls) : ISchedulingConstraintProvider
    {
        public int CallCount { get; private set; }

        public Task<SchedulingConstraintProviderResult> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            CallCount++;
            calls.Add($"constraint:{problem.ProblemId}");
            return Task.FromResult(new SchedulingConstraintProviderResult(problem, problem, []));
        }
    }

    private sealed class CountingEngine(List<string> calls) : ISchedulingEngine
    {
        public string EngineId => "counting-engine";

        public string Version => "test-v1";

        public int CallCount { get; private set; }

        public SchedulePlanContract Schedule(
            SchedulingProblemContract problem,
            string planId,
            DateTimeOffset generatedAtUtc)
        {
            CallCount++;
            calls.Add($"engine:{problem.ProblemId}:{generatedAtUtc:O}");
            return new FiniteCapacityScheduler().Schedule(problem, planId, generatedAtUtc);
        }
    }

    private sealed class StubEquipmentAvailabilityProvider(
        EquipmentRuntimeAvailabilityResponse response,
        Action<SchedulingProblemContract>? onQuery = null)
        : ISchedulingEquipmentAvailabilityProvider
    {
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            onQuery?.Invoke(problem);
            return Task.FromResult(response);
        }
    }

    private sealed class StubMaterialReadinessProvider(
        IReadOnlyCollection<SchedulingMaterialReadinessContract> readiness,
        Action<SchedulingProblemContract>? onQuery = null)
        : ISchedulingMaterialReadinessProvider
    {
        public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            onQuery?.Invoke(problem);
            return Task.FromResult(readiness);
        }
    }

    private sealed class FixedOverrideOverlay(
        SchedulingProblemContract result,
        List<string>? calls = null)
        : ISchedulingOperationOverrideOverlay
    {
        public Task<SchedulingProblemContract> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            calls?.Add("override");
            return Task.FromResult(result);
        }
    }

    private sealed class PassthroughOverrideOverlay : ISchedulingOperationOverrideOverlay
    {
        public Task<SchedulingProblemContract> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(problem);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
