using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public sealed class AppHubHeartbeatTimeoutScanOptions
{
    public const string SectionName = "AppHub:HeartbeatTimeoutScan";

    public bool Enabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int Take { get; set; } = 100;
}

public sealed record AppHubHeartbeatTimeoutScanResult(int MarkedUnreachableCount);

public sealed record AppHubHeartbeatTimeoutScanCommand(
    DateTimeOffset Now,
    int Take) : ICommand<AppHubHeartbeatTimeoutScanResult>;

public sealed class AppHubHeartbeatTimeoutScanCommandHandler(IServiceProvider services)
    : ICommandHandler<AppHubHeartbeatTimeoutScanCommand, AppHubHeartbeatTimeoutScanResult>
{
    public Task<AppHubHeartbeatTimeoutScanResult> Handle(
        AppHubHeartbeatTimeoutScanCommand request,
        CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            return Task.FromResult(new AppHubHeartbeatTimeoutScanResult(0));
        }

        var scanner = services.GetRequiredService<AppHubHeartbeatTimeoutScanner>();
        return scanner.ScanAsync(request.Now, request.Take, cancellationToken);
    }
}

public sealed class AppHubHeartbeatTimeoutScanner(
    ApplicationDbContext dbContext,
    IOptions<ConnectorCollectionHealthOptions> collectionHealthOptions)
{
    public async Task<AppHubHeartbeatTimeoutScanResult> ScanAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var heartbeatTimeout = collectionHealthOptions.Value.HostLivenessTimeout;
        var cutoff = now.Subtract(heartbeatTimeout);
        var candidates = await dbContext.ApplicationInstances
            .Include(x => x.Heartbeat)
            .Where(x =>
                x.Heartbeat != null
                && x.Heartbeat.Reachable
                && x.ConnectorHostId != ""
                && x.Heartbeat.LastHeartbeatAtUtc <= cutoff)
            .OrderBy(x => x.Heartbeat!.LastHeartbeatAtUtc)
            .ThenBy(x => x.InstanceKey)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        var marked = 0;
        foreach (var instance in candidates)
        {
            if (!instance.MarkHeartbeatUnreachable(now, heartbeatTimeout))
            {
                continue;
            }

            marked++;
        }

        return new AppHubHeartbeatTimeoutScanResult(marked);
    }
}

internal sealed class AppHubHeartbeatTimeoutScanWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AppHubHeartbeatTimeoutScanOptions> options,
    ILogger<AppHubHeartbeatTimeoutScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = options.Value;
        if (!currentOptions.Enabled || currentOptions.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(currentOptions.PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var result = await sender.Send(
                    new AppHubHeartbeatTimeoutScanCommand(
                        timeProvider.GetUtcNow(),
                        currentOptions.Take),
                    stoppingToken);
                if (result.MarkedUnreachableCount > 0)
                {
                    logger.LogWarning(
                        "AppHubHeartbeatTimeoutScanMarkedUnreachable Count={Count}",
                        result.MarkedUnreachableCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "AppHub heartbeat timeout scan failed.");
            }
        }
    }
}
