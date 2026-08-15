using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
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
        // 员工档案落 MasterData（「人」的业务权威），58 人一人一条，工号 EMP-001..058。
        Assert.Equal(58, await db.Workers.CountAsync());
        // 班组是车间级的：6 个班组必须各自挂到所属车间，否则派工按工作中心收敛会查空。
        Assert.Empty(await db.Teams.Where(x => x.WorkshopCode == null).ToArrayAsync());
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
    public async Task Dispatch_candidates_resolve_from_a_work_center_to_that_workshop_crew()
    {
        await using var db = CreateDbContext();
        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        // WC-ROD-01 属机加车间 → 该车间两个班组（早班 / 中班）的在册成员即候选人。
        var response = await new ListWorkerDirectoryQueryHandler(db).Handle(
            new ListWorkerDirectoryQuery("org-001", "env-dev", WorkCenterCode: "WC-ROD-01", PageSize: 200),
            CancellationToken.None);

        var machiningTeamCodes = WorldBibleSpec.Teams
            .Where(x => x.WorkshopCode == WorldBibleSpec.MachiningWorkshopCode)
            .Select(x => x.Code)
            .ToArray();
        var expected = WorldBibleSpec.Employees
            .Where(x => x.TeamCode is not null && machiningTeamCodes.Contains(x.TeamCode))
            .Select(x => x.UserId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 既不是空（回归 #1127 与 #1124 的对撞），也不是把全厂 58 人一股脑列出来。
        Assert.NotEmpty(expected);
        Assert.Equal(expected, response.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
        Assert.True(response.TotalCount < 58);
        Assert.All(response.Items, item => Assert.StartsWith("EMP-", item.EmployeeNo, StringComparison.Ordinal));
        Assert.All(response.Items, item => Assert.NotEmpty(item.Teams));
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

    [Fact]
    public async Task Seed_backfills_customer_credit_limits_without_touching_tenant_values()
    {
        await using var db = CreateDbContext();
        var seed = new WorldBibleSeedService(db);

        // 预置两个既有客户：一个租户已维护额度（不许动），一个额度为空（该补）。
        db.BusinessPartners.Add(Domain.AggregatesModel.BusinessPartnerAggregate.BusinessPartner.Create(
            "org-001", "env-dev", "CUST-WB-001", "customer", "长三角整车一厂",
            ["customer"], taxId: null, creditLimit: 5_000_000m, creditCurrencyCode: "CNY"));
        db.BusinessPartners.Add(Domain.AggregatesModel.BusinessPartnerAggregate.BusinessPartner.Create(
            "org-001", "env-dev", "CUST-WB-002", "customer", "长三角整车二厂"));
        await db.SaveChangesAsync();

        await seed.SeedAsync("org-001", "env-dev");

        // 全部 7 家世界观客户都有信用额度档案（#1290：额度全空导致任何转订单必 400）。
        var customers = await db.BusinessPartners
            .Where(x => x.Code.StartsWith("CUST-WB-"))
            .ToArrayAsync();
        Assert.Equal(7, customers.Length);
        Assert.All(customers, customer =>
        {
            Assert.NotNull(customer.CreditLimit);
            Assert.Equal("CNY", customer.CreditCurrencyCode);
        });

        // 租户已维护的额度保持不变；空额度按档案补齐。
        Assert.Equal(5_000_000m, customers.Single(x => x.Code == "CUST-WB-001").CreditLimit);
        Assert.Equal(22_000_000m, customers.Single(x => x.Code == "CUST-WB-002").CreditLimit);

        // 低额度演示客户（信用冻结场景）：路航售后连锁 / 华远国际贸易。
        Assert.Equal(1_500_000m, customers.Single(x => x.Code == "CUST-WB-005").CreditLimit);
        Assert.Equal(800_000m, customers.Single(x => x.Code == "CUST-WB-007").CreditLimit);

        // 供应商不参与销售信用检查，不补额度。
        var suppliers = await db.BusinessPartners.Where(x => x.Code.StartsWith("SUP-WB-")).ToArrayAsync();
        Assert.Equal(10, suppliers.Length);
        Assert.All(suppliers, supplier => Assert.Null(supplier.CreditLimit));
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
