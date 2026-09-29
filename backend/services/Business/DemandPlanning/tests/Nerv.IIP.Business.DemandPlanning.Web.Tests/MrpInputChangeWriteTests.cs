using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpInputChangeWriteTests
{
    [Fact]
    public async Task Manual_demand_move_and_physical_delete_record_old_date_and_identity()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldDate = new DateOnly(2026, 10, 10);
        var newDate = new DateOnly(2026, 12, 10);
        var create = new CreateOrUpdateDemandSourceCommandHandler(db);
        var id = await create.Handle(new("org-a", "env-a", "manual", "D-1", "SKU", "pcs", "SITE", 10, oldDate), default);
        await db.SaveChangesAsync();
        await create.Handle(new("org-a", "env-a", "manual", "D-1", "SKU", "pcs", "SITE", 10, newDate), default);
        await db.SaveChangesAsync();
        await new CancelDemandSourceCommandHandler(db).Handle(new("org-a", "env-a", id), default);
        await db.SaveChangesAsync();
        await create.Handle(new("org-a", "env-a", "manual", "D-2", "SKU", "pcs", "SITE", 10, oldDate), default);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.DemandSources.CountAsync(x => x.SourceReference == "D-1"));
        var facts = await db.MrpInputChanges.OrderBy(x => x.OccurredAtUtc).ToListAsync();
        Assert.Equal(4, facts.Count);
        Assert.Contains(facts, x => x.SourceReference == "D-1" && x.Operation == MrpInputChangeOperation.Updated
            && x.PreviousStartDate == oldDate && x.CurrentStartDate == newDate);
        Assert.Contains(facts, x => x.SourceReference == "D-1" && x.Operation == MrpInputChangeOperation.Deleted
            && x.PreviousStartDate == newDate && !x.CurrentlyEligible);
        Assert.Contains(facts, x => x.SourceReference == "D-2" && x.Operation == MrpInputChangeOperation.Created);
    }

    [Fact]
    public async Task Forecast_and_released_mps_record_intervals_and_eligibility_per_tenant()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        var forecast = new CreateOrUpdateForecastInputCommandHandler(db);
        await forecast.Handle(new("org-a", "env-a", "F-1", "SKU", "pcs", "SITE", start, end, 10), default);
        await db.SaveChangesAsync();
        await forecast.Handle(new("org-a", "env-a", "F-1", "SKU", "pcs", "SITE", start.AddMonths(1), end.AddMonths(1), 10), default);
        await db.SaveChangesAsync();

        var mpsId = await new CreateMasterProductionScheduleBucketCommandHandler(db)
            .Handle(new("org-b", "env-a", "SKU", "pcs", "SITE", start, 10), default);
        await db.SaveChangesAsync();
        await new ReviewMasterProductionScheduleBucketCommandHandler(db)
            .Handle(new("org-b", "env-a", mpsId, "reviewer"), default);
        await db.SaveChangesAsync();
        await new ReleaseMasterProductionScheduleBucketCommandHandler(db)
            .Handle(new("org-b", "env-a", mpsId, "releaser"), default);
        await db.SaveChangesAsync();

        var a = await db.MrpInputChanges.Where(x => x.OrganizationId == "org-a").ToListAsync();
        Assert.Equal(2, a.Count);
        Assert.Contains(a, x => x.InputType == "forecast" && x.PreviousStartDate == start
            && x.PreviousEndDate == end && x.CurrentStartDate == start.AddMonths(1));
        var b = await db.MrpInputChanges.Where(x => x.OrganizationId == "org-b").ToListAsync();
        Assert.Contains(b, x => x.InputType == "mps" && !x.PreviouslyEligible && x.CurrentlyEligible
            && x.CurrentStartDate == start);
        Assert.All(b, x => Assert.Equal("env-a", x.EnvironmentId));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"mrp-input-write-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
