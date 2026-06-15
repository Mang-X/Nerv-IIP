using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

        Assert.Equal(13, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/work-orders/{workOrderId}/complete" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "completeMaintenanceWorkOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/work-orders" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceWorkOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "createMaintenancePlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/plans" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "listMaintenancePlans");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/inspections" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "recordMaintenanceInspection");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/inspections" && x.PermissionCode == MaintenancePermissionCodes.PlansRead && x.OperationId == "listMaintenanceInspections");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/spare-parts" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "listMaintenanceSpareParts");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/spare-parts" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersManage && x.OperationId == "createMaintenanceSparePart");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/assets/{deviceAssetId}/availability-windows" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "getMaintenanceAssetAvailabilityWindows");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/availability-windows" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "queryMaintenanceAvailabilityWindows");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/maintenance/plans/generate-due" && x.PermissionCode == MaintenancePermissionCodes.PlansManage && x.OperationId == "generateDueMaintenanceWorkOrders");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/maintenance/assets/{deviceAssetId}/reliability" && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead && x.OperationId == "queryMaintenanceAssetReliability");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
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
    public async Task Generate_due_maintenance_work_orders_creates_one_idempotent_work_order_per_due_plan()
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

        Assert.Equal(1, first.GeneratedCount);
        Assert.Equal(0, second.GeneratedCount);
        var workOrder = Assert.Single(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal("DEV-CNC-01", workOrder.DeviceAssetId);
        Assert.Equal("PM-WEEKLY", workOrder.SourcePlanCode);
        var plan = await dbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-WEEKLY");
        Assert.Equal(new DateOnly(2026, 6, 8), plan.LastGeneratedOn);
        Assert.Equal(new DateOnly(2026, 6, 15), plan.NextDueOn);
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

        var response = await new QueryAssetReliabilityQueryHandler(dbContext).Handle(
            new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
            CancellationToken.None);

        Assert.Equal(2, response.FailureCount);
        Assert.Equal(1, response.RepairCount);
        Assert.Equal(12m, response.MtbfHours);
        Assert.Equal(120m, response.MttrMinutes);
    }

    [Fact]
    public async Task Maintenance_inspection_timestamps_are_normalized_to_utc()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-INSPECT-UTC", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.Add(plan);
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-availability-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
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
