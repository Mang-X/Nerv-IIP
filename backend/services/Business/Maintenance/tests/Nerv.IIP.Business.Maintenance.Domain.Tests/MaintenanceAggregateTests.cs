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
            x => Assert.IsType<MaintenanceSparePartIssuedDomainEvent>(x),
            x => Assert.IsType<MaintenanceWorkOrderCompletedDomainEvent>(x),
            x => Assert.IsType<AssetRestoredDomainEvent>(x));
    }

    [Fact]
    public void Work_order_from_alarm_can_be_marked_alarm_cleared_without_auto_completion()
    {
        var clearedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");

        workOrder.MarkAlarmCleared(clearedAtUtc);
        workOrder.MarkAlarmCleared(clearedAtUtc.AddMinutes(5));

        Assert.True(workOrder.AlarmCleared);
        Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
        Assert.Single(workOrder.GetDomainEvents().OfType<MaintenanceWorkOrderAlarmClearedDomainEvent>());
    }

    [Fact]
    public void Manual_work_order_completion_does_not_emit_asset_restored_when_asset_was_not_unavailable()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");

        workOrder.Complete("fixed", "minor-stop", 5, []);

        Assert.Equal(MaintenanceWorkOrderStatus.Completed, workOrder.Status);
        Assert.DoesNotContain(workOrder.GetDomainEvents(), x => x is AssetRestoredDomainEvent);
    }

    [Fact]
    public void Repair_start_cannot_be_before_work_order_opened_time()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");

        Assert.Throws<ArgumentOutOfRangeException>(() => workOrder.MarkRepairStarted(workOrder.OpenedAtUtc.AddMinutes(-1)));
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
    public void Maintenance_plan_tracks_next_due_date_for_iso_day_interval()
    {
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", new DateOnly(2026, 6, 1), "maintenance");

        Assert.Equal(new DateOnly(2026, 6, 1), plan.NextDueOn);
        Assert.True(plan.IsDueOn(new DateOnly(2026, 6, 8)));

        plan.MarkGenerated(new DateOnly(2026, 6, 8));

        Assert.Equal(new DateOnly(2026, 6, 8), plan.LastGeneratedOn);
        Assert.Equal(new DateOnly(2026, 6, 15), plan.NextDueOn);
        Assert.False(plan.IsDueOn(new DateOnly(2026, 6, 14)));
    }

    [Fact]
    public void Maintenance_plan_catches_up_missed_periods_without_phase_drift()
    {
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", new DateOnly(2026, 6, 1), "maintenance");

        var dueDates = plan.ConsumeDueDates(new DateOnly(2026, 6, 22));

        Assert.Equal(
            [
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 8),
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 22),
            ],
            dueDates);
        Assert.Equal(new DateOnly(2026, 6, 22), plan.LastGeneratedOn);
        Assert.Equal(new DateOnly(2026, 6, 29), plan.NextDueOn);
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
        var reason = DowntimeReason.Create("org-001", "env-dev", "equipment-failure", "Equipment failure", "breakdown", "equipment-failure");

        Assert.Equal("P7D", plan.Interval);
        Assert.Equal(plan.Id, inspection.PlanId);
        Assert.Equal("equipment-failure", reason.ReasonCode);
        Assert.Equal("breakdown", reason.ReasonCategory);
        Assert.Equal("equipment-failure", reason.LossCategory);
        Assert.IsType<MaintenancePlanCreatedDomainEvent>(plan.GetDomainEvents().Single());
        Assert.IsType<MaintenanceInspectionRecordedDomainEvent>(inspection.GetDomainEvents().Single());
    }

    [Fact]
    public void Inspection_measurement_lines_evaluate_value_against_acceptable_range()
    {
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "bearing-temperature", "P1D", new DateOnly(2026, 7, 1), "maintenance");

        var inspection = MaintenanceInspection.RecordForPlan(
            "org-001",
            "env-dev",
            plan.Id,
            "operator-001",
            "passed",
            DateTimeOffset.UtcNow,
            [
                new MaintenanceInspectionMeasurementDraft("bearing-temperature", 65m, "C", 0m, 70m),
                new MaintenanceInspectionMeasurementDraft("noise", 82m, "dB", null, 80m),
            ]);

        Assert.Collection(
            inspection.Measurements.OrderBy(x => x.CharacteristicCode),
            line =>
            {
                Assert.Equal("bearing-temperature", line.CharacteristicCode);
                Assert.True(line.IsWithinSpec);
            },
            line =>
            {
                Assert.Equal("noise", line.CharacteristicCode);
                Assert.False(line.IsWithinSpec);
            });
    }

    [Fact]
    public void Work_order_completion_records_technician_labor_and_cost_fields()
    {
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "normal",
            "operator-001",
            assignedTechnicianUserId: "worker-001",
            estimatedLaborMinutes: 90);

        workOrder.Complete(
            "fixed",
            "minor-stop",
            5,
            [],
            actualLaborMinutes: 75,
            sparePartCostAmount: 120.50m,
            externalServiceCostAmount: 35m,
            costCurrencyCode: "CNY");

        Assert.Equal("worker-001", workOrder.AssignedTechnicianUserId);
        Assert.Equal(90, workOrder.EstimatedLaborMinutes);
        Assert.Equal(75, workOrder.ActualLaborMinutes);
        Assert.Equal(120.50m, workOrder.SparePartCostAmount);
        Assert.Equal(35m, workOrder.ExternalServiceCostAmount);
        Assert.Equal("CNY", workOrder.CostCurrencyCode);
    }
}
