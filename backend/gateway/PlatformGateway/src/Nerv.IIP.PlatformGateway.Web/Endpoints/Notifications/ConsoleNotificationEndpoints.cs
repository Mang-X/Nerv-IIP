using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Notifications;

public sealed class ConsoleNotificationDeadLetterListRequest
{
    public string? ConsumerName { get; set; }
    public string? EventType { get; set; }
    public string? Status { get; set; }
    public int? Skip { get; set; }
    public int? Take { get; set; }
}

[HttpGet("/api/console/v1/notifications/messages")]
[GatewayOperationId("listConsoleNotificationMessages")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleNotificationMessagesEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<NotificationMessageListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationMessagesRead,
            GatewayNotificationEndpointContext.WithPrincipalQueryString(HttpContext, "/api/notifications/v1/messages"),
            "notification-message",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.ListMessagesAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/notifications/tasks")]
[GatewayOperationId("listConsoleNotificationTasks")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleNotificationTasksEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<NotificationTaskListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationTasksRead,
            GatewayNotificationEndpointContext.WithPrincipalQueryString(HttpContext, "/api/notifications/v1/tasks"),
            "notification-task",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.ListTasksAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/notifications/dlq")]
[GatewayOperationId("listConsoleNotificationDeadLetters")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleNotificationDeadLettersEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<ConsoleNotificationDeadLetterListRequest, ResponseData<NotificationDeadLetterListResponse>>
{
    public override async Task HandleAsync(ConsoleNotificationDeadLetterListRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersRead,
            GatewayNotificationEndpointContext.WithQueryString(HttpContext, "/api/notifications/v1/dlq"),
            "notification-dead-letter",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.ListDeadLettersAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/notifications/dlq/metrics")]
[GatewayOperationId("getConsoleNotificationDeadLetterMetrics")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleNotificationDeadLetterMetricsEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterMetricsResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersRead,
            "/api/notifications/v1/dlq/metrics",
            "notification-dead-letter",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.GetDeadLetterMetricsAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpGet("/api/console/v1/notifications/dlq/{deadLetterId}")]
[GatewayOperationId("getConsoleNotificationDeadLetter")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class GetConsoleNotificationDeadLetterEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterDetailResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var deadLetterId = Route<string>("deadLetterId")!;
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersRead,
            $"/api/notifications/v1/dlq/{Uri.EscapeDataString(deadLetterId)}",
            "notification-dead-letter",
            deadLetterId,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.GetDeadLetterAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/dlq/{deadLetterId}/replay")]
[GatewayOperationId("replayConsoleNotificationDeadLetter")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ReplayConsoleNotificationDeadLetterEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterReplayResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var deadLetterId = Route<string>("deadLetterId")!;
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersManage,
            $"/api/notifications/v1/dlq/{Uri.EscapeDataString(deadLetterId)}/replay",
            "notification-dead-letter",
            deadLetterId,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.ReplayDeadLetterAsync(context, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/dlq/replay-batch")]
[GatewayOperationId("replayConsoleNotificationDeadLetters")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ReplayConsoleNotificationDeadLettersEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<ReplayNotificationDeadLetterBatchRequest, ResponseData<NotificationDeadLetterBatchReplayResponse>>
{
    public override async Task HandleAsync(ReplayNotificationDeadLetterBatchRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersManage,
            "/api/notifications/v1/dlq/replay-batch",
            "notification-dead-letter",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.ReplayDeadLettersAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/dlq/{deadLetterId}/ignore")]
[GatewayOperationId("ignoreConsoleNotificationDeadLetter")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class IgnoreConsoleNotificationDeadLetterEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<IgnoreNotificationDeadLetterRequest, ResponseData<NotificationDeadLetterDetailResponse>>
{
    public override async Task HandleAsync(IgnoreNotificationDeadLetterRequest req, CancellationToken ct)
    {
        var deadLetterId = Route<string>("deadLetterId")!;
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeadLettersManage,
            $"/api/notifications/v1/dlq/{Uri.EscapeDataString(deadLetterId)}/ignore",
            "notification-dead-letter",
            deadLetterId,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.IgnoreDeadLetterAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/intents")]
[GatewayOperationId("submitConsoleNotificationIntent")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class SubmitConsoleNotificationIntentEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<SubmitNotificationIntentRequest, ResponseData<NotificationIntentResponse>>
{
    public override async Task HandleAsync(SubmitNotificationIntentRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationIntentsSubmit,
            "/api/notifications/v1/intents",
            req.Resource?.ResourceType,
            req.Resource?.ResourceId,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.SubmitIntentAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/messages/{messageId}/read")]
[GatewayOperationId("markConsoleNotificationMessageRead")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class MarkConsoleNotificationMessageReadEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : EndpointWithoutRequest<ResponseData<MarkNotificationMessageReadResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var messageId = Route<string>("messageId")!;
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationMessagesMarkRead,
            $"/api/notifications/v1/messages/{Uri.EscapeDataString(messageId)}/read",
            "notification-message",
            messageId,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.MarkMessageReadAsync(context, messageId, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/messages/read-batch")]
[GatewayOperationId("markConsoleNotificationMessagesRead")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class MarkConsoleNotificationMessagesReadEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<MarkNotificationMessagesReadRequest, ResponseData<IReadOnlyCollection<MarkNotificationMessageReadResponse>>>
{
    public override async Task HandleAsync(MarkNotificationMessagesReadRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationMessagesMarkRead,
            "/api/notifications/v1/messages/read-batch",
            "notification-message",
            null,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.MarkMessagesReadAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/delivery/preferences")]
[GatewayOperationId("upsertConsoleNotificationPreference")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpsertConsoleNotificationPreferenceEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<UpsertNotificationPreferenceRequest, ResponseData<NotificationPreferenceResponse>>
{
    public override async Task HandleAsync(UpsertNotificationPreferenceRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeliveryManage,
            "/api/notifications/v1/delivery/preferences",
            "notification-preference",
            req.RecipientRef,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.UpsertPreferenceAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/delivery/subscriptions")]
[GatewayOperationId("upsertConsoleNotificationSubscription")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpsertConsoleNotificationSubscriptionEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<UpsertNotificationSubscriptionRequest, ResponseData<NotificationSubscriptionResponse>>
{
    public override async Task HandleAsync(UpsertNotificationSubscriptionRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeliveryManage,
            "/api/notifications/v1/delivery/subscriptions",
            "notification-subscription",
            req.RecipientRef,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.UpsertSubscriptionAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

[HttpPost("/api/console/v1/notifications/delivery/recipient-channel-bindings")]
[GatewayOperationId("upsertConsoleNotificationRecipientChannelBinding")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class UpsertConsoleNotificationRecipientChannelBindingEndpoint(
    IGatewayNotificationClient notificationClient,
    IGatewayAuthorizationClient auth)
    : Endpoint<UpsertNotificationRecipientChannelBindingRequest, ResponseData<NotificationRecipientChannelBindingResponse>>
{
    public override async Task HandleAsync(UpsertNotificationRecipientChannelBindingRequest req, CancellationToken ct)
    {
        var context = await GatewayNotificationEndpointContext.AuthorizeAsync(
            HttpContext,
            auth,
            GatewayPermissions.NotificationDeliveryManage,
            "/api/notifications/v1/delivery/recipient-channel-bindings",
            "notification-recipient-channel-binding",
            req.RecipientRef,
            ct);
        if (context is null)
        {
            return;
        }

        try
        {
            var response = await notificationClient.UpsertRecipientChannelBindingAsync(context, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayNotificationException ex)
        {
            await GatewayNotificationEndpointResults.WriteDownstreamErrorAsync(HttpContext, ex, ct);
        }
        catch (HttpRequestException ex)
        {
            await GatewayNotificationEndpointResults.WriteBadGatewayAsync(HttpContext, ex, ct);
        }
    }
}

internal static class GatewayNotificationEndpointContext
{
    public static string WithQueryString(HttpContext context, string path) =>
        path + context.Request.QueryString.Value;

    public static string WithPrincipalQueryString(HttpContext context, string path)
        => path;

    public static async Task<GatewayNotificationRequestContext?> AuthorizeAsync(
        HttpContext context,
        IGatewayAuthorizationClient auth,
        string permissionCode,
        string downstreamRequestUri,
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        var organizationId = context.Request.Headers["X-Organization-Id"].ToString();
        var environmentId = context.Request.Headers["X-Environment-Id"].ToString();
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "X-Organization-Id and X-Environment-Id headers are required.",
                cancellationToken);
            return null;
        }

        var requirement = new GatewayPermissionRequirement(
            permissionCode,
            organizationId,
            environmentId,
            resourceType,
            resourceId);
        var principal = await GatewayAuthorization.RequirePermissionAsync(context, auth, requirement, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        var bearerToken = await context.GetTokenAsync("access_token");
        var recipientRef = principal.PrincipalId!;
        var recipientSeparator = downstreamRequestUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var scopedRequestUri = resourceType is "notification-message" or "notification-task"
            ? $"{downstreamRequestUri}{recipientSeparator}recipientRef={Uri.EscapeDataString(recipientRef)}"
            : downstreamRequestUri;
        var status = context.Request.Query["status"].ToString();
        if (!string.IsNullOrWhiteSpace(status) && resourceId is null && resourceType is "notification-message" or "notification-task")
        {
            scopedRequestUri += $"&status={Uri.EscapeDataString(status)}";
        }
        return new GatewayNotificationRequestContext(
            scopedRequestUri,
            organizationId,
            environmentId,
            bearerToken!,
            HeaderOrNull(context, "X-Correlation-Id"),
            HeaderOrNull(context, "Idempotency-Key") ?? HeaderOrNull(context, "X-Idempotency-Key"),
            requirement,
            recipientRef);
    }

    private static string? HeaderOrNull(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

internal static class GatewayNotificationEndpointResults
{
    public static async Task WriteDownstreamErrorAsync(HttpContext context, GatewayNotificationException exception, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            (int)exception.StatusCode,
            exception.Message,
            cancellationToken);
    }

    public static async Task WriteBadGatewayAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status502BadGateway,
            $"Notification unavailable: {exception.Message}",
            cancellationToken);
    }
}
