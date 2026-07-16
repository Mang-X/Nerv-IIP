using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

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
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateAssetUnavailableEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal("maintenance.AssetUnavailable", invalidation.SourceEventType);
            Assert.Equal(FixedNow, invalidation.RecordedAtUtc);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_event_rejects_blank_device_asset_id()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateAssetUnavailableEvent() with
        {
            Payload = new AssetUnavailablePayload(" ", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero))
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(integrationEvent, CancellationToken.None));

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
    }

    [Fact]
    public async Task Maintenance_asset_unavailable_event_logs_when_resource_mapping_matches_no_generated_plan()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var logger = new RecordingLogger<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>();
        var handler = new AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            logger);
        var integrationEvent = CreateAssetUnavailableEvent() with
        {
            Payload = new AssetUnavailablePayload("ASSET-NOT-MAPPED", "breakdown", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero))
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Contains(logger.Messages, x =>
            x.LogLevel == LogLevel.Information &&
            x.Message.Contains("ASSET-NOT-MAPPED", StringComparison.Ordinal) &&
            x.Message.Contains("matched no schedule plan", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>().Published);
    }

    [Fact]
    public async Task Maintenance_asset_restored_event_invalidates_generated_plan_for_affected_resource()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateAssetRestoredEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentRestored, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal("maintenance.AssetRestored", invalidation.SourceEventType);
        });
    }

    [Fact]
    public async Task IndustrialTelemetry_device_state_changed_event_invalidates_generated_or_released_plans_for_affected_device_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateDeviceStateChangedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.DeviceStateChanged, invalidation.ReasonCode);
            Assert.Equal("ASSET-CNC-01", invalidation.AffectedResourceId);
            Assert.Equal(IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged, invalidation.SourceEventType);
            Assert.Equal(FixedNow, invalidation.RecordedAtUtc);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());

        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Equal(DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName, processed.ConsumerName);
        Assert.Equal("industrialTelemetry:device-state:org-001:env-dev:ASSET-CNC-01:state-seq-009:state-snapshot-001", processed.IdempotencyKey);
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
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(CreateStockAvailabilityChangedEvent(), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.MaterialReadinessChanged, invalidation.ReasonCode);
            Assert.Equal("SKU-001", invalidation.AffectedSkuCode);
            Assert.Equal("inventory.StockAvailabilityChanged", invalidation.SourceEventType);
        });
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
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(CreateInspectionEvent(eventType), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(expectedReason, invalidation.ReasonCode);
            Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
            Assert.Equal(eventType, invalidation.SourceEventType);
        });
    }

    [Fact]
    public async Task Quality_inspection_event_for_operation_source_publishes_only_affected_operation()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());

        await handler.HandleAsync(
            CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, sourceDocumentId: "OP-002"),
            CancellationToken.None);

        var invalidations = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .SchedulePlanInvalidations
            .OrderBy(x => x.PlanId)
            .ToArrayAsync();
        Assert.Equal(2, invalidations.Length);
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal("OP-002", invalidation.AffectedOperationId);
            Assert.Null(invalidation.AffectedWorkOrderId);
            Assert.Contains("OP-002", invalidation.SourceEventId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Quality_inspection_event_ignores_non_mes_source_service()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>());
        var integrationEvent = CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected) with
        {
            Payload = CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected).Payload with
            {
                SourceService = QualityIntegrationEventSources.BusinessQuality
            }
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
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
            scope.ServiceProvider.GetRequiredService<ISender>());
        var integrationEvent = CreateWorkOrderReleasedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidations = await dbContext.SchedulePlanInvalidations.OrderBy(x => x.PlanId).ToArrayAsync();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal(SchedulingPlanInvalidationReasons.WorkOrderReleased, invalidation.ReasonCode);
            Assert.Equal("WO-NEW", invalidation.AffectedWorkOrderId);
            Assert.Equal("mes.WorkOrderReleased", invalidation.SourceEventType);
        });
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task MasterData_work_calendar_changed_event_invalidates_only_generated_plans_using_the_calendar_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateWorkCalendarChangedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-calendar-target", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.WorkCalendarChanged, invalidation.ReasonCode);
        Assert.Equal(MasterDataIntegrationEventTypes.WorkCalendarChanged, invalidation.SourceEventType);

        var plans = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            new ListSchedulePlansQuery("org-001", "env-dev"),
            CancellationToken.None);
        Assert.True(plans.Single(x => x.PlanId == "plan-calendar-target").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-calendar-released").IsInvalidated);
        Assert.False(plans.Single(x => x.PlanId == "plan-calendar-other").IsInvalidated);

        var published = Assert.Single(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>());
        Assert.Equal(["ASSET-CNC-01", "WC-CNC"], published.Payload.AffectedResourceIds);

        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Equal(WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName, processed.ConsumerName);
    }

    [Fact]
    public async Task MasterData_resource_changed_event_invalidates_only_generated_plans_assigned_to_the_resource_once()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var integrationEvent = CreateResourceChangedEvent("WorkCenter", "WC-CNC");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invalidation = Assert.Single(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Equal("plan-calendar-target", invalidation.PlanId);
        Assert.Equal(SchedulingPlanInvalidationReasons.ResourceChanged, invalidation.ReasonCode);
        Assert.Equal("WC-CNC", invalidation.AffectedResourceId);
        Assert.Equal(MasterDataIntegrationEventTypes.ResourceChanged, invalidation.SourceEventType);
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
    }

    [Fact]
    public async Task MasterData_work_center_change_publishes_only_operations_whose_work_center_matches()
    {
        await using var provider = CreateInMemoryProvider();
        using (var seedScope = provider.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.SchedulePlans.Add(CreatePlanWithAssignments(
                "plan-work-center-collision",
                "problem-work-center-collision",
                [
                    new ScheduleAssignmentContract(
                        "assign-target",
                        "WO-001",
                        "OP-TARGET",
                        10,
                        "ASSET-CNC-01",
                        "WC-CNC",
                        new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        false,
                        "scheduled"),
                    new ScheduleAssignmentContract(
                        "assign-resource-code-collision",
                        "WO-001",
                        "OP-UNRELATED",
                        20,
                        "WC-CNC",
                        "WC-LATHE",
                        new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                        false,
                        "scheduled"),
                ]));
            await dbContext.SaveChangesAsync();
        }

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());

        await handler.HandleAsync(CreateResourceChangedEvent("WorkCenter", "WC-CNC"), CancellationToken.None);

        var published = Assert.Single(scope.ServiceProvider.GetRequiredService<RecordingIntegrationEventPublisher>()
            .Published.OfType<SchedulePlanInvalidatedIntegrationEvent>());
        Assert.Equal(["OP-TARGET"], published.Payload.AffectedOperations.Select(x => x.OperationId));
    }

    [Fact]
    public async Task MasterData_resource_changed_event_with_untraceable_hierarchy_is_a_successful_no_match()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var logger = new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            logger);

        await handler.HandleAsync(CreateResourceChangedEvent("Shift", "WC-CNC"), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.SchedulePlanInvalidations.ToArrayAsync());
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Contains(logger.Messages, x =>
            x.LogLevel == LogLevel.Information &&
            x.Message.Contains("WC-CNC", StringComparison.Ordinal) &&
            x.Message.Contains("matched no schedule plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MasterData_work_calendar_changes_with_distinct_event_ids_are_not_collapsed_by_the_stable_source_key()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var first = CreateWorkCalendarChangedEvent();
        var second = first with
        {
            EventId = "evt-masterdata-calendar-002",
            OccurredAtUtc = first.OccurredAtUtc.AddHours(1),
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, await dbContext.SchedulePlanInvalidations.CountAsync());
    }

    [Fact]
    public async Task MasterData_resource_changes_with_distinct_event_ids_are_not_collapsed_by_the_stable_source_key()
    {
        await using var provider = CreateInMemoryProvider();
        await SeedMasterDataScopedPlansAsync(provider);

        using var scope = provider.CreateScope();
        var handler = new ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new InMemoryIntegrationEventDeadLetterStore(),
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new RecordingLogger<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>());
        var first = CreateResourceChangedEvent("WorkCenter", "WC-CNC");
        var second = first with
        {
            EventId = "evt-masterdata-resource-WorkCenter-WC-CNC-002",
            OccurredAtUtc = first.OccurredAtUtc.AddHours(1),
        };

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, await dbContext.SchedulePlanInvalidations.CountAsync());
    }

    [Fact]
    public void Scheduling_input_change_handlers_have_cap_subscriptions()
    {
        AssertSubscription<AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans>(
            "AssetUnavailableIntegrationEvent",
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans>(
            "AssetRestoredIntegrationEvent",
            AssetRestoredIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "DeviceStateChangedIntegrationEvent",
            DeviceStateChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "StockAvailabilityChangedIntegrationEvent",
            StockAvailabilityChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans>(
            "InspectionResultIntegrationEvent",
            QualityInspectionResultIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "WorkOrderReleasedIntegrationEvent",
            WorkOrderReleasedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "WorkCalendarChangedIntegrationEvent",
            WorkCalendarChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
        AssertSubscription<ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans>(
            "ResourceChangedIntegrationEvent",
            ResourceChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName);
    }

    private static async Task SeedMasterDataScopedPlansAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.SchedulePlans.Add(CreatePlan(
            "plan-calendar-target",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-target",
            "ASSET-CNC-01",
            "WC-CNC"));
        var released = CreatePlan(
            "plan-calendar-released",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-released",
            "ASSET-CNC-01",
            "WC-CNC");
        released.Release(FixedNow);
        dbContext.SchedulePlans.Add(released);
        dbContext.SchedulePlans.Add(CreatePlan(
            "plan-calendar-other",
            SchedulePlanStatusContract.Generated,
            "org-001",
            "env-dev",
            "problem-calendar-other",
            "ASSET-LATHE-01",
            "WC-LATHE"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-target", "CAL-A", "ASSET-CNC-01", "WC-CNC"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-released", "CAL-A", "ASSET-CNC-01", "WC-CNC"));
        dbContext.ScheduleProblems.Add(CreateProblemSnapshot("problem-calendar-other", "CAL-B", "ASSET-LATHE-01", "WC-LATHE"));
        await dbContext.SaveChangesAsync();
    }

    private static ScheduleProblemSnapshot CreateProblemSnapshot(
        string problemId,
        string calendarId,
        string resourceId,
        string workCenterId)
    {
        var horizonStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var horizonEnd = horizonStart.AddHours(8);
        var problem = new SchedulingProblemContract(
            1,
            problemId,
            "org-001",
            "env-dev",
            horizonStart,
            horizonEnd,
            [],
            [new SchedulingResourceContract(resourceId, workCenterId, [], 1, calendarId, resourceId)],
            [new SchedulingCalendarContract(calendarId, [new SchedulingTimeWindowContract(horizonStart, horizonEnd, "regular")])],
            [],
            [],
            [],
            []);
        return new ScheduleProblemSnapshot(
            problemId,
            1,
            "org-001",
            "env-dev",
            $"fingerprint-{problemId}",
            JsonSerializer.Serialize(problem, SchedulingJson.Options),
            horizonStart,
            horizonEnd,
            FixedNow);
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
        string environmentId,
        string problemId = "problem-001",
        string resourceId = "ASSET-CNC-01",
        string workCenterId = "WC-CNC")
    {
        return CreatePlanWithAssignments(
            planId,
            problemId,
            [
                new ScheduleAssignmentContract(
                    AssignmentId: $"assign-{planId}",
                    OrderId: "WO-001",
                    OperationId: "OP-001",
                    OperationSequence: 10,
                    ResourceId: resourceId,
                    WorkCenterId: workCenterId,
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    IsLocked: false,
                    ExplanationCode: "scheduled"),
                new ScheduleAssignmentContract(
                    AssignmentId: $"assign-{planId}-2",
                    OrderId: "WO-001",
                    OperationId: "OP-002",
                    OperationSequence: 20,
                    ResourceId: resourceId,
                    WorkCenterId: workCenterId,
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    IsLocked: false,
                    ExplanationCode: "scheduled"),
            ],
            status,
            organizationId,
            environmentId);
    }

    private static SchedulePlan CreatePlanWithAssignments(
        string planId,
        string problemId,
        IReadOnlyCollection<ScheduleAssignmentContract> assignments,
        SchedulePlanStatusContract status = SchedulePlanStatusContract.Generated,
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        return SchedulePlan.FromGeneratedPlan(
            organizationId,
            environmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: problemId,
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
                Assignments: assignments,
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
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddScoped<ISchedulingIntegrationEventContextAccessor, StubSchedulingIntegrationEventContextAccessor>();
        services.AddScoped<SchedulePlanInvalidatedIntegrationEventConverter>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
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

    private static AssetRestoredIntegrationEvent CreateAssetRestoredEvent()
    {
        return new AssetRestoredIntegrationEvent(
            "evt-maint-restored-001",
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-maint-001",
            "wo-maint-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "maintenance:asset-restored:ASSET-CNC-01",
            new AssetRestoredPayload("ASSET-CNC-01", new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero)));
    }

    private static DeviceStateChangedIntegrationEvent CreateDeviceStateChangedEvent()
    {
        return new DeviceStateChangedIntegrationEvent(
            "evt-iiot-state-001",
            IndustrialTelemetryIntegrationEventTypes.DeviceStateChanged,
            IndustrialTelemetryIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 2, 0, TimeSpan.Zero),
            IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            "corr-iiot-001",
            "state-snapshot-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            "industrialTelemetry:device-state:org-001:env-dev:ASSET-CNC-01:state-seq-009:state-snapshot-001",
            new DeviceStateChangedPayload(
                "state-snapshot-001",
                "ASSET-CNC-01",
                "faulted",
                "state-seq-009"));
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

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType, string sourceDocumentId = "WO-001")
    {
        return new InspectionResultIntegrationEvent(
            $"evt-quality-{eventType}-{sourceDocumentId}",
            eventType,
            QualityIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-quality-001",
            "inspection-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection:{eventType}:{sourceDocumentId}",
            new InspectionResultPayload(
                "INS-001",
                null,
                "mes-work-order",
                QualityIntegrationEventSources.BusinessMes,
                sourceDocumentId,
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

    private static WorkCalendarChangedIntegrationEvent CreateWorkCalendarChangedEvent()
    {
        return new WorkCalendarChangedIntegrationEvent(
            "evt-masterdata-calendar-001",
            MasterDataIntegrationEventTypes.WorkCalendarChanged,
            MasterDataIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 20, 0, TimeSpan.Zero),
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-masterdata-001",
            "calendar-CAL-A",
            "org-001",
            "env-dev",
            "user:planner",
            "work-calendar-changed:org-001:env-dev:CAL-A",
            new MasterDataChangedPayload("work-calendar", "CAL-A", "active", FixedNow));
    }

    private static ResourceChangedIntegrationEvent CreateResourceChangedEvent(string resourceType, string code)
    {
        return new ResourceChangedIntegrationEvent(
            $"evt-masterdata-resource-{resourceType}-{code}",
            MasterDataIntegrationEventTypes.ResourceChanged,
            MasterDataIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 6, 1, 9, 25, 0, TimeSpan.Zero),
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-masterdata-001",
            $"resource-{code}",
            "org-001",
            "env-dev",
            "user:planner",
            $"resource-changed:org-001:env-dev:{resourceType}:{code}",
            new ResourceChangedPayload(resourceType, code, "active", FixedNow));
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class StubSchedulingIntegrationEventContextAccessor : ISchedulingIntegrationEventContextAccessor
    {
        public SchedulingIntegrationEventContext GetContext()
        {
            return new SchedulingIntegrationEventContext(
                "corr-scheduling-test",
                "cause-scheduling-test",
                "system:test");
        }
    }
}
