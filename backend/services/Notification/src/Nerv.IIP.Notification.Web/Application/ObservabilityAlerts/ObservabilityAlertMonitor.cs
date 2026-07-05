using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.ServiceAuth;
using Npgsql;

namespace Nerv.IIP.Notification.Web.Application.ObservabilityAlerts;

public sealed class ObservabilityAlertOptions
{
    public const string SectionName = "Observability:Alerts";

    public bool Enabled { get; set; }
    public string? OrganizationId { get; set; }
    public string? EnvironmentId { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan DedupeWindow { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SilentWindow { get; set; } = TimeSpan.FromMinutes(10);
    public List<string> RecipientRefs { get; set; } = ["role:ops-admin"];
    public string? InternalServiceBearerToken { get; set; }
    public string? AppHubBaseUrl { get; set; }
    public List<ObservabilityAlertRuleOptions> Rules { get; set; } = [];
}

public sealed class ObservabilityAlertRuleOptions
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Severity { get; set; } = NotificationContractConstants.SeverityCritical;
    public string? HealthUrl { get; set; }
    public string? ConnectionStringName { get; set; }
    public int Threshold { get; set; } = 1;
    public double WatermarkPercent { get; set; } = 85;
    public double? CapacityMegabytes { get; set; }
    public string? MetricName { get; set; }
    public double? CurrentValue { get; set; }
    public TimeSpan HeartbeatMaxAge { get; set; } = TimeSpan.FromMinutes(5);
    public int QueryPageSize { get; set; } = 250;
}

public enum ObservabilityAlertStatus
{
    Normal,
    Firing,
    Resolved
}

public sealed record ObservabilityAlertSample(
    string RuleId,
    string RuleName,
    ObservabilityAlertStatus Status,
    string Summary,
    string? ResourceId = null,
    string? Severity = null);

public sealed record ObservabilityAlertMonitorResult(int SubmittedCount, int SuppressedCount);

public interface IObservabilityAlertProbe
{
    Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class ObservabilityAlertMonitor(
    IEnumerable<IObservabilityAlertProbe> probes,
    IMediator mediator,
    IOptions<ObservabilityAlertOptions> options,
    ILogger<ObservabilityAlertMonitor> logger)
{
    private readonly Dictionary<string, AlertState> alertStates = new(StringComparer.Ordinal);

    public async Task<ObservabilityAlertMonitorResult> CheckOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var currentOptions = options.Value;
        if (!IsConfigured(currentOptions))
        {
            return new ObservabilityAlertMonitorResult(0, 0);
        }

        var submitted = 0;
        var suppressed = 0;
        foreach (var probe in probes)
        {
            var samples = await probe.CollectAsync(now, cancellationToken);
            foreach (var sample in samples)
            {
                var state = GetState(sample.RuleId);
                switch (sample.Status)
                {
                    case ObservabilityAlertStatus.Firing:
                        if (state.LastSubmittedAtUtc is not null
                            && now - state.LastSubmittedAtUtc.Value < currentOptions.SilentWindow)
                        {
                            suppressed++;
                            continue;
                        }

                        await SubmitAsync(sample, currentOptions, now, resolved: false, cancellationToken);
                        state.Active = true;
                        state.LastSubmittedAtUtc = now;
                        submitted++;
                        break;
                    case ObservabilityAlertStatus.Resolved when state.Active:
                        await SubmitAsync(sample, currentOptions, now, resolved: true, cancellationToken);
                        state.Active = false;
                        state.LastSubmittedAtUtc = now;
                        submitted++;
                        break;
                }
            }
        }

        return new ObservabilityAlertMonitorResult(submitted, suppressed);
    }

    private async Task SubmitAsync(
        ObservabilityAlertSample sample,
        ObservabilityAlertOptions currentOptions,
        DateTimeOffset now,
        bool resolved,
        CancellationToken cancellationToken)
    {
        var windowStart = TruncateToWindow(now, currentOptions.DedupeWindow);
        var dedupeKeyPrefix = resolved ? "observability-alert-resolved" : "observability-alert";
        var dedupeKey = $"{dedupeKeyPrefix}:{sample.RuleId}:{windowStart:yyyyMMddHHmm}";
        var severity = resolved
            ? NotificationContractConstants.SeverityInfo
            : NormalizeSeverity(sample.Severity);
        var response = await mediator.Send(
            new SubmitNotificationIntentCommand(
                currentOptions.OrganizationId!,
                currentOptions.EnvironmentId!,
                new SubmitNotificationIntentRequest(
                    SourceService: "observability",
                    SourceEventType: resolved ? "observability.AlertResolved" : "observability.AlertFiring",
                    SourceEventId: dedupeKey,
                    IntentType: resolved ? NotificationContractConstants.IntentTypeMessage : NotificationContractConstants.IntentTypeTask,
                    Severity: severity,
                    DedupeKey: dedupeKey,
                    Resource: new NotificationResourceRef("observability-alert-rule", sample.ResourceId ?? sample.RuleId, null),
                    Title: resolved ? $"{sample.RuleName} resolved" : $"{sample.RuleName} firing",
                    Summary: resolved ? $"{sample.RuleName} resolved. {sample.Summary}" : sample.Summary,
                    SuggestedRecipientRefs: currentOptions.RecipientRefs.Where(IsNonEmpty).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray()),
                now),
            cancellationToken);

        logger.LogWarning(
            "ObservabilityAlertNotificationSubmitted RuleId={RuleId} Resolved={Resolved} Duplicate={Duplicate} Severity={Severity}",
            sample.RuleId,
            resolved,
            response.Duplicate,
            severity);
    }

    private AlertState GetState(string ruleId)
    {
        if (!alertStates.TryGetValue(ruleId, out var state))
        {
            state = new AlertState();
            alertStates[ruleId] = state;
        }

        return state;
    }

    private static bool IsConfigured(ObservabilityAlertOptions options) =>
        options.Enabled
        && IsNonEmpty(options.OrganizationId)
        && IsNonEmpty(options.EnvironmentId)
        && options.RecipientRefs.Any(IsNonEmpty);

    private static string NormalizeSeverity(string? value) =>
        string.Equals(value, NotificationContractConstants.SeverityWarning, StringComparison.OrdinalIgnoreCase)
            ? NotificationContractConstants.SeverityWarning
            : string.Equals(value, NotificationContractConstants.SeverityInfo, StringComparison.OrdinalIgnoreCase)
                ? NotificationContractConstants.SeverityInfo
                : NotificationContractConstants.SeverityCritical;

    private static DateTimeOffset TruncateToWindow(DateTimeOffset value, TimeSpan window)
    {
        var utc = value.ToUniversalTime();
        return window <= TimeSpan.Zero
            ? utc
            : new DateTimeOffset(utc.Ticks / window.Ticks * window.Ticks, TimeSpan.Zero);
    }

    private static bool IsNonEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed class AlertState
    {
        public bool Active { get; set; }
        public DateTimeOffset? LastSubmittedAtUtc { get; set; }
    }
}

internal sealed class ObservabilityAlertWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ObservabilityAlertOptions> options,
    ILogger<ObservabilityAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = options.Value;
        if (!currentOptions.Enabled || currentOptions.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(currentOptions.PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var monitor = scope.ServiceProvider.GetRequiredService<ObservabilityAlertMonitor>();
                await monitor.CheckOnceAsync(timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Observability alert monitor tick failed.");
            }
        }
    }
}

public sealed class ServiceHealthAlertProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<ObservabilityAlertOptions> options,
    ILogger<ServiceHealthAlertProbe> logger) : IObservabilityAlertProbe
{
    public async Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = options.Value.Rules.Where(x => IsEnabled(x, "service-health") && !string.IsNullOrWhiteSpace(x.HealthUrl));
        var samples = new List<ObservabilityAlertSample>();
        var client = httpClientFactory.CreateClient();
        foreach (var rule in rules)
        {
            try
            {
                using var response = await client.GetAsync(rule.HealthUrl, cancellationToken);
                samples.Add(new ObservabilityAlertSample(
                    rule.RuleId,
                    DisplayName(rule),
                    response.IsSuccessStatusCode ? ObservabilityAlertStatus.Resolved : ObservabilityAlertStatus.Firing,
                    $"{rule.HealthUrl} returned HTTP {(int)response.StatusCode}.",
                    rule.RuleId,
                    rule.Severity));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Service health alert probe failed RuleId={RuleId}", rule.RuleId);
                samples.Add(new ObservabilityAlertSample(
                    rule.RuleId,
                    DisplayName(rule),
                    ObservabilityAlertStatus.Firing,
                    $"{rule.HealthUrl} is unreachable: {exception.Message}",
                    rule.RuleId,
                    rule.Severity));
            }
        }

        return samples;
    }

    private static bool IsEnabled(ObservabilityAlertRuleOptions rule, string kind) =>
        rule.Enabled && string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.RuleId);

    private static string DisplayName(ObservabilityAlertRuleOptions rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleId : rule.Name;
}

public sealed class NotificationDeadLetterBacklogAlertProbe(
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOptions<ObservabilityAlertOptions> options) : IObservabilityAlertProbe
{
    public async Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = options.Value.Rules.Where(x => IsEnabled(x, "cap-dlq-backlog") || IsEnabled(x, "notification-dlq-backlog")).ToArray();
        if (rules.Length == 0)
        {
            return [];
        }

        var metrics = await deadLetterStore.GetMetricsAsync(cancellationToken);
        return rules.Select(rule => new ObservabilityAlertSample(
                rule.RuleId,
                DisplayName(rule),
                metrics.ActionableCount >= Math.Max(1, rule.Threshold) ? ObservabilityAlertStatus.Firing : ObservabilityAlertStatus.Resolved,
                $"Notification DLQ actionable backlog is {metrics.ActionableCount}, threshold is {Math.Max(1, rule.Threshold)}. Pending={metrics.PendingCount}, Failed={metrics.FailedCount}.",
                "notification-dlq",
                rule.Severity))
            .ToArray();
    }

    private static bool IsEnabled(ObservabilityAlertRuleOptions rule, string kind) =>
        rule.Enabled && string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.RuleId);

    private static string DisplayName(ObservabilityAlertRuleOptions rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleId : rule.Name;
}

public sealed class AppHubConnectorHeartbeatAlertProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<ObservabilityAlertOptions> options,
    ILogger<AppHubConnectorHeartbeatAlertProbe> logger) : IObservabilityAlertProbe
{
    public async Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var currentOptions = options.Value;
        var rules = currentOptions.Rules.Where(x => IsEnabled(x, "connector-heartbeat-stale")).ToArray();
        if (rules.Length == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(currentOptions.AppHubBaseUrl))
        {
            return rules.Select(rule => new ObservabilityAlertSample(
                rule.RuleId,
                DisplayName(rule),
                ObservabilityAlertStatus.Firing,
                "AppHubBaseUrl is not configured for connector heartbeat alert probing.",
                rule.RuleId,
                rule.Severity)).ToArray();
        }

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(currentOptions.AppHubBaseUrl, UriKind.Absolute);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            string.IsNullOrWhiteSpace(currentOptions.InternalServiceBearerToken)
                ? InternalServiceAuthentication.DefaultDevelopmentBearerToken
                : currentOptions.InternalServiceBearerToken);

        var request = new InstanceListQuery(
            currentOptions.OrganizationId ?? string.Empty,
            currentOptions.EnvironmentId ?? string.Empty,
            PageIndex: 1,
            PageSize: Math.Max(1, rules.Max(x => x.QueryPageSize)),
            SortBy: "lastHeartbeatAtUtc",
            SortOrder: "asc",
            FilterSearch: null);

        try
        {
            using var response = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var instances = await response.Content.ReadFromJsonAsync<InstanceListResponse>(cancellationToken)
                ?? new InstanceListResponse(1, request.PageSize, 0, []);
            return rules.Select(rule =>
            {
                var stale = instances.Items
                    .Where(x => x.LastHeartbeatAtUtc is null || now - x.LastHeartbeatAtUtc.Value > rule.HeartbeatMaxAge)
                    .ToArray();
                return new ObservabilityAlertSample(
                    rule.RuleId,
                    DisplayName(rule),
                    stale.Length >= Math.Max(1, rule.Threshold) ? ObservabilityAlertStatus.Firing : ObservabilityAlertStatus.Resolved,
                    $"{stale.Length} connector/application instance heartbeats are stale beyond {rule.HeartbeatMaxAge}.",
                    "apphub-connector-heartbeats",
                    rule.Severity);
            }).ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Connector heartbeat alert probe failed.");
            return rules.Select(rule => new ObservabilityAlertSample(
                rule.RuleId,
                DisplayName(rule),
                ObservabilityAlertStatus.Firing,
                $"AppHub heartbeat query failed: {exception.Message}",
                rule.RuleId,
                rule.Severity)).ToArray();
        }
    }

    private static bool IsEnabled(ObservabilityAlertRuleOptions rule, string kind) =>
        rule.Enabled && string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.RuleId);

    private static string DisplayName(ObservabilityAlertRuleOptions rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleId : rule.Name;
}

public sealed class PostgreSqlWatermarkAlertProbe(
    IConfiguration configuration,
    IOptions<ObservabilityAlertOptions> options,
    ILogger<PostgreSqlWatermarkAlertProbe> logger) : IObservabilityAlertProbe
{
    public async Task<IReadOnlyCollection<ObservabilityAlertSample>> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var samples = new List<ObservabilityAlertSample>();
        foreach (var rule in options.Value.Rules.Where(x => IsEnabled(x, "postgres-watermark")))
        {
            try
            {
                var percent = await ReadPercentAsync(rule, cancellationToken);
                if (percent is null)
                {
                    continue;
                }

                samples.Add(new ObservabilityAlertSample(
                    rule.RuleId,
                    DisplayName(rule),
                    percent >= rule.WatermarkPercent ? ObservabilityAlertStatus.Firing : ObservabilityAlertStatus.Resolved,
                    $"{rule.MetricName ?? "PostgreSQL watermark"} is {percent:0.##}%, threshold is {rule.WatermarkPercent:0.##}%.",
                    rule.RuleId,
                    rule.Severity));
            }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
            {
                logger.LogWarning(exception, "PostgreSQL watermark alert probe failed RuleId={RuleId}", rule.RuleId);
                samples.Add(new ObservabilityAlertSample(
                    rule.RuleId,
                    DisplayName(rule),
                    ObservabilityAlertStatus.Firing,
                    $"PostgreSQL watermark probe failed: {exception.Message}",
                    rule.RuleId,
                    rule.Severity));
            }
        }

        return samples;
    }

    private async Task<double?> ReadPercentAsync(ObservabilityAlertRuleOptions rule, CancellationToken cancellationToken)
    {
        if (rule.CurrentValue is not null)
        {
            return rule.CurrentValue;
        }

        var connectionStringName = string.IsNullOrWhiteSpace(rule.ConnectionStringName)
            ? "NotificationDb"
            : rule.ConnectionStringName;
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (string.Equals(rule.MetricName, "database-size", StringComparison.OrdinalIgnoreCase))
        {
            if (rule.CapacityMegabytes is null or <= 0)
            {
                return null;
            }

            await using var command = new NpgsqlCommand("select pg_database_size(current_database())", connection);
            var bytes = Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            return bytes / 1024d / 1024d / rule.CapacityMegabytes.Value * 100d;
        }

        await using var activeCommand = new NpgsqlCommand("select count(*) from pg_stat_activity", connection);
        var active = Convert.ToDouble(await activeCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        await using var maxCommand = new NpgsqlCommand("show max_connections", connection);
        var maxValue = Convert.ToDouble(await maxCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return maxValue <= 0 ? null : active / maxValue * 100d;
    }

    private static bool IsEnabled(ObservabilityAlertRuleOptions rule, string kind) =>
        rule.Enabled && string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.RuleId);

    private static string DisplayName(ObservabilityAlertRuleOptions rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleId : rule.Name;
}
