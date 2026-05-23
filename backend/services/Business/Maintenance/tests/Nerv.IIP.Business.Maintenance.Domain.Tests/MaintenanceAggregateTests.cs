using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.Tests;

public sealed class MaintenanceAggregateTests
{
    [Fact]
    public void Work_order_from_alarm_can_mark_asset_unavailable_and_complete_with_downtime_attribution()
    {
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "over temperature");

        workOrder.Complete("replaced sensor", "equipment-failure", 45, [new SparePartLineDraft("SKU-SP-001", 1m, "pcs")]);

        Assert.Equal(MaintenanceWorkOrderStatus.Completed, workOrder.Status);
        Assert.Equal("equipment-failure", workOrder.DowntimeReasonCode);
        Assert.Equal(45, workOrder.DowntimeMinutes);
        Assert.Collection(
            workOrder.GetDomainEvents(),
            x => Assert.IsType<MaintenanceWorkOrderOpenedDomainEvent>(x),
            x => Assert.IsType<AssetUnavailableDomainEvent>(x),
            x => Assert.IsType<MaintenanceWorkOrderCompletedDomainEvent>(x),
            x => Assert.IsType<AssetRestoredDomainEvent>(x));
    }

    [Fact]
    public void Manual_work_order_completion_does_not_emit_asset_restored_when_asset_was_not_unavailable()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");

        workOrder.Complete("fixed", "minor-stop", 5, []);

        Assert.Equal(MaintenanceWorkOrderStatus.Completed, workOrder.Status);
        Assert.DoesNotContain(workOrder.GetDomainEvents(), x => x is AssetRestoredDomainEvent);
    }

    [Theory]
    [InlineData("", "equipment-failure", 10)]
    [InlineData("fixed", "", 10)]
    [InlineData("fixed", "equipment-failure", 0)]
    public void Completion_requires_result_reason_and_positive_downtime(string result, string reasonCode, int downtimeMinutes)
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");

        Assert.ThrowsAny<Exception>(() => workOrder.Complete(result, reasonCode, downtimeMinutes, []));
    }

    [Fact]
    public void Spare_part_quantities_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SparePartLine.Create(new SparePartLineDraft("SKU-SP-001", 0m)));
    }

    [Fact]
    public void Completed_work_order_is_terminal()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.Complete("fixed", "minor-stop", 5, []);

        Assert.Throws<InvalidOperationException>(() => workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "again"));
    }

    [Fact]
    public void Maintenance_plan_requires_explicit_interval()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "", DateOnly.FromDateTime(DateTime.UtcNow), "maintenance"));
    }

    [Fact]
    public void Inspection_must_reference_a_plan_or_work_order()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenanceInspection.Record("org-001", "env-dev", null, null, "operator-001", "passed", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Plan_inspection_and_downtime_reason_capture_owned_facts_without_external_ownership()
    {
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", DateOnly.FromDateTime(DateTime.UtcNow), "maintenance");
        var inspection = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "operator-001", "passed", DateTimeOffset.UtcNow);
        var reason = DowntimeReason.Create("org-001", "env-dev", "equipment-failure", "Equipment failure");

        Assert.Equal("P7D", plan.Interval);
        Assert.Equal(plan.Id, inspection.PlanId);
        Assert.Equal("equipment-failure", reason.ReasonCode);
        Assert.IsType<MaintenancePlanCreatedDomainEvent>(plan.GetDomainEvents().Single());
        Assert.IsType<MaintenanceInspectionRecordedDomainEvent>(inspection.GetDomainEvents().Single());
    }
}
