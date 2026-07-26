using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 员工目录读面：派工候选靠「工作中心 → 车间 → 该车间班组 → 班组成员」收敛（班组是车间级的，
/// 一个班次的人覆盖本车间全部工作中心），技能只算当前有效的登记。
/// 这些过滤是派工弹窗候选人正确与否的唯一依据，必须锁死。
/// </summary>
public sealed class WorkerDirectoryQueryTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private static readonly DateOnly Past = new(2020, 1, 1);
    private static readonly DateOnly Future = new(2099, 12, 31);

    [Fact]
    public async Task Work_center_filter_resolves_through_the_workshop_of_that_work_center()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, WorkCenterCode: "WC-CNC"));

        Assert.Equal(["user-cnc-01", "user-cnc-02"], response.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Team_filter_narrows_to_the_single_team()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, TeamCode: "TEAM-ASSY"));

        Assert.Equal(["user-assy-01"], response.Items.Select(x => x.UserId));
    }

    [Fact]
    public async Task Expired_membership_is_not_a_dispatch_candidate()
    {
        await using var db = CreateDbContext();
        Seed(db);
        db.TeamMembers.Add(TeamMember.Assign(Org, Env, "TEAM-CNC", "user-left-01", false, Past, new DateOnly(2021, 1, 1)));
        db.Workers.Add(Worker.Create(Org, Env, "EMP-9001", "离岗人员", "user-left-01", "DEPT-PROD", null, Worker.StatusActive, null));
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, WorkCenterCode: "WC-CNC"));

        Assert.DoesNotContain(response.Items, x => x.UserId == "user-left-01");
    }

    [Fact]
    public async Task Employment_status_filter_keeps_only_on_duty_workers()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, EmploymentStatus: Worker.StatusActive));

        Assert.DoesNotContain(response.Items, x => x.UserId == "user-cnc-02");
    }

    [Fact]
    public async Task Skill_filter_ignores_expired_skill_records()
    {
        await using var db = CreateDbContext();
        Seed(db);
        // 过期技能不该让人出现在候选里。
        db.PersonnelSkills.Add(PersonnelSkill.Assign(Org, Env, "user-assy-01", "cnc-operation", "junior", Past, new DateOnly(2021, 1, 1)));
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, SkillCode: "cnc-operation"));

        Assert.Equal(["user-cnc-01"], response.Items.Select(x => x.UserId));
    }

    [Fact]
    public async Task Directory_row_carries_team_skill_and_department_display_names()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, UserId: "user-cnc-01"));

        var row = Assert.Single(response.Items);
        Assert.Equal("EMP-1001", row.EmployeeNo);
        Assert.Equal("陈志强", row.Name);
        Assert.Equal("生产部", row.DepartmentName);
        var team = Assert.Single(row.Teams);
        Assert.Equal("CNC 精加工班组", team.TeamName);
        Assert.Equal("WS-MC", team.WorkshopCode);
        Assert.True(team.IsLeader);
        var skill = Assert.Single(row.Skills);
        Assert.Equal("CNC 操作", skill.SkillName);
        Assert.Equal("senior", skill.Level);
    }

    [Fact]
    public async Task Disabled_workers_are_hidden_unless_requested()
    {
        await using var db = CreateDbContext();
        Seed(db);
        var archived = Worker.Create(Org, Env, "EMP-9002", "已离职", "user-gone-01", "DEPT-PROD", null, Worker.StatusResigned, null);
        archived.Disable("离职归档");
        db.Workers.Add(archived);
        await db.SaveChangesAsync();

        var hidden = await Handle(db, new ListWorkerDirectoryQuery(Org, Env));
        Assert.DoesNotContain(hidden.Items, x => x.UserId == "user-gone-01");

        var shown = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, IncludeDisabled: true));
        Assert.Contains(shown.Items, x => x.UserId == "user-gone-01" && !x.Active);
    }

    [Fact]
    public async Task Sibling_work_center_in_the_same_workshop_sees_the_same_crew()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, WorkCenterCode: "WC-GRD"));

        Assert.Equal(["user-cnc-01", "user-cnc-02"], response.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Work_center_without_a_workshop_yields_no_candidates_instead_of_the_whole_plant()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();

        var response = await Handle(db, new ListWorkerDirectoryQuery(Org, Env, WorkCenterCode: "WC-ORPHAN"));

        // 宁可空候选让前端给出显式出路，也不能悄悄降级成「全厂在岗」。
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    private static Task<ListWorkerDirectoryResponse> Handle(ApplicationDbContext db, ListWorkerDirectoryQuery query) =>
        new ListWorkerDirectoryQueryHandler(db).Handle(query, CancellationToken.None);

    private static void Seed(ApplicationDbContext db)
    {
        db.Departments.Add(Department.Create(Org, Env, "DEPT-PROD", "生产部", null));
        db.Skills.Add(Skill.Create(Org, Env, "cnc-operation", "CNC 操作", "设备操作", false, null, null));

        db.Teams.Add(Team.Create(Org, Env, "TEAM-CNC", "CNC 精加工班组", "DEPT-PROD", "DAY", "WS-MC"));
        db.Teams.Add(Team.Create(Org, Env, "TEAM-ASSY", "装配班组", "DEPT-PROD", "DAY", "WS-AS"));

        // 同一车间挂两个工作中心：任一个都应收敛到该车间的班组，验证不是 1:1 绑定。
        db.WorkCenters.Add(WorkCenter.CreateResource(Org, Env, "WC-CNC", "CNC 加工中心", 480, "work-center", "SITE-1", "LINE-1", "WS-MC", "STANDARD", "minute", true));
        db.WorkCenters.Add(WorkCenter.CreateResource(Org, Env, "WC-GRD", "精磨中心", 480, "work-center", "SITE-1", "LINE-1", "WS-MC", "STANDARD", "minute", true));
        db.WorkCenters.Add(WorkCenter.CreateResource(Org, Env, "WC-ASSY", "装配中心", 480, "work-center", "SITE-1", "LINE-2", "WS-AS", "STANDARD", "minute", true));
        db.WorkCenters.Add(WorkCenter.CreateResource(Org, Env, "WC-ORPHAN", "未挂车间的中心", 480, "work-center", "SITE-1", "LINE-1", "STANDARD", "minute", true));

        db.Workers.Add(Worker.Create(Org, Env, "EMP-1001", "陈志强", "user-cnc-01", "DEPT-PROD", "CNC 操作工", Worker.StatusActive, null));
        db.Workers.Add(Worker.Create(Org, Env, "EMP-1002", "李海涛", "user-cnc-02", "DEPT-PROD", "CNC 操作工", Worker.StatusOnLeave, null));
        db.Workers.Add(Worker.Create(Org, Env, "EMP-1003", "王建军", "user-assy-01", "DEPT-PROD", "装配操作工", Worker.StatusActive, null));

        db.TeamMembers.Add(TeamMember.Assign(Org, Env, "TEAM-CNC", "user-cnc-01", true, Past, null));
        db.TeamMembers.Add(TeamMember.Assign(Org, Env, "TEAM-CNC", "user-cnc-02", false, Past, null));
        db.TeamMembers.Add(TeamMember.Assign(Org, Env, "TEAM-ASSY", "user-assy-01", false, Past, null));

        db.PersonnelSkills.Add(PersonnelSkill.Assign(Org, Env, "user-cnc-01", "cnc-operation", "senior", Past, Future));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"worker-directory-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
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
