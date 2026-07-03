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

public sealed class NotificationDeliveryService(
    ApplicationDbContext dbContext,
    IEnumerable<INotificationDeliveryProvider> providers,
    IOptions<NotificationDeliveryOptions> deliveryOptions)
{
    private readonly IReadOnlyDictionary<string, INotificationDeliveryProvider> providerByChannel = providers
        .GroupBy(x => x.Channel, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    private readonly NotificationDeliveryOptions options = deliveryOptions.Value;
    private readonly Dictionary<(string Channel, DateTimeOffset WindowStart), int> deliveryCounts = [];

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

                await DispatchExternalAttemptAsync(intent, message, binding, now, cancellationToken);
            }
        }
    }

    public async Task DispatchStartedAttemptsAsync(NotificationIntent intent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var messageIds = intent.Messages.Select(x => x.Id).ToHashSet();
        var attempts = dbContext.DeliveryAttempts.Local
            .Where(x =>
                messageIds.Contains(x.NotificationMessageId)
                && x.Status == NotificationDeliveryAttemptStatuses.Started
                && x.Channel != NotificationDeliveryChannels.InApp)
            .ToList();

        foreach (var attempt in attempts)
        {
            var message = intent.Messages.Single(x => x.Id == attempt.NotificationMessageId);
            await SendAttemptAsync(intent, message, attempt, now, cancellationToken);
        }

        if (attempts.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> RetryDueAttemptsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pendingAttempts = await dbContext.DeliveryAttempts
            .Where(x =>
                x.Status == NotificationDeliveryAttemptStatuses.PendingRetry
                && x.RecipientAddress != null)
            .ToListAsync(cancellationToken);
        var dueAttempts = pendingAttempts
            .Where(x => x.NextRetryAtUtc <= now)
            .OrderBy(x => x.NextRetryAtUtc)
            .Take(100)
            .ToList();

        foreach (var attempt in dueAttempts)
        {
            var message = await dbContext.NotificationMessages
                .SingleAsync(x => x.Id == attempt.NotificationMessageId, cancellationToken);
            var intent = await dbContext.NotificationIntents
                .SingleAsync(x => x.Id == message.NotificationIntentId, cancellationToken);

            attempt.StartRetry(now);
            await SendAttemptAsync(intent, message, attempt, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return dueAttempts.Count;
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

    private Task DispatchExternalAttemptAsync(
        NotificationIntent intent,
        NotificationMessage message,
        NotificationRecipientChannelBinding binding,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempt = DeliveryAttempt.StartExternal(
            message.Id,
            binding.Channel,
            binding.RecipientAddress,
            providerName: binding.Channel,
            now);
        dbContext.DeliveryAttempts.Add(attempt);
        return Task.CompletedTask;
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
        if (!options.ChannelRateLimits.TryGetValue(channel, out var maxPerMinute) || maxPerMinute <= 0)
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
