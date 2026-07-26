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

        Assert.Equal("早班", (await db.Shifts.SingleAsync(x => x.Code == "DAY")).Name);
        Assert.Equal("晚班", (await db.Shifts.SingleAsync(x => x.Code == "NIGHT")).Name);
        Assert.Equal("标准工作日历", (await db.WorkCalendars.SingleAsync(x => x.Code == "STANDARD")).Name);
        Assert.Equal("千克", (await db.UnitsOfMeasure.SingleAsync(x => x.Code == "kg")).Name);
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
        Assert.Equal("装配一线早班组", team.Name);
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
