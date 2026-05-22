using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MaintenanceEventHandlerTests
{
    [Fact]
    public async Task AssetUnavailableHandler_RecordsOpenUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));

        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true });

        await handler.HandleAsync(CreateUnavailableEvent(now), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal("WC-A", window.WorkCenterId);
        Assert.Null(window.ToUtc);
        Assert.Equal("breakdown", window.Reason);
        Assert.Equal(RescheduleTrigger.AssetUnavailable, Assert.Single(store.ScheduleResults).Trigger);
    }

    [Fact]
    public async Task AssetRestoredHandler_ClosesUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));

        var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true });

        await handler.HandleAsync(CreateRestoredEvent(now.AddHours(2)), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal(now.AddHours(2), window.ToUtc);
        Assert.Equal(RescheduleTrigger.AssetRestored, Assert.Single(store.ScheduleResults).Trigger);
    }

    private static AssetUnavailableIntegrationEvent CreateUnavailableEvent(DateTimeOffset fromUtc)
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetUnavailable:ASSET-CNC-01:20260522080000",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", fromUtc));
    }

    private static AssetRestoredIntegrationEvent CreateRestoredEvent(DateTimeOffset restoredAtUtc)
    {
        return new AssetRestoredIntegrationEvent(
            "evt-002",
            MaintenanceIntegrationEventTypes.AssetRestored,
            MaintenanceIntegrationEventVersions.V1,
            restoredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "evt-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetRestored:ASSET-CNC-01:20260522100000",
            new AssetRestoredPayload("ASSET-CNC-01", restoredAtUtc));
    }
}
