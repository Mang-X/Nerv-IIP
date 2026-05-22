using System.Net.Http.Json;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.Sdk.Notification;

public interface INotificationClient
{
    Task<NotificationIntentResponse> SubmitIntentAsync(
        SubmitNotificationIntentRequest request,
        PlatformRequestContext context,
        CancellationToken cancellationToken = default);

    Task<NotificationMessageListResponse> ListMessagesAsync(
        PlatformRequestContext context,
        string? recipientRef = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<MarkNotificationMessageReadResponse> MarkReadAsync(
        string messageId,
        PlatformRequestContext context,
        CancellationToken cancellationToken = default);
}

public sealed class HttpNotificationClient(HttpClient httpClient) : INotificationClient
{
    public async Task<NotificationIntentResponse> SubmitIntentAsync(
        SubmitNotificationIntentRequest request,
        PlatformRequestContext context,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = PlatformApiClient.CreateRequest(
            HttpMethod.Post,
            "/api/notifications/v1/intents",
            context);
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<NotificationIntentResponse>(response, cancellationToken);
    }

    public async Task<NotificationMessageListResponse> ListMessagesAsync(
        PlatformRequestContext context,
        string? recipientRef = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        using var request = PlatformApiClient.CreateRequest(
            HttpMethod.Get,
            $"/api/notifications/v1/messages{CreateMessageQuery(recipientRef, status)}",
            context);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<NotificationMessageListResponse>(response, cancellationToken);
    }

    public async Task<MarkNotificationMessageReadResponse> MarkReadAsync(
        string messageId,
        PlatformRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        using var request = PlatformApiClient.CreateRequest(
            HttpMethod.Post,
            $"/api/notifications/v1/messages/{Uri.EscapeDataString(messageId)}/read",
            context);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await PlatformApiClient.ReadResponseDataAsync<MarkNotificationMessageReadResponse>(response, cancellationToken);
    }

    private static string CreateMessageQuery(string? recipientRef, string? status)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(recipientRef))
        {
            query.Add($"recipientRef={Uri.EscapeDataString(recipientRef)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        return query.Count is 0 ? string.Empty : $"?{string.Join("&", query)}";
    }
}
