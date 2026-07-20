using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceSeedServiceTests
{
    [Fact]
    public async Task Seeds_selectable_inspection_plans_for_a_fresh_environment()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        await new MaintenanceSeedService(db).SeedAsync("org-001", "env-dev");

        var plans = await db.MaintenancePlans
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .ToListAsync();

        Assert.Equal(3, plans.Count);
        // 计划绑定设备资产 + 覆盖日/周/月三档 ISO 日周期，供点检页选计划。
        Assert.Contains(plans, p => p.PlanCode == "PM-INSP-DAILY-01" && p.DeviceAssetId == "DEV-CNC-01" && p.Interval == "P1D");
        Assert.Contains(plans, p => p.PlanCode == "PM-INSP-WEEKLY-02" && p.DeviceAssetId == "DEV-PUMP-02" && p.Interval == "P7D");
        Assert.Contains(plans, p => p.PlanCode == "PM-INSP-MONTHLY-03" && p.DeviceAssetId == "DEV-COMP-03" && p.Interval == "P30D");
    }

    [Fact]
    public async Task Is_idempotent_and_only_fills_missing_plans()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var seed = new MaintenanceSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        // 重复 seed 不新增（按 planCode 判重），仍是 3 条。
        var count = await db.MaintenancePlans
            .CountAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Preserves_a_tenant_maintained_plan_and_adds_the_rest()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        // 租户已按同一 planCode 维护了一条计划（不同设备/周期）——seed 不得覆写它。
        db.MaintenancePlans.Add(MaintenancePlan.Create(
            "org-001", "env-dev", "DEV-TENANT-99", "PM-INSP-WEEKLY-02", "P14D", new DateOnly(2026, 6, 1), "operator"));
        await db.SaveChangesAsync();

        await new MaintenanceSeedService(db).SeedAsync("org-001", "env-dev");

        var plans = await db.MaintenancePlans
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .ToListAsync();

        // 3 条：租户那条 WEEKLY-02 保留原样 + 补齐缺失的 DAILY-01 / MONTHLY-03。
        Assert.Equal(3, plans.Count);
        var weekly = Assert.Single(plans, p => p.PlanCode == "PM-INSP-WEEKLY-02");
        Assert.Equal("DEV-TENANT-99", weekly.DeviceAssetId);
        Assert.Equal("P14D", weekly.Interval);
    }

    [Fact]
    public async Task Scopes_plans_to_the_seeded_organization_and_environment()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        await new MaintenanceSeedService(db).SeedAsync("org-001", "env-dev");

        // 另一租户/环境不受影响（无泄漏）。
        var otherScope = await db.MaintenancePlans
            .CountAsync(x => x.OrganizationId == "org-999" || x.EnvironmentId == "env-prod");
        Assert.Equal(0, otherScope);
    }

    [Fact]
    public async Task Leader_demo_seed_creates_one_open_alarm_sourced_work_order_without_final_repair_facts()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var workOrder = Assert.Single(await db.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal("ALARM-DEMO-001", workOrder.SourceAlarmId);
        Assert.Equal(LeaderDemoSeedService.WorkOrderReference, workOrder.SourceReferenceId);
        Assert.Equal(LeaderDemoSeedService.DeviceAssetId, workOrder.DeviceAssetId);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
        Assert.Null(workOrder.CompletedAtUtc);
        Assert.Null(workOrder.CompletionResult);
        Assert.Null(workOrder.ActualLaborMinutes);
        Assert.False(workOrder.AlarmCleared);
    }

    [Fact]
    public async Task Leader_demo_alarm_raise_and_clear_reuse_the_seeded_work_order_lifecycle()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");
        var seeded = await db.MaintenanceWorkOrders.SingleAsync();

        var raisedWorkOrderId = await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
            new CreateMaintenanceWorkOrderCommand(
                "org-001",
                "env-dev",
                LeaderDemoSeedService.DeviceAssetId,
                "critical",
                "ALARM-DEMO-001",
                "industrialTelemetry",
                null),
            CancellationToken.None);

        Assert.Equal(seeded.Id, raisedWorkOrderId);
        Assert.Single(await db.MaintenanceWorkOrders.ToArrayAsync());

        var clearedAtUtc = seeded.OpenedAtUtc.AddMinutes(1);
        await new MarkMaintenanceWorkOrderAlarmClearedCommandHandler(db).Handle(
            new MarkMaintenanceWorkOrderAlarmClearedCommand(
                "org-001",
                "env-dev",
                "ALARM-DEMO-001",
                clearedAtUtc),
            CancellationToken.None);

        Assert.True(seeded.AlarmCleared);
        Assert.Equal(clearedAtUtc, seeded.AlarmClearedAtUtc);
    }

    [Fact]
    public async Task Leader_demo_seed_rejects_an_incompatible_reserved_work_order_reference()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenFromAlarm(
            "org-001",
            "env-dev",
            "DEV-OTHER",
            "ALARM-DEMO-001",
            "low"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(LeaderDemoSeedService.WorkOrderReference, exception.Message, StringComparison.Ordinal);
        Assert.Equal("DEV-OTHER", (await db.MaintenanceWorkOrders.SingleAsync()).DeviceAssetId);
    }
}
