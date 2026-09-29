using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpRunDemandChangeQueryTests
{
    [Fact]
    public async Task Latest_completed_run_counts_distinct_changed_inputs_in_its_horizon()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        var completedAt = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var run = AddCompletedRun(db, start, end, completedAt.AddMinutes(-1), completedAt);
        db.MrpInputChanges.AddRange(
            Change("demand", "PRE", completedAt.AddMinutes(-1), null, start),
            Change("demand", "D-1", completedAt.AddMinutes(1), start, start.AddDays(1)),
            Change("demand", "D-1", completedAt.AddMinutes(2), start.AddDays(1), start.AddDays(2)),
            Change("forecast", "F-1", completedAt.AddMinutes(1), start.AddDays(-2), start.AddDays(2)),
            Change("mps", "MPS:1", completedAt.AddMinutes(1), null, end),
            Change("demand", "D-2", completedAt.AddMinutes(1), null, end),
            Change("demand", "OUTSIDE", completedAt.AddMinutes(1), end.AddDays(1), end.AddDays(2)),
            Change("demand", "OTHER-ORG", completedAt.AddMinutes(1), null, start, organizationId: "other-org"),
            Change("demand", "INELIGIBLE", completedAt.AddMinutes(1), null, start, currentlyEligible: false));
        await db.SaveChangesAsync();

        var result = Assert.Single(await new ListMrpRunsQueryHandler(db)
            .Handle(new ListMrpRunsQuery("org-001", "env-dev"), default));
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(4, result.DemandChangeCount);
    }

    [Fact]
    public async Task Moving_inputs_out_and_physically_deleting_demand_still_counts_each_source()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        var outside = end.AddDays(1);
        var demands = new CreateOrUpdateDemandSourceCommandHandler(db);
        await demands.Handle(new("org-001", "env-dev", "manual", "D-MOVE", "SKU", "pcs", "SITE", 10, start), default);
        var deletedId = await demands.Handle(new("org-001", "env-dev", "manual", "D-DELETE", "SKU", "pcs", "SITE", 10, start), default);
        var forecasts = new CreateOrUpdateForecastInputCommandHandler(db);
        await forecasts.Handle(new("org-001", "env-dev", "F-MOVE", "SKU", "pcs", "SITE", start, end, 10), default);
        await db.SaveChangesAsync();
        var run = MrpRun.Create("org-001", "env-dev", start, end);
        run.Start(new PlanningInputSnapshot("test-production", "test-inventory", 3, 0));
        run.Complete(0);
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();

        await demands.Handle(new("org-001", "env-dev", "manual", "D-MOVE", "SKU", "pcs", "SITE", 10, outside), default);
        await db.SaveChangesAsync();
        await forecasts.Handle(new("org-001", "env-dev", "F-MOVE", "SKU", "pcs", "SITE", outside, outside.AddDays(1), 10), default);
        await db.SaveChangesAsync();
        await new CancelDemandSourceCommandHandler(db).Handle(new("org-001", "env-dev", deletedId), default);
        await db.SaveChangesAsync();
        await demands.Handle(new("org-001", "env-dev", "manual", "D-NEW", "SKU", "pcs", "SITE", 10, start), default);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DemandSources.CountAsync());
        Assert.Contains(await db.MrpInputChanges.ToListAsync(), x =>
            x.SourceReference == "D-DELETE" && x.Operation == MrpInputChangeOperation.Deleted);
        var result = Assert.Single(await new ListMrpRunsQueryHandler(db)
            .Handle(new ListMrpRunsQuery("org-001", "env-dev"), default));
        Assert.Equal(4, result.DemandChangeCount);
    }

    [Fact]
    public async Task Same_reference_in_different_demand_types_counts_as_two_sources()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        var run = AddCompletedRun(db, start, end, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();

        var demands = new CreateOrUpdateDemandSourceCommandHandler(db);
        await demands.Handle(new("org-001", "env-dev", "manual", "SAME", "SKU", "pcs", "SITE", 10, start), default);
        await db.SaveChangesAsync();
        await demands.Handle(new("org-001", "env-dev", "safety-stock", "SAME", "SKU", "pcs", "SITE", 10, start), default);
        await db.SaveChangesAsync();
        await demands.Handle(new("org-001", "env-dev", "manual", "SAME", "SKU", "pcs", "SITE", 20, start), default);
        await db.SaveChangesAsync();

        var facts = await db.MrpInputChanges.Where(x => x.SourceReference == "SAME").ToListAsync();
        Assert.Equal(3, facts.Count);
        Assert.Contains(facts, x => x.DemandType == "manual");
        Assert.Contains(facts, x => x.DemandType == "safety-stock");
        var result = Assert.Single(await ListRuns(db));
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(2, result.DemandChangeCount);
    }

    [Fact]
    public async Task Failed_runs_do_not_reset_count_and_next_completion_uses_new_horizon()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        var time = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var failed = MrpRun.Create("org-001", "env-dev", start, end);
        failed.Fail("input unavailable");
        db.MrpRuns.Add(failed);
        await db.SaveChangesAsync();
        Assert.Equal(0, Assert.Single(await ListRuns(db)).DemandChangeCount);

        var first = AddCompletedRun(db, start, end, time.AddMinutes(-1), time);
        db.MrpInputChanges.Add(Change("demand", "D-1", time.AddMinutes(1), null, start));
        var laterFailure = MrpRun.Create("org-001", "env-dev", start, end);
        laterFailure.Fail("input unavailable");
        db.MrpRuns.Add(laterFailure);
        await db.SaveChangesAsync();
        var runs = await ListRuns(db);
        Assert.Equal(1, Assert.Single(runs, x => x.RunId == first.Id).DemandChangeCount);
        Assert.Equal(0, Assert.Single(runs, x => x.RunId == laterFailure.Id).DemandChangeCount);

        var second = AddCompletedRun(db, start.AddDays(10), end, time.AddMinutes(-2), time.AddMinutes(2));
        await db.SaveChangesAsync();
        runs = await ListRuns(db);
        Assert.Equal(0, Assert.Single(runs, x => x.RunId == first.Id).DemandChangeCount);
        Assert.Equal(0, Assert.Single(runs, x => x.RunId == second.Id).DemandChangeCount);
        db.MrpInputChanges.Add(Change("demand", "D-2", time.AddMinutes(3), null, start.AddDays(11)));
        await db.SaveChangesAsync();
        runs = await ListRuns(db);
        Assert.Equal(1, Assert.Single(runs, x => x.RunId == second.Id).DemandChangeCount);
    }

    private static Task<IReadOnlyCollection<MrpRunResponse>> ListRuns(ApplicationDbContext db) =>
        new ListMrpRunsQueryHandler(db).Handle(new ListMrpRunsQuery("org-001", "env-dev"), default);

    private static MrpRun AddCompletedRun(ApplicationDbContext db, DateOnly start, DateOnly end,
        DateTimeOffset createdAt, DateTimeOffset completedAt)
    {
        var run = MrpRun.Create("org-001", "env-dev", start, end);
        run.Start(new PlanningInputSnapshot("test-production", "test-inventory", 0, 0));
        run.Complete(0);
        db.MrpRuns.Add(run);
        db.Entry(run).Property(x => x.CreatedAtUtc).CurrentValue = createdAt;
        db.Entry(run).Property(x => x.CompletedAtUtc).CurrentValue = completedAt;
        return run;
    }

    private static MrpInputChange Change(string type, string reference, DateTimeOffset time,
        DateOnly? previousDate, DateOnly currentDate, string organizationId = "org-001", bool currentlyEligible = true) =>
        MrpInputChange.Record(organizationId, "env-dev", type, type == "demand" ? "manual" : null, reference, "", time,
            previousDate is null ? MrpInputChangeOperation.Created : MrpInputChangeOperation.Updated,
            previousDate, previousDate, previousDate is not null,
            currentDate, currentDate, currentlyEligible);

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"mrp-change-query-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
