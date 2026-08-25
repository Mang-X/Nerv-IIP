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
        IReadOnlyList<PendingPeriodicInspectionQuantityContext> candidates;
        try
        {
            using var queryScope = scopeFactory.CreateScope();
            candidates = await queryScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ListPendingPeriodicInspectionQuantityContextsQuery(ContextBatchSize),
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

        foreach (var candidate in candidates)
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
                        PeriodicInspectionQuantityTaskGeneration.MaxWindowsPerTransaction),
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
                    "Periodic quantity continuation failed for {OrganizationId}/{EnvironmentId}/{WorkOrderId}/{OperationId}/{RuntimeContextId}; remaining candidates will continue.",
                    candidate.OrganizationId,
                    candidate.EnvironmentId,
                    candidate.WorkOrderId,
                    candidate.OperationId,
                    candidate.RuntimeContextId);
            }
        }
    }
}
