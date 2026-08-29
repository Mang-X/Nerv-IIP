using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

namespace Nerv.IIP.Business.Quality.Web.Application.Scheduling;

public sealed class PeriodicInspectionQuantityContinuationScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicInspectionQuantityContinuationScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private const int ContextBatchSize = 100;
    private const int MaxConcurrentCandidates = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval, timeProvider);
        await TryContinueAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryContinueAsync(stoppingToken);
        }
    }

    private async Task TryContinueAsync(CancellationToken cancellationToken)
    {
        var attemptedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var nextAttemptAtUtc = attemptedAtUtc.Add(ScanInterval);
        IReadOnlyList<PendingPeriodicInspectionQuantityContext> candidates;
        try
        {
            using var queryScope = scopeFactory.CreateScope();
            candidates = await queryScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ListPendingPeriodicInspectionQuantityContextsQuery(attemptedAtUtc, ContextBatchSize),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality periodic quantity continuation scan failed; the scheduler will retry on the next tick.");
            return;
        }

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentCandidates,
                CancellationToken = cancellationToken,
            },
            async (candidate, token) =>
            {
                try
                {
                    using var candidateScope = scopeFactory.CreateScope();
                    await candidateScope.ServiceProvider.GetRequiredService<ISender>().Send(
                        new GeneratePeriodicInspectionQuantityTaskBatchForContextCommand(
                            candidate.OrganizationId,
                            candidate.EnvironmentId,
                            candidate.WorkOrderId,
                            candidate.OperationId,
                            candidate.RuntimeContextId,
                            candidate.ObservedNextAttemptAtUtc,
                            nextAttemptAtUtc,
                            PeriodicInspectionQuantityTaskGeneration.MaxWindowsPerTransaction),
                        token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Periodic quantity continuation failed for {OrganizationId}/{EnvironmentId}/{WorkOrderId}/{OperationId}/{RuntimeContextId}; remaining candidates will continue.",
                        candidate.OrganizationId,
                        candidate.EnvironmentId,
                        candidate.WorkOrderId,
                        candidate.OperationId,
                        candidate.RuntimeContextId);
                    await TryDeferFailedCandidateAsync(candidate, nextAttemptAtUtc, token);
                }
            });
    }

    private async Task TryDeferFailedCandidateAsync(
        PendingPeriodicInspectionQuantityContext candidate,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            using var deferScope = scopeFactory.CreateScope();
            await deferScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new DeferPeriodicInspectionQuantityContinuationCommand(
                    candidate.OrganizationId,
                    candidate.EnvironmentId,
                    candidate.WorkOrderId,
                    candidate.OperationId,
                    candidate.RuntimeContextId,
                    candidate.ObservedNextAttemptAtUtc,
                    nextAttemptAtUtc),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not persist the periodic quantity continuation deferral for {RuntimeContextId}; the candidate remains due.",
                candidate.RuntimeContextId);
        }
    }
}
