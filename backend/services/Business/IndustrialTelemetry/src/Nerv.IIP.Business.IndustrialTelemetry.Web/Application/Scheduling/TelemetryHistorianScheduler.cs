using System.Globalization;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Historian;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Scheduling;

public sealed class TelemetryHistorianScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelemetryHistorianScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultRawRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultHourlyRetention = TimeSpan.FromDays(90);
    private static readonly TimeSpan DefaultDailyRetention = TimeSpan.FromDays(730);
    private const int DefaultMaxPendingHourlyWindows = 50000;
    private const int DefaultMaxPendingDailyWindows = 50000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadEnabled(configuration, logger))
        {
            return;
        }

        var scopes = GetConfiguredScopes(configuration, logger).Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("IndustrialTelemetry historian scheduler is enabled but no valid organization/environment scope is configured.");
            return;
        }

        var interval = ReadInterval(configuration, logger);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "IndustrialTelemetry historian interval {Interval} is not positive; falling back to {DefaultInterval}.",
                interval,
                DefaultInterval);
            interval = DefaultInterval;
        }

        using var timer = new PeriodicTimer(interval);
        await TryRunAllScopesAsync(scopes, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryRunAllScopesAsync(scopes, stoppingToken);
        }
    }

    public static IEnumerable<TelemetryHistorianScope> GetConfiguredScopes(IConfiguration configuration, ILogger? logger = null)
    {
        foreach (var scopeSection in configuration.GetSection("IndustrialTelemetry:Historian:Scopes").GetChildren())
        {
            var scope = TryReadScope(scopeSection, logger);
            if (scope is not null)
            {
                yield return scope;
            }
        }

        var singleScope = TryReadScope(configuration.GetSection("IndustrialTelemetry:Historian"), logger);
        if (singleScope is not null)
        {
            yield return singleScope;
        }
    }

    private async Task TryRunAllScopesAsync(IReadOnlyCollection<TelemetryHistorianScope> scopes, CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            await TryRunAsync(scope, cancellationToken);
        }
    }

    private async Task TryRunAsync(TelemetryHistorianScope scope, CancellationToken cancellationToken)
    {
        try
        {
            using var serviceScope = scopeFactory.CreateScope();
            var historian = serviceScope.ServiceProvider.GetRequiredService<TelemetryHistorianService>();
            var dbContext = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var asOfUtc = timeProvider.GetUtcNow();

            var downsampling = await historian.RunDownsamplingAsync(
                scope.OrganizationId,
                scope.EnvironmentId,
                asOfUtc,
                cancellationToken,
                scope.MaxPendingHourlyWindows,
                scope.MaxPendingDailyWindows);
            await dbContext.SaveChangesAsync(cancellationToken);

            var cleanup = await historian.ApplyRetentionAsync(
                scope.OrganizationId,
                scope.EnvironmentId,
                asOfUtc,
                scope.RetentionPolicy,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (downsampling.HourlyRollupsCreated > 0
                || downsampling.DailyRollupsCreated > 0
                || cleanup.RawSamplesDeleted > 0
                || cleanup.HourlyRollupsDeleted > 0
                || cleanup.DailyRollupsDeleted > 0)
            {
                logger.LogInformation(
                    "IndustrialTelemetry historian processed {OrganizationId}/{EnvironmentId}: hourly={HourlyRollupsCreated}, daily={DailyRollupsCreated}, rawDeleted={RawSamplesDeleted}, hourlyDeleted={HourlyRollupsDeleted}, dailyDeleted={DailyRollupsDeleted}.",
                    scope.OrganizationId,
                    scope.EnvironmentId,
                    downsampling.HourlyRollupsCreated,
                    downsampling.DailyRollupsCreated,
                    cleanup.RawSamplesDeleted,
                    cleanup.HourlyRollupsDeleted,
                    cleanup.DailyRollupsDeleted);
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
                "IndustrialTelemetry historian scheduler failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                scope.OrganizationId,
                scope.EnvironmentId);
        }
    }

    private static TimeSpan ReadInterval(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["IndustrialTelemetry:Historian:Interval"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultInterval;
        }

        if (TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var interval))
        {
            return interval;
        }

        logger.LogWarning(
            "IndustrialTelemetry historian interval value {IntervalValue} is invalid; falling back to {DefaultInterval}.",
            configured,
            DefaultInterval);
        return DefaultInterval;
    }

    private static bool ReadEnabled(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["IndustrialTelemetry:Historian:Enabled"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var normalized = configured.Trim();
        if (bool.TryParse(normalized, out var enabled))
        {
            return enabled;
        }

        if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        logger.LogWarning(
            "IndustrialTelemetry historian enabled value {EnabledValue} is invalid; disabling scheduler.",
            configured);
        return false;
    }

    private static TelemetryHistorianScope? TryReadScope(IConfiguration section, ILogger? logger)
    {
        try
        {
            return ReadScope(section, logger);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            logger?.LogWarning(ex, "IndustrialTelemetry historian scope configuration is invalid and will be skipped.");
            return null;
        }
    }

    private static TelemetryHistorianScope? ReadScope(IConfiguration section, ILogger? logger)
    {
        var organizationId = section["OrganizationId"];
        var environmentId = section["EnvironmentId"];
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            return null;
        }

        var maxPendingHourlyWindows = ReadPositiveInt(section, "MaxPendingHourlyWindows", "MaxRawSamples", DefaultMaxPendingHourlyWindows, logger);
        var maxPendingDailyWindows = ReadPositiveInt(section, "MaxPendingDailyWindows", "MaxHourlyRollups", DefaultMaxPendingDailyWindows, logger);

        return new TelemetryHistorianScope(
            organizationId.Trim(),
            environmentId.Trim(),
            ReadRetention(section, "RawRetention", DefaultRawRetention, logger),
            ReadRetention(section, "HourlyRetention", DefaultHourlyRetention, logger),
            ReadRetention(section, "DailyRetention", DefaultDailyRetention, logger),
            Math.Min(maxPendingHourlyWindows, 250000),
            Math.Min(maxPendingDailyWindows, 250000));
    }

    private static int ReadPositiveInt(IConfiguration section, string key, string? legacyKey, int defaultValue, ILogger? logger)
    {
        var configured = section[key];
        if (string.IsNullOrWhiteSpace(configured) && legacyKey is not null)
        {
            configured = section[legacyKey];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                logger?.LogWarning(
                    "IndustrialTelemetry historian {LegacyKey} is deprecated; use {Key}. The value still limits pending windows, not raw rows.",
                    legacyKey,
                    key);
            }
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultValue;
        }

        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        logger?.LogWarning("IndustrialTelemetry historian {Key} value {Value} is invalid; falling back to {DefaultValue}.", key, configured, defaultValue);
        return defaultValue;
    }

    private static TimeSpan ReadRetention(IConfiguration section, string key, TimeSpan defaultValue, ILogger? logger)
    {
        var configured = section[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultValue;
        }

        if (TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var value) && value > TimeSpan.Zero)
        {
            return value;
        }

        logger?.LogWarning("IndustrialTelemetry historian retention {Key} value {Value} is invalid; falling back to {DefaultValue}.", key, configured, defaultValue);
        return defaultValue;
    }
}

public sealed record TelemetryHistorianScope(
    string OrganizationId,
    string EnvironmentId,
    TimeSpan RawRetention,
    TimeSpan HourlyRetention,
    TimeSpan DailyRetention,
    int MaxPendingHourlyWindows,
    int MaxPendingDailyWindows)
{
    public TelemetryHistorianRetentionPolicy RetentionPolicy => new(RawRetention, HourlyRetention, DailyRetention);
}
