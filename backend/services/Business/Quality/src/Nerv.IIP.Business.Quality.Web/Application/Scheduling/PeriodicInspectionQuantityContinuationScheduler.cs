using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

namespace Nerv.IIP.Business.Quality.Web.Application.Scheduling;

public sealed class PeriodicInspectionQuantityContinuationScheduler : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private const int ContextBatchSize = 100;
    private const int MaxConcurrentCandidates = 4;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<PeriodicInspectionQuantityContinuationScheduler> logger;
    private readonly TimeProvider timeProvider;
    private readonly IPeriodicInspectionCandidateExecutor candidateExecutor;

    public PeriodicInspectionQuantityContinuationScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<PeriodicInspectionQuantityContinuationScheduler> logger,
        TimeProvider timeProvider)
        : this(scopeFactory, logger, timeProvider, new ParallelPeriodicInspectionCandidateExecutor())
    {
    }

    internal PeriodicInspectionQuantityContinuationScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<PeriodicInspectionQuantityContinuationScheduler> logger,
        TimeProvider timeProvider,
        IPeriodicInspectionCandidateExecutor candidateExecutor)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.timeProvider = timeProvider;
        this.candidateExecutor = candidateExecutor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval, timeProvider);
        await TryContinueAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryContinueAsync(stoppingToken);
        }
    }

    internal async Task TryContinueAsync(CancellationToken cancellationToken)
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

        await candidateExecutor.ExecuteAsync(
            candidates,
            MaxConcurrentCandidates,
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
            },
            cancellationToken);
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

internal interface IPeriodicInspectionCandidateExecutor
{
    Task ExecuteAsync<TCandidate>(
        IReadOnlyList<TCandidate> candidates,
        int maxConcurrency,
        Func<TCandidate, CancellationToken, Task> executeCandidate,
        CancellationToken cancellationToken);
}

internal sealed class ParallelPeriodicInspectionCandidateExecutor : IPeriodicInspectionCandidateExecutor
{
    public Task ExecuteAsync<TCandidate>(
        IReadOnlyList<TCandidate> candidates,
        int maxConcurrency,
        Func<TCandidate, CancellationToken, Task> executeCandidate,
        CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken,
            },
            (candidate, token) => new ValueTask(executeCandidate(candidate, token)));
}
