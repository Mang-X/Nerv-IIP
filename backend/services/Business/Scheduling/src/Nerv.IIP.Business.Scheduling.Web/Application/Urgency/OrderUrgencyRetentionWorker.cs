using Prometheus;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public sealed record OrderUrgencyRetentionWorkerIdentity(string Value);

public static class OrderUrgencyRetentionMetrics
{
    private static readonly Counter Runs = Metrics.CreateCounter(
        "nerv_iip_order_urgency_retention_runs_total",
        "Order urgency retention run outcomes.",
        new CounterConfiguration { LabelNames = ["outcome"] });
    private static readonly Counter Snapshots = Metrics.CreateCounter(
        "nerv_iip_order_urgency_retention_snapshots_total",
        "Order urgency snapshots processed by retention lifecycle outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });
    private static readonly Gauge EligibleBacklog = Metrics.CreateGauge(
        "nerv_iip_order_urgency_retention_eligible_snapshots",
        "Eligible urgency snapshots observed before the most recent retention batch.");
    private static readonly Gauge OldestEligibleAge = Metrics.CreateGauge(
        "nerv_iip_order_urgency_retention_oldest_eligible_age_seconds",
        "Age in seconds of the oldest eligible urgency snapshot observed before the most recent retention batch.");

    public static void Observe(OrderUrgencyRetentionRunResult result)
    {
        var outcome = result.LegalHoldActive
            ? "held"
            : !result.LeaseAcquired
                ? "lease-skipped"
                : result.Failures > 0 ? "failed" : "succeeded";
        Runs.WithLabels(outcome).Inc();
        Snapshots.WithLabels("archived").Inc(result.ArchivedSnapshots);
        Snapshots.WithLabels("source-deleted").Inc(result.SourceDeletedSnapshots);
        Snapshots.WithLabels("archive-deleted").Inc(result.ArchiveDeletedBatches);
        EligibleBacklog.Set(result.EligibleSnapshots);
        OldestEligibleAge.Set(result.OldestEligibleAgeSeconds);
    }

    public static void ConfigurationRejected(int count)
    {
        Runs.WithLabels("configuration-rejected").Inc(count);
    }

    public static void Crashed()
    {
        Runs.WithLabels("crashed").Inc();
    }
}

public sealed class OrderUrgencyRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OrderUrgencyRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("OrderUrgencyRetention:Enabled")) return;
        var intervalMinutes = configuration.GetValue<int?>("OrderUrgencyRetention:IntervalMinutes") ?? 60;
        if (intervalMinutes is < 1 or > 1440)
        {
            logger.LogError(
                "Order urgency retention is disabled because IntervalMinutes {IntervalMinutes} is outside 1-1440.",
                intervalMinutes);
            OrderUrgencyRetentionMetrics.ConfigurationRejected(1);
            return;
        }

        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var policy = OrderUrgencyRetentionPolicy.Load(configuration, timeProvider.GetUtcNow());
        if (policy.Errors.Count > 0)
        {
            foreach (var error in policy.Errors)
            {
                logger.LogError("Order urgency retention configuration rejected: {Error}", error);
            }
            OrderUrgencyRetentionMetrics.ConfigurationRejected(policy.Errors.Count);
        }

        foreach (var retentionScope in policy.Scopes)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderUrgencyRetentionService>();
                var result = await service.RunScopeAsync(retentionScope, cancellationToken);
                OrderUrgencyRetentionMetrics.Observe(result);
                if (result.Failures > 0)
                {
                    logger.LogError(
                        "Order urgency retention failed for organization {OrganizationId}, environment {EnvironmentId}; source rows were preserved.",
                        retentionScope.OrganizationId,
                        retentionScope.EnvironmentId);
                }
                else if (result.EligibleSnapshots > retentionScope.BatchSize)
                {
                    logger.LogWarning(
                        "Order urgency retention backlog alert for organization {OrganizationId}, environment {EnvironmentId}: {EligibleSnapshots} eligible rows exceed batch size {BatchSize}; oldest eligible age is {OldestEligibleAgeSeconds} seconds.",
                        retentionScope.OrganizationId,
                        retentionScope.EnvironmentId,
                        result.EligibleSnapshots,
                        retentionScope.BatchSize,
                        result.OldestEligibleAgeSeconds);
                }
                else
                {
                    logger.LogInformation(
                        "Order urgency retention organization {OrganizationId}, environment {EnvironmentId}: archived {Archived}, source deleted {SourceDeleted}, archive versions deleted {ArchiveDeleted}, held {Held}, lease acquired {LeaseAcquired}.",
                        retentionScope.OrganizationId,
                        retentionScope.EnvironmentId,
                        result.ArchivedSnapshots,
                        result.SourceDeletedSnapshots,
                        result.ArchiveDeletedBatches,
                        result.LegalHoldActive,
                        result.LeaseAcquired);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                OrderUrgencyRetentionMetrics.Crashed();
                logger.LogError(
                    exception,
                    "Order urgency retention crashed for organization {OrganizationId}, environment {EnvironmentId}; source rows were preserved.",
                    retentionScope.OrganizationId,
                    retentionScope.EnvironmentId);
            }
        }
    }
}
