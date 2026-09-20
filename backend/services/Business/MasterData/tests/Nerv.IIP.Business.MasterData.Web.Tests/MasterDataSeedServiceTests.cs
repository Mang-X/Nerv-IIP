using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 常规（非 leader-demo）主数据 seed：显示名必须为中文，且技能目录/人员技能/产品分类/部门
/// 这些页面可见的基础目录不得为空；重复执行幂等，已存在的租户事实一律不覆写。
/// </summary>
public sealed class MasterDataSeedServiceTests
{
    [Fact]
    public async Task Seed_uses_chinese_display_names()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        // 显示名的权威是产品文档 docs/product/master-data/design.md §5.3：
        // DAY=白班(08:00-20:00)、NIGHT=夜班(20:00-08:00,跨天)。这两条断言钉的是「种子与该约定一致」，
        // 不是「DAY 这个码天生叫什么」——#3473 之前它们被写成「早班」「晚班」，其中「早班」还与
        // 设定集种子的 EARLY(08–16) 撞名，PDA 班次选择器里出现两条「早班」，操作工分不清选哪个。
        Assert.Equal("白班", (await db.Shifts.SingleAsync(x => x.Code == "DAY")).Name);
        Assert.Equal("夜班", (await db.Shifts.SingleAsync(x => x.Code == "NIGHT")).Name);
        Assert.Equal("标准工作日历", (await db.WorkCalendars.SingleAsync(x => x.Code == "STANDARD")).Name);
        Assert.Equal("千克", (await db.UnitsOfMeasure.SingleAsync(x => x.Code == "kg")).Name);
        Assert.Equal(
            "原料库",
            (await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "inventory-location" && x.Code == "loc-raw-01")).Name);
        Assert.Equal("生产工单", (await db.CodeRules.SingleAsync(x => x.RuleKey == "work-order")).DisplayName);
        Assert.Equal("工艺路线", (await db.CodeRules.SingleAsync(x => x.RuleKey == "routing")).DisplayName);

        Assert.DoesNotContain(
            await db.CodeRules.Select(x => x.DisplayName).ToArrayAsync(),
            name => name.Any(ch => ch is >= 'a' and <= 'z'));
    }

    [Fact]
    public async Task Seed_fills_department_team_skill_and_category_catalogs()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal("生产部", (await db.Departments.SingleAsync(x => x.Code == "DEPT-PROD")).Name);
        Assert.Equal(5, await db.Departments.CountAsync());

        var rootCategory = await db.ProductCategories.SingleAsync(x => x.CategoryCode == "PCAT-SHOCK");
        Assert.Equal("减振器总成", rootCategory.CategoryName);
        Assert.Null(rootCategory.ParentCode);
        Assert.Equal("PCAT-SHOCK", (await db.ProductCategories.SingleAsync(x => x.CategoryCode == "PCAT-SHOCK-FR")).ParentCode);

        var skill = await db.Skills.SingleAsync(x => x.SkillCode == "cnc-operation");
        Assert.Equal("CNC 操作", skill.SkillName);
        Assert.True(skill.RequiresCertification);
        Assert.Equal(24, skill.ValidityMonths);
        Assert.Equal(6, await db.Skills.CountAsync());

        var team = await db.Teams.SingleAsync(x => x.Code == "TEAM-ASSY-A");
        Assert.Equal("装配一线白班组", team.Name);
        Assert.Equal("DEPT-PROD", team.DepartmentCode);
        Assert.Equal("DAY", team.ShiftCode);
        Assert.Equal(3, await db.TeamMembers.CountAsync(x => x.TeamCode == "TEAM-ASSY-A"));
        Assert.Single(await db.TeamMembers.Where(x => x.TeamCode == "TEAM-ASSY-A" && x.IsLeader).ToArrayAsync());

        // 每个班组成员至少绑定 2 条技能，技能矩阵页不再空白。
        var personnelSkills = await db.PersonnelSkills.ToArrayAsync();
        Assert.All(
            personnelSkills.GroupBy(x => x.UserId, StringComparer.Ordinal),
            group => Assert.True(group.Count() >= 2));
        var skillCodes = await db.Skills.Select(x => x.SkillCode).ToArrayAsync();
        Assert.All(personnelSkills, x => Assert.Contains(x.SkillCode, skillCodes));
    }

    [Fact]
    public async Task Seed_is_idempotent_and_keeps_tenant_facts()
    {
        await using var db = CreateDbContext();
        db.Departments.Add(Department.Create("org-001", "env-dev", "DEPT-PROD", "制造中心", null));
        db.Skills.Add(Skill.Create("org-001", "env-dev", "welding", "焊工（租户）", "自定义组", false, null, null));
        await db.SaveChangesAsync();

        var seed = new MasterDataSeedService(db);
        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal("制造中心", (await db.Departments.SingleAsync(x => x.Code == "DEPT-PROD")).Name);
        Assert.Equal("焊工（租户）", (await db.Skills.SingleAsync(x => x.SkillCode == "welding")).SkillName);
        Assert.Equal(5, await db.Departments.CountAsync());
        Assert.Equal(6, await db.Skills.CountAsync());
        Assert.Equal(6, await db.ProductCategories.CountAsync());
        Assert.Equal(2, await db.Teams.CountAsync());
        Assert.Equal(6, await db.TeamMembers.CountAsync());
        Assert.Equal(13, await db.PersonnelSkills.CountAsync());
        Assert.Equal(
            MasterDataDictionaryRules.StandardReferenceData.Count(x => x.CodeSet == "inventory-location"),
            await db.ReferenceDataCodes.CountAsync(x =>
                x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.CodeSet == "inventory-location"));
    }

    /// <summary>
    /// #3473 的真不变量：**同一套栈上两个种子并排跑完之后，班次显示名两两可区分**。
    ///
    /// 光钉「DAY 叫什么」钉不住这件事——撞名是跨种子的（常规种子的 DAY 08:00–20:00／720 分
    /// 与设定集种子的 EARLY 08:00–16:00／480 分是两个真不同的班次，只是名字起重了），
    /// 任一种子单独看都自洽。操作工在班次选择器里只看得到显示名，重名即不可选。
    /// </summary>
    [Fact]
    public async Task Shift_display_names_stay_distinguishable_across_both_seeds()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");
        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        var shifts = await db.Shifts
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => new { x.Code, x.Name })
            .ToArrayAsync();

        Assert.Equal(
            ["DAY", "EARLY", "MIDDLE", "NIGHT"],
            shifts.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal));
        var duplicated = shifts
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}={string.Join('+', group.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal))}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([], duplicated);
    }

    /// <summary>
    /// 班组名不得与它绑定的班次自相矛盾。
    ///
    /// <para>这不是「读着别扭」：#3473 把 <c>DAY</c> 改名为「白班」之后，原封不动的班组名
    /// 「装配一线**早**班组」绑在「**白**班」上，屏上一行就同时写着两个班次词，
    /// 与本票要修的「操作工看到的字说了假话」是同一形状——而且这一次是改名**制造**出来的。</para>
    ///
    /// <para>判据写成结构性的而不是逐个班组点名：班组名里**不得出现它自己那个班次以外的任何班次显示名**。
    /// 这样将来再改任一侧的名字都会在这里显影，不用维护一张会漂的对照表。
    /// 「CNC 精加工班组」这类不含班次词的名字天然不触发。</para>
    /// </summary>
    [Fact]
    public async Task Team_names_never_contradict_the_shift_they_are_bound_to()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");
        await new WorldBibleSeedService(db).SeedAsync("org-001", "env-dev");

        var shiftNames = await db.Shifts
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => new { x.Code, x.Name })
            .ToArrayAsync();
        var teams = await db.Teams
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => new { x.Code, x.Name, x.ShiftCode })
            .ToArrayAsync();

        Assert.NotEmpty(shiftNames);
        Assert.NotEmpty(teams);

        var contradictions = (
            from team in teams
            from shift in shiftNames
            where !string.Equals(shift.Code, team.ShiftCode, StringComparison.Ordinal)
                && team.Name.Contains(shift.Name, StringComparison.Ordinal)
            select $"{team.Code}「{team.Name}」绑定 {team.ShiftCode}，名字里却写着另一个班次「{shift.Name}」({shift.Code})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], contradictions);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new SeedTestMediator());
    }

    private sealed class SeedTestMediator : IMediator
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
