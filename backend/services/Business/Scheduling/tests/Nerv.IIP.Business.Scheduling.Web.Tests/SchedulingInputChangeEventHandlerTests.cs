using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingInputChangeEventHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Maintenance_asset_unavailable_event_invalidates_generated_plan_for_affected_resource()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            new FixedTimeProvider(FixedNow));

        await handler.HandleAsync(CreateAssetUnavailableEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-generated", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalidation.ReasonCode);
        Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
        Assert.Equal("maintenance.AssetUnavailable", invalidation.SourceEventType);
        Assert.Equal(FixedNow, invalidation.RecordedAtUtc);
    }

    [Fact]
    public async Task Stock_availability_changed_event_invalidates_generated_plans_in_same_business_scope()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            new FixedTimeProvider(FixedNow));

        await handler.HandleAsync(CreateStockAvailabilityChangedEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-generated", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.MaterialReadinessChanged, invalidation.ReasonCode);
        Assert.Equal("SKU-001", invalidation.AffectedSkuCode);
        Assert.Equal("inventory.StockAvailabilityChanged", invalidation.SourceEventType);
    }

    [Theory]
    [InlineData(QualityIntegrationEventTypes.InspectionRejected, SchedulingPlanInvalidationReasons.QualityBlocked)]
    [InlineData(QualityIntegrationEventTypes.InspectionPassed, SchedulingPlanInvalidationReasons.QualityReleased)]
    public async Task Quality_inspection_event_invalidates_generated_plan_for_affected_work_order(string eventType, string expectedReason)
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            new FixedTimeProvider(FixedNow));

        await handler.HandleAsync(CreateInspectionEvent(eventType), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-generated", invalidation.PlanId);
        Assert.Equal(expectedReason, invalidation.ReasonCode);
        Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
        Assert.Equal(eventType, invalidation.SourceEventType);
    }

    [Fact]
    public async Task Mes_work_order_released_event_invalidates_generated_plans_in_same_business_scope_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            new FixedTimeProvider(FixedNow));
        var integrationEvent = CreateWorkOrderReleasedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-generated", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.WorkOrderReleased, invalidation.ReasonCode);
        Assert.Equal("WO-NEW", invalidation.AffectedWorkOrderId);
        Assert.Equal("mes.WorkOrderReleased", invalidation.SourceEventType);
    }

    [Fact]
    public void Scheduling_input_change_handlers_have_cap_subscriptions()
    {
        AssertSubscription<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>(
            "Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent",
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "Nerv.IIP.Contracts.Inventory.StockAvailabilityChangedIntegrationEvent",
            StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans>(
            "Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent",
            QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "Nerv.IIP.Contracts.Mes.WorkOrderReleasedIntegrationEvent",
            WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
    }

    private static async Task SeedPlansAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.SchedulePlans.Add(CreatePlan("plan-generated", SchedulePlanStatusContract.Generated, "org-001", "env-dev"));
        var released = CreatePlan("plan-released", SchedulePlanStatusContract.Generated, "org-001", "env-dev");
        released.Release(FixedNow);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlan("plan-other-env", SchedulePlanStatusContract.Generated, "org-001", "env-other"));
        await dbContext.SaveChangesAsync();
    }

    private static SchedulePlan CreatePlan(
        string planId,
        SchedulePlanStatusContract status,
        string organizationId,
        string environmentId)
    {
        return SchedulePlan.FromGeneratedPlan(
            organizationId,
            environmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: "problem-001",
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: status,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: 1,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: 60,
                    MakespanMinutes: 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        AssignmentId: $"assign-{planId}",
                        OrderId: "WO-001",
                        OperationId: "OP-001",
                        OperationSequence: 10,
                        ResourceId: "ASSET-CNC-01",
                        WorkCenterId: "WC-CNC",
                        StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        IsLocked: false,
                        ExplanationCode: "scheduled")
                ],
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"scheduling-events-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static AssetUnavailableIntegrationEvent CreateAssetUnavailableEvent()
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-maint-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "maintenance:asset-unavailable:ASSET-CNC-01",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)));
    }

    private static StockAvailabilityChangedIntegrationEvent CreateStockAvailabilityChangedEvent()
    {
        return new StockAvailabilityChangedIntegrationEvent(
            "evt-stock-001",
            InventoryIntegrationEventTypes.StockAvailabilityChanged,
            InventoryIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 5, 0, TimeSpan.Zero),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-stock-001",
            "movement-001",
            "org-001",
            "env-dev",
            "system:inventory",
            "inventory:stock-availability:SKU-001",
            new StockAvailabilityChangedPayload(
                "SKU-001",
                "EA",
                "production",
                "line-side",
                null,
                null,
                "Unrestricted",
                "production",
                null,
                10,
                2,
                8,
                42,
                new DateTimeOffset(2026, 6, 1, 9, 5, 0, TimeSpan.Zero),
                12,
                120));
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType)
    {
        return new InspectionResultIntegrationEvent(
            $"evt-quality-{eventType}",
            eventType,
            QualityIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-quality-001",
            "inspection-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection:{eventType}:WO-001",
            new InspectionResultPayload(
                "INS-001",
                null,
                "mes-work-order",
                QualityIntegrationEventSources.BusinessMes,
                "WO-001",
                "SKU-001",
                1,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "Rejected" : "Passed",
                null,
                [],
                new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero)));
    }

    private static WorkOrderReleasedIntegrationEvent CreateWorkOrderReleasedEvent()
    {
        return new WorkOrderReleasedIntegrationEvent(
            "evt-mes-wo-001",
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero),
            MesIntegrationEventSources.BusinessMes,
            "corr-mes-001",
            "WO-NEW",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:work-order-released:WO-NEW",
            new WorkOrderReleasedPayload(
                "WO-NEW",
                "SKU-001",
                10,
                new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero),
                [new ReleasedOperationPayload("OP-NEW-10", 10, "WC-CNC")]));
    }

    private static void AssertSubscription<THandler>(string expectedTopic, string expectedGroup)
    {
        var attribute = typeof(THandler)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(CapSubscribeAttribute), false).Cast<CapSubscribeAttribute>())
            .Single();

        Assert.Equal(expectedTopic, attribute.Name);
        Assert.Equal(expectedGroup, attribute.Group);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
