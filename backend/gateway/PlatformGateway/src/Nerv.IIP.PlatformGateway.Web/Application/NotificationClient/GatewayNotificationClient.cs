using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;

public sealed record GatewayNotificationRequestContext(
    string RequestUri,
    string OrganizationId,
    string EnvironmentId,
    string BearerToken,
    string? CorrelationId,
    string? IdempotencyKey,
    GatewayPermissionRequirement PermissionRequirement,
    string RecipientRef = "");

public sealed class GatewayNotificationException(
    HttpStatusCode statusCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public interface IGatewayNotificationClient
{
    Task<NotificationMessageListResponse> ListMessagesAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationTaskListResponse> ListTasksAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterListResponse> ListDeadLettersAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterMetricsResponse> GetDeadLetterMetricsAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterDetailResponse> GetDeadLetterAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterReplayResponse> ReplayDeadLetterAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterBatchReplayResponse> ReplayDeadLettersAsync(
        GatewayNotificationRequestContext context,
        ReplayNotificationDeadLetterBatchRequest request,
        CancellationToken cancellationToken);

    Task<NotificationDeadLetterDetailResponse> IgnoreDeadLetterAsync(
        GatewayNotificationRequestContext context,
        IgnoreNotificationDeadLetterRequest request,
        CancellationToken cancellationToken);

    Task<NotificationIntentResponse> SubmitIntentAsync(
        GatewayNotificationRequestContext context,
        SubmitNotificationIntentRequest request,
        CancellationToken cancellationToken);

    Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        GatewayNotificationRequestContext context,
        string messageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<MarkNotificationMessageReadResponse>> MarkMessagesReadAsync(
        GatewayNotificationRequestContext context,
        MarkNotificationMessagesReadRequest request,
        CancellationToken cancellationToken);

    Task<NotificationPreferenceResponse> UpsertPreferenceAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationPreferenceRequest request,
        CancellationToken cancellationToken);

    Task<NotificationSubscriptionResponse> UpsertSubscriptionAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationSubscriptionRequest request,
        CancellationToken cancellationToken);

    Task<NotificationRecipientChannelBindingResponse> UpsertRecipientChannelBindingAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationRecipientChannelBindingRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpGatewayNotificationClient(HttpClient httpClient, IInternalServiceTokenProvider internalServiceToken) : IGatewayNotificationClient
{
    public Task<NotificationMessageListResponse> ListMessagesAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationMessageListResponse>(
            context,
            HttpMethod.Get,
            () => null,
            cancellationToken);

    public Task<NotificationTaskListResponse> ListTasksAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationTaskListResponse>(
            context,
            HttpMethod.Get,
            () => null,
            cancellationToken);

    public Task<NotificationDeadLetterListResponse> ListDeadLettersAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterListResponse>(
            context,
            HttpMethod.Get,
            () => null,
            cancellationToken);

    public Task<NotificationDeadLetterMetricsResponse> GetDeadLetterMetricsAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterMetricsResponse>(
            context,
            HttpMethod.Get,
            () => null,
            cancellationToken);

    public Task<NotificationDeadLetterDetailResponse> GetDeadLetterAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterDetailResponse>(
            context,
            HttpMethod.Get,
            () => null,
            cancellationToken);

    public Task<NotificationDeadLetterReplayResponse> ReplayDeadLetterAsync(
        GatewayNotificationRequestContext context,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterReplayResponse>(
            context,
            HttpMethod.Post,
            () => null,
            cancellationToken);

    public Task<NotificationDeadLetterBatchReplayResponse> ReplayDeadLettersAsync(
        GatewayNotificationRequestContext context,
        ReplayNotificationDeadLetterBatchRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterBatchReplayResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<NotificationDeadLetterDetailResponse> IgnoreDeadLetterAsync(
        GatewayNotificationRequestContext context,
        IgnoreNotificationDeadLetterRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationDeadLetterDetailResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<NotificationIntentResponse> SubmitIntentAsync(
        GatewayNotificationRequestContext context,
        SubmitNotificationIntentRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationIntentResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        GatewayNotificationRequestContext context,
        string messageId,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<MarkNotificationMessageReadResponse>(
            context,
            HttpMethod.Post,
            () => null,
            cancellationToken);

    public Task<IReadOnlyCollection<MarkNotificationMessageReadResponse>> MarkMessagesReadAsync(
        GatewayNotificationRequestContext context,
        MarkNotificationMessagesReadRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<IReadOnlyCollection<MarkNotificationMessageReadResponse>>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<NotificationPreferenceResponse> UpsertPreferenceAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationPreferenceRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationPreferenceResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<NotificationSubscriptionResponse> UpsertSubscriptionAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationSubscriptionRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationSubscriptionResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    public Task<NotificationRecipientChannelBindingResponse> UpsertRecipientChannelBindingAsync(
        GatewayNotificationRequestContext context,
        UpsertNotificationRecipientChannelBindingRequest request,
        CancellationToken cancellationToken) =>
        SendForJsonAsync<NotificationRecipientChannelBindingResponse>(
            context,
            HttpMethod.Post,
            () => JsonContent.Create(request),
            cancellationToken);

    private async Task<T> SendForJsonAsync<T>(
        GatewayNotificationRequestContext context,
        HttpMethod method,
        Func<HttpContent?> contentFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, context.RequestUri);
            request.Content = contentFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalServiceToken.BearerToken);
            request.Headers.TryAddWithoutValidation("X-Organization-Id", context.OrganizationId);
            request.Headers.TryAddWithoutValidation("X-Environment-Id", context.EnvironmentId);
            if (!string.IsNullOrWhiteSpace(context.CorrelationId))
            {
                request.Headers.TryAddWithoutValidation("X-Correlation-Id", context.CorrelationId);
            }

            if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", context.IdempotencyKey);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
            if (envelope is null)
            {
                throw new HttpRequestException("Notification returned an empty response.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new GatewayNotificationException(
                    response.StatusCode,
                    string.IsNullOrWhiteSpace(envelope.Message) ? response.ReasonPhrase ?? "Notification request failed." : envelope.Message);
            }

            if (!envelope.Success)
            {
                throw new GatewayNotificationException(
                    response.StatusCode,
                    string.IsNullOrWhiteSpace(envelope.Message) ? "Notification request failed." : envelope.Message);
            }

            return envelope.Data ?? throw new HttpRequestException("Notification returned an empty data response.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Notification returned an invalid response.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new HttpRequestException("Notification returned an invalid response.", ex);
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
