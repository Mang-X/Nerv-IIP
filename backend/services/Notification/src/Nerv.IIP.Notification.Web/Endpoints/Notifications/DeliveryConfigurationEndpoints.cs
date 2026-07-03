using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpPost("/api/notifications/v1/delivery/recipient-channel-bindings")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class UpsertNotificationRecipientChannelBindingEndpoint(ApplicationDbContext dbContext)
    : Endpoint<UpsertNotificationRecipientChannelBindingRequest, ResponseData<NotificationRecipientChannelBindingResponse>>
{
    public override async Task HandleAsync(UpsertNotificationRecipientChannelBindingRequest req, CancellationToken ct)
    {
        var organizationId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id");
        var environmentId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id");
        var recipientRef = req.RecipientRef.Trim();
        var channel = NormalizeChannel(req.Channel);
        var now = DateTimeOffset.UtcNow;

        var binding = await dbContext.RecipientChannelBindings.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.RecipientRef == recipientRef
            && x.Channel == channel, ct);
        if (binding is null)
        {
            binding = NotificationRecipientChannelBinding.Create(
                organizationId,
                environmentId,
                recipientRef,
                channel,
                req.RecipientAddress.Trim(),
                now);
            dbContext.RecipientChannelBindings.Add(binding);
        }

        binding.Update(req.RecipientAddress.Trim(), req.Enabled, now);
        await dbContext.SaveChangesAsync(ct);
        await Send.OkAsync(new NotificationRecipientChannelBindingResponse(
            binding.RecipientRef,
            binding.Channel,
            binding.RecipientAddress,
            binding.Enabled,
            binding.UpdatedAtUtc).AsResponseData(), ct);
    }

    private static string NormalizeChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant();
    }
}

[HttpPost("/api/notifications/v1/delivery/preferences")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class UpsertNotificationPreferenceEndpoint(ApplicationDbContext dbContext)
    : Endpoint<UpsertNotificationPreferenceRequest, ResponseData<NotificationPreferenceResponse>>
{
    public override async Task HandleAsync(UpsertNotificationPreferenceRequest req, CancellationToken ct)
    {
        var organizationId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id");
        var environmentId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id");
        var recipientRef = req.RecipientRef.Trim();
        var notificationType = req.NotificationType.Trim();
        var channel = NormalizeChannel(req.Channel);
        var now = DateTimeOffset.UtcNow;

        var preference = await dbContext.NotificationPreferences.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.RecipientRef == recipientRef
            && x.NotificationType == notificationType
            && x.Channel == channel, ct);
        if (preference is null)
        {
            preference = NotificationPreference.Create(
                organizationId,
                environmentId,
                recipientRef,
                notificationType,
                channel,
                req.Enabled,
                now);
            dbContext.NotificationPreferences.Add(preference);
        }

        preference.Update(req.Enabled, now);
        await dbContext.SaveChangesAsync(ct);
        await Send.OkAsync(new NotificationPreferenceResponse(
            preference.RecipientRef,
            preference.NotificationType,
            preference.Channel,
            preference.Enabled,
            preference.UpdatedAtUtc).AsResponseData(), ct);
    }

    private static string NormalizeChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant();
    }
}

[HttpPost("/api/notifications/v1/delivery/subscriptions")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class UpsertNotificationSubscriptionEndpoint(ApplicationDbContext dbContext)
    : Endpoint<UpsertNotificationSubscriptionRequest, ResponseData<NotificationSubscriptionResponse>>
{
    public override async Task HandleAsync(UpsertNotificationSubscriptionRequest req, CancellationToken ct)
    {
        var organizationId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id");
        var environmentId = NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id");
        var recipientRef = req.RecipientRef.Trim();
        var notificationType = req.NotificationType.Trim();
        var channel = NormalizeChannel(req.Channel);
        var now = DateTimeOffset.UtcNow;

        var subscription = await dbContext.NotificationSubscriptions.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.RecipientRef == recipientRef
            && x.NotificationType == notificationType
            && x.Channel == channel, ct);
        if (subscription is null)
        {
            subscription = NotificationSubscription.Create(
                organizationId,
                environmentId,
                recipientRef,
                notificationType,
                channel,
                now);
            dbContext.NotificationSubscriptions.Add(subscription);
        }

        subscription.Update(req.Enabled, now);
        await dbContext.SaveChangesAsync(ct);
        await Send.OkAsync(new NotificationSubscriptionResponse(
            subscription.RecipientRef,
            subscription.NotificationType,
            subscription.Channel,
            subscription.Enabled,
            subscription.UpdatedAtUtc).AsResponseData(), ct);
    }

    private static string NormalizeChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant();
    }
}

public sealed class UpsertNotificationRecipientChannelBindingRequestValidator
    : Validator<UpsertNotificationRecipientChannelBindingRequest>
{
    public UpsertNotificationRecipientChannelBindingRequestValidator()
    {
        RuleFor(x => x.RecipientRef).NotEmpty();
        RuleFor(x => x.Channel).NotEmpty().Must(DeliveryConfigurationEndpointValidation.IsSupportedChannel);
        RuleFor(x => x.RecipientAddress).NotEmpty();
    }
}

public sealed class UpsertNotificationPreferenceRequestValidator : Validator<UpsertNotificationPreferenceRequest>
{
    public UpsertNotificationPreferenceRequestValidator()
    {
        RuleFor(x => x.RecipientRef).NotEmpty();
        RuleFor(x => x.NotificationType).NotEmpty();
        RuleFor(x => x.Channel).NotEmpty().Must(DeliveryConfigurationEndpointValidation.IsSupportedChannel);
    }
}

public sealed class UpsertNotificationSubscriptionRequestValidator : Validator<UpsertNotificationSubscriptionRequest>
{
    public UpsertNotificationSubscriptionRequestValidator()
    {
        RuleFor(x => x.RecipientRef).NotEmpty();
        RuleFor(x => x.NotificationType).NotEmpty();
        RuleFor(x => x.Channel).NotEmpty().Must(DeliveryConfigurationEndpointValidation.IsSupportedChannel);
    }
}

internal static class DeliveryConfigurationEndpointValidation
{
    public static bool IsSupportedChannel(string? channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant();
        return normalized is NotificationDeliveryChannels.WeCom
            or NotificationDeliveryChannels.DingTalk
            or NotificationDeliveryChannels.Email
            or NotificationDeliveryChannels.Webhook;
    }
}
