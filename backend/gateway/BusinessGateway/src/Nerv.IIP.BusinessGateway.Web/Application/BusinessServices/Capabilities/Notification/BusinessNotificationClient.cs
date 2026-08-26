using Nerv.IIP.Contracts.Notification;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessNotificationClient
{
    Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken);

    Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken);

    Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        string internalBearerToken,
        BusinessConsoleMarkNotificationMessageReadRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class HttpBusinessNotificationClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessNotificationClient
{
    public Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<NotificationMessageListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/notifications/v1/messages?" + NotificationQuery(request),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(notificationRequest, request));

    public Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<NotificationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/notifications/v1/tasks?" + NotificationQuery(request),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(notificationRequest, request));

    public Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        string internalBearerToken,
        BusinessConsoleMarkNotificationMessageReadRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MarkNotificationMessageReadResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/notifications/v1/messages/{Uri.EscapeDataString(request.MessageId)}/read?" + Query(("recipientRef", request.RecipientRef)),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(
                notificationRequest,
                request.OrganizationId,
                request.EnvironmentId));

    private static string NotificationQuery(BusinessConsoleNotificationListRequest request) =>
        Query(
            ("recipientRef", request.RecipientRef),
            ("status", request.Status));

    private static void AddNotificationScopeHeaders(HttpRequestMessage httpRequest, BusinessConsoleNotificationListRequest request)
        => AddNotificationScopeHeaders(httpRequest, request.OrganizationId, request.EnvironmentId);

    private static void AddNotificationScopeHeaders(
        HttpRequestMessage httpRequest,
        string organizationId,
        string environmentId)
    {
        httpRequest.Headers.TryAddWithoutValidation("X-Organization-Id", organizationId);
        httpRequest.Headers.TryAddWithoutValidation("X-Environment-Id", environmentId);
    }
}
