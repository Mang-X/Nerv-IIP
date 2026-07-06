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
        if (!configuration.GetValue<bool>("IndustrialTelemetry:AlarmEscalation:Enabled"))
        {
            return;
        }

        var scopes = GetConfiguredScopes(configuration, logger).Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("IndustrialTelemetry alarm escalation scheduler is enabled but no valid organization/environment scope is configured.");
            return;
        }

        var interval = configuration.GetValue("IndustrialTelemetry:AlarmEscalation:Interval", DefaultInterval);
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
            var scope = ReadScope(scopeSection, logger);
            if (scope is not null)
            {
                yield return scope;
            }
        }

        var singleScope = ReadScope(configuration.GetSection("IndustrialTelemetry:AlarmEscalation"), logger);
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
