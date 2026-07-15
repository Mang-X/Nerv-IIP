using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Auth;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceEndpointContractTests
{
    [Fact]
    public void Maintenance_endpoints_expose_issue_130_routes_permissions_policies_and_operation_ids()
    {
        var contracts = MaintenanceEndpointContracts.All.ToArray();

        Assert.Equal(20, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders/{workOrderId}/repair-started" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "startMaintenanceRepair");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders/{workOrderId}/complete" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "completeMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceWorkOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "createMaintenancePlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "listMaintenancePlans");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/inspections" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "recordMaintenanceInspection");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/inspections" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "listMaintenanceInspections");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/inspection-measurements/trends" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "queryMaintenanceInspectionMeasurementTrend");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/reliability/summary" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "queryMaintenanceReliabilitySummary");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/spare-parts" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceSpareParts");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/spare-parts" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceSparePart");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/downtime-reasons" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceDowntimeReason");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/downtime-reasons" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceDowntimeReasons");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/maintenance/downtime-reasons/{reasonCode}" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "updateMaintenanceDowntimeReason");
        Assert.Contains(contracts, x => x.HttpMethod == "DELETE" && x.Route == "/api/business/v1/maintenance/downtime-reasons/{reasonCode}" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "deleteMaintenanceDowntimeReason");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/assets/{deviceAssetId}/availability-windows" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "getMaintenanceAssetAvailabilityWindows");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/availability-windows" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "queryMaintenanceAvailabilityWindows");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/plans/generate-due" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "generateDueMaintenanceWorkOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/assets/{deviceAssetId}/reliability" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "queryMaintenanceAssetReliability");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
    }

    [Fact]
    public void Maintenance_command_validators_reject_decimal_values_outside_database_precision()
    {
        const decimal maxNumeric18Scale6 = 999_999_999_999.999999m;
        const decimal tooLargeForNumeric18Scale6 = 1_000_000_000_000m;

        var validInspection = new RecordMaintenanceInspectionCommandValidator().Validate(
            new RecordMaintenanceInspectionCommand(
                "org-001",
                "env-dev",
                new MaintenancePlanId(Guid.CreateVersion7()),
                null,
                "inspector-001",
                "passed",
                DateTimeOffset.UtcNow,
                [new MaintenanceInspectionMeasurementInput("bearing-temperature", maxNumeric18Scale6, "C", -maxNumeric18Scale6, maxNumeric18Scale6)]));

        var invalidInspection = new RecordMaintenanceInspectionCommandValidator().Validate(
            new RecordMaintenanceInspectionCommand(
                "org-001",
                "env-dev",
                new MaintenancePlanId(Guid.CreateVersion7()),
                null,
                "inspector-001",
                "passed",
                DateTimeOffset.UtcNow,
                [new MaintenanceInspectionMeasurementInput("bearing-temperature", tooLargeForNumeric18Scale6, "C", -tooLargeForNumeric18Scale6, tooLargeForNumeric18Scale6)]));

        var invalidCompletion = new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "fixed",
                "equipment-failure",
                10,
                [new MaintenanceSparePartInput("SKU-BEARING", tooLargeForNumeric18Scale6, "EA")],
                SparePartCostAmount: tooLargeForNumeric18Scale6,
                ExternalServiceCostAmount: tooLargeForNumeric18Scale6,
                CostCurrencyCode: "CNY"));

        Assert.True(validInspection.IsValid);
        Assert.Contains(invalidInspection.Errors, x => x.ErrorMessage == "Measured value must fit numeric(18,6).");
        Assert.Contains(invalidInspection.Errors, x => x.ErrorMessage == "Lower spec limit must fit numeric(18,6).");
        Assert.Contains(invalidInspection.Errors, x => x.ErrorMessage == "Upper spec limit must fit numeric(18,6).");
        Assert.Contains(invalidCompletion.Errors, x => x.ErrorMessage == "Spare part cost amount must fit numeric(18,6).");
        Assert.Contains(invalidCompletion.Errors, x => x.ErrorMessage == "External service cost amount must fit numeric(18,6).");
        Assert.Contains(invalidCompletion.Errors, x => x.ErrorMessage == "Spare part quantity must fit numeric(18,6).");
    }

    [Fact]
    public void Completion_validator_rejects_actual_technician_references_over_150_characters()
    {
        var result = new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "fixed",
                "equipment-failure",
                10,
                [],
                ActualTechnicianUserId: new string('x', 151)));

        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CompleteMaintenanceWorkOrderCommand.ActualTechnicianUserId));
    }

    [Fact]
    public async Task Maintenance_work_order_and_plan_lists_return_skip_take_and_total()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-001", "normal", "operator-001"));
        dbContext.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-002", "high", "operator-001"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-001", "PM-001", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-002", "PM-002", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        await dbContext.SaveChangesAsync();

        var workOrders = await new ListMaintenanceWorkOrdersQueryHandler(dbContext).Handle(
            new ListMaintenanceWorkOrdersQuery("org-001", "env-dev", 1, 1),
            CancellationToken.None);
        var plans = await new ListMaintenancePlansQueryHandler(dbContext).Handle(
            new ListMaintenancePlansQuery("org-001", "env-dev", 1, 1),
            CancellationToken.None);

        Assert.Equal(1, workOrders.Skip);
        Assert.Equal(1, workOrders.Take);
        Assert.Equal(2, workOrders.Total);
        Assert.Single(workOrders.Items);
        Assert.Equal(1, plans.Skip);
        Assert.Equal(1, plans.Take);
        Assert.Equal(2, plans.Total);
        Assert.Single(plans.Items);
    }

    [Fact]
    public async Task Maintenance_plan_list_projects_trigger_mode_and_next_due_fields()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CAL", "PM-CAL", "P30D", new DateOnly(2026, 6, 1), "maintenance"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-RUN", "PM-RUN", "P30D", new DateOnly(2026, 6, 1), "maintenance", runtimeHourInterval: 1000m));
        await dbContext.SaveChangesAsync();

        var plans = await new ListMaintenancePlansQueryHandler(dbContext).Handle(
            new ListMaintenancePlansQuery("org-001", "env-dev", 0, 100),
            CancellationToken.None);

        var calendar = plans.Items.Single(x => x.PlanCode == "PM-CAL");
        Assert.Null(calendar.RuntimeHourInterval);
        Assert.Null(calendar.NextDueRuntimeHours);
        Assert.Equal(new DateOnly(2026, 6, 1), calendar.NextDueOn);
        Assert.Equal(0m, calendar.LastGeneratedRuntimeHours);

        var runtime = plans.Items.Single(x => x.PlanCode == "PM-RUN");
        Assert.Equal(1000m, runtime.RuntimeHourInterval);
        Assert.Equal(1000m, runtime.NextDueRuntimeHours);
        Assert.Equal(0m, runtime.LastGeneratedRuntimeHours);
    }

    [Fact]
    public async Task Maintenance_inspection_list_returns_paged_inspection_facts()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "passed", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)));
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-002", "failed", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var result = await new ListMaintenanceInspectionsQueryHandler(dbContext).Handle(
            new ListMaintenanceInspectionsQuery("org-001", "env-dev", 0, 1),
            CancellationToken.None);

        Assert.Equal(0, result.Skip);
        Assert.Equal(1, result.Take);
        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("inspector-002", item.Inspector);
        Assert.Equal("failed", item.Result);
        Assert.Equal(plan.Id, item.PlanId);
    }

    [Fact]
    public async Task Maintenance_inspection_records_measurement_values_and_trend_query_returns_device_characteristic_history()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-MEASURE", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        await new RecordMaintenanceInspectionCommandHandler(dbContext).Handle(
            new RecordMaintenanceInspectionCommand(
                "org-001",
                "env-dev",
                plan.Id,
                null,
                "inspector-001",
                "passed",
                inspectedAtUtc,
                [
                    new MaintenanceInspectionMeasurementInput("bearing-temperature", 65m, "C", 0m, 70m),
                    new MaintenanceInspectionMeasurementInput("noise", 82m, "dB", null, 80m),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var listed = await new ListMaintenanceInspectionsQueryHandler(dbContext).Handle(
            new ListMaintenanceInspectionsQuery("org-001", "env-dev"),
            CancellationToken.None);
        var trend = await new QueryMaintenanceInspectionMeasurementTrendQueryHandler(dbContext).Handle(
            new QueryMaintenanceInspectionMeasurementTrendQuery(
                "org-001",
                "env-dev",
                "DEV-CNC-01",
                "bearing-temperature",
                inspectedAtUtc.AddMinutes(-1),
                inspectedAtUtc.AddMinutes(1)),
            CancellationToken.None);

        var inspection = Assert.Single(listed.Items);
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

        var trendItem = Assert.Single(trend.Items);
        Assert.Equal(65m, trendItem.MeasuredValue);
        Assert.Equal("C", trendItem.UomCode);
        Assert.True(trendItem.IsWithinSpec);
    }

    [Fact]
    public async Task Maintenance_inspection_measurement_trend_matches_work_order_device_when_plan_device_differs()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-MEASURE", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-02", "normal", "operator-001");
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        await new RecordMaintenanceInspectionCommandHandler(dbContext).Handle(
            new RecordMaintenanceInspectionCommand(
                "org-001",
                "env-dev",
                plan.Id,
                workOrder.Id,
                "inspector-001",
                "passed",
                inspectedAtUtc,
                [new MaintenanceInspectionMeasurementInput("bearing-temperature", 65m, "C", 0m, 70m)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var trend = await new QueryMaintenanceInspectionMeasurementTrendQueryHandler(dbContext).Handle(
            new QueryMaintenanceInspectionMeasurementTrendQuery(
                "org-001",
                "env-dev",
                "DEV-CNC-02",
                "bearing-temperature",
                inspectedAtUtc.AddMinutes(-1),
                inspectedAtUtc.AddMinutes(1)),
            CancellationToken.None);

        var item = Assert.Single(trend.Items);
        Assert.Equal(workOrder.Id, item.WorkOrderId);
        Assert.Equal(plan.Id, item.PlanId);
    }

    [Theory]
    [InlineData("NG")]
    [InlineData("Failed")]
    [InlineData("不合格")]
    public async Task Failed_maintenance_inspection_opens_traceable_inspection_work_order(string result)
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-NG", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        var inspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", result, new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var inspection = await dbContext.MaintenanceInspections.SingleAsync();
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal(inspection.Id, inspectionId);
        Assert.Equal("DEV-CNC-01", workOrder.DeviceAssetId);
        Assert.Equal("inspection", GetWorkOrderStringProperty(workOrder, "SourceType"));
        Assert.Equal(inspection.Id.ToString(), GetWorkOrderStringProperty(workOrder, "SourceReferenceId"));
        Assert.Contains(result, GetWorkOrderStringProperty(workOrder, "DiagnosticDescription"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("maintenanceInspection", workOrder.OpenedBy);
        Assert.Equal("high", workOrder.Priority);
    }

    [Fact]
    public async Task Failed_maintenance_inspection_for_work_order_opens_traceable_inspection_work_order()
    {
        await using var dbContext = CreateDbContext();
        var sourceWorkOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-02", "normal", "operator-001");
        dbContext.MaintenanceWorkOrders.Add(sourceWorkOrder);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        var inspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", null, sourceWorkOrder.Id, "inspector-001", "NG", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var generatedWorkOrder = await dbContext.MaintenanceWorkOrders.SingleAsync(x => x.Id != sourceWorkOrder.Id);
        Assert.Equal("DEV-CNC-02", generatedWorkOrder.DeviceAssetId);
        Assert.Equal("inspection", GetWorkOrderStringProperty(generatedWorkOrder, "SourceType"));
        Assert.Equal(inspectionId.ToString(), GetWorkOrderStringProperty(generatedWorkOrder, "SourceReferenceId"));
    }

    [Fact]
    public async Task Passed_maintenance_inspection_does_not_open_work_order()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-PASS", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "passed", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.MaintenanceInspections.CountAsync());
        Assert.Equal(0, await dbContext.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task Failed_maintenance_inspection_replay_reuses_inspection_and_work_order()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-REPLAY", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);
        var command = new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "NG", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));

        var firstInspectionId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var replayInspectionId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(firstInspectionId, replayInspectionId);
        Assert.Equal(1, await dbContext.MaintenanceInspections.CountAsync());
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal("inspection", GetWorkOrderStringProperty(workOrder, "SourceType"));
        Assert.Equal(firstInspectionId.ToString(), GetWorkOrderStringProperty(workOrder, "SourceReferenceId"));
    }

    [Fact]
    public async Task Failed_maintenance_inspection_replay_normalizes_result_for_idempotency()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-NORMALIZE", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        var firstInspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", " NG ", inspectedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var replayInspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "ng", inspectedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(firstInspectionId, replayInspectionId);
        Assert.Equal(1, await dbContext.MaintenanceInspections.CountAsync());
        Assert.Equal(1, await dbContext.MaintenanceWorkOrders.CountAsync());
        var inspection = await dbContext.MaintenanceInspections.SingleAsync();
        Assert.Equal("ng", inspection.Result);
    }

    [Fact]
    public async Task Failed_maintenance_inspection_replay_tolerates_duplicate_historical_inspection_facts()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-HISTORY", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var first = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        var duplicate = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenanceInspections.AddRange(first, duplicate);
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        var replayInspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "NG", inspectedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Contains(replayInspectionId, new[] { first.Id, duplicate.Id });
        Assert.Equal(2, await dbContext.MaintenanceInspections.CountAsync());
        Assert.Equal(1, await dbContext.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task Failed_maintenance_inspection_replay_reuses_existing_work_order_for_duplicate_historical_fact()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-HISTORY-WO", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var first = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        var duplicateWithWorkOrder = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenanceInspections.AddRange(first, duplicateWithWorkOrder);
        dbContext.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenFromInspection("org-001", "env-dev", "DEV-CNC-01", duplicateWithWorkOrder.Id, duplicateWithWorkOrder.Result));
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        var replayInspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "NG", inspectedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(duplicateWithWorkOrder.Id, replayInspectionId);
        Assert.Equal(2, await dbContext.MaintenanceInspections.CountAsync());
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal(duplicateWithWorkOrder.Id.ToString(), GetWorkOrderStringProperty(workOrder, "SourceReferenceId"));
    }

    [Fact]
    public async Task Failed_maintenance_inspection_replay_ignores_cross_tenant_work_order_for_duplicate_historical_fact()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-HISTORY-TENANT", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        var inspectedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var first = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        var duplicateWithCrossTenantWorkOrder = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "ng", inspectedAtUtc);
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenanceInspections.AddRange(first, duplicateWithCrossTenantWorkOrder);
        dbContext.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenFromInspection("org-002", "env-dev", "DEV-CNC-99", duplicateWithCrossTenantWorkOrder.Id, duplicateWithCrossTenantWorkOrder.Result));
        await dbContext.SaveChangesAsync();
        var handler = new RecordMaintenanceInspectionCommandHandler(dbContext);

        var replayInspectionId = await handler.Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "NG", inspectedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(first.Id, replayInspectionId);
        Assert.Equal(2, await dbContext.MaintenanceWorkOrders.CountAsync());
        var generatedWorkOrder = await dbContext.MaintenanceWorkOrders.SingleAsync(x => x.OrganizationId == "org-001");
        Assert.Equal(first.Id.ToString(), GetWorkOrderStringProperty(generatedWorkOrder, "SourceReferenceId"));
    }

    [Fact]
    public async Task Maintenance_spare_part_create_and_list_use_work_order_scope()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "operator-001");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var sparePartId = await new CreateMaintenanceSparePartCommandHandler(dbContext).Handle(
            new CreateMaintenanceSparePartCommand("org-001", "env-dev", workOrder.Id, "SPARE-001", 2m, "EA"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var result = await new ListMaintenanceSparePartsQueryHandler(dbContext).Handle(
            new ListMaintenanceSparePartsQuery("org-001", "env-dev", 0, 10),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal(sparePartId, item.SparePartLineId);
        Assert.Equal(workOrder.Id, item.WorkOrderId);
        Assert.Equal("SPARE-001", item.SkuCode);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal("EA", item.UomCode);
        Assert.Equal("DEV-CNC-01", item.DeviceAssetId);
    }

    [Fact]
    public async Task Maintenance_spare_part_create_rejects_completed_work_order_with_known_exception()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "operator-001");
        workOrder.Complete("restored", "mechanical", 15, []);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateMaintenanceSparePartCommandHandler(dbContext).Handle(
                new CreateMaintenanceSparePartCommand("org-001", "env-dev", workOrder.Id, "SPARE-001", 2m, "EA"),
                CancellationToken.None));

        Assert.Equal("Completed maintenance work orders are immutable.", exception.Message);
    }

    [Fact]
    public async Task Asset_availability_query_returns_active_alarm_and_planned_maintenance_windows()
    {
        await using var dbContext = CreateDbContext();
        var queryStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var queryEnd = queryStart.AddHours(4);
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        workOrder.MarkAssetUnavailable(queryStart.AddMinutes(15), "spindle alarm");
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-001", "P7D", DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance", queryStart.AddHours(1), queryStart.AddHours(2));
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new GetMaintenanceAssetAvailabilityWindowsQueryHandler(dbContext);

        var response = await handler.Handle(new GetMaintenanceAssetAvailabilityWindowsQuery("org-001", "env-dev", "DEV-CNC-01", queryStart, queryEnd), CancellationToken.None);

        Assert.Equal(1, response.ContractVersion);
        Assert.Equal(queryStart, response.QueryWindowStartUtc);
        Assert.Equal(queryEnd, response.QueryWindowEndUtc);
        Assert.Contains(response.Items, x =>
            x.DeviceAssetId == "DEV-CNC-01"
            && x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm
            && x.SourceType == EquipmentRuntimeSourceType.Alarm
            && x.SourceReferenceId == "alarm-001"
            && x.StartUtc == queryStart.AddMinutes(15)
            && x.EndUtc == queryEnd);
        Assert.Contains(response.Items, x =>
            x.DeviceAssetId == "DEV-CNC-01"
            && x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow
            && x.SourceType == EquipmentRuntimeSourceType.MaintenanceWindow
            && x.SourceReferenceId == "PM-001"
            && x.StartUtc == queryStart.AddHours(1)
            && x.EndUtc == queryStart.AddHours(2));
    }

    [Fact]
    public async Task Availability_query_clips_completed_downtime_and_failed_inspection_to_query_window()
    {
        await using var dbContext = CreateDbContext();
        var queryStart = DateTimeOffset.UtcNow.AddMinutes(-30);
        var queryEnd = DateTimeOffset.UtcNow.AddMinutes(30);
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        workOrder.MarkAssetUnavailable(queryStart.AddHours(-1), "manual downtime");
        workOrder.Complete("restored", "mechanical", 45, []);
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT", "P7D", DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        var failPlan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-FAIL", "P7D", DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        var inspection = MaintenanceInspection.RecordForPlan("org-001", "env-dev", plan.Id, "inspector-001", "failed", queryStart.AddMinutes(10));
        var failInspection = MaintenanceInspection.RecordForPlan("org-001", "env-dev", failPlan.Id, "inspector-001", "fail", queryStart.AddMinutes(20));
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.MaintenancePlans.Add(plan);
        dbContext.MaintenancePlans.Add(failPlan);
        dbContext.MaintenanceInspections.Add(inspection);
        dbContext.MaintenanceInspections.Add(failInspection);
        await dbContext.SaveChangesAsync();
        var handler = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
        var request = new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", queryStart, queryEnd, ["DEV-CNC-01"], null);

        var response = await handler.Handle(new QueryMaintenanceAvailabilityWindowsQuery(request), CancellationToken.None);

        var downtime = Assert.Single(response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.Downtime);
        Assert.Equal(queryStart, downtime.StartUtc);
        Assert.True(downtime.EndUtc <= queryEnd);
        Assert.Equal(EquipmentRuntimeSourceType.Downtime, downtime.SourceType);

        var inspectionRequiredWindows = response.Items
            .Where(x => x.ReasonCode == EquipmentRuntimeReasonCodes.InspectionRequired)
            .OrderBy(x => x.StartUtc)
            .ToArray();
        Assert.Equal(2, inspectionRequiredWindows.Length);
        var inspectionRequired = inspectionRequiredWindows[0];
        Assert.Equal(queryStart.AddMinutes(10), inspectionRequired.StartUtc);
        Assert.Equal(queryEnd, inspectionRequired.EndUtc);
        Assert.Equal(EquipmentRuntimeSourceType.Inspection, inspectionRequired.SourceType);
        Assert.Equal(queryStart.AddMinutes(20), inspectionRequiredWindows[1].StartUtc);
    }

    [Fact]
    public async Task Availability_query_rejects_invalid_window_and_unresolved_work_centers()
    {
        await using var dbContext = CreateDbContext();
        var handler = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
        var start = DateTimeOffset.UtcNow;

        var invalidWindow = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", start, start, ["DEV-CNC-01"], null)), CancellationToken.None));
        Assert.Equal("Maintenance availability window end must be after start.", invalidWindow.Message);

        var unresolvedWorkCenters = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", start, start.AddHours(1), null, ["WC-001"])), CancellationToken.None));
        Assert.Equal("Maintenance availability windows require explicit device asset ids; work center resolution is not available in P0.", unresolvedWorkCenters.Message);
    }

    [Fact]
    public async Task Availability_query_uses_latest_inspection_result_per_reference()
    {
        await using var dbContext = CreateDbContext();
        var queryStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var queryEnd = queryStart.AddHours(4);
        var passedPlan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-PASS", "P7D", DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        var failedPlan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-FAIL", "P7D", DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        dbContext.MaintenancePlans.AddRange(passedPlan, failedPlan);
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", passedPlan.Id, "inspector-001", "failed", queryStart.AddMinutes(10)));
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", passedPlan.Id, "inspector-001", "pass", queryStart.AddMinutes(20)));
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", failedPlan.Id, "inspector-001", "failed", queryStart.AddMinutes(30)));
        dbContext.MaintenanceInspections.Add(MaintenanceInspection.RecordForPlan("org-001", "env-dev", failedPlan.Id, "inspector-001", "blocked", queryStart.AddMinutes(40)));
        await dbContext.SaveChangesAsync();
        var handler = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);

        var response = await handler.Handle(new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", queryStart, queryEnd, ["DEV-CNC-01"], null)), CancellationToken.None);

        var inspectionRequired = Assert.Single(response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.InspectionRequired);
        Assert.Equal(queryStart.AddMinutes(40), inspectionRequired.StartUtc);
        Assert.Equal(queryEnd, inspectionRequired.EndUtc);
        Assert.Equal(EquipmentRuntimeSourceType.Inspection, inspectionRequired.SourceType);
    }

    [Fact]
    public async Task Maintenance_plan_runtime_windows_require_both_bounds()
    {
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        await using var dbContext = CreateDbContext();
        var handler = new CreateMaintenancePlanCommandHandler(dbContext);

        Assert.Throws<ArgumentException>(() =>
            MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-PARTIAL-DOMAIN", "P7D", DateOnly.FromDateTime(start.UtcDateTime), "maintenance", start, null));
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new CreateMaintenancePlanCommand("org-001", "env-dev", "DEV-CNC-01", "PM-PARTIAL-COMMAND", "P7D", DateOnly.FromDateTime(start.UtcDateTime), "maintenance", start, null), CancellationToken.None));
        Assert.Equal("Maintenance availability window start and end must be provided together.", exception.Message);
    }

    [Fact]
    public async Task Maintenance_plan_create_allocates_code_when_omitted()
    {
        await using var dbContext = CreateDbContext();
        var handler = new CreateMaintenancePlanCommandHandler(dbContext, new MaintenanceCodingService());

        var id = await handler.Handle(
            new CreateMaintenancePlanCommand(
                "org-001",
                "env-dev",
                "DEV-CNC-01",
                null,
                "P7D",
                new DateOnly(2026, 6, 1),
                "maintenance",
                null,
                null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var plan = await dbContext.MaintenancePlans.SingleAsync(x => x.Id == id);
        Assert.StartsWith("PM-", plan.PlanCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maintenance_plan_create_replays_same_plan_for_same_idempotency_key()
    {
        await using var dbContext = CreateDbContext();
        var handler = new CreateMaintenancePlanCommandHandler(dbContext, new MaintenanceCodingService());

        var first = await handler.Handle(
            new CreateMaintenancePlanCommand(
                "org-001",
                "env-dev",
                "DEV-CNC-01",
                null,
                "P7D",
                new DateOnly(2026, 6, 1),
                "maintenance",
                null,
                null,
                "maintenance-plan-create-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var replay = await handler.Handle(
            new CreateMaintenancePlanCommand(
                "org-001",
                "env-dev",
                "DEV-CNC-01",
                null,
                "P7D",
                new DateOnly(2026, 6, 1),
                "maintenance",
                null,
                null,
                "maintenance-plan-create-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(first, replay);
        Assert.Equal(1, await dbContext.MaintenancePlans.CountAsync());
    }

    [Fact]
    public async Task Maintenance_plan_runtime_windows_are_normalized_to_utc()
    {
        await using var dbContext = CreateDbContext();
        var handler = new CreateMaintenancePlanCommandHandler(dbContext);
        var localStart = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(8));
        var localEnd = localStart.AddHours(1);
        await handler.Handle(new CreateMaintenancePlanCommand("org-001", "env-dev", "DEV-CNC-01", "PM-UTC", "P7D", DateOnly.FromDateTime(localStart.UtcDateTime), "maintenance", localStart, localEnd), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var availability = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
        var queryStart = localStart.ToUniversalTime().AddHours(-1);
        var queryEnd = localEnd.ToUniversalTime().AddHours(1);

        var response = await availability.Handle(new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", queryStart, queryEnd, ["DEV-CNC-01"], null)), CancellationToken.None);

        var window = Assert.Single(response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow);
        Assert.Equal(localStart.ToUniversalTime(), window.StartUtc);
        Assert.Equal(TimeSpan.Zero, window.StartUtc.Offset);
        Assert.Equal(localEnd.ToUniversalTime(), window.EndUtc);
        Assert.Equal(TimeSpan.Zero, window.EndUtc.Offset);
    }

    [Fact]
    public async Task Generate_due_maintenance_work_orders_creates_idempotent_catch_up_work_orders_per_due_plan()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-WEEKLY", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-FUTURE", "P7D", new DateOnly(2026, 6, 10), "maintenance"));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext);

        var first = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, first.GeneratedCount);
        Assert.Equal(0, second.GeneratedCount);
        var workOrders = await dbContext.MaintenanceWorkOrders.ToArrayAsync();
        Assert.Equal(2, workOrders.Length);
        Assert.All(workOrders, workOrder =>
        {
            Assert.Equal("DEV-CNC-01", workOrder.DeviceAssetId);
            Assert.Equal("PM-WEEKLY", workOrder.SourcePlanCode);
        });
        var plan = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-WEEKLY");
        Assert.Equal(new DateOnly(2026, 6, 8), plan.LastGeneratedOn);
        Assert.Equal(new DateOnly(2026, 6, 15), plan.NextDueOn);
    }

    [Fact]
    public async Task Generate_due_maintenance_work_orders_skips_paused_plan_without_advancing_due_state()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-PAUSED", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        plan.Pause();
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext);

        var result = await handler.Handle(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, result.GeneratedCount);
        Assert.Empty(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal(new DateOnly(2026, 6, 1), plan.NextDueOn);
        Assert.Null(plan.LastGeneratedOn);
    }

    [Fact]
    public async Task Generate_due_maintenance_work_orders_catches_up_missed_periods_and_usage_thresholds()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-WEEKLY", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-RUNTIME", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 100m));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            dbContext,
            new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(125m, AssetRuntimeSources.Oee, HasRuntimeSamples: true)));

        var result = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 22), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(5, result.GeneratedCount);
        Assert.Equal(4, await dbContext.MaintenanceWorkOrders.CountAsync(x => x.DeviceAssetId == "DEV-CNC-01"));
        Assert.Equal(1, await dbContext.MaintenanceWorkOrders.CountAsync(x => x.DeviceAssetId == "DEV-CNC-02"));
        var weekly = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-WEEKLY");
        var runtime = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME");
        Assert.Equal(new DateOnly(2026, 6, 29), weekly.NextDueOn);
        Assert.Equal(125m, runtime.LastGeneratedRuntimeHours);
        Assert.Equal(200m, runtime.NextDueRuntimeHours);
    }

    [Fact]
    public async Task Generate_due_maintenance_work_orders_caps_backlog_catch_up_per_run()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-DAILY", "P1D", new DateOnly(2025, 1, 1), "maintenance"));
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-RUNTIME", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 1m));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            dbContext,
            new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(1000m, AssetRuntimeSources.Oee, HasRuntimeSamples: true)));

        var result = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 1), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(MaintenancePlan.MaxCatchUpOccurrencesPerRun * 2, result.GeneratedCount);
        Assert.Equal(MaintenancePlan.MaxCatchUpOccurrencesPerRun, await dbContext.MaintenanceWorkOrders.CountAsync(x => x.DeviceAssetId == "DEV-CNC-01"));
        Assert.Equal(MaintenancePlan.MaxCatchUpOccurrencesPerRun, await dbContext.MaintenanceWorkOrders.CountAsync(x => x.DeviceAssetId == "DEV-CNC-02"));
        var daily = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-DAILY");
        var runtime = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME");
        Assert.Equal(new DateOnly(2025, 1, 31), daily.LastGeneratedOn);
        Assert.Equal(new DateOnly(2025, 2, 1), daily.NextDueOn);
        Assert.Equal(MaintenancePlan.MaxCatchUpOccurrencesPerRun + 1, runtime.NextDueRuntimeHours);
    }

    [Fact]
    public async Task Runtime_catch_up_work_orders_reference_crossed_thresholds()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-RUNTIME-TRACE", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 100m));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            dbContext,
            new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(350m, AssetRuntimeSources.Oee, HasRuntimeSamples: true)));

        var result = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 22), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(3, result.GeneratedCount);
        var sourceReferences = await dbContext.MaintenanceWorkOrders
            .OrderBy(x => x.SourceReferenceId)
            .Select(x => x.SourceReferenceId!)
            .ToArrayAsync();
        Assert.Equal(
            [
                "PM-RUNTIME-TRACE:runtime:100:1",
                "PM-RUNTIME-TRACE:runtime:200:2",
                "PM-RUNTIME-TRACE:runtime:300:3",
            ],
            sourceReferences);
        var runtime = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME-TRACE");
        Assert.Equal(350m, runtime.LastGeneratedRuntimeHours);
        Assert.Equal(400m, runtime.NextDueRuntimeHours);
    }

    [Fact]
    public async Task Runtime_hour_pm_generation_logs_and_retries_when_provider_has_no_real_runtime_samples()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-RUNTIME-RETRY", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 10m));
        await dbContext.SaveChangesAsync();
        var logger = new TestLogger<GenerateDueMaintenanceWorkOrdersCommandHandler>();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            dbContext,
            new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(25m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)),
            logger);

        var result = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 22), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, result.GeneratedCount);
        Assert.Empty(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        var runtime = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME-RETRY");
        Assert.Equal(0m, runtime.LastGeneratedRuntimeHours);
        Assert.Equal(10m, runtime.NextDueRuntimeHours);
        Assert.Contains(logger.Messages, message => message.LogLevel == LogLevel.Warning && message.Message.Contains("PM-RUNTIME-RETRY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_hour_provider_drives_pm_generation_from_industrial_telemetry_runtime_hours_response()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-RUNTIME-HTTP", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 2.5m);
        _ = plan.ConsumeDueDates(new DateOnly(2026, 7, 1)).ToArray();
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var responseHandler = new JsonResponseHandler("""
            {
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "deviceAssetId": "DEV-CNC-02",
                "windowStartUtc": "2026-07-01T00:00:00Z",
                "windowEndUtc": "2026-07-02T00:00:00Z",
                "stateSampleCount": 2,
                "totalRuntimeHours": 3.25,
                "totalLoadingHours": 4,
                "hasRuntimeSamples": true,
                "daily": [
                  {
                    "businessDate": "2026-07-01",
                    "runtimeHours": 3.25,
                    "loadingHours": 4,
                    "stateSampleCount": 2
                  }
                ]
              },
              "success": true,
              "message": "",
              "code": 0
            }
            """);
        var httpClient = new HttpClient(responseHandler)
        {
            BaseAddress = new Uri("https://industrial-telemetry.local"),
        };
        var runtimeProvider = new HttpIndustrialTelemetryAssetRuntimeHoursProvider(
            new FixedHttpClientFactory(httpClient),
            tokenProvider: null,
            new ThrowingRuntimeHoursFallbackProvider(),
            new TestLogger<HttpIndustrialTelemetryAssetRuntimeHoursProvider>());
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            dbContext,
            runtimeProvider,
            new TestLogger<GenerateDueMaintenanceWorkOrdersCommandHandler>());

        var result = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 7, 1), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, result.GeneratedCount);
        Assert.Contains("/api/business/v1/iiot/runtime-hours?", responseHandler.LastRequestUri);
        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal("PM-RUNTIME-HTTP:runtime:2.5:1", workOrder.SourceReferenceId);
        var runtime = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME-HTTP");
        Assert.Equal(3.25m, runtime.LastGeneratedRuntimeHours);
        Assert.Equal(5.0m, runtime.NextDueRuntimeHours);
    }

    [Fact]
    public async Task Complete_work_order_requires_existing_downtime_reason_and_keeps_reason_classification()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "equipment-failure", "Equipment failure", "breakdown", "equipment-failure"));
        await dbContext.SaveChangesAsync();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(dbContext);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(workOrder.Id, "fixed", "unknown-reason", 10, []),
            CancellationToken.None));

        await handler.Handle(new CompleteMaintenanceWorkOrderCommand(workOrder.Id, "fixed", " equipment-failure ", 10, []), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var reason = await dbContext.DowntimeReasons.SingleAsync();
        Assert.Equal("breakdown", reason.ReasonCategory);
        Assert.Equal("equipment-failure", reason.LossCategory);
        Assert.Equal("equipment-failure", workOrder.DowntimeReasonCode);
    }

    [Fact]
    public async Task Complete_work_order_can_require_actual_labor_minutes_by_configuration()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-001", 90);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "equipment-failure", "Equipment failure", "breakdown", "equipment-failure"));
        await dbContext.SaveChangesAsync();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(
            dbContext,
            Options.Create(new MaintenanceCompletionOptions { RequireActualLaborMinutes = true }));

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(workOrder.Id, "fixed", "equipment-failure", 10, []),
            CancellationToken.None));

        await handler.Handle(
            new CompleteMaintenanceWorkOrderCommand(
                workOrder.Id,
                "fixed",
                "equipment-failure",
                10,
                [],
                ActualLaborMinutes: 75,
                SparePartCostAmount: 120.50m,
                ExternalServiceCostAmount: 35m,
                CostCurrencyCode: "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(75, workOrder.ActualLaborMinutes);
        Assert.Equal(120.50m, workOrder.SparePartCostAmount);
        Assert.Equal(35m, workOrder.ExternalServiceCostAmount);
        Assert.Equal("CNY", workOrder.CostCurrencyCode);
    }

    [Fact]
    public async Task Downtime_reason_commands_update_delete_and_protect_referenced_reasons()
    {
        await using var dbContext = CreateDbContext();
        var createHandler = new CreateDowntimeReasonCommandHandler(dbContext);
        var updateHandler = new UpdateDowntimeReasonCommandHandler(dbContext);
        var deleteHandler = new DeleteDowntimeReasonCommandHandler(dbContext);
        var completeHandler = new CompleteMaintenanceWorkOrderCommandHandler(dbContext);

        await createHandler.Handle(new CreateDowntimeReasonCommand("org-001", "env-dev", "adjustment", "Initial", "planned", "scheduled-loss"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await updateHandler.Handle(new UpdateDowntimeReasonCommand("org-001", "env-dev", "adjustment", "Updated", "micro-stop", "availability-loss"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var updated = await dbContext.DowntimeReasons.SingleAsync(x => x.ReasonCode == "adjustment");
        Assert.Equal("Updated", updated.Description);
        Assert.Equal("micro-stop", updated.ReasonCategory);
        Assert.Equal("availability-loss", updated.LossCategory);

        await deleteHandler.Handle(new DeleteDowntimeReasonCommand("org-001", "env-dev", "adjustment"), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        Assert.False(await dbContext.DowntimeReasons.AnyAsync(x => x.ReasonCode == "adjustment"));

        var referenced = DowntimeReason.Create("org-001", "env-dev", "equipment-failure", "Equipment failure", "breakdown", "equipment-failure");
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        dbContext.DowntimeReasons.Add(referenced);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        await completeHandler.Handle(new CompleteMaintenanceWorkOrderCommand(workOrder.Id, "fixed", "equipment-failure", 10, []), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => deleteHandler.Handle(new DeleteDowntimeReasonCommand("org-001", "env-dev", "equipment-failure"), CancellationToken.None));
    }

    [Fact]
    public async Task Reliability_query_returns_mtbf_and_mttr_from_fault_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddHours(24);
        var completed = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        completed.Complete("fixed", "equipment-failure", 120, []);
        var open = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-002", "critical");
        dbContext.MaintenanceWorkOrders.AddRange(completed, open);
        dbContext.Entry(completed).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(2);
        dbContext.Entry(completed).Property(x => x.CompletedAtUtc).CurrentValue = windowStart.AddHours(4);
        dbContext.Entry(open).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(10);
        await dbContext.SaveChangesAsync();

        var response = await new QueryAssetReliabilityQueryHandler(
                dbContext,
                new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(24m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)))
            .Handle(
            new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
            CancellationToken.None);

        Assert.Equal(2, response.FailureCount);
        Assert.Equal(1, response.RepairCount);
        Assert.Equal(12m, response.MtbfHours);
        Assert.Equal(120m, response.MttrMinutes);
        Assert.Equal(AssetRuntimeSources.Fallback, response.MtbfRuntimeSource);
        Assert.False(response.MtbfRuntimeHasSamples);
    }

    [Fact]
    public async Task Reliability_summary_aggregates_labor_and_cost_by_device_and_technician()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddDays(1);
        var first = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-001", 90);
        var second = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-001", 30);
        var otherTechnician = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-002", 45);
        var otherCurrency = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-001", 15);
        var open = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001", "worker-001", 60);
        var otherDevice = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-02", "normal", "operator-001", "worker-001", 45);
        first.Complete("fixed", "equipment-failure", 10, [], actualLaborMinutes: 75, sparePartCostAmount: 120m, externalServiceCostAmount: 30m, costCurrencyCode: "CNY", actualTechnicianUserId: "worker-actual");
        second.Complete("fixed", "equipment-failure", 10, [], actualLaborMinutes: 20, sparePartCostAmount: 10m, externalServiceCostAmount: 5m, costCurrencyCode: "CNY");
        otherTechnician.Complete("fixed", "equipment-failure", 10, [], actualLaborMinutes: 25, sparePartCostAmount: 20m, externalServiceCostAmount: 0m, costCurrencyCode: "CNY");
        otherCurrency.Complete("fixed", "equipment-failure", 10, [], actualLaborMinutes: 10, sparePartCostAmount: 8m, externalServiceCostAmount: 2m, costCurrencyCode: "USD");
        otherDevice.Complete("fixed", "equipment-failure", 10, [], actualLaborMinutes: 35, sparePartCostAmount: 40m, externalServiceCostAmount: 0m, costCurrencyCode: "CNY");
        dbContext.MaintenanceWorkOrders.AddRange(first, second, otherTechnician, otherCurrency, open, otherDevice);
        dbContext.Entry(first).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(1);
        dbContext.Entry(second).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(2);
        dbContext.Entry(otherTechnician).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(3);
        dbContext.Entry(otherCurrency).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(4);
        dbContext.Entry(open).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(5);
        dbContext.Entry(otherDevice).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(6);
        await dbContext.SaveChangesAsync();

        var response = await new QueryMaintenanceReliabilitySummaryQueryHandler(dbContext).Handle(
            new QueryMaintenanceReliabilitySummaryQuery("org-001", "env-dev", windowStart, windowEnd, DeviceAssetId: "DEV-CNC-01"),
            CancellationToken.None);

        Assert.Equal(4, response.Items.Count);
        Assert.DoesNotContain(response.Items, x => x.CostCurrencyCode is null);
        var item = Assert.Single(response.Items, x => x.ActualTechnicianUserId == "worker-001" && x.CostCurrencyCode == "CNY");
        Assert.Equal("DEV-CNC-01", item.DeviceAssetId);
        Assert.Equal("worker-001", item.AssignedTechnicianUserId);
        Assert.Equal(1, item.WorkOrderCount);
        Assert.Equal(30, item.EstimatedLaborMinutes);
        Assert.Equal(20, item.ActualLaborMinutes);
        Assert.Equal(10m, item.SparePartCostAmount);
        Assert.Equal(5m, item.ExternalServiceCostAmount);
        Assert.Equal(15m, item.TotalCostAmount);
        Assert.Single(response.Items, x => x.ActualTechnicianUserId == "worker-002" && x.CostCurrencyCode == "CNY");
        Assert.Single(response.Items, x => x.ActualTechnicianUserId == "worker-001" && x.CostCurrencyCode == "USD");
        var actualTechnician = Assert.Single(response.Items, x => x.ActualTechnicianUserId == "worker-actual" && x.CostCurrencyCode == "CNY");
        Assert.Equal("worker-001", actualTechnician.AssignedTechnicianUserId);
        Assert.Equal(75, actualTechnician.ActualLaborMinutes);
    }

    [Fact]
    public async Task Reliability_query_uses_effective_repair_segment_and_counts_inspection_faults()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddHours(24);
        var alarmFault = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        dbContext.MaintenanceWorkOrders.Add(alarmFault);
        dbContext.Entry(alarmFault).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(1);
        await dbContext.SaveChangesAsync();
        await new StartMaintenanceRepairCommandHandler(dbContext).Handle(
            new StartMaintenanceRepairCommand(alarmFault.Id, windowStart.AddHours(3)),
            CancellationToken.None);
        alarmFault.Complete("fixed", "equipment-failure", 120, []);
        var inspectionFault = MaintenanceWorkOrder.OpenFromInspection(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            new MaintenanceInspectionId(Guid.CreateVersion7()),
            "bearing vibration failed");
        dbContext.MaintenanceWorkOrders.Add(inspectionFault);
        dbContext.Entry(alarmFault).Property(x => x.CompletedAtUtc).CurrentValue = windowStart.AddHours(4);
        dbContext.Entry(inspectionFault).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(10);
        await dbContext.SaveChangesAsync();

        var response = await new QueryAssetReliabilityQueryHandler(
                dbContext,
                new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(24m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)))
            .Handle(
                new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
                CancellationToken.None);

        Assert.Equal(2, response.FailureCount);
        Assert.Equal(1, response.RepairCount);
        Assert.Equal(60m, response.MttrMinutes);
    }

    [Fact]
    public async Task Start_repair_rejects_repair_time_before_work_order_opened_time()
    {
        await using var dbContext = CreateDbContext();
        var openedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "normal", "operator-001");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.Entry(workOrder).Property(x => x.OpenedAtUtc).CurrentValue = openedAtUtc;
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new StartMaintenanceRepairCommandHandler(dbContext).Handle(
            new StartMaintenanceRepairCommand(workOrder.Id, openedAtUtc.AddMinutes(-1)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Reliability_query_uses_actual_runtime_hours_for_mtbf()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddHours(24);
        var completed = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        completed.Complete("fixed", "equipment-failure", 120, []);
        var open = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-002", "critical");
        dbContext.MaintenanceWorkOrders.AddRange(completed, open);
        dbContext.Entry(completed).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(2);
        dbContext.Entry(completed).Property(x => x.CompletedAtUtc).CurrentValue = windowStart.AddHours(4);
        dbContext.Entry(open).Property(x => x.OpenedAtUtc).CurrentValue = windowStart.AddHours(10);
        await dbContext.SaveChangesAsync();

        var response = await new QueryAssetReliabilityQueryHandler(
                dbContext,
                new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(6m, AssetRuntimeSources.Oee, HasRuntimeSamples: true)))
            .Handle(
                new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
                CancellationToken.None);

        Assert.Equal(2, response.FailureCount);
        Assert.Equal(3m, response.MtbfHours);
        Assert.Equal(120m, response.MttrMinutes);
        Assert.Equal(AssetRuntimeSources.Oee, response.MtbfRuntimeSource);
        Assert.True(response.MtbfRuntimeHasSamples);
    }

    [Fact]
    public async Task Reliability_query_returns_null_metrics_when_no_fault_samples_exist()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddHours(24);

        var response = await new QueryAssetReliabilityQueryHandler(
                dbContext,
                new FixedAssetRuntimeHoursProvider(new AssetRuntimeHoursResult(24m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false)))
            .Handle(
            new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
            CancellationToken.None);

        Assert.Equal(0, response.FailureCount);
        Assert.Equal(0, response.RepairCount);
        var body = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("MtbfHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("MttrMinutes").ValueKind);
        Assert.Equal(AssetRuntimeSources.Fallback, document.RootElement.GetProperty("MtbfRuntimeSource").GetString());
        Assert.False(document.RootElement.GetProperty("MtbfRuntimeHasSamples").GetBoolean());
    }

    [Fact]
    public async Task Maintenance_fallback_runtime_provider_uses_mediator_pipeline()
    {
        await using var dbContext = CreateDbContext();
        var windowStart = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddHours(4);
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "PM-RUNTIME",
            "P7D",
            new DateOnly(2026, 6, 1),
            "maintenance",
            windowStart.AddHours(1),
            windowStart.AddHours(2)));
        await dbContext.SaveChangesAsync();
        var probe = new QueryPipelineProbe();
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(probe);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(QueryPipelineProbeBehavior<,>));
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtimeProvider = new MaintenanceUnavailableWindowRuntimeHoursProvider(scope.ServiceProvider.GetRequiredService<ISender>());

        var result = await runtimeProvider.CalculateFallbackAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            windowStart,
            windowEnd,
            CancellationToken.None);

        Assert.Equal(1, probe.MaintenanceAvailabilityQueryCalls);
        Assert.Equal(3m, result.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Fallback, result.RuntimeSource);
        Assert.False(result.HasRuntimeSamples);
    }

    [Fact]
    public async Task Maintenance_inspection_timestamps_are_normalized_to_utc()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-UTC", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var inspectionHandler = new RecordMaintenanceInspectionCommandHandler(dbContext);
        var localInspectedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(8));
        await inspectionHandler.Handle(new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "fail", localInspectedAt), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var availability = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
        var queryStart = new DateTimeOffset(2026, 6, 1, 1, 0, 0, TimeSpan.Zero);
        var queryEnd = new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero);

        var response = await availability.Handle(new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", queryStart, queryEnd, ["DEV-CNC-01"], null)), CancellationToken.None);

        var inspectionRequired = Assert.Single(response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.InspectionRequired);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), inspectionRequired.StartUtc);
        Assert.Equal(TimeSpan.Zero, inspectionRequired.StartUtc.Offset);
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Maintenance_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Maintenance_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/maintenance/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-CNC-01",
            priority = "critical",
            openedBy = "operator-001",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return MaintenanceEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    internal static ApplicationDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-availability-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ApplicationDbContext CreateDbContext() => CreateTestDbContext();

    private sealed class FixedAssetRuntimeHoursProvider(AssetRuntimeHoursResult result) : IAssetRuntimeHoursProvider
    {
        public Task<AssetRuntimeHoursResult> CalculateAsync(
            string organizationId,
            string environmentId,
            string deviceAssetId,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken)
        {
            _ = organizationId;
            _ = environmentId;
            _ = deviceAssetId;
            _ = windowStartUtc;
            _ = windowEndUtc;
            _ = cancellationToken;
            return Task.FromResult(result);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            _ = state;
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            _ = logLevel;
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = eventId;
            _ = exception;
            Messages.Add(new LogMessage(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogMessage(LogLevel LogLevel, string Message);

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpIndustrialTelemetryAssetRuntimeHoursProvider.ClientName, name);
            return client;
        }
    }

    private sealed class ThrowingRuntimeHoursFallbackProvider : IAssetRuntimeHoursFallbackProvider
    {
        public Task<AssetRuntimeHoursResult> CalculateFallbackAsync(
            string organizationId,
            string environmentId,
            string deviceAssetId,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken)
        {
            _ = organizationId;
            _ = environmentId;
            _ = deviceAssetId;
            _ = windowStartUtc;
            _ = windowEndUtc;
            _ = cancellationToken;
            throw new InvalidOperationException("Fallback should not be used when IndustrialTelemetry returns real runtime samples.");
        }
    }

    private sealed class JsonResponseHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequestUri = request.RequestUri?.PathAndQuery;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class QueryPipelineProbe
    {
        public int MaintenanceAvailabilityQueryCalls { get; private set; }

        public void Record(TRequestMarker marker)
        {
            _ = marker;
            MaintenanceAvailabilityQueryCalls++;
        }
    }

    private enum TRequestMarker
    {
        MaintenanceAvailability,
    }

    private sealed class QueryPipelineProbeBehavior<TRequest, TResponse>(QueryPipelineProbe probe) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is QueryMaintenanceAvailabilityWindowsQuery)
            {
                probe.Record(TRequestMarker.MaintenanceAvailability);
            }

            return await next(cancellationToken);
        }
    }

    private static string GetWorkOrderStringProperty(MaintenanceWorkOrder workOrder, string propertyName)
    {
        var property = typeof(MaintenanceWorkOrder).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"MaintenanceWorkOrder.{propertyName} is required for traceable inspection work orders.");
        return Assert.IsType<string>(property.GetValue(workOrder));
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
