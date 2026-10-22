using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

/// <summary>
/// Creates one rolling MRP run for each organization/environment that already has a run.
/// The run is persisted through the ordinary acceptance command before it enters the worker queue.
/// </summary>
public sealed class DailyMrpScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IMrpRunExecutionQueue queue,
    ILogger<DailyMrpScheduler> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeOfDay = TimeOnly.Parse(configuration["Planning:DailyMrp:TimeOfDay"]
            ?? throw new InvalidOperationException("Planning:DailyMrp:TimeOfDay is required."));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Planning:DailyMrp:TimeZoneId"]
            ?? throw new InvalidOperationException("Planning:DailyMrp:TimeZoneId is required."));
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        var scheduleDate = DateOnly.FromDateTime(now.DateTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var dueUtc = TimeZoneInfo.ConvertTimeToUtc(scheduleDate.ToDateTime(timeOfDay), timeZone);
            var delay = new DateTimeOffset(dueUtc, TimeSpan.Zero) - clock.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, clock, stoppingToken);
            }

            await ScheduleDateAsync(scheduleDate, stoppingToken);
            scheduleDate = scheduleDate.AddDays(1);
        }
    }

    private async Task ScheduleDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var listScope = scopeFactory.CreateAsyncScope();
        var db = listScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var latestRuns = await db.MrpRuns.AsNoTracking()
            .GroupBy(run => new { run.OrganizationId, run.EnvironmentId })
            .Select(group => group.OrderByDescending(run => run.CreatedAtUtc).First())
            .ToArrayAsync(cancellationToken);

        foreach (var latest in latestRuns)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var alreadyScheduled = await scopedDb.MrpRuns.AsNoTracking().AnyAsync(run =>
                    run.OrganizationId == latest.OrganizationId &&
                    run.EnvironmentId == latest.EnvironmentId &&
                    run.HorizonStart == date,
                    cancellationToken);
                if (alreadyScheduled)
                {
                    continue;
                }

                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var runId = await sender.Send(new RunMrpCommand(
                    latest.OrganizationId,
                    latest.EnvironmentId,
                    date,
                    date.AddDays(latest.HorizonEnd.DayNumber - latest.HorizonStart.DayNumber)), cancellationToken);
                queue.Enqueue(runId);
                logger.LogInformation("Scheduled daily MRP run {RunId} for {OrganizationId}/{EnvironmentId} on {ScheduleDate}.",
                    runId, latest.OrganizationId, latest.EnvironmentId, date);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to schedule daily MRP run for {OrganizationId}/{EnvironmentId} on {ScheduleDate}.",
                    latest.OrganizationId, latest.EnvironmentId, date);
            }
        }
    }
}
