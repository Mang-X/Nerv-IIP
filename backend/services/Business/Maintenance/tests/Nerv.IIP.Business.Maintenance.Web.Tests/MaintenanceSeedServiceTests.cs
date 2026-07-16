using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
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
}
