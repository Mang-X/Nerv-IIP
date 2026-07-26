using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L0 主数据（MasterData 侧）的黄金向量：规模逐条对应设定集 §1–§6，
/// 重复执行幂等，且与 MAN-519 固定演示事实 / 千单规模块号段互不干扰。
/// </summary>
public sealed class MasterDataWorldBibleSeedServiceTests
{
    [Fact]
    public void Spec_matches_the_world_bible_counts()
    {
        Assert.Equal(3, WorldBibleSpec.Workshops.Length);
        // 设定集 §2 表头写 13，逐行合计为 14；实现以逐行表格为准。
        Assert.Equal(14, WorldBibleSpec.ProductionLines.Length);
        Assert.Equal(17, WorldBibleSpec.WorkCenters.Length);
        Assert.Equal(46, WorldBibleSpec.Devices.Count);
        Assert.Equal(6, WorldBibleSpec.Departments.Length);
        Assert.Equal(6, WorldBibleSpec.Teams.Length);
        Assert.Equal(10, WorldBibleSpec.Skills.Length);
        Assert.Equal(58, WorldBibleSpec.Employees.Count);
        Assert.Equal(7, WorldBibleSpec.Customers.Length);
        Assert.Equal(10, WorldBibleSpec.Suppliers.Length);

        Assert.Equal(24, WorldBibleSpec.FinishedGoods.Count);
        Assert.Equal(18, WorldBibleSpec.SemiFinishedGoods.Length);
        Assert.Equal(30, WorldBibleSpec.RawMaterials.Length);
        Assert.Equal(12, WorldBibleSpec.PackagingMaterials.Length);
        Assert.Equal(84, WorldBibleSpec.AllSkus.Length);
        Assert.Equal(84, WorldBibleSpec.AllSkus.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Spec_uses_chinese_display_names_and_isolated_code_segments()
    {
        Assert.All(WorldBibleSpec.AllSkus, sku =>
        {
            Assert.Contains(sku.Name, name => name > '一' && name < '鿿');
            Assert.DoesNotContain("DEMO", sku.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("SCALE", sku.Code, StringComparison.Ordinal);
        });
        Assert.All(WorldBibleSpec.Devices, device => Assert.DoesNotContain("DEMO", device.Code, StringComparison.Ordinal));
        Assert.All(WorldBibleSpec.ProductionLines, line => Assert.StartsWith("LINE-WB-", line.Code, StringComparison.Ordinal));

        Assert.Equal(
            ["FG-QJ-P1-L", "FG-QJ-P1-R", "FG-HJ-P1-L", "FG-HJ-P1-R"],
            WorldBibleSpec.FinishedGoods.Take(4).Select(x => x.Code));
        Assert.Equal("P1 平台前滑柱总成（左）", WorldBibleSpec.FinishedGoods[0].Name);
    }

    [Fact]
    public void Employee_roster_matches_the_world_bible_headcount_plan()
    {
        var employees = WorldBibleSpec.Employees;
        Assert.Equal(28, employees.Count(x => x.DepartmentCode == "DEPT-PROD"));
        Assert.Equal(4, employees.Count(x => x.DepartmentCode == "DEPT-PLAN"));
        Assert.Equal(9, employees.Count(x => x.DepartmentCode == "DEPT-QA"));
        Assert.Equal(6, employees.Count(x => x.DepartmentCode == "DEPT-EQ"));
        Assert.Equal(7, employees.Count(x => x.DepartmentCode == "DEPT-WH"));
        Assert.Equal(4, employees.Count(x => x.DepartmentCode == "DEPT-BIZ"));

        Assert.Equal(3, employees.Count(x => x.RoleName == "车间主任"));
        Assert.Equal(6, employees.Count(x => x.RoleName == "班组长"));
        Assert.Equal(19, employees.Count(x => x.RoleName == "操作工"));
        Assert.Equal(6, employees.Count(x => x.IsTeamLeader));

        // 工号 EMP-001..EMP-058 连续且唯一，userId 与工号一一对应。
        Assert.Equal("EMP-001", employees[0].EmployeeNo);
        Assert.Equal("EMP-058", employees[^1].EmployeeNo);
        Assert.Equal(58, employees.Select(x => x.EmployeeNo).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, employees.Select(x => x.UserId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, employees.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());

        // 操作工每人 1–3 项技能，且技能必须在 10 项目录内。
        var catalog = WorldBibleSpec.Skills.Select(x => x.Code).ToArray();
        foreach (var operatorEmployee in employees.Where(x => x.RoleName == "操作工"))
        {
            Assert.InRange(operatorEmployee.SkillCodes.Length, 1, 3);
            Assert.All(operatorEmployee.SkillCodes, code => Assert.Contains(code, catalog));
        }

        // 每个班组恰好一名班组长，19 名操作工全部落到 6 个班组。
        foreach (var team in WorldBibleSpec.Teams)
        {
            Assert.Single(employees, x => x.TeamCode == team.Code && x.IsTeamLeader);
        }

        Assert.All(employees, employee =>
            Assert.Contains(employee.DepartmentCode, WorldBibleSpec.Departments.Select(x => x.Code)));
    }

    [Fact]
    public void Every_device_and_work_center_resolves_to_a_declared_line_and_workshop()
    {
        var workshopCodes = WorldBibleSpec.Workshops.Select(x => x.Code).ToArray();
        var lineCodes = WorldBibleSpec.ProductionLines.Select(x => x.Code).ToArray();
        var workCenterCodes = WorldBibleSpec.WorkCenters.Select(x => x.Code).ToArray();

        Assert.All(WorldBibleSpec.ProductionLines, line => Assert.Contains(line.WorkshopCode, workshopCodes));
        Assert.All(WorldBibleSpec.WorkCenters, workCenter =>
        {
            Assert.Contains(workCenter.LineCode, lineCodes);
            Assert.Contains(workCenter.WorkshopCode, workshopCodes);
        });
        Assert.All(WorldBibleSpec.Devices, device => Assert.Contains(device.WorkCenterCode, workCenterCodes));

        // 设定集 §3 的类别配比。
        Assert.Equal(10, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-CNC-", StringComparison.Ordinal)));
        Assert.Equal(4, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-GRD-", StringComparison.Ordinal)));
        Assert.Equal(12, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-ASM-", StringComparison.Ordinal)));
        Assert.Equal(3, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-WLD-", StringComparison.Ordinal)));
        Assert.Equal(3, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-CTG-", StringComparison.Ordinal)));
        Assert.Equal(4, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-TST-", StringComparison.Ordinal)));
        Assert.Equal(2, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-PKG-", StringComparison.Ordinal)));
        Assert.Equal(8, WorldBibleSpec.Devices.Count(x => x.Code.StartsWith("DEV-AUX-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Seed_creates_the_full_l0_master_data_once()
    {
        await using var db = CreateDbContext();
        var seed = new WorldBibleSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(3, await db.Workshops.CountAsync());
        Assert.Equal(14, await db.ProductionLines.CountAsync());
        Assert.Equal(17, await db.WorkCenters.CountAsync());
        Assert.Equal(46, await db.DeviceAssets.CountAsync());
        Assert.Equal(84, await db.Skus.CountAsync());
        Assert.Equal(17, await db.BusinessPartners.CountAsync());
        Assert.Equal(6, await db.Departments.CountAsync());
        Assert.Equal(6, await db.Teams.CountAsync());
        Assert.Equal(10, await db.Skills.CountAsync());
        Assert.Equal(2, await db.Shifts.CountAsync());
        Assert.Single(await db.Sites.ToArrayAsync());

        // 58 名员工：25 名生产现场人员进班组（6 班组长 + 19 操作工），全员至少 1 项技能绑定。
        Assert.Equal(25, await db.TeamMembers.CountAsync());
        Assert.Equal(6, await db.TeamMembers.CountAsync(x => x.IsLeader));
        // 经营部 4 人（销售/采购）不属于现场技能矩阵，其余 54 人全部有技能绑定。
        var skilledUsers = await db.PersonnelSkills.Select(x => x.UserId).Distinct().ToArrayAsync();
        Assert.Equal(54, skilledUsers.Length);
        Assert.All(
            WorldBibleSpec.Employees.Where(x => x.SkillCodes.Length == 0),
            employee => Assert.Equal("DEPT-BIZ", employee.DepartmentCode));

        var device = await db.DeviceAssets.SingleAsync(x => x.Code == "DEV-CNC-01");
        Assert.Equal("数控车床 CK6150", device.Model);
        Assert.Equal("WC-ROD-01", device.WorkCenterCode);
        Assert.Equal("LINE-WB-ROD-01", device.LineCode);
        Assert.Equal(WorldBibleSpec.MachiningWorkshopCode, device.WorkshopCode);
        Assert.Equal(WorldBibleSpec.SiteCode, device.SiteCode);
        Assert.True(device.TelemetryEnabled);
    }

    [Fact]
    public async Task Seed_leaves_the_frozen_leader_demo_facts_untouched()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");

        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        // LINE-DEMO-01 的 WorkshopCode 必须保持为空——固定案例断言依赖该事实。
        var demoLine = await db.ProductionLines.SingleAsync(x => x.Code == "LINE-DEMO-01");
        Assert.Null(demoLine.WorkshopCode);
        Assert.Single(await db.WorkCenters.Where(x => x.Code == "WC-CNC-DEMO").ToArrayAsync());
        Assert.Single(await db.DeviceAssets.Where(x => x.Code == "DEV-CNC-DEMO").ToArrayAsync());
        Assert.Single(await db.Skus.Where(x => x.Code == "SKU-DEMO-001").ToArrayAsync());
        Assert.Single(await db.BusinessPartners.Where(x => x.Code == "CUST-DEMO-001").ToArrayAsync());
        Assert.Equal("一号工厂", (await db.Sites.SingleAsync(x => x.Code == "SITE-001")).Name);
    }

    [Fact]
    public async Task Seed_coexists_with_the_scale_block()
    {
        await using var db = CreateDbContext();
        await new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev");
        await new LeaderDemoScaleSeedService(db).SeedAsync("org-001", "env-dev", 1000);

        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal(4, await db.WorkCenters.CountAsync(x => x.Code.StartsWith("WC-SCALE-")));
        Assert.Equal(24, await db.DeviceAssets.CountAsync(x => x.Code.StartsWith("DEV-SCALE-")));
        Assert.Equal(6, await db.Skus.CountAsync(x => x.Code.StartsWith("SKU-SCALE-0")));
        Assert.Single(await db.ProductionLines.Where(x => x.Code == "LINE-SCALE-01").ToArrayAsync());
        Assert.Equal(46, await db.DeviceAssets.CountAsync(x => x.Code.StartsWith("DEV-") && !x.Code.Contains("SCALE") && !x.Code.Contains("DEMO")));
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_tenant_fact_without_overwriting_it()
    {
        await using var db = CreateDbContext();
        db.ProductionLines.Add(ProductionLine.Create("org-001", "env-dev", "LINE-WB-ROD-01", "租户自维护产线", "SITE-002", null));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains("LINE-WB-ROD-01", exception.Message, StringComparison.Ordinal);
        Assert.Equal("租户自维护产线", (await db.ProductionLines.SingleAsync(x => x.Code == "LINE-WB-ROD-01")).Name);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-world-bible-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldBibleSeedTestMediator());
    }

    private sealed class WorldBibleSeedTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
