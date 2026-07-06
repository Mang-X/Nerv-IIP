using System.Globalization;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Scheduling;

public sealed class AlarmEscalationScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AlarmEscalationScheduler> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private const int DefaultBatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadEnabled(configuration, logger))
        {
            return;
        }

        var scopes = GetConfiguredScopes(configuration, logger).Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("IndustrialTelemetry alarm escalation scheduler is enabled but no valid organization/environment scope is configured.");
            return;
        }

        var interval = ReadInterval(configuration, logger);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "IndustrialTelemetry alarm escalation interval {Interval} is not positive; falling back to {DefaultInterval}.",
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

    public static IEnumerable<AlarmEscalationScope> GetConfiguredScopes(IConfiguration configuration, ILogger? logger = null)
    {
        foreach (var scopeSection in configuration.GetSection("IndustrialTelemetry:AlarmEscalation:Scopes").GetChildren())
        {
            var scope = TryReadScope(scopeSection, logger);
            if (scope is not null)
            {
                yield return scope;
            }
        }

        var singleScope = TryReadScope(configuration.GetSection("IndustrialTelemetry:AlarmEscalation"), logger);
        if (singleScope is not null)
        {
            yield return singleScope;
        }
    }

    private async Task TryRunAllScopesAsync(IReadOnlyCollection<AlarmEscalationScope> scopes, CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            await TryRunAsync(scope, cancellationToken);
        }
    }

    private async Task TryRunAsync(AlarmEscalationScope scope, CancellationToken cancellationToken)
    {
        try
        {
            using var serviceScope = scopeFactory.CreateScope();
            var sender = serviceScope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(
                new RunAlarmEscalationsCommand(
                    scope.OrganizationId,
                    scope.EnvironmentId,
                    timeProvider.GetUtcNow(),
                    scope.UnacknowledgedTimeoutMinutes,
                    scope.SeverityLevels,
                    scope.RecipientRefs,
                    scope.MaxAlarms),
                cancellationToken);
            if (result.EscalatedCount > 0)
            {
                logger.LogInformation(
                    "Escalated {EscalatedCount} IndustrialTelemetry alarms for {OrganizationId}/{EnvironmentId}.",
                    result.EscalatedCount,
                    scope.OrganizationId,
                    scope.EnvironmentId);
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
                "IndustrialTelemetry alarm escalation scheduler failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                scope.OrganizationId,
                scope.EnvironmentId);
        }
    }

    private static TimeSpan ReadInterval(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["IndustrialTelemetry:AlarmEscalation:Interval"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultInterval;
        }

        if (TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var interval))
        {
            return interval;
        }

        logger.LogWarning(
            "IndustrialTelemetry alarm escalation interval value {IntervalValue} is invalid; falling back to {DefaultInterval}.",
            configured,
            DefaultInterval);
        return DefaultInterval;
    }

    private static bool ReadEnabled(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["IndustrialTelemetry:AlarmEscalation:Enabled"];
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
            "IndustrialTelemetry alarm escalation enabled value {EnabledValue} is invalid; disabling scheduler.",
            configured);
        return false;
    }

    private static AlarmEscalationScope? TryReadScope(IConfiguration section, ILogger? logger)
    {
        try
        {
            return ReadScope(section, logger);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            logger?.LogWarning(ex, "IndustrialTelemetry alarm escalation scope configuration is invalid and will be skipped.");
            return null;
        }
    }

    private static AlarmEscalationScope? ReadScope(IConfiguration section, ILogger? logger)
    {
        var organizationId = section["OrganizationId"];
        var environmentId = section["EnvironmentId"];
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            return null;
        }

        var recipientRefs = ReadValues(section.GetSection("RecipientRefs"), section["RecipientRefs"]);
        if (recipientRefs.Length == 0)
        {
            logger?.LogWarning(
                "IndustrialTelemetry alarm escalation scope {OrganizationId}/{EnvironmentId} has no recipient refs and will be skipped.",
                organizationId,
                environmentId);
            return null;
        }

        var severityLevels = ReadValues(section.GetSection("SeverityLevels"), section["SeverityLevels"]);
        var timeoutMinutes = section.GetValue("UnacknowledgedTimeoutMinutes", 0);
        var maxAlarms = section.GetValue("MaxAlarms", DefaultBatchSize);
        if (maxAlarms <= 0)
        {
            maxAlarms = DefaultBatchSize;
        }

        return new AlarmEscalationScope(
            organizationId.Trim(),
            environmentId.Trim(),
            timeoutMinutes,
            severityLevels,
            recipientRefs,
            Math.Min(maxAlarms, 5000));
    }

    private static string[] ReadValues(IConfigurationSection section, string? scalarValue)
    {
        var childValues = section.GetChildren()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (childValues.Length > 0)
        {
            return childValues;
        }

        return scalarValue?
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
    }
}

public sealed record AlarmEscalationScope(
    string OrganizationId,
    string EnvironmentId,
    int UnacknowledgedTimeoutMinutes,
    IReadOnlyCollection<string> SeverityLevels,
    IReadOnlyCollection<string> RecipientRefs,
    int MaxAlarms);
