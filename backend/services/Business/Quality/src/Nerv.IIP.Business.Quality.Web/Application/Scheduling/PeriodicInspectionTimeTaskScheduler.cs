using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

namespace Nerv.IIP.Business.Quality.Web.Application.Scheduling;

public sealed class PeriodicInspectionTimeTaskScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PeriodicInspectionTimeTaskScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);
    private const int DefaultMaxWindowsPerContext = 24;
    private const int DefaultContextBatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Quality:PeriodicInspectionTime:Enabled"))
        {
            return;
        }

        var scopes = GetConfiguredScopes().Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("Quality periodic inspection time generation is enabled but no organization/environment scope is configured.");
            return;
        }

        var interval = configuration.GetValue("Quality:PeriodicInspectionTime:Interval", DefaultInterval);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "Quality periodic inspection time generation interval {Interval} is not positive; falling back to {DefaultInterval}.",
                interval,
                DefaultInterval);
            interval = DefaultInterval;
        }

        var maxWindowsPerContext = PositiveBoundedSetting(
            "Quality:PeriodicInspectionTime:MaxWindowsPerContext",
            DefaultMaxWindowsPerContext);
        var contextBatchSize = PositiveBoundedSetting(
            "Quality:PeriodicInspectionTime:ContextBatchSize",
            DefaultContextBatchSize);

        using var timer = new PeriodicTimer(interval, timeProvider);
        await TryGenerateAllScopesAsync(scopes, maxWindowsPerContext, contextBatchSize, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryGenerateAllScopesAsync(scopes, maxWindowsPerContext, contextBatchSize, stoppingToken);
        }
    }

    private int PositiveBoundedSetting(string key, int fallback)
    {
        var value = configuration.GetValue(key, fallback);
        if (value is >= 1 and <= 1000)
        {
            return value;
        }

        logger.LogWarning(
            "Quality periodic inspection setting {SettingKey} value {SettingValue} is outside 1..1000; falling back to {Fallback}.",
            key,
            value,
            fallback);
        return fallback;
    }

    private IEnumerable<PeriodicInspectionTimeScope> GetConfiguredScopes()
    {
        foreach (var scopeSection in configuration.GetSection("Quality:PeriodicInspectionTime:Scopes").GetChildren())
        {
            var organizationId = scopeSection["OrganizationId"];
            var environmentId = scopeSection["EnvironmentId"];
            if (!string.IsNullOrWhiteSpace(organizationId) && !string.IsNullOrWhiteSpace(environmentId))
            {
                yield return new PeriodicInspectionTimeScope(organizationId.Trim(), environmentId.Trim());
            }
        }

        var singleOrganizationId = configuration["Quality:PeriodicInspectionTime:OrganizationId"];
        var singleEnvironmentId = configuration["Quality:PeriodicInspectionTime:EnvironmentId"];
        if (!string.IsNullOrWhiteSpace(singleOrganizationId) && !string.IsNullOrWhiteSpace(singleEnvironmentId))
        {
            yield return new PeriodicInspectionTimeScope(singleOrganizationId.Trim(), singleEnvironmentId.Trim());
        }
    }

    private async Task TryGenerateAllScopesAsync(
        IReadOnlyCollection<PeriodicInspectionTimeScope> scopes,
        int maxWindowsPerContext,
        int contextBatchSize,
        CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            try
            {
                using var serviceScope = scopeFactory.CreateScope();
                var sender = serviceScope.ServiceProvider.GetRequiredService<ISender>();
                var generated = await sender.Send(
                    new GeneratePeriodicInspectionTimeTasksCommand(
                        scope.OrganizationId,
                        scope.EnvironmentId,
                        maxWindowsPerContext,
                        contextBatchSize),
                    cancellationToken);
                if (generated > 0)
                {
                    logger.LogInformation(
                        "Generated {GeneratedCount} periodic inspection time tasks for {OrganizationId}/{EnvironmentId}.",
                        generated,
                        scope.OrganizationId,
                        scope.EnvironmentId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Quality periodic inspection time generation failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                    scope.OrganizationId,
                    scope.EnvironmentId);
            }
        }
    }

    private sealed record PeriodicInspectionTimeScope(string OrganizationId, string EnvironmentId);
}
