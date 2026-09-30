using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3852：维修工单正式单号由编码规则 <c>maintenance-work-order</c> 分配（MWO-yyyyMMdd-NNNNNN），
/// 四个开单入口（手工 / 报警、v2、计划到期、点检不合格）都要拿到单号；列表、详情、备件与关键字检索都以它为人读单号。
/// </summary>
public sealed class MaintenanceWorkOrderNumberTests
{
    private static readonly Regex RuleShaped = new(@"^MWO-\d{8}-\d{6}$", RegexOptions.Compiled);

    [Fact]
    public async Task Manual_alarm_and_v2_create_entries_allocate_distinct_rule_numbers()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var coding = new MaintenanceCodingService();

        await new CreateMaintenanceWorkOrderCommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", null, "operator-001", null),
            CancellationToken.None);
        await new CreateMaintenanceWorkOrderCommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", "alarm-001", "operator-001", null),
            CancellationToken.None);
        await new CreateMaintenanceWorkOrderV2CommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderV2Command("org-001", "env-dev", "DEV-CNC-02", "medium", null, "operator-001", null),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var numbers = await db.MaintenanceWorkOrders.AsNoTracking().Select(x => x.WorkOrderNo).ToListAsync();
        Assert.Equal(3, numbers.Count);
        Assert.All(numbers, number => Assert.Matches(RuleShaped, number));
        Assert.Equal(3, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(numbers, number => Assert.Equal($"MWO-{DateTimeOffset.UtcNow:yyyyMMdd}", number[..12]));
    }

    [Fact]
    public async Task Plan_generation_and_failed_inspection_also_allocate_rule_numbers()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var coding = new MaintenanceCodingService();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-0001", "P7D", new DateOnly(2026, 9, 1), "maintenance");
        db.MaintenancePlans.Add(plan);
        await db.SaveChangesAsync();

        await new GenerateDueMaintenanceWorkOrdersCommandHandler(db, codingService: coding).Handle(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 9, 1), "system:pm-scheduler"),
            CancellationToken.None);
        await new RecordMaintenanceInspectionCommandHandler(db, coding).Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "failed", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var workOrders = await db.MaintenanceWorkOrders.AsNoTracking().ToListAsync();
        Assert.Contains(workOrders, x => x.SourceType == MaintenanceWorkOrderSourceTypes.Plan && RuleShaped.IsMatch(x.WorkOrderNo));
        Assert.Contains(workOrders, x => x.SourceType == MaintenanceWorkOrderSourceTypes.Inspection && RuleShaped.IsMatch(x.WorkOrderNo));
        Assert.Equal(workOrders.Count, workOrders.Select(x => x.WorkOrderNo).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_same_create_intent_gets_the_same_number_back_on_retry()
    {
        var coding = new MaintenanceCodingService();

        var first = await MaintenanceWorkOrderNumbers.AllocateAsync(coding, "org-001", "env-dev", "alarm:alarm-001", CancellationToken.None);
        var retry = await MaintenanceWorkOrderNumbers.AllocateAsync(coding, "org-001", "env-dev", "alarm:alarm-001", CancellationToken.None);
        var other = await MaintenanceWorkOrderNumbers.AllocateAsync(coding, "org-001", "env-dev", "alarm:alarm-002", CancellationToken.None);
        var unkeyed = await MaintenanceWorkOrderNumbers.AllocateAsync(coding, "org-001", "env-dev", null, CancellationToken.None);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, other);
        Assert.NotEqual(other, unkeyed);
    }

    [Fact]
    public async Task List_detail_spare_parts_and_keyword_search_carry_the_formal_number()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var target = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "MWO-20260928-000007", "DEV-CNC-01", "high", "operator-001");
        var other = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "MWO-20260928-000008", "DEV-CNC-01", "high", "operator-001");
        target.AddSparePartLine(new SparePartLineDraft("SPARE-001", 1m, "pcs", "SITE-001", "loc-spare-01"));
        db.MaintenanceWorkOrders.AddRange(target, other);
        await db.SaveChangesAsync();

        var searched = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery("org-001", "env-dev", Keyword: "000007"),
            CancellationToken.None);
        var detail = await new GetMaintenanceWorkOrderQueryHandler(db).Handle(
            new GetMaintenanceWorkOrderQuery("org-001", "env-dev", target.Id),
            CancellationToken.None);
        var spareParts = await new ListMaintenanceSparePartsQueryHandler(db).Handle(
            new ListMaintenanceSparePartsQuery("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal("MWO-20260928-000007", Assert.Single(searched.Items).WorkOrderNo);
        Assert.Equal("MWO-20260928-000007", detail.WorkOrder.WorkOrderNo);
        Assert.Equal("MWO-20260928-000007", Assert.Single(spareParts.Items).WorkOrderNo);
    }
}
