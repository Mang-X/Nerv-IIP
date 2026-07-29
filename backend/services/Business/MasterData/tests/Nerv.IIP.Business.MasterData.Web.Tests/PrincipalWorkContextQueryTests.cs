using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class PrincipalWorkContextQueryTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private static readonly DateOnly EffectiveFrom = new(2020, 1, 1);

    [Fact]
    public async Task Current_worker_context_returns_multi_team_work_center_and_shift_names()
    {
        await using var db = CreateDbContext();
        db.Departments.Add(Department.Create(OrganizationId, EnvironmentId, "DEPT-PROD", "生产部", null));
        db.Sites.Add(Site.Create(OrganizationId, EnvironmentId, "SITE-NJ", "南京工厂", "Asia/Shanghai"));
        db.Workshops.AddRange(
            Workshop.Create(OrganizationId, EnvironmentId, "WS-MC", "机加工车间", "SITE-NJ", null, null),
            Workshop.Create(OrganizationId, EnvironmentId, "WS-AS", "装配车间", "SITE-NJ", null, null));
        db.ProductionLines.AddRange(
            ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-MC", "机加工产线", "SITE-NJ", "WS-MC"),
            ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-AS", "装配产线", "SITE-NJ", "WS-AS"));
        db.Shifts.AddRange(
            Shift.Create(OrganizationId, EnvironmentId, "SHIFT-DAY", "白班", new TimeOnly(8, 0), new TimeOnly(16, 0), 480),
            Shift.Create(OrganizationId, EnvironmentId, "SHIFT-NIGHT", "夜班", new TimeOnly(20, 0), new TimeOnly(4, 0), 480));
        db.Teams.AddRange(
            Team.Create(OrganizationId, EnvironmentId, "TEAM-CNC", "机加工一班", "DEPT-PROD", "SHIFT-DAY", "WS-MC"),
            Team.Create(OrganizationId, EnvironmentId, "TEAM-ASSY", "装配支援组", "DEPT-PROD", "SHIFT-NIGHT", "WS-AS"));
        db.WorkCenters.AddRange(
            WorkCenter.CreateResource(OrganizationId, EnvironmentId, "WC-CNC", "数控加工中心", 480, "work-center", "SITE-NJ", "LINE-MC", "WS-MC", "STANDARD", "minute", true),
            WorkCenter.CreateResource(OrganizationId, EnvironmentId, "WC-GRD", "精磨中心", 480, "work-center", "SITE-NJ", "LINE-MC", "WS-MC", "STANDARD", "minute", true),
            WorkCenter.CreateResource(OrganizationId, EnvironmentId, "WC-ASSY", "装配中心", 480, "work-center", "SITE-NJ", "LINE-AS", "WS-AS", "STANDARD", "minute", true));
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-010",
            "吴桂芳",
            "user-emp-010",
            "DEPT-PROD",
            "机加操作工",
            Worker.StatusActive,
            null));
        db.TeamMembers.AddRange(
            TeamMember.Assign(OrganizationId, EnvironmentId, "TEAM-CNC", "user-emp-010", true, EffectiveFrom, null),
            TeamMember.Assign(OrganizationId, EnvironmentId, "TEAM-ASSY", "user-emp-010", false, EffectiveFrom, null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-emp-010"),
            CancellationToken.None);

        Assert.Equal("ready-with-gaps", result.ResolutionStatus);
        Assert.Equal("EMP-010", result.Worker!.EmployeeNo);
        Assert.Equal("吴桂芳", result.Worker.Name);
        Assert.Equal("机加操作工", result.Worker.JobTitle);
        Assert.Equal(["TEAM-ASSY", "TEAM-CNC"], result.Teams.Select(x => x.Id));
        Assert.Equal(["WC-ASSY", "WC-CNC", "WC-GRD"], result.CoveredWorkCenters.Select(x => x.Id));
        Assert.Equal(["WS-AS", "WS-MC"], result.Workshops.Select(x => x.Id));
        Assert.Equal(["SHIFT-DAY", "SHIFT-NIGHT"], result.Shifts.Select(x => x.Id));
        Assert.Equal(["SITE-NJ"], result.Sites.Select(x => x.Id));
        Assert.Contains("work-center", result.CandidateScopeKinds);
        Assert.Contains("site", result.CandidateScopeKinds);
        Assert.Contains(result.CandidateScopes, x =>
            x.Kind == "site"
            && x.Id == "SITE-NJ"
            && x.Relationship == "resolved-site"
            && x.Ancestors.Contains(new WorkContextScopeAncestor("organization", OrganizationId)));
        Assert.Contains(result.CandidateScopes, x =>
            x.Kind == "work-center"
            && x.Id == "WC-CNC"
            && x.Relationship == "workshop-covered"
            && x.Ancestors.Contains(new WorkContextScopeAncestor("workshop", "WS-MC"))
            && x.Ancestors.Contains(new WorkContextScopeAncestor("site", "SITE-NJ"))
            && x.Ancestors.Contains(new WorkContextScopeAncestor("production-line", "LINE-MC")));
        Assert.Contains(result.CandidateScopes, x =>
            x.Kind == "team"
            && x.Id == "TEAM-CNC"
            && x.Ancestors.Contains(new WorkContextScopeAncestor("workshop", "WS-MC"))
            && x.Ancestors.Contains(new WorkContextScopeAncestor("site", "SITE-NJ")));
        Assert.Contains(result.CandidateScopes, x =>
            x.Kind == "self"
            && x.Ancestors.Contains(new WorkContextScopeAncestor("workshop", "WS-MC"))
            && x.Ancestors.Contains(new WorkContextScopeAncestor("workshop", "WS-AS"))
            && x.Ancestors.Contains(new WorkContextScopeAncestor("site", "SITE-NJ")));
        Assert.Equal(["position-master-not-modeled"], result.Issues);
    }

    [Fact]
    public async Task Missing_worker_mapping_is_explicit_and_does_not_fall_back_to_factory_scope()
    {
        await using var db = CreateDbContext();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-without-worker"),
            CancellationToken.None);

        Assert.Equal("worker-not-mapped", result.ResolutionStatus);
        Assert.Null(result.Worker);
        Assert.Empty(result.CoveredWorkCenters);
        Assert.Empty(result.CandidateScopeKinds);
        Assert.Equal(["worker-not-mapped"], result.Issues);
    }

    [Fact]
    public async Task Duplicate_worker_mapping_fails_closed_instead_of_picking_the_first_row()
    {
        await using var db = CreateDbContext();
        db.Workers.AddRange(
            Worker.Create(OrganizationId, EnvironmentId, "EMP-901", "重复甲", "user-duplicate", null, null, Worker.StatusActive, null),
            Worker.Create(OrganizationId, EnvironmentId, "EMP-902", "重复乙", "user-duplicate", null, null, Worker.StatusActive, null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-duplicate"),
            CancellationToken.None);

        Assert.Equal("worker-mapping-conflict", result.ResolutionStatus);
        Assert.Null(result.Worker);
        Assert.Empty(result.CandidateScopeKinds);
        Assert.Equal(["worker-mapping-conflict"], result.Issues);
    }

    [Fact]
    public async Task Missing_shift_mapping_is_reported_without_inventing_a_shift()
    {
        await using var db = CreateDbContext();
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-011",
            "无班次人员",
            "user-no-shift",
            null,
            "装配操作工",
            Worker.StatusActive,
            null));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-NO-SHIFT",
            "待配置班组",
            "DEPT-PROD",
            "SHIFT-MISSING",
            null));
        db.TeamMembers.Add(TeamMember.Assign(
            OrganizationId,
            EnvironmentId,
            "TEAM-NO-SHIFT",
            "user-no-shift",
            false,
            EffectiveFrom,
            null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-no-shift"),
            CancellationToken.None);

        Assert.Equal("incomplete", result.ResolutionStatus);
        Assert.Empty(result.Shifts);
        Assert.DoesNotContain("shift", result.CandidateScopeKinds);
        Assert.Equal(
            [
                "position-master-not-modeled",
                "shift-not-mapped:TEAM-NO-SHIFT:SHIFT-MISSING",
                "workshop-not-mapped:TEAM-NO-SHIFT",
            ],
            result.Issues);
    }

    [Fact]
    public async Task Worker_without_an_effective_team_has_explicit_team_and_shift_results()
    {
        await using var db = CreateDbContext();
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-049",
            "周文斌",
            "user-emp-049",
            "DEPT-WH",
            "库管",
            Worker.StatusActive,
            null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-emp-049"),
            CancellationToken.None);

        Assert.Equal("incomplete", result.ResolutionStatus);
        Assert.Empty(result.Teams);
        Assert.Empty(result.Shifts);
        Assert.Equal(
            [
                "position-master-not-modeled",
                "shift-not-assigned",
                "team-not-assigned",
            ],
            result.Issues);
    }

    [Fact]
    public async Task Inactive_worker_keeps_identity_facts_but_has_no_operation_candidates()
    {
        await using var db = CreateDbContext();
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-099",
            "休假人员",
            "user-on-leave",
            "DEPT-PROD",
            "机加操作工",
            Worker.StatusOnLeave,
            null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-on-leave"),
            CancellationToken.None);

        Assert.Equal("worker-inactive", result.ResolutionStatus);
        Assert.Equal("EMP-099", result.Worker!.EmployeeNo);
        Assert.Empty(result.CandidateScopes);
        Assert.Empty(result.CandidateScopeKinds);
        Assert.Contains("worker-inactive", result.Issues);
    }

    [Fact]
    public async Task Orphan_membership_and_team_relationships_are_reported_without_expanding_candidates()
    {
        await using var db = CreateDbContext();
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-012",
            "装配人员",
            "user-orphan-team",
            "DEPT-PROD",
            "装配操作工",
            Worker.StatusActive,
            null));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-ORPHAN",
            "孤立班组",
            "DEPT-PROD",
            "SHIFT-MISSING",
            "WS-MISSING"));
        db.TeamMembers.AddRange(
            TeamMember.Assign(
                OrganizationId,
                EnvironmentId,
                "TEAM-ORPHAN",
                "user-orphan-team",
                false,
                EffectiveFrom,
                null),
            TeamMember.Assign(
                OrganizationId,
                EnvironmentId,
                "TEAM-NOT-FOUND",
                "user-orphan-team",
                false,
                EffectiveFrom,
                null));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-orphan-team"),
            CancellationToken.None);

        Assert.Contains("team-not-mapped:TEAM-NOT-FOUND", result.Issues);
        Assert.Contains("workshop-not-mapped:TEAM-ORPHAN:WS-MISSING", result.Issues);
        Assert.Contains("shift-not-mapped:TEAM-ORPHAN:SHIFT-MISSING", result.Issues);
        Assert.DoesNotContain(result.CandidateScopes, x =>
            x.Kind is "work-center" or "workshop"
            && x.Id.Contains("MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Work_centers_under_an_unresolved_workshop_are_not_candidates()
    {
        await using var db = CreateDbContext();
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-013",
            "孤立车间人员",
            "user-orphan-workshop",
            null,
            "操作工",
            Worker.StatusActive,
            null));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-ORPHAN-WS",
            "孤立车间班组",
            "DEPT-PROD",
            "SHIFT-MISSING",
            "WS-NOT-FOUND"));
        db.TeamMembers.Add(TeamMember.Assign(
            OrganizationId,
            EnvironmentId,
            "TEAM-ORPHAN-WS",
            "user-orphan-workshop",
            false,
            EffectiveFrom,
            null));
        db.WorkCenters.Add(WorkCenter.CreateResource(
            OrganizationId,
            EnvironmentId,
            "WC-ORPHAN",
            "孤立工作中心",
            480,
            "work-center",
            "SITE-NJ",
            "",
            "WS-NOT-FOUND",
            "STANDARD",
            "minute",
            true));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-orphan-workshop"),
            CancellationToken.None);

        Assert.DoesNotContain(result.CoveredWorkCenters, x => x.Id == "WC-ORPHAN");
        Assert.DoesNotContain(result.CandidateScopes, x => x.Kind == "work-center" && x.Id == "WC-ORPHAN");
        Assert.Contains("workshop-not-mapped:TEAM-ORPHAN-WS:WS-NOT-FOUND", result.Issues);
    }

    [Fact]
    public async Task Work_center_with_conflicting_production_line_lineage_is_not_a_candidate()
    {
        await using var db = CreateDbContext();
        db.Sites.Add(Site.Create(OrganizationId, EnvironmentId, "SITE-NJ", "南京工厂", "Asia/Shanghai"));
        db.Workshops.AddRange(
            Workshop.Create(OrganizationId, EnvironmentId, "WS-MC", "机加工车间", "SITE-NJ", null, null),
            Workshop.Create(OrganizationId, EnvironmentId, "WS-AS", "装配车间", "SITE-NJ", null, null));
        db.ProductionLines.Add(ProductionLine.Create(
            OrganizationId,
            EnvironmentId,
            "LINE-AS",
            "装配产线",
            "SITE-NJ",
            "WS-AS"));
        db.Shifts.Add(Shift.Create(
            OrganizationId,
            EnvironmentId,
            "SHIFT-DAY",
            "白班",
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            480));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-CNC",
            "机加工一班",
            "DEPT-PROD",
            "SHIFT-DAY",
            "WS-MC"));
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-014",
            "错配产线人员",
            "user-lineage-conflict",
            null,
            "操作工",
            Worker.StatusActive,
            null));
        db.TeamMembers.Add(TeamMember.Assign(
            OrganizationId,
            EnvironmentId,
            "TEAM-CNC",
            "user-lineage-conflict",
            false,
            EffectiveFrom,
            null));
        db.WorkCenters.Add(WorkCenter.CreateResource(
            OrganizationId,
            EnvironmentId,
            "WC-CROSS",
            "跨车间工作中心",
            480,
            "work-center",
            "SITE-NJ",
            "LINE-AS",
            "WS-MC",
            "STANDARD",
            "minute",
            true));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-lineage-conflict"),
            CancellationToken.None);

        Assert.DoesNotContain(result.CoveredWorkCenters, x => x.Id == "WC-CROSS");
        Assert.DoesNotContain(result.CandidateScopes, x => x.Kind == "work-center" && x.Id == "WC-CROSS");
        Assert.Contains("production-line-lineage-conflict:WC-CROSS:LINE-AS", result.Issues);
    }

    [Fact]
    public async Task Work_center_under_a_workshop_with_no_resolved_site_is_not_a_candidate()
    {
        await using var db = CreateDbContext();
        db.Workshops.Add(Workshop.Create(
            OrganizationId,
            EnvironmentId,
            "WS-NO-SITE",
            "缺站点车间",
            "SITE-MISSING",
            null,
            null));
        db.Shifts.Add(Shift.Create(
            OrganizationId,
            EnvironmentId,
            "SHIFT-DAY",
            "白班",
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            480));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-NO-SITE",
            "缺站点班组",
            "DEPT-PROD",
            "SHIFT-DAY",
            "WS-NO-SITE"));
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-015",
            "缺站点人员",
            "user-no-site",
            null,
            "操作工",
            Worker.StatusActive,
            null));
        db.TeamMembers.Add(TeamMember.Assign(
            OrganizationId,
            EnvironmentId,
            "TEAM-NO-SITE",
            "user-no-site",
            false,
            EffectiveFrom,
            null));
        db.WorkCenters.Add(WorkCenter.CreateResource(
            OrganizationId,
            EnvironmentId,
            "WC-NO-SITE",
            "缺站点工作中心",
            480,
            "work-center",
            "SITE-MISSING",
            "",
            "WS-NO-SITE",
            "STANDARD",
            "minute",
            true));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-no-site"),
            CancellationToken.None);

        Assert.DoesNotContain(result.CoveredWorkCenters, x => x.Id == "WC-NO-SITE");
        Assert.DoesNotContain(result.CandidateScopes, x => x.Kind == "work-center" && x.Id == "WC-NO-SITE");
        Assert.Contains("site-not-mapped:WS-NO-SITE:SITE-MISSING", result.Issues);
        Assert.Contains("work-center-site-not-mapped:WC-NO-SITE:WS-NO-SITE", result.Issues);
    }

    [Fact]
    public async Task Work_center_with_a_site_conflicting_with_its_workshop_is_not_a_candidate()
    {
        await using var db = CreateDbContext();
        db.Sites.AddRange(
            Site.Create(OrganizationId, EnvironmentId, "SITE-NJ", "南京工厂", "Asia/Shanghai"),
            Site.Create(OrganizationId, EnvironmentId, "SITE-SH", "上海工厂", "Asia/Shanghai"));
        db.Workshops.Add(Workshop.Create(
            OrganizationId,
            EnvironmentId,
            "WS-MC",
            "机加工车间",
            "SITE-NJ",
            null,
            null));
        db.Shifts.Add(Shift.Create(
            OrganizationId,
            EnvironmentId,
            "SHIFT-DAY",
            "白班",
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            480));
        db.Teams.Add(Team.Create(
            OrganizationId,
            EnvironmentId,
            "TEAM-CNC",
            "机加工一班",
            "DEPT-PROD",
            "SHIFT-DAY",
            "WS-MC"));
        db.Workers.Add(Worker.Create(
            OrganizationId,
            EnvironmentId,
            "EMP-016",
            "跨站点人员",
            "user-site-conflict",
            null,
            "操作工",
            Worker.StatusActive,
            null));
        db.TeamMembers.Add(TeamMember.Assign(
            OrganizationId,
            EnvironmentId,
            "TEAM-CNC",
            "user-site-conflict",
            false,
            EffectiveFrom,
            null));
        db.WorkCenters.Add(WorkCenter.CreateResource(
            OrganizationId,
            EnvironmentId,
            "WC-CROSS-SITE",
            "跨站点工作中心",
            480,
            "work-center",
            "SITE-SH",
            "",
            "WS-MC",
            "STANDARD",
            "minute",
            true));
        await db.SaveChangesAsync();

        var result = await new GetPrincipalWorkContextQueryHandler(db).Handle(
            new GetPrincipalWorkContextQuery(OrganizationId, EnvironmentId, "user-site-conflict"),
            CancellationToken.None);

        Assert.DoesNotContain(result.CoveredWorkCenters, x => x.Id == "WC-CROSS-SITE");
        Assert.DoesNotContain(result.CandidateScopes, x =>
            x.Kind == "work-center" && x.Id == "WC-CROSS-SITE");
        Assert.Contains("work-center-site-lineage-conflict:WC-CROSS-SITE:SITE-SH:SITE-NJ", result.Issues);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"principal-work-context-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
