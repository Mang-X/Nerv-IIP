using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

// ADR 0032 §2 / #4127: prediction is an explicit input, never an actual restore.
public sealed class MaintenanceRestorePredictionTests
{
    [Theory]
    [InlineData(true, "explicit-etr", 120)]
    [InlineData(false, "configuration-default", 37)]
    public async Task Availability_maps_the_owner_prediction_without_releasing_or_shortening_actual_downtime(bool explicitEtr, string source, int minutes)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var expected = from.AddMinutes(minutes);
        var order = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        order.MarkAssetUnavailable(from, "fault", explicitEtr ? expected : null);
        db.MaintenanceWorkOrders.Add(order);
        await db.SaveChangesAsync();
        var response = await new QueryMaintenanceAvailabilityWindowsQueryHandler(db, new GetMaintenanceRestorePredictionQueryHandler(db, Microsoft.Extensions.Options.Options.Create(new MaintenanceRestorePredictionOptions { DefaultDowntimeMinutes = 37 }))).Handle(
            new(new Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeAvailabilityRequest(
                "org", "env", from.AddHours(1), from.AddHours(6), ["device"], null)), default);
        var item = Assert.Single(response.Items);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(item, Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeJson.Options);
        Assert.Equal(expected, json.GetProperty("expectedRestoreAtUtc").GetDateTimeOffset());
        Assert.Equal(source, json.GetProperty("restorePredictionSource").GetString());
        var owner = await new GetMaintenanceRestorePredictionQueryHandler(db, Options.Create(new MaintenanceRestorePredictionOptions { DefaultDowntimeMinutes = 37 }))
            .Handle(new("org", "env", order.Id), default);
        Assert.Equal(owner.SourceVersion, json.GetProperty("restorePredictionSourceVersion").GetString());
        Assert.Equal(from.AddHours(1), item.StartUtc);
        Assert.Equal(from.AddHours(6), item.EndUtc);
        Assert.Null(order.CompletedAtUtc);
        Assert.Empty(order.GetDomainEvents().OfType<AssetRestoredDomainEvent>());
    }

    [Fact]
    public void Explicit_ETR_is_preserved_in_the_unavailable_event()
    {
        using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var order = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var expected = from.AddHours(2);
        order.MarkAssetUnavailable(from, "fault", expected);
        var fact = Assert.Single(order.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        var envelope = new AssetUnavailableIntegrationEventConverter().Convert(fact);
        Assert.Equal(expected, envelope.Payload.ExpectedRestoreAtUtc);
        Assert.Empty(order.GetDomainEvents().OfType<AssetRestoredDomainEvent>());
    }

    [Fact]
    public async Task Creation_update_clear_and_replay_preserve_explicit_ETR_without_restoring_the_asset()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var expected = DateTimeOffset.UtcNow.AddHours(2);
        var create = new CreateMaintenanceWorkOrderCommand("org", "env", "device", "high", null, "operator", "fault",
            IdempotencyKey: "create", ExpectedRestoreAtUtc: expected);
        var result = await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(create, default);
        await db.SaveChangesAsync();
        var order = db.MaintenanceWorkOrders.Local.Single();
        var original = new AssetUnavailableIntegrationEventConverter().Convert(
            Assert.Single(order.GetDomainEvents().OfType<AssetUnavailableDomainEvent>()));
        order.ClearDomainEvents();
        var handler = new TransitionMaintenanceWorkOrderCommandHandler(db);
        var update = new TransitionMaintenanceWorkOrderCommand("org", "env", result.WorkOrderId,
            MaintenanceWorkOrderAction.UpdateExpectedRestore, "operator", "new estimate", "update", order.Version,
            ExpectedRestoreAtUtc: expected.AddHours(1));
        var updated = await handler.Handle(update, default);
        await db.SaveChangesAsync();
        var fact = Assert.Single(order.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        var envelope = new AssetUnavailableIntegrationEventConverter().Convert(fact);
        Assert.NotEqual(original.IdempotencyKey, envelope.IdempotencyKey);
        Assert.Equal(original.Payload.FromUtc, envelope.Payload.FromUtc);
        Assert.Equal(expected.AddHours(1), envelope.Payload.ExpectedRestoreAtUtc);
        Assert.Empty(order.GetDomainEvents().OfType<AssetRestoredDomainEvent>());
        order.ClearDomainEvents();
        Assert.Equal(updated, await handler.Handle(update, default));
        Assert.Empty(order.GetDomainEvents());
        await Assert.ThrowsAsync<MaintenanceIdempotencyConflictException>(() => handler.Handle(update with { ExpectedRestoreAtUtc = null }, default));
        db.ChangeTracker.Clear();
        var detail = await new GetMaintenanceWorkOrderQueryHandler(db).Handle(new("org", "env", result.WorkOrderId), default);
        Assert.Equal(expected.AddHours(1), detail.WorkOrder.ExpectedRestoreAtUtc);
        var cleared = await handler.Handle(update with { IdempotencyKey = "clear", ExpectedVersion = updated.Version, ExpectedRestoreAtUtc = null }, default);
        await db.SaveChangesAsync();
        var persisted = db.MaintenanceWorkOrders.Local.Single();
        Assert.Null(persisted.ExpectedRestoreAtUtc);
        Assert.Null(new AssetUnavailableIntegrationEventConverter().Convert(
            Assert.Single(persisted.GetDomainEvents().OfType<AssetUnavailableDomainEvent>())).Payload.ExpectedRestoreAtUtc);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, cleared.Status);
        persisted.Cancel();
        Assert.Throws<InvalidOperationException>(() => persisted.UpdateExpectedRestore(expected));
    }

    [Fact]
    public async Task Prediction_uses_ended_downtime_mean_in_exact_scope_then_explicit_ETR_takes_priority()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var active = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        active.MarkAssetUnavailable(from, "fault");
        db.MaintenanceWorkOrders.Add(active);
        foreach (var (org, env, device, minutes, cancel) in new[] {
            ("org", "env", "device", 30, false), ("org", "env", "device", 90, true),
            ("other", "env", "device", 900, false), ("org", "other", "device", 900, false),
            ("org", "env", "other", 900, false) })
        {
            var sample = MaintenanceWorkOrder.OpenManual(org, env, $"MWO-T-{Guid.NewGuid():N}", device, "high", "operator");
            sample.MarkAssetUnavailable(from, "fault");
            if (cancel) sample.Cancel();
            else sample.Complete("fixed", "fault", 1, []);
            db.MaintenanceWorkOrders.Add(sample);
            db.Entry(sample).Property(cancel ? "CancelledAtUtc" : "CompletedAtUtc").CurrentValue = from.AddMinutes(minutes);
            // Repair labor cannot explain the 30/90 minute downtime samples.
            db.Entry(sample).Property(x => x.RepairStartedAtUtc).CurrentValue = from.AddMinutes(minutes - 1);
        }
        var clearedAlarm = MaintenanceWorkOrder.OpenFromAlarm("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "alarm", "high");
        clearedAlarm.MarkAssetUnavailable(from.AddHours(-10), "fault");
        clearedAlarm.MarkAlarmCleared(from);
        db.MaintenanceWorkOrders.Add(clearedAlarm);
        await db.SaveChangesAsync();
        var query = new GetMaintenanceRestorePredictionQuery("org", "env", active.Id);
        var handler = new GetMaintenanceRestorePredictionQueryHandler(db, Options.Create(new MaintenanceRestorePredictionOptions { DefaultDowntimeMinutes = 120 }));
        var mean = await handler.Handle(query, default);
        Assert.Equal("device-mttr", mean.Source);
        Assert.Equal(60m, mean.SelectedDowntimeMinutes);
        Assert.Equal(from.AddMinutes(60), mean.PredictedRestoreAtUtc);
        Assert.Null(active.ExpectedRestoreAtUtc);
        Assert.Equal(mean, await handler.Handle(query, default));
        active.UpdateExpectedRestore(from.AddMinutes(15));
        await db.SaveChangesAsync();
        var explicitResult = await handler.Handle(query, default);
        Assert.Equal("explicit-etr", explicitResult.Source);
        Assert.Equal(from.AddMinutes(15), explicitResult.PredictedRestoreAtUtc);
        Assert.NotEqual(mean.SourceVersion, explicitResult.SourceVersion);
    }

    [Fact]
    public async Task No_samples_use_configured_default_without_backfilling_explicit_ETR()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var order = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        order.MarkAssetUnavailableByReasonCode(from, "fault");
        db.MaintenanceWorkOrders.Add(order);
        await db.SaveChangesAsync();
        var handler = new GetMaintenanceRestorePredictionQueryHandler(db, Options.Create(new MaintenanceRestorePredictionOptions { DefaultDowntimeMinutes = 75 }));
        var result = await handler.Handle(new("org", "env", order.Id), default);
        Assert.Equal("configuration-default", result.Source);
        Assert.Equal(75m, result.SelectedDowntimeMinutes);
        Assert.Equal(from.AddMinutes(75), result.PredictedRestoreAtUtc);
        Assert.Equal(result, await handler.Handle(new("org", "env", order.Id), default));
        var changedConfiguration = new GetMaintenanceRestorePredictionQueryHandler(db,
            Options.Create(new MaintenanceRestorePredictionOptions { DefaultDowntimeMinutes = 90 }));
        var changed = await changedConfiguration.Handle(new("org", "env", order.Id), default);
        Assert.Equal("configuration-default", changed.Source);
        Assert.Equal(90m, changed.SelectedDowntimeMinutes);
        Assert.Equal(from.AddMinutes(90), changed.PredictedRestoreAtUtc);
        Assert.NotEqual(result.SourceVersion, changed.SourceVersion);
        Assert.Null(result.ExplicitExpectedRestoreAtUtc);
        var (v1, v2) = AssetUnavailableV2IntegrationEventPublisher.Build(
            Assert.Single(order.GetDomainEvents().OfType<AssetUnavailableByReasonCodeDomainEvent>()));
        Assert.Null(v1.Payload.ExpectedRestoreAtUtc);
        Assert.Null(v2.Payload.ExpectedRestoreAtUtc);
        Assert.Null(order.ExpectedRestoreAtUtc);
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
