using System.Text.RegularExpressions;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// 设备可用性窗口的「来源引用」必须给出人读标识。
///
/// 回归背景：<c>EquipmentRuntimeAvailabilityWindowContract</c> 长期只回一个 <c>SourceReferenceId</c>，
/// 而维修工单/点检这两路填的是聚合 GUID。整个窗口对象里没有第二个可读字段，
/// <c>/maintenance/availability</c> 的「来源引用」列便只能把 GUID 原样上屏，前端无从兜底。
///
/// 这里断言标签是**业务标识**而不是 GUID —— 用 GUID 形状做否定断言，比断言某个具体字符串更能
/// 拦住「换了个字段但还是个 id」的回归。
/// </summary>
public sealed class MaintenanceAvailabilitySourceLabelTests
{
    private static readonly Regex GuidShaped = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    [Fact]
    public async Task Availability_windows_expose_business_identifiers_not_guids()
    {
        var queryStart = DateTimeOffset.UtcNow;
        var queryEnd = queryStart.AddHours(4);

        await using var dbContext = CreateDbContext();

        // 维修工单：标签就是它的正式单号（#3852，编码规则分配），与来源引用无关。
        var workOrder = MaintenanceWorkOrder.OpenFromAlarm(
            "org-001",
            "env-dev",
            "MWO-20260928-000042",
            "DEV-CNC-01",
            sourceAlarmId: "WH-DEV-CNC-01-spindle-temperature:0001",
            priority: "high",
            sourceReferenceId: "MWO-2026-0042");
        workOrder.MarkAssetUnavailable(queryStart.AddHours(-1), "alarm downtime");

        // 保养计划窗口：SourceReferenceId 本就是计划编码。
        var windowPlan = MaintenancePlan.Create(
            "org-001", "env-dev", "DEV-CNC-01", "PM-WINDOW-01", "P7D",
            DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance",
            windowStartUtc: queryStart.AddMinutes(30),
            windowEndUtc: queryStart.AddMinutes(90));

        // 点检要求窗口：点检记录本身无编号，标签取所属计划编码。
        var inspectionPlan = MaintenancePlan.Create(
            "org-001", "env-dev", "DEV-CNC-01", "PM-INSP-DAILY-01", "P1D",
            DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        var inspection = MaintenanceInspection.RecordForPlan(
            "org-001", "env-dev", inspectionPlan.Id, "inspector-001", "failed", queryStart.AddMinutes(10));

        dbContext.MaintenanceWorkOrders.Add(workOrder);
        dbContext.MaintenancePlans.Add(windowPlan);
        dbContext.MaintenancePlans.Add(inspectionPlan);
        dbContext.MaintenanceInspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        var response = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext, new GetMaintenanceRestorePredictionQueryHandler(dbContext, Microsoft.Extensions.Options.Options.Create(new MaintenanceRestorePredictionOptions()))).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(
                new EquipmentRuntimeAvailabilityRequest("org-001", "env-dev", queryStart, queryEnd, ["DEV-CNC-01"], null)),
            CancellationToken.None);

        Assert.NotEmpty(response.Items);

        // 总纲：任何一条窗口的来源标签都不得是 GUID。
        foreach (var window in response.Items)
        {
            Assert.False(
                window.SourceReferenceLabel is not null && GuidShaped.IsMatch(window.SourceReferenceLabel),
                $"来源引用标签仍是 GUID：reasonCode={window.ReasonCode}, label={window.SourceReferenceLabel}");
        }

        // 由报警升级而来的工单，窗口原因码是 ActiveAlarm；标签仍应是工单号而不是 GUID。
        var alarmWindow = Assert.Single(response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Equal("MWO-20260928-000042", alarmWindow.SourceReferenceLabel);

        var maintenanceWindow = Assert.Single(
            response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow);
        Assert.Equal("PM-WINDOW-01", maintenanceWindow.SourceReferenceLabel);

        var inspectionRequired = Assert.Single(
            response.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.InspectionRequired);
        Assert.Equal("PM-INSP-DAILY-01", inspectionRequired.SourceReferenceLabel);

        // 点检窗口的 SourceReferenceId 仍是 GUID（机器用），标签才是给人看的 —— 两者分工明确。
        Assert.Matches(GuidShaped, inspectionRequired.SourceReferenceId);
    }

    /// <summary>
    /// 每张维修工单都有正式单号（#3852）：计划、报警、点检三路来源的工单，窗口标签都是单号，
    /// 不再回落到计划编码、报警合成键或点检 GUID。
    /// </summary>
    [Fact]
    public async Task Plan_alarm_and_inspection_work_orders_all_label_their_windows_with_the_formal_number()
    {
        var queryStart = DateTimeOffset.UtcNow;
        var queryEnd = queryStart.AddHours(4);

        await using var dbContext = CreateDbContext();

        var planWorkOrder = MaintenanceWorkOrder.OpenFromPlan(
            "org-001", "env-dev", "MWO-20260928-000001", "DEV-PLAN-01", planCode: "PM-INSP-WEEKLY-02", openedBy: "maintenance");
        planWorkOrder.MarkAssetUnavailable(queryStart.AddHours(-1), "planned downtime");
        var alarmWorkOrder = MaintenanceWorkOrder.OpenFromAlarm(
            "org-001", "env-dev", "MWO-20260928-000002", "DEV-ALARM-01",
            sourceAlarmId: "WH-DEV-ASM-12-press-force:0000", priority: "high");
        alarmWorkOrder.MarkAssetUnavailable(queryStart.AddHours(-1), "alarm downtime");
        var inspectionPlan = MaintenancePlan.Create(
            "org-001", "env-dev", "DEV-INSP-01", "PM-INSP-DAILY-09", "P1D",
            DateOnly.FromDateTime(queryStart.UtcDateTime), "maintenance");
        var inspection = MaintenanceInspection.RecordForPlan(
            "org-001", "env-dev", inspectionPlan.Id, "inspector-001", "passed", queryStart.AddMinutes(-30));
        var inspectionWorkOrder = MaintenanceWorkOrder.OpenFromInspection(
            "org-001", "env-dev", "MWO-20260928-000003", "DEV-INSP-01", inspection.Id, "failed");
        inspectionWorkOrder.MarkAssetUnavailable(queryStart.AddHours(-1), "inspection failed");

        dbContext.MaintenanceWorkOrders.AddRange(planWorkOrder, alarmWorkOrder, inspectionWorkOrder);
        dbContext.MaintenancePlans.Add(inspectionPlan);
        dbContext.MaintenanceInspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        var response = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext, new GetMaintenanceRestorePredictionQueryHandler(dbContext, Microsoft.Extensions.Options.Options.Create(new MaintenanceRestorePredictionOptions()))).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(
                new EquipmentRuntimeAvailabilityRequest(
                    "org-001", "env-dev", queryStart, queryEnd, ["DEV-PLAN-01", "DEV-ALARM-01", "DEV-INSP-01"], null)),
            CancellationToken.None);

        Assert.Equal("MWO-20260928-000001", Assert.Single(response.Items, x => x.DeviceAssetId == "DEV-PLAN-01").SourceReferenceLabel);
        Assert.Equal("MWO-20260928-000002", Assert.Single(response.Items, x => x.DeviceAssetId == "DEV-ALARM-01").SourceReferenceLabel);
        Assert.Equal("MWO-20260928-000003", Assert.Single(response.Items, x => x.DeviceAssetId == "DEV-INSP-01").SourceReferenceLabel);
    }

    private static ApplicationDbContext CreateDbContext() =>
        MaintenanceEndpointContractTests.CreateTestDbContext();
}
