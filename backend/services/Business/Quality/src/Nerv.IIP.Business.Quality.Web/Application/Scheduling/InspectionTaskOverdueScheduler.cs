using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;

namespace Nerv.IIP.Business.Quality.Web.Application.Scheduling;

public sealed class InspectionTaskOverdueScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<InspectionTaskOverdueScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Quality:InspectionTaskOverdue:Enabled")
            && !configuration.GetValue<bool>("Quality:MeasuringDevice:CalibrationScanEnabled"))
        {
            return;
        }

        var scopes = GetConfiguredScopes().Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("Quality inspection task overdue check is enabled but no organization/environment scope is configured.");
            return;
        }

        var interval = configuration.GetValue("Quality:InspectionTaskOverdue:Interval", DefaultInterval);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "Quality inspection task overdue check interval {Interval} is not positive; falling back to {DefaultInterval}.",
                interval,
                DefaultInterval);
            interval = DefaultInterval;
        }

        using var timer = new PeriodicTimer(interval);
        await TryCheckAllScopesAsync(scopes, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryCheckAllScopesAsync(scopes, stoppingToken);
        }
    }

    private IEnumerable<InspectionTaskOverdueCheckScope> GetConfiguredScopes()
    {
        foreach (var scopeSection in configuration.GetSection("Quality:InspectionTaskOverdue:Scopes").GetChildren())
        {
            var organizationId = scopeSection["OrganizationId"];
            var environmentId = scopeSection["EnvironmentId"];
            if (!string.IsNullOrWhiteSpace(organizationId) && !string.IsNullOrWhiteSpace(environmentId))
            {
                yield return new InspectionTaskOverdueCheckScope(organizationId, environmentId);
            }
        }

        var singleOrganizationId = configuration["Quality:InspectionTaskOverdue:OrganizationId"];
        var singleEnvironmentId = configuration["Quality:InspectionTaskOverdue:EnvironmentId"];
        if (!string.IsNullOrWhiteSpace(singleOrganizationId) && !string.IsNullOrWhiteSpace(singleEnvironmentId))
        {
            yield return new InspectionTaskOverdueCheckScope(singleOrganizationId, singleEnvironmentId);
        }
    }

    private async Task TryCheckAllScopesAsync(
        IReadOnlyCollection<InspectionTaskOverdueCheckScope> scopes,
        CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            await TryCheckAsync(scope.OrganizationId, scope.EnvironmentId, cancellationToken);
        }
    }

    private async Task TryCheckAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var published = configuration.GetValue<bool>("Quality:InspectionTaskOverdue:Enabled")
                ? await sender.Send(
                    new PublishOverdueInspectionTaskRemindersCommand(organizationId, environmentId, timeProvider.GetUtcNow()),
                    cancellationToken)
                : 0;
            var calibrationPublished = 0;
            if (configuration.GetValue<bool>("Quality:MeasuringDevice:CalibrationScanEnabled"))
            {
                calibrationPublished = await sender.Send(
                    new PublishMeasuringDeviceCalibrationAlertsCommand(organizationId, environmentId, timeProvider.GetUtcNow()),
                    cancellationToken);
            }
            if (published > 0)
            {
                logger.LogInformation(
                    "Published {PublishedCount} overdue quality inspection task reminders for {OrganizationId}/{EnvironmentId}.",
                    published,
                    organizationId,
                    environmentId);
            }
            if (calibrationPublished > 0)
            {
                logger.LogInformation(
                    "Published {PublishedCount} measuring-device calibration alerts for {OrganizationId}/{EnvironmentId}.",
                    calibrationPublished,
                    organizationId,
                    environmentId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Quality inspection task overdue check failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                organizationId,
                environmentId);
        }
    }

    private sealed record InspectionTaskOverdueCheckScope(string OrganizationId, string EnvironmentId);
}
