using Nerv.IIP.Business.Maintenance.Web.Application.Commands;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

public sealed class MaintenancePlanDueScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MaintenancePlanDueScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Maintenance:PmGeneration:Enabled"))
        {
            return;
        }

        var organizationId = configuration["Maintenance:PmGeneration:OrganizationId"];
        var environmentId = configuration["Maintenance:PmGeneration:EnvironmentId"];
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            logger.LogWarning("Maintenance PM generation is enabled but OrganizationId or EnvironmentId is missing.");
            return;
        }

        var interval = configuration.GetValue("Maintenance:PmGeneration:Interval", DefaultInterval);
        var businessTimeZone = ResolveBusinessTimeZone();
        using var timer = new PeriodicTimer(interval);
        await TryGenerateAsync(organizationId, environmentId, businessTimeZone, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryGenerateAsync(organizationId, environmentId, businessTimeZone, stoppingToken);
        }
    }

    private async Task TryGenerateAsync(string organizationId, string environmentId, TimeZoneInfo businessTimeZone, CancellationToken cancellationToken)
    {
        try
        {
            await GenerateAsync(organizationId, environmentId, businessTimeZone, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Maintenance PM generation failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                organizationId,
                environmentId);
        }
    }

    private async Task GenerateAsync(string organizationId, string environmentId, TimeZoneInfo businessTimeZone, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var businessDate = GetBusinessDate(businessTimeZone);
        var result = await sender.Send(
            new GenerateDueMaintenanceWorkOrdersCommand(organizationId, environmentId, businessDate, "system:pm-scheduler"),
            cancellationToken);
        if (result.GeneratedCount > 0)
        {
            logger.LogInformation(
                "Generated {GeneratedCount} due maintenance work orders for {OrganizationId}/{EnvironmentId} on {BusinessDate}.",
                result.GeneratedCount,
                organizationId,
                environmentId,
                businessDate);
        }
    }

    private DateOnly GetBusinessDate(TimeZoneInfo businessTimeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), businessTimeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private TimeZoneInfo ResolveBusinessTimeZone()
    {
        var timeZoneId = configuration["Maintenance:PmGeneration:TimeZoneId"];
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        if (TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return timeZone;
        }

        logger.LogWarning("Maintenance PM generation TimeZoneId '{TimeZoneId}' was not found. Falling back to UTC.", timeZoneId);
        return TimeZoneInfo.Utc;
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsTimeZoneId) && TryFindNativeTimeZone(windowsTimeZoneId, out timeZone))
        {
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaTimeZoneId) && TryFindNativeTimeZone(ianaTimeZoneId, out timeZone))
        {
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static bool TryFindNativeTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}
