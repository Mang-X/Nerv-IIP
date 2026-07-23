using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Auth;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingEndpointContractTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scheduling_endpoints_expose_issue_206_routes_permissions_policies_and_operation_ids()
    {
        var contracts = SchedulingEndpointContracts.All.ToArray();
        var allowedPermissions = new[]
        {
            SchedulingPermissionCodes.PlansRead,
            SchedulingPermissionCodes.PlansManage,
            SchedulingPermissionCodes.PlansRelease
        };

        Assert.Equal(15, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/preview" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "previewSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/workbench/plans" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createSchedulingWorkbenchPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/{planId}/revisions" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createSchedulingPlanRevision");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/problems/assemble" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "assembleSchedulingProblem");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listSchedulingPlans");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans/{planId}" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans/{planId}/gantt" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getSchedulingPlanGantt");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/{planId}/release" && x.PermissionCode == SchedulingPermissionCodes.PlansRelease && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "releaseSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/{planId}/revoke" && x.PermissionCode == SchedulingPermissionCodes.PlansRelease && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "revokeSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/scheduling/plans/{planId}/operations/{operationId}/override" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "upsertSchedulingOperationOverride");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/order-urgencies" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listOrderUrgencies");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/order-urgencies/{orderReference}" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getOrderUrgency");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/scheduling/order-urgencies/{orderReference}/business-priority" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "setOrderUrgencyBusinessPriority");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/internal/v1/scheduling/order-urgency-archives/restore" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "restoreOrderUrgencyArchive");
        Assert.All(contracts, x => Assert.Contains(x.PermissionCode, allowedPermissions));
    }

    [Theory]
    [InlineData(typeof(PreviewSchedulePlanEndpoint))]
    [InlineData(typeof(CreateSchedulePlanEndpoint))]
    [InlineData(typeof(CreateSchedulingWorkbenchPlanEndpoint))]
    [InlineData(typeof(CreateSchedulePlanRevisionEndpoint))]
    [InlineData(typeof(AssembleSchedulingProblemEndpoint))]
    [InlineData(typeof(ListSchedulePlansEndpoint))]
    [InlineData(typeof(GetSchedulePlanEndpoint))]
    [InlineData(typeof(GetSchedulePlanGanttEndpoint))]
    [InlineData(typeof(ReleaseSchedulePlanEndpoint))]
    [InlineData(typeof(RevokeSchedulePlanEndpoint))]
    [InlineData(typeof(UpsertScheduleOperationOverrideEndpoint))]
    [InlineData(typeof(ListOrderUrgenciesEndpoint))]
    [InlineData(typeof(GetOrderUrgencyEndpoint))]
    [InlineData(typeof(SetOrderUrgencyBusinessPriorityEndpoint))]
    public void Scheduling_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Testing_startup_requires_explicit_postgresql_connection_string_when_persistence_is_not_replaced()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting("ConnectionStrings:PostgreSQL", string.Empty);
            });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        });

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings:PostgreSQL", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_returns_deterministic_shock_absorber_plan_without_persisting_release_state()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new PreviewSchedulePlanCommandHandler(
            new FiniteCapacityScheduler(),
            new FixedTimeProvider(FixedNow),
            new NoopSchedulingEquipmentAvailabilityProvider(),
            new NoopSchedulingMaterialReadinessProvider(),
            new PassthroughSchedulingOperationOverrideOverlay());
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();

        var first = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);
        var second = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Preview, first.Status);
        Assert.Equal(first.Assignments.Select(x => (x.OperationId, x.ResourceId, x.StartUtc, x.EndUtc)), second.Assignments.Select(x => (x.OperationId, x.ResourceId, x.StartUtc, x.EndUtc)));
        Assert.NotEmpty(first.GanttItems);
        Assert.False(await dbContext.SchedulePlans.AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Preview_merges_runtime_equipment_availability_before_scheduling()
    {
        var problem = CreateSingleOperationProblem();
        var availabilityProvider = new StubSchedulingEquipmentAvailabilityProvider(
            new EquipmentRuntimeAvailabilityResponse(
                1,
                problem.OrganizationId,
                problem.EnvironmentId,
                problem.HorizonStartUtc,
                problem.HorizonEndUtc,
                [
                    new EquipmentRuntimeAvailabilityWindowContract(
                        "DEV-SNAPSHOT-01",
                        "WC-SNAPSHOT",
                        EquipmentRuntimeAvailabilityStatus.Unavailable,
                        "equipment.activeAlarm",
                        EquipmentRuntimeSeverity.Critical,
                        problem.HorizonStartUtc,
                        problem.HorizonEndUtc,
                        EquipmentRuntimeSourceType.Alarm,
                        "alarm-001",
                        "equipment.activeAlarm",
                        [])
                ]));
        var handler = new PreviewSchedulePlanCommandHandler(
            new FiniteCapacityScheduler(),
            new FixedTimeProvider(FixedNow),
            availabilityProvider,
            new NoopSchedulingMaterialReadinessProvider(),
            new PassthroughSchedulingOperationOverrideOverlay());

        var plan = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);

        Assert.True(availabilityProvider.WasCalled);
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
    }

    [Fact]
    public async Task Preview_merges_mes_material_readiness_before_scheduling()
    {
        var problem = CreateSingleOperationProblem();
        var materialReadinessProvider = new StubSchedulingMaterialReadinessProvider(
            [
                new SchedulingMaterialReadinessContract(
                    ScopeType: "order",
                    ScopeId: "WO-SNAPSHOT-001",
                    MaterialReadyUtc: null,
                    IsReady: false,
                    ReasonCodes: ["MAT-A shortage 2"])
            ]);
        var handler = new PreviewSchedulePlanCommandHandler(
            new FiniteCapacityScheduler(),
            new FixedTimeProvider(FixedNow),
            new NoopSchedulingEquipmentAvailabilityProvider(),
            materialReadinessProvider,
            new PassthroughSchedulingOperationOverrideOverlay());

        var plan = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);

        Assert.True(materialReadinessProvider.WasCalled);
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Material);
    }

    [Fact]
    public async Task Create_persists_generated_plan_and_detail_returns_all_persisted_plan_facts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var detailHandler = new GetSchedulePlanDetailQueryHandler(dbContext);

        var problem = CreateProblemWithUnscheduledOperation();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var detail = await detailHandler.Handle(
            new GetSchedulePlanDetailQuery(created.PlanId, problem.OrganizationId, problem.EnvironmentId),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Generated, created.Status);
        Assert.Equal(created.PlanId, detail.PlanId);
        Assert.NotEmpty(detail.Assignments);
        Assert.NotEmpty(detail.Conflicts);
        Assert.NotEmpty(detail.UnscheduledOperations);
        Assert.NotEmpty(detail.ResourceLoads);
        Assert.Contains(detail.ChangeSummary, x =>
            x.OrderId == "WO-NO-CAP-001"
            && x.OperationId == "WO-NO-CAP-001-PAINT"
            && x.ChangeType == ScheduleChangeTypeContract.Blocked);
        Assert.NotEmpty(detail.GanttItems);
        Assert.True(await dbContext.ScheduleProblems.AnyAsync(x => x.ProblemId == created.ProblemId, CancellationToken.None));
        Assert.True(await dbContext.SchedulePlans.AnyAsync(x => x.PlanId == created.PlanId, CancellationToken.None));
    }

    [Fact]
    public async Task List_caps_default_page_size_and_keeps_stable_descending_order()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var index = 0; index < 105; index++)
        {
            dbContext.SchedulePlans.Add(CreatePersistedPlan(
                planId: $"plan-{index:000}",
                problemId: $"problem-{index:000}",
                generatedAtUtc: FixedNow.AddMinutes(index)));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ListSchedulePlansQueryHandler(dbContext);

        var results = await handler.Handle(
            new ListSchedulePlansQuery("org-001", "prod"),
            CancellationToken.None);

        Assert.Equal(100, results.Count);
        Assert.Equal("plan-104", results.First().PlanId);
        Assert.Equal("plan-005", results.Last().PlanId);
    }

    [Fact]
    public async Task List_applies_requested_page_after_clamping_page_size()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var index = 0; index < 105; index++)
        {
            dbContext.SchedulePlans.Add(CreatePersistedPlan(
                planId: $"plan-{index:000}",
                problemId: $"problem-{index:000}",
                generatedAtUtc: FixedNow.AddMinutes(index)));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ListSchedulePlansQueryHandler(dbContext);

        var results = await handler.Handle(
            new ListSchedulePlansQuery("org-001", "prod", PageIndex: 1, PageSize: 250),
            CancellationToken.None);

        Assert.Equal(5, results.Count);
        Assert.Equal("plan-004", results.First().PlanId);
        Assert.Equal("plan-000", results.Last().PlanId);
    }

    [Fact]
    public async Task Create_is_idempotent_for_same_problem_id_and_same_fingerprint()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var command = new CreateSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem());

        var first = await createHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await createHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.ProblemFingerprint, second.ProblemFingerprint);
        Assert.Equal(1, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
        Assert.Equal(1, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_idempotent_retry_reuses_the_plan_and_refreshes_urgency_facts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var initialHandler = CreatePlanHandler(dbContext);
        var command = new CreateSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem());
        var existing = await initialHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var retryHandler = CreatePlanHandler(dbContext);

        var retried = await retryHandler.Handle(command, CancellationToken.None);

        Assert.Equal(existing.PlanId, retried.PlanId);
        Assert.Equal(existing.ProblemFingerprint, retried.ProblemFingerprint);
        Assert.Equal(command.Problem.Orders.Count, await dbContext.OrderUrgencySnapshots.CountAsync());
        Assert.Equal(1, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
        Assert.Equal(1, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_scopes_problem_id_idempotency_to_organization_and_environment()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var firstProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var secondTenantProblem = firstProblem with
        {
            OrganizationId = "org-002",
            EnvironmentId = "env-prod"
        };

        var first = await createHandler.Handle(new CreateSchedulePlanCommand(firstProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await createHandler.Handle(new CreateSchedulePlanCommand(secondTenantProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(first.PlanId, second.PlanId);
        Assert.Equal(firstProblem.ProblemId, second.ProblemId);
        Assert.Equal(2, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
        Assert.Equal(2, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_same_problem_id_with_different_fingerprint_as_business_conflict()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var firstProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var changedProblem = firstProblem with
        {
            Orders =
            [
                firstProblem.Orders.First() with
                {
                    Quantity = firstProblem.Orders.First().Quantity + 1
                },
                ..firstProblem.Orders.Skip(1)
            ]
        };

        await createHandler.Handle(new CreateSchedulePlanCommand(firstProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            createHandler.Handle(new CreateSchedulePlanCommand(changedProblem), CancellationToken.None));

        Assert.Contains(firstProblem.ProblemId, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
        Assert.Equal(1, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public void Preview_and_create_validators_reject_structurally_invalid_problem_before_handler_execution()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var firstOrder = problem.Orders.First();
        var invalidProblem = problem with
        {
            Orders =
            [
                firstOrder with
                {
                    Operations =
                    [
                        firstOrder.Operations.First() with
                        {
                            DurationMinutes = 0
                        }
                    ]
                },
                ..problem.Orders.Skip(1)
            ]
        };

        var previewResult = new PreviewSchedulePlanCommandValidator()
            .Validate(new PreviewSchedulePlanCommand(invalidProblem));
        var createResult = new CreateSchedulePlanCommandValidator()
            .Validate(new CreateSchedulePlanCommand(invalidProblem));

        Assert.Contains(previewResult.Errors, x => x.ErrorMessage.Contains("DurationMinutes", StringComparison.Ordinal));
        Assert.Contains(createResult.Errors, x => x.ErrorMessage.Contains("DurationMinutes", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_and_create_validators_reject_null_nested_collections_before_handler_execution()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var firstOrder = problem.Orders.First();
        var invalidProblem = problem with
        {
            Orders =
            [
                firstOrder with
                {
                    Operations =
                    [
                        firstOrder.Operations.First() with
                        {
                            PredecessorOperationIds = null!
                        }
                    ]
                },
                ..problem.Orders.Skip(1)
            ]
        };

        var previewResult = new PreviewSchedulePlanCommandValidator()
            .Validate(new PreviewSchedulePlanCommand(invalidProblem));
        var createResult = new CreateSchedulePlanCommandValidator()
            .Validate(new CreateSchedulePlanCommand(invalidProblem));

        Assert.Contains(previewResult.Errors, x => x.ErrorMessage.Contains("PredecessorOperationIds", StringComparison.Ordinal));
        Assert.Contains(createResult.Errors, x => x.ErrorMessage.Contains("PredecessorOperationIds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Release_changes_status_to_released_and_is_idempotent_for_same_plan()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow.AddHours(2)),
            new PostgreSqlScheduleReleaseScopeLock(dbContext));

        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var first = await releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Released, first.Status);
        Assert.Equal(SchedulePlanStatusContract.Released, second.Status);
        Assert.Equal(first.ReleasedAtUtc, second.ReleasedAtUtc);
    }

    [Fact]
    public void Release_unique_conflict_behavior_wraps_unit_of_work_save()
    {
        using var factory = new SchedulingLiveHttpTestFactory();
        using var scope = factory.Services.CreateScope();
        var behaviorTypes = scope.ServiceProvider
            .GetServices<IPipelineBehavior<ReleaseSchedulePlanCommand, ReleaseSchedulePlanResponse>>()
            .Select(x => x.GetType())
            .ToArray();

        var conflictIndex = Array.FindIndex(
            behaviorTypes,
            x => x == typeof(ReleaseSchedulePlanUniqueConflictBehavior));
        var unitOfWorkIndex = Array.FindIndex(
            behaviorTypes,
            x => x.Name.Contains("UnitOfWork", StringComparison.Ordinal));

        Assert.True(conflictIndex >= 0, $"Missing release conflict behavior. Pipeline: {string.Join(", ", behaviorTypes.Select(x => x.Name))}");
        Assert.True(unitOfWorkIndex >= 0, $"Missing unit-of-work behavior. Pipeline: {string.Join(", ", behaviorTypes.Select(x => x.Name))}");
        Assert.True(
            conflictIndex < unitOfWorkIndex,
            $"Release conflict behavior must wrap unit-of-work save. Pipeline: {string.Join(", ", behaviorTypes.Select(x => x.Name))}");
    }

    [Fact]
    public async Task Release_rejects_plans_with_error_conflicts_or_unscheduled_operations()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow.AddHours(2)),
            new PostgreSqlScheduleReleaseScopeLock(dbContext));

        var problem = CreateProblemWithUnscheduledOperation();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None));

        Assert.Contains("cannot be released", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, (await dbContext.SchedulePlans.SingleAsync(x => x.PlanId == created.PlanId)).Status);
    }

    [Fact]
    public async Task Release_updates_header_without_tracking_plan_child_collections()
    {
        await using var provider = CreateInMemoryProvider();
        using var seedScope = provider.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = CreatePersistedPlan("plan-release-001", "problem-release-001", FixedNow, includeUnscheduledOperation: false);
        seedContext.SchedulePlans.Add(plan);
        await seedContext.SaveChangesAsync(CancellationToken.None);

        using var releaseScope = provider.CreateScope();
        var releaseContext = releaseScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(
            releaseContext,
            new FixedTimeProvider(FixedNow.AddHours(2)),
            new PostgreSqlScheduleReleaseScopeLock(releaseContext));

        var response = await releaseHandler.Handle(
            new ReleaseSchedulePlanCommand("plan-release-001", "org-001", "prod"),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Released, response.Status);
        Assert.DoesNotContain(releaseContext.ChangeTracker.Entries(), x =>
            x.Entity is SchedulePlanAssignment or SchedulePlanResourceLoad or SchedulePlanConflict or SchedulePlanUnscheduledOperation);
    }

    [Fact]
    public async Task Detail_gantt_and_release_reject_plan_id_outside_requested_tenant_context()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = CreatePlanHandler(dbContext);
        var detailHandler = new GetSchedulePlanDetailQueryHandler(dbContext);
        var ganttHandler = new GetSchedulePlanGanttQueryHandler(dbContext);
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(
            dbContext,
            new FixedTimeProvider(FixedNow.AddHours(2)),
            new PostgreSqlScheduleReleaseScopeLock(dbContext));

        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() =>
            detailHandler.Handle(new GetSchedulePlanDetailQuery(created.PlanId, "org-other", problem.EnvironmentId), CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() =>
            ganttHandler.Handle(new GetSchedulePlanGanttQuery(created.PlanId, problem.OrganizationId, "env-other"), CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() =>
            releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, "org-other", "env-other"), CancellationToken.None));
    }

    [Fact]
    public async Task Scheduling_authorized_http_endpoints_execute_mediator_pipeline()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var createResponse = await client.PostAsJsonAsync("/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()));
        createResponse.EnsureSuccessStatusCode();
        var createdEnvelope = await createResponse.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>(SchedulingJson.Options);
        var created = Assert.IsType<SchedulePlanContract>(createdEnvelope?.Data);

        var contextQuery = "organizationId=org-001&environmentId=prod";
        var list = await client.GetFromJsonAsync<ResponseData<IReadOnlyCollection<SchedulePlanSummaryResponse>>>(
            $"/api/business/v1/scheduling/plans?{contextQuery}&pageIndex=0&pageSize=10",
            SchedulingJson.Options);
        var detail = await client.GetFromJsonAsync<ResponseData<SchedulePlanContract>>($"/api/business/v1/scheduling/plans/{created.PlanId}?{contextQuery}", SchedulingJson.Options);
        var gantt = await client.GetFromJsonAsync<ResponseData<IReadOnlyCollection<GanttScheduleItemContract>>>($"/api/business/v1/scheduling/plans/{created.PlanId}/gantt?{contextQuery}", SchedulingJson.Options);
        var releaseResponse = await client.PostAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/release?{contextQuery}", null);
        releaseResponse.EnsureSuccessStatusCode();
        var releasedEnvelope = await releaseResponse.Content.ReadFromJsonAsync<ResponseData<ReleaseSchedulePlanResponse>>(SchedulingJson.Options);

        Assert.Contains(list?.Data ?? [], x => x.PlanId == created.PlanId && x.Status == SchedulePlanStatusContract.Generated);
        Assert.Equal(created.PlanId, detail?.Data?.PlanId);
        Assert.Contains(gantt?.Data ?? [], x => x.Status == SchedulePlanStatusContract.Generated);
        Assert.Equal(SchedulePlanStatusContract.Released, releasedEnvelope?.Data?.Status);
    }

    [Fact]
    public async Task Scheduling_authorized_http_endpoints_accept_string_enum_payloads()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var requestJson = JsonSerializer.Serialize(
            new PreviewSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()),
            SchedulingJson.Options);
        using var content = new StringContent(
            requestJson,
            System.Text.Encoding.UTF8,
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));

        var response = await client.PostAsync("/api/business/v1/scheduling/plans/preview", content);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"splitPolicy\":\"nonSplittable\"", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"preview\"", responseBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("P4")]
    public async Task Priority_http_endpoint_rejects_invalid_levels_with_stable_wire_field(string level)
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.PutAsJsonAsync(
            "/api/business/v1/scheduling/order-urgencies/WO-001/business-priority",
            new
            {
                organizationId = "org-001",
                environmentId = "prod",
                level,
                reason = "invalid priority contract test",
            });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.False(errors.TryGetProperty("Level", out _));
        var message = Assert.Single(errors.GetProperty("level").EnumerateArray()).GetString();
        Assert.Equal("Level must be P0, P1, P2, or P3.", message);
    }

    [Fact]
    public async Task Scheduling_authorized_http_endpoints_scope_plan_routes_by_requested_context()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var createResponse = await client.PostAsJsonAsync("/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()));
        createResponse.EnsureSuccessStatusCode();
        var createdEnvelope = await createResponse.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>(SchedulingJson.Options);
        var created = Assert.IsType<SchedulePlanContract>(createdEnvelope?.Data);

        var detail = await client.GetAsync($"/api/business/v1/scheduling/plans/{created.PlanId}?organizationId=org-other&environmentId=prod");
        var gantt = await client.GetAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/gantt?organizationId=org-001&environmentId=env-other");
        var release = await client.PostAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/release?organizationId=org-other&environmentId=env-other", null);

        await AssertRejectedKnownExceptionEnvelopeAsync(detail);
        await AssertRejectedKnownExceptionEnvelopeAsync(gantt);
        await AssertRejectedKnownExceptionEnvelopeAsync(release);
    }

    [Theory]
    [MemberData(nameof(AnonymousEndpointRequests))]
    public async Task Scheduling_http_endpoints_reject_anonymous_callers_before_handler_execution(HttpRequestMessage request)
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden,
            $"Expected anonymous request to be rejected before handler execution, got {(int)response.StatusCode}.");
    }

    public static IEnumerable<object[]> AnonymousEndpointRequests()
    {
        yield return [JsonRequest(HttpMethod.Post, "/api/business/v1/scheduling/plans/preview", new PreviewSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()))];
        yield return [JsonRequest(HttpMethod.Post, "/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()))];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans?organizationId=org-001&environmentId=prod")];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans/plan-missing")];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans/plan-missing/gantt")];
        yield return [new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/scheduling/plans/plan-missing/release")];
    }

    private static CreateSchedulePlanCommandHandler CreatePlanHandler(ApplicationDbContext dbContext)
    {
        var clock = new FixedTimeProvider(FixedNow);
        return new CreateSchedulePlanCommandHandler(
            dbContext,
            new FiniteCapacityScheduler(),
            clock,
            new NoopSchedulingEquipmentAvailabilityProvider(),
            new NoopSchedulingMaterialReadinessProvider(),
            new SchedulingOperationOverrideOverlay(dbContext),
            new OrderUrgencyService(dbContext, clock));
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"scheduling-api-contract-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static SchedulingProblemContract CreateProblemWithUnscheduledOperation()
    {
        var baseProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var blockedOperation = new SchedulingOperationContract(
            OperationId: "WO-NO-CAP-001-PAINT",
            OperationSequence: 10,
            PredecessorOperationIds: [],
            DurationMinutes: 30,
            RequiredCapabilityCode: "CAP-PAINT",
            EligibleResourceIds: ["DEV-PAINT-404"],
            PrimaryResourceId: "DEV-PAINT-404",
            EarliestStartUtc: baseProblem.HorizonStartUtc,
            DueUtc: baseProblem.HorizonEndUtc,
            Priority: 1,
            IsRush: false,
            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
            MaterialReadyUtc: baseProblem.HorizonStartUtc,
            QualityBlockReason: null,
            SourceReference: "MES:WO-NO-CAP-001");

        return baseProblem with
        {
            Orders =
            [
                ..baseProblem.Orders,
                new SchedulingOrderContract(
                    OrderId: "WO-NO-CAP-001",
                    SkuCode: "FG-CUSTOM",
                    Quantity: 1,
                    DueUtc: baseProblem.HorizonEndUtc,
                    Priority: 1,
                    IsRush: false,
                    Operations: [blockedOperation])
            ]
        };
    }

    private static SchedulePlan CreatePersistedPlan(
        string planId,
        string problemId,
        DateTimeOffset generatedAtUtc,
        bool includeUnscheduledOperation = true)
    {
        IReadOnlyCollection<UnscheduledOperationContract> unscheduledOperations = includeUnscheduledOperation
            ?
            [
                new UnscheduledOperationContract(
                    OrderId: $"wo-unscheduled-{planId}",
                    OperationId: $"op-unscheduled-{planId}",
                    ReasonCode: ScheduleConflictReasonCodeContract.NoEligibleResource,
                    Message: "No eligible resource.")
            ]
            : Array.Empty<UnscheduledOperationContract>();

        return SchedulePlan.FromGeneratedPlan("org-001", "prod", SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
            ContractVersion: 1,
            PlanId: planId,
            ProblemId: problemId,
            ProblemFingerprint: $"fingerprint-{planId}",
            AlgorithmVersion: "aps-lite-v1",
            Status: SchedulePlanStatusContract.Generated,
            GeneratedAtUtc: generatedAtUtc,
            Metrics: new SchedulePlanMetricsContract(
                ScheduledOperationCount: 1,
                UnscheduledOperationCount: unscheduledOperations.Count,
                AssignedMinutes: 30,
                MakespanMinutes: 30,
                TotalTardinessMinutes: 0,
                LateOperationCount: 0,
                OnTimeRate: 1m,
                AverageResourceUtilization: 0.0625m),
            Assignments:
            [
                new ScheduleAssignmentContract(
                    AssignmentId: $"assign-{planId}",
                    OrderId: $"wo-{planId}",
                    OperationId: $"op-{planId}",
                    OperationSequence: 10,
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL",
                    StartUtc: generatedAtUtc,
                    EndUtc: generatedAtUtc.AddMinutes(30),
                    IsLocked: false,
                    ExplanationCode: "scheduled")
            ],
            ResourceLoads:
            [
                new ScheduleResourceLoadContract(
                    ResourceId: "DEV-OIL-01",
                    WindowStartUtc: generatedAtUtc,
                    WindowEndUtc: generatedAtUtc.AddHours(8),
                    AssignedMinutes: 30,
                    AvailableMinutes: 480,
                    Utilization: 0.0625m)
            ],
            Conflicts:
            [
                new ScheduleConflictContract(
                    ConflictId: $"conflict-{planId}",
                    ReasonCode: ScheduleConflictReasonCodeContract.DueDate,
                    Severity: ScheduleConflictSeverityContract.Warning,
                    OrderId: $"wo-{planId}",
                    OperationId: $"op-{planId}",
                    ResourceId: "DEV-OIL-01",
                    Message: "Assignment finishes after due date.")
            ],
            UnscheduledOperations: unscheduledOperations,
            ChangeSummary: [],
            GanttItems: [])));
    }

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string requestUri, T body)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertRejectedKnownExceptionEnvelopeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Schedule plan was not found", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static SchedulingProblemContract CreateSingleOperationProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-equipment-provider-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-SNAPSHOT-001",
                    SkuCode: "FG-SNAPSHOT",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-SNAPSHOT-001-OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-SNAPSHOT",
                            EligibleResourceIds: ["DEV-SNAPSHOT-01"],
                            PrimaryResourceId: "DEV-SNAPSHOT-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: shiftEnd,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:SNAPSHOT")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    CapabilityCodes: ["CAP-SNAPSHOT"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-SNAPSHOT",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows: [new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private sealed class StubSchedulingEquipmentAvailabilityProvider(EquipmentRuntimeAvailabilityResponse availability)
        : ISchedulingEquipmentAvailabilityProvider
    {
        public bool WasCalled { get; private set; }

        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(availability);
        }
    }

    private sealed class StubSchedulingMaterialReadinessProvider(
        IReadOnlyCollection<SchedulingMaterialReadinessContract> materialReadiness)
        : ISchedulingMaterialReadinessProvider
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(materialReadiness);
        }
    }

    private sealed class ThrowingSchedulingEquipmentAvailabilityProvider : ISchedulingEquipmentAvailabilityProvider
    {
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Availability provider should not be called on idempotent retries.");
        }
    }

    private sealed class ThrowingSchedulingMaterialReadinessProvider : ISchedulingMaterialReadinessProvider
    {
        public Task<IReadOnlyCollection<SchedulingMaterialReadinessContract>> QueryAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Material readiness provider should not be called on idempotent retries.");
        }
    }

    private sealed class PassthroughSchedulingOperationOverrideOverlay : ISchedulingOperationOverrideOverlay
    {
        public Task<SchedulingProblemContract> ApplyAsync(
            SchedulingProblemContract problem,
            CancellationToken cancellationToken) => Task.FromResult(problem);
    }

    private sealed class SchedulingLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"scheduling-live-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.UseSetting("ConnectionStrings:PostgreSQL", "Host=unused;Database=nerv_iip_scheduling_live_http;Username=nerv;Password=nerv");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            throw new InvalidOperationException("The generation path should not be entered for idempotent retries.");
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
