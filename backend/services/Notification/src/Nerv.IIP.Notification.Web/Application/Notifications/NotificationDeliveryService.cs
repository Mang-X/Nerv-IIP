using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

public sealed class NotificationDeliveryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public bool RetryWorkerEnabled { get; set; } = true;
    public TimeSpan RetryPollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public Dictionary<string, int> ChannelRateLimits { get; set; } = new(StringComparer.Ordinal);
}

public sealed record NotificationDeliveryRequest(
    string OrganizationId,
    string EnvironmentId,
    string NotificationType,
    string Severity,
    string Channel,
    string RecipientRef,
    string RecipientAddress,
    string Title,
    string Summary,
    string? ResourceType,
    string? ResourceId,
    string? FileId);

public sealed record NotificationDeliveryProviderResult(bool Success, string? ProviderMessageId, string? FailureReason)
{
    public static NotificationDeliveryProviderResult Succeeded(string? providerMessageId = null)
    {
        return new NotificationDeliveryProviderResult(true, providerMessageId, null);
    }

    public static NotificationDeliveryProviderResult Failed(string failureReason)
    {
        return new NotificationDeliveryProviderResult(false, null, failureReason);
    }
}

public interface INotificationDeliveryProvider
{
    string Channel { get; }
    Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken);
}

public sealed class NotificationChannelRateLimiter
{
    private readonly object gate = new();
    private readonly Dictionary<(string Channel, DateTimeOffset WindowStart), int> deliveryCounts = [];

    public bool TryAcquire(string channel, DateTimeOffset now, IReadOnlyDictionary<string, int> channelRateLimits)
    {
        var maxPerMinute = 0;
        foreach (var rateLimit in channelRateLimits)
        {
            if (string.Equals(rateLimit.Key, channel, StringComparison.OrdinalIgnoreCase))
            {
                maxPerMinute = rateLimit.Value;
                break;
            }
        }

        if (maxPerMinute <= 0)
        {
            return true;
        }

        var windowStart = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            0,
            now.Offset);

        lock (gate)
        {
            foreach (var expiredKey in deliveryCounts.Keys.Where(x => x.WindowStart < windowStart.AddMinutes(-1)).ToArray())
            {
                deliveryCounts.Remove(expiredKey);
            }

            var key = (channel, windowStart);
            deliveryCounts.TryGetValue(key, out var current);
            if (current >= maxPerMinute)
            {
                return false;
            }

            deliveryCounts[key] = current + 1;
            return true;
        }
    }
}

public sealed class NotificationDeliveryService(
    ApplicationDbContext dbContext,
    IEnumerable<INotificationDeliveryProvider> providers,
    IOptions<NotificationDeliveryOptions> deliveryOptions,
    NotificationChannelRateLimiter rateLimiter)
{
    private readonly IReadOnlyDictionary<string, INotificationDeliveryProvider> providerByChannel = providers
        .GroupBy(x => x.Channel, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    private readonly NotificationDeliveryOptions options = deliveryOptions.Value;

    public async Task StageSubmittedIntentAsync(NotificationIntent intent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var message in intent.Messages)
        {
            dbContext.DeliveryAttempts.Add(DeliveryAttempt.Succeeded(
                message.Id,
                NotificationDeliveryChannels.InApp,
                now));
        }

        var bindings = await LoadBindingsAsync(intent, cancellationToken);
        if (bindings.Count == 0)
        {
            return;
        }

        var preferences = await LoadPreferencesAsync(intent, cancellationToken);
        var subscriptions = await LoadSubscriptionsAsync(intent, cancellationToken);
        foreach (var message in intent.Messages)
        {
            foreach (var binding in bindings.Where(x => string.Equals(x.RecipientRef, message.RecipientRef, StringComparison.Ordinal)))
            {
                if (!ShouldDispatchExternal(intent, binding, preferences, subscriptions))
                {
                    continue;
                }

                DispatchExternalAttempt(intent, message, binding, now);
            }
        }
    }

    public async Task<int> DispatchDueAttemptsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var startedAttempts = await LoadStartedAttemptsAsync(100, cancellationToken);
        var remaining = Math.Max(0, 100 - startedAttempts.Count);
        var duePendingAttempts = remaining == 0
            ? new List<DeliveryAttempt>()
            : await LoadDuePendingAttemptsAsync(now, remaining, cancellationToken);
        var attempts = startedAttempts.Concat(duePendingAttempts).ToList();
        foreach (var attempt in attempts)
        {
            var message = await dbContext.NotificationMessages
                .SingleAsync(x => x.Id == attempt.NotificationMessageId, cancellationToken);
            var intent = await dbContext.NotificationIntents
                .SingleAsync(x => x.Id == message.NotificationIntentId, cancellationToken);

            if (string.Equals(attempt.Status, NotificationDeliveryAttemptStatuses.PendingRetry, StringComparison.Ordinal))
            {
                attempt.StartRetry(now);
            }

            await SendAttemptAsync(intent, message, attempt, now, cancellationToken);
        }

        if (attempts.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return attempts.Count;
    }

    private async Task<IReadOnlyCollection<NotificationRecipientChannelBinding>> LoadBindingsAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken)
    {
        var recipientRefs = intent.Messages.Select(x => x.RecipientRef).Distinct(StringComparer.Ordinal).ToArray();
        return await dbContext.RecipientChannelBindings
            .Where(x =>
                x.OrganizationId == intent.OrganizationId
                && x.EnvironmentId == intent.EnvironmentId
                && x.Enabled
                && recipientRefs.Contains(x.RecipientRef))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<NotificationPreference>> LoadPreferencesAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken)
    {
        var recipientRefs = intent.Messages.Select(x => x.RecipientRef).Distinct(StringComparer.Ordinal).ToArray();
        return await dbContext.NotificationPreferences
            .Where(x =>
                x.OrganizationId == intent.OrganizationId
                && x.EnvironmentId == intent.EnvironmentId
                && recipientRefs.Contains(x.RecipientRef)
                && (x.NotificationType == intent.SourceEventType || x.NotificationType == "*"))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<NotificationSubscription>> LoadSubscriptionsAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken)
    {
        var recipientRefs = intent.Messages.Select(x => x.RecipientRef).Distinct(StringComparer.Ordinal).ToArray();
        return await dbContext.NotificationSubscriptions
            .Where(x =>
                x.OrganizationId == intent.OrganizationId
                && x.EnvironmentId == intent.EnvironmentId
                && x.Enabled
                && recipientRefs.Contains(x.RecipientRef)
                && (x.NotificationType == intent.SourceEventType || x.NotificationType == "*"))
            .ToListAsync(cancellationToken);
    }

    private static bool ShouldDispatchExternal(
        NotificationIntent intent,
        NotificationRecipientChannelBinding binding,
        IReadOnlyCollection<NotificationPreference> preferences,
        IReadOnlyCollection<NotificationSubscription> subscriptions)
    {
        var forced = string.Equals(intent.Severity, NotificationContractConstants.SeverityCritical, StringComparison.Ordinal);
        if (!forced && !subscriptions.Any(x =>
            string.Equals(x.RecipientRef, binding.RecipientRef, StringComparison.Ordinal)
            && string.Equals(x.Channel, binding.Channel, StringComparison.Ordinal)))
        {
            return false;
        }

        var preference = preferences.FirstOrDefault(x =>
            string.Equals(x.RecipientRef, binding.RecipientRef, StringComparison.Ordinal)
            && string.Equals(x.Channel, binding.Channel, StringComparison.Ordinal)
            && string.Equals(x.NotificationType, intent.SourceEventType, StringComparison.Ordinal))
            ?? preferences.FirstOrDefault(x =>
                string.Equals(x.RecipientRef, binding.RecipientRef, StringComparison.Ordinal)
                && string.Equals(x.Channel, binding.Channel, StringComparison.Ordinal)
                && string.Equals(x.NotificationType, "*", StringComparison.Ordinal));

        return forced || preference is null || preference.Enabled;
    }

    private void DispatchExternalAttempt(
        NotificationIntent intent,
        NotificationMessage message,
        NotificationRecipientChannelBinding binding,
        DateTimeOffset now)
    {
        var attempt = DeliveryAttempt.StartExternal(
            message.Id,
            binding.Channel,
            binding.RecipientAddress,
            providerName: binding.Channel,
            now);
        dbContext.DeliveryAttempts.Add(attempt);
    }

    private async Task SendAttemptAsync(
        NotificationIntent intent,
        NotificationMessage message,
        DeliveryAttempt attempt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!providerByChannel.TryGetValue(attempt.Channel, out var provider))
        {
            attempt.MarkFailed($"No delivery provider is registered for channel '{attempt.Channel}'.", now, options.MaxAttempts, options.RetryDelay);
            return;
        }

        if (!TryAcquireRateLimit(attempt.Channel, now))
        {
            attempt.MarkFailed("rate-limit", now, options.MaxAttempts, options.RetryDelay);
            return;
        }

        NotificationDeliveryProviderResult result;
        try
        {
            result = await provider.SendAsync(new NotificationDeliveryRequest(
                intent.OrganizationId,
                intent.EnvironmentId,
                intent.SourceEventType,
                intent.Severity,
                attempt.Channel,
                message.RecipientRef,
                attempt.RecipientAddress ?? string.Empty,
                message.Title,
                message.Summary,
                message.ResourceType,
                message.ResourceId,
                message.FileId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = NotificationDeliveryProviderResult.Failed(exception.Message);
        }
        if (result.Success)
        {
            attempt.MarkSucceeded(now, result.ProviderMessageId);
            return;
        }

        attempt.MarkFailed(result.FailureReason ?? "Delivery provider failed.", now, options.MaxAttempts, options.RetryDelay);
    }

    private bool TryAcquireRateLimit(string channel, DateTimeOffset now)
    {
        return rateLimiter.TryAcquire(channel, now, options.ChannelRateLimits);
    }

    private async Task<List<DeliveryAttempt>> LoadDuePendingAttemptsAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DeliveryAttempts.Where(x =>
            x.Status == NotificationDeliveryAttemptStatuses.PendingRetry
            && x.RecipientAddress != null);

        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return await query
                .Where(x => x.NextRetryAtUtc <= now)
                .OrderBy(x => x.NextRetryAtUtc)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        // SQLite cannot translate DateTimeOffset ordering/comparison reliably; production PostgreSQL keeps this due filter in SQL.
        return (await query.ToListAsync(cancellationToken))
            .Where(x => x.NextRetryAtUtc <= now)
            .OrderBy(x => x.NextRetryAtUtc)
            .Take(take)
            .ToList();
    }

    private async Task<List<DeliveryAttempt>> LoadStartedAttemptsAsync(int take, CancellationToken cancellationToken)
    {
        var query = dbContext.DeliveryAttempts.Where(x =>
            x.Status == NotificationDeliveryAttemptStatuses.Started
            && x.Channel != NotificationDeliveryChannels.InApp
            && x.RecipientAddress != null);

        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return await query
                .OrderBy(x => x.AttemptedAtUtc)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        // SQLite cannot translate DateTimeOffset ordering; production PostgreSQL keeps this ordering and limit in SQL.
        return (await query.ToListAsync(cancellationToken))
            .OrderBy(x => x.AttemptedAtUtc)
            .Take(take)
            .ToList();
    }
}
