using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;
using Nerv.IIP.PlatformGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Notifications;

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
            GatewayNotificationEndpointContext.WithQueryString(HttpContext, "/api/notifications/v1/messages"),
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
            GatewayNotificationEndpointContext.WithQueryString(HttpContext, "/api/notifications/v1/tasks"),
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

internal static class GatewayNotificationEndpointContext
{
    public static string WithQueryString(HttpContext context, string path) =>
        path + context.Request.QueryString.Value;

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
        return new GatewayNotificationRequestContext(
            downstreamRequestUri,
            organizationId,
            environmentId,
            bearerToken!,
            HeaderOrNull(context, "X-Correlation-Id"),
            HeaderOrNull(context, "Idempotency-Key") ?? HeaderOrNull(context, "X-Idempotency-Key"),
            requirement);
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
