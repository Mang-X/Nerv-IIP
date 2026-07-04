using MediatR;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;

namespace Nerv.IIP.Notification.Web.Application.DeadLetters;

public sealed class NotificationDeadLetterAlertOptions
{
    public const string SectionName = "Notification:DeadLetterAlerts";

    public bool Enabled { get; set; }
    public string? OrganizationId { get; set; }
    public string? EnvironmentId { get; set; }
    public int Threshold { get; set; } = 1;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan DedupeWindow { get; set; } = TimeSpan.FromHours(1);
    public List<string> RecipientRefs { get; set; } = ["role:ops-admin"];
}

public sealed record NotificationDeadLetterAlertResult(
    IntegrationEventDeadLetterMetrics Metrics,
    bool AlertSubmitted,
    bool Duplicate);

public sealed class NotificationDeadLetterAlertMonitor(
    IIntegrationEventDeadLetterStore deadLetterStore,
    IMediator mediator,
    IOptions<NotificationDeadLetterAlertOptions> options,
    ILogger<NotificationDeadLetterAlertMonitor> logger)
{
    public async Task<NotificationDeadLetterAlertResult> CheckOnceAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var metrics = await deadLetterStore.GetMetricsAsync(cancellationToken);
        var currentOptions = options.Value;
        if (!ShouldSubmitAlert(currentOptions, metrics))
        {
            return new NotificationDeadLetterAlertResult(metrics, AlertSubmitted: false, Duplicate: false);
        }

        var windowStart = TruncateToWindow(now, currentOptions.DedupeWindow);
        var dedupeKey = $"notification-dlq-backlog:{currentOptions.OrganizationId}:{currentOptions.EnvironmentId}:{currentOptions.Threshold}:{windowStart:yyyyMMddHHmm}";
        var response = await mediator.Send(
            new SubmitNotificationIntentCommand(
                currentOptions.OrganizationId!,
                currentOptions.EnvironmentId!,
                new SubmitNotificationIntentRequest(
                    SourceService: "notification",
                    SourceEventType: "notification.DeadLetterBacklogThresholdExceeded",
                    SourceEventId: dedupeKey,
                    IntentType: NotificationContractConstants.IntentTypeTask,
                    Severity: NotificationContractConstants.SeverityCritical,
                    DedupeKey: dedupeKey,
                    Resource: new NotificationResourceRef("notification-dead-letter-backlog", "notification-dlq", null),
                    Title: "Notification DLQ backlog threshold exceeded",
                    Summary: $"Notification DLQ actionable backlog is {metrics.ActionableCount}, threshold is {currentOptions.Threshold}. Pending={metrics.PendingCount}, Failed={metrics.FailedCount}.",
                    SuggestedRecipientRefs: currentOptions.RecipientRefs.Where(IsNonEmpty).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray()),
                now),
            cancellationToken);

        logger.LogWarning(
            "NotificationDlqBacklogThresholdExceeded ActionableCount={ActionableCount} PendingCount={PendingCount} FailedCount={FailedCount} Threshold={Threshold} Duplicate={Duplicate}",
            metrics.ActionableCount,
            metrics.PendingCount,
            metrics.FailedCount,
            currentOptions.Threshold,
            response.Duplicate);
        return new NotificationDeadLetterAlertResult(metrics, AlertSubmitted: true, response.Duplicate);
    }

    private static bool ShouldSubmitAlert(
        NotificationDeadLetterAlertOptions options,
        IntegrationEventDeadLetterMetrics metrics) =>
        options.Enabled
        && options.Threshold > 0
        && metrics.ActionableCount >= options.Threshold
        && IsNonEmpty(options.OrganizationId)
        && IsNonEmpty(options.EnvironmentId)
        && options.RecipientRefs.Any(IsNonEmpty);

    private static DateTimeOffset TruncateToWindow(DateTimeOffset value, TimeSpan window)
    {
        var utc = value.ToUniversalTime();
        if (window <= TimeSpan.Zero)
        {
            return utc;
        }

        return new DateTimeOffset(utc.Ticks / window.Ticks * window.Ticks, TimeSpan.Zero);
    }

    private static bool IsNonEmpty(string? value) => !string.IsNullOrWhiteSpace(value);
}

internal sealed class NotificationDeadLetterAlertWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<NotificationDeadLetterAlertOptions> options,
    ILogger<NotificationDeadLetterAlertWorker> logger) : BackgroundService
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
                var monitor = scope.ServiceProvider.GetRequiredService<NotificationDeadLetterAlertMonitor>();
                await monitor.CheckOnceAsync(timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification DLQ alert monitor tick failed.");
            }
        }
    }
}
