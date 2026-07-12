using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Notifications;

[HttpGet("/api/business-console/v1/notifications/messages")]
[BusinessGatewayOperationId("listBusinessConsoleNotificationMessages")]
public sealed class ListBusinessConsoleNotificationMessagesEndpoint(
    IBusinessNotificationClient notification,
    IBusinessGatewayAuthorizationClient auth,
    IInternalServiceTokenProvider internalServiceToken)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNotificationListRequest, NotificationMessageListResponse>(
        auth,
        BusinessGatewayPermissions.NotificationMessagesRead)
{
    protected override string OrganizationId(BusinessConsoleNotificationListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleNotificationListRequest request) => request.EnvironmentId;

    protected override Task<NotificationMessageListResponse> ForwardAsync(
        BusinessConsoleNotificationListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        notification.ListMessagesAsync(
            internalServiceToken.BearerToken,
            request with { RecipientRef = RequireAuthorizedPrincipalActor().ActorRef },
            cancellationToken);
}

[HttpGet("/api/business-console/v1/notifications/tasks")]
[BusinessGatewayOperationId("listBusinessConsoleNotificationTasks")]
public sealed class ListBusinessConsoleNotificationTasksEndpoint(
    IBusinessNotificationClient notification,
    IBusinessGatewayAuthorizationClient auth,
    IInternalServiceTokenProvider internalServiceToken)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNotificationListRequest, NotificationTaskListResponse>(
        auth,
        BusinessGatewayPermissions.NotificationTasksRead)
{
    protected override string OrganizationId(BusinessConsoleNotificationListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleNotificationListRequest request) => request.EnvironmentId;

    protected override Task<NotificationTaskListResponse> ForwardAsync(
        BusinessConsoleNotificationListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        notification.ListTasksAsync(
            internalServiceToken.BearerToken,
            request with { RecipientRef = RequireAuthorizedPrincipalActor().ActorRef },
            cancellationToken);
}

[HttpPost("/api/business-console/v1/notifications/messages/{messageId}/read")]
[BusinessGatewayOperationId("markBusinessConsoleNotificationMessageRead")]
public sealed class MarkBusinessConsoleNotificationMessageReadEndpoint(
    IBusinessNotificationClient notification,
    IBusinessGatewayAuthorizationClient auth,
    IInternalServiceTokenProvider internalServiceToken)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMarkNotificationMessageReadRequest, MarkNotificationMessageReadResponse>(
        auth,
        BusinessGatewayPermissions.NotificationMessagesRead)
{
    protected override string OrganizationId(BusinessConsoleMarkNotificationMessageReadRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMarkNotificationMessageReadRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleMarkNotificationMessageReadRequest request) => "notification-message";

    protected override string ResourceId(BusinessConsoleMarkNotificationMessageReadRequest request) => request.MessageId;

    protected override Task<MarkNotificationMessageReadResponse> ForwardAsync(
        BusinessConsoleMarkNotificationMessageReadRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        notification.MarkMessageReadAsync(
            internalServiceToken.BearerToken,
            request with { RecipientRef = RequireAuthorizedPrincipalActor().ActorRef },
            cancellationToken);
}
