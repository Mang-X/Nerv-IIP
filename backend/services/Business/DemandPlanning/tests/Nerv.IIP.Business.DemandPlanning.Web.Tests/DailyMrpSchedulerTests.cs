using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class DailyMrpSchedulerTests
{
    [Fact]
    public async Task Daily_schedule_uses_configured_zone_rolls_latest_horizon_and_does_not_repeat_the_day()
    {
        var clock = new TimerRegistrationObservingTimeProvider(new DateTimeOffset(2026, 9, 29, 1, 59, 0, TimeSpan.Zero));
        await using var services = CreateServices();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var older = CompletedRun("org-001", "env-dev", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 21));
            var latest = CompletedRun("org-001", "env-dev", new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5));
            db.MrpRuns.AddRange(older, latest,
                CompletedRun("org-002", "env-prod", new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 9)));
            db.Entry(older).Property(run => run.CreatedAtUtc).CurrentValue = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
            db.Entry(latest).Property(run => run.CreatedAtUtc).CurrentValue = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            await db.SaveChangesAsync();
        }

        var scheduler = CreateScheduler(services, clock);
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await clock.WaitForFirstTimerAsync();
            Assert.Equal(3, await RunCountAsync(services));

            clock.Advance(TimeSpan.FromMinutes(1)); // 10:00 Asia/Shanghai
            await Eventually.WaitAsync(
                "both scopes receive a persisted daily MRP run",
                async _ => await RunCountAsync(services),
                count => count == 5,
                count => $"runs={count}",
                new EventuallyOptions(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), []));

            var today = await RunsOnAsync(services, new DateOnly(2026, 9, 29));
            Assert.Equal(2, today.Length);
            Assert.Contains(today, run => run.OrganizationId == "org-001" && run.EnvironmentId == "env-dev" && run.HorizonEnd == new DateOnly(2026, 10, 6));
            Assert.Contains(today, run => run.OrganizationId == "org-002" && run.EnvironmentId == "env-prod" && run.HorizonEnd == new DateOnly(2026, 10, 13));
            var queuedIds = await TestTimeout.RunAsync(
                "both persisted daily runs enter the existing MRP worker queue",
                async token =>
                {
                    var ids = new List<MrpRunId>();
                    await using var queued = services.GetRequiredService<IMrpRunExecutionQueue>()
                        .DequeueAllAsync(token).GetAsyncEnumerator(token);
                    for (var index = 0; index < today.Length; index++)
                    {
                        Assert.True(await queued.MoveNextAsync());
                        ids.Add(queued.Current);
                    }

                    return ids;
                },
                TimeSpan.FromSeconds(10));
            Assert.Equal(today.Select(run => run.Id).OrderBy(id => id.ToString()), queuedIds.OrderBy(id => id.ToString()));

            await clock.WaitForTimerCountAsync(2);
            clock.Advance(TimeSpan.FromDays(1));
            await Eventually.WaitAsync(
                "both scopes receive the next day's MRP run",
                async _ => await RunCountAsync(services),
                count => count == 7,
                count => $"runs={count}",
                new EventuallyOptions(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), []));

            var tomorrow = await RunsOnAsync(services, new DateOnly(2026, 9, 30));
            Assert.Equal(2, tomorrow.Length);
            Assert.Contains(tomorrow, run => run.OrganizationId == "org-001" && run.HorizonEnd == new DateOnly(2026, 10, 7));
            Assert.Contains(tomorrow, run => run.OrganizationId == "org-002" && run.HorizonEnd == new DateOnly(2026, 10, 14));
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }

        // A restart on the same schedule day must reuse persisted daily runs.
        var timersBeforeRestart = clock.TimersCreated;
        var restarted = CreateScheduler(services, clock);
        await restarted.StartAsync(CancellationToken.None);
        try
        {
            // The prior scheduler is stopped; its timer registrations cannot satisfy this barrier.
            // The new scheduler registers its next-day timer only after today's pass is complete.
            await clock.WaitForTimerCountAsync(timersBeforeRestart + 1);
            Assert.Equal(7, await RunCountAsync(services));
        }
        finally
        {
            await restarted.StopAsync(CancellationToken.None);
        }
    }

    private static MrpRun CompletedRun(string organizationId, string environmentId, DateOnly start, DateOnly end)
    {
        var run = MrpRun.Create(organizationId, environmentId, start, end);
        run.Start(new PlanningInputSnapshot("product-engineering-http:0", "inventory-http:0", 0, 0));
        run.Complete(0);
        return run;
    }

    private static DailyMrpScheduler CreateScheduler(ServiceProvider services, TimeProvider clock) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Planning:DailyMrp:TimeOfDay"] = "10:00:00",
                ["Planning:DailyMrp:TimeZoneId"] = "Asia/Shanghai",
            }).Build(),
            services.GetRequiredService<IMrpRunExecutionQueue>(),
            NullLogger<DailyMrpScheduler>.Instance,
            clock);

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        var databaseName = $"daily-mrp-{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddSingleton<IMrpRunExecutionQueue, MrpRunExecutionQueue>();
        return services.BuildServiceProvider();
    }

    private static async Task<int> RunCountAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MrpRuns.CountAsync();
    }

    private static async Task<MrpRun[]> RunsOnAsync(ServiceProvider services, DateOnly date)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MrpRuns.AsNoTracking()
            .Where(run => run.HorizonStart == date)
            .ToArrayAsync();
    }
}
