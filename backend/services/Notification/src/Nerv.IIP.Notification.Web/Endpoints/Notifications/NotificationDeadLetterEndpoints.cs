using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

public sealed class ListNotificationDeadLettersRequest
{
    public string? ConsumerName { get; set; }
    public string? EventType { get; set; }
    public string? Status { get; set; }
    public int? Skip { get; set; }
    public int? Take { get; set; }
}

[HttpGet("/api/notifications/v1/dlq")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListNotificationDeadLettersEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : Endpoint<ListNotificationDeadLettersRequest, ResponseData<NotificationDeadLetterListResponse>>
{
    public override async Task HandleAsync(ListNotificationDeadLettersRequest req, CancellationToken ct)
    {
        var messages = await deadLetterStore.ListAsync(NotificationDeadLetterEndpointMapper.QueryFrom(req), ct);
        await Send.OkAsync(new NotificationDeadLetterListResponse(
            messages.Select(NotificationDeadLetterEndpointMapper.ToResponse).ToArray()).AsResponseData(), ct);
    }
}

[HttpGet("/api/notifications/v1/dlq/metrics")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetNotificationDeadLetterMetricsEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterMetricsResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var metrics = await deadLetterStore.GetMetricsAsync(ct);
        await Send.OkAsync(NotificationDeadLetterEndpointMapper.ToMetricsResponse(metrics).AsResponseData(), ct);
    }
}

[HttpGet("/api/notifications/v1/dlq/{deadLetterId}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetNotificationDeadLetterEndpoint(IIntegrationEventDeadLetterStore deadLetterStore)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterDetailResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var message = await deadLetterStore.GetAsync(Route<Guid>("deadLetterId"), ct);
        if (message is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(NotificationDeadLetterEndpointMapper.ToDetailResponse(message).AsResponseData(), ct);
    }
}

[HttpPost("/api/notifications/v1/dlq/{deadLetterId}/replay")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ReplayNotificationDeadLetterEndpoint(IntegrationEventDeadLetterReplayExecutor replayExecutor)
    : EndpointWithoutRequest<ResponseData<NotificationDeadLetterReplayResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await replayExecutor.ReplayAsync(Route<Guid>("deadLetterId"), ct);
        if (result.Status == "NotFound")
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(NotificationDeadLetterEndpointMapper.ToReplayResponse(result).AsResponseData(), ct);
    }
}

[HttpPost("/api/notifications/v1/dlq/replay-batch")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ReplayNotificationDeadLettersEndpoint(IntegrationEventDeadLetterReplayExecutor replayExecutor)
    : Endpoint<ReplayNotificationDeadLetterBatchRequest, ResponseData<NotificationDeadLetterBatchReplayResponse>>
{
    public override async Task HandleAsync(ReplayNotificationDeadLetterBatchRequest req, CancellationToken ct)
    {
        var results = await replayExecutor.ReplayBatchAsync(
            new IntegrationEventDeadLetterQuery(
                req.ConsumerName,
                NotificationDeadLetterEndpointMapper.ParseStatus(req.Status),
                req.EventType,
                Skip: 0,
                Take: req.Take ?? 100),
            ct);
        await Send.OkAsync(new NotificationDeadLetterBatchReplayResponse(
            results.Select(NotificationDeadLetterEndpointMapper.ToReplayResponse).ToArray()).AsResponseData(), ct);
    }
}

[HttpPost("/api/notifications/v1/dlq/{deadLetterId}/ignore")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class IgnoreNotificationDeadLetterEndpoint(
    IIntegrationEventDeadLetterStore deadLetterStore,
    TimeProvider timeProvider)
    : Endpoint<IgnoreNotificationDeadLetterRequest, ResponseData<NotificationDeadLetterDetailResponse>>
{
    public override async Task HandleAsync(IgnoreNotificationDeadLetterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            throw new KnownException("Ignore reason is required.");
        }

        var deadLetterId = Route<Guid>("deadLetterId");
        if (await deadLetterStore.GetAsync(deadLetterId, ct) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await deadLetterStore.MarkIgnoredAsync(deadLetterId, req.Reason, timeProvider.GetUtcNow(), ct);
        var updated = await deadLetterStore.GetAsync(deadLetterId, ct)
            ?? throw new InvalidOperationException($"Dead-letter message '{deadLetterId}' was not found after ignore.");
        await Send.OkAsync(NotificationDeadLetterEndpointMapper.ToDetailResponse(updated).AsResponseData(), ct);
    }
}

internal static class NotificationDeadLetterEndpointMapper
{
    public static IntegrationEventDeadLetterQuery QueryFrom(ListNotificationDeadLettersRequest request) =>
        new(
            request.ConsumerName,
            ParseStatus(request.Status),
            request.EventType,
            ParseSkip(request.Skip),
            ParseTake(request.Take));

    public static NotificationDeadLetterResponse ToResponse(IntegrationEventDeadLetterMessage message) =>
        new(
            message.Id,
            message.ConsumerName,
            message.EventId,
            message.EventType,
            message.EventVersion,
            message.SourceService,
            message.IdempotencyKey,
            message.FailureCode,
            message.FailureMessage,
            message.Status.ToString(),
            message.DeadLetteredAtUtc,
            message.ReplayedAtUtc);

    public static NotificationDeadLetterDetailResponse ToDetailResponse(IntegrationEventDeadLetterMessage message) =>
        new(
            message.Id,
            message.ConsumerName,
            message.EventId,
            message.EventType,
            message.EventVersion,
            message.SourceService,
            message.IdempotencyKey,
            message.EventClrType,
            message.EventJson,
            message.FailureCode,
            message.FailureMessage,
            message.Status.ToString(),
            message.DeadLetteredAtUtc,
            message.ReplayedAtUtc);

    public static NotificationDeadLetterReplayResponse ToReplayResponse(IntegrationEventDeadLetterReplayResult result) =>
        new(result.Id, result.Succeeded, result.Status, result.Message);

    public static NotificationDeadLetterMetricsResponse ToMetricsResponse(IntegrationEventDeadLetterMetrics metrics) =>
        new(
            metrics.ActionableCount,
            metrics.PendingCount,
            metrics.FailedCount,
            metrics.IgnoredCount,
            metrics.ReplayedCount,
            metrics.EventTypes.Select(ToEventTypeMetricsResponse).ToArray());

    private static NotificationDeadLetterEventTypeMetricsResponse ToEventTypeMetricsResponse(
        IntegrationEventDeadLetterEventTypeMetrics metrics) =>
        new(
            metrics.EventType,
            metrics.ActionableCount,
            metrics.PendingCount,
            metrics.FailedCount,
            metrics.IgnoredCount,
            metrics.ReplayedCount);

    public static IntegrationEventDeadLetterStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<IntegrationEventDeadLetterStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : throw new KnownException($"Dead-letter status '{status}' is not supported.");
    }

    private static int ParseSkip(int? skip)
    {
        if (skip is null)
        {
            return 0;
        }

        return skip >= 0
            ? skip.Value
            : throw new KnownException("Dead-letter skip must be a non-negative integer.");
    }

    private static int ParseTake(int? take)
    {
        if (take is null)
        {
            return 100;
        }

        return take > 0
            ? take.Value
            : throw new KnownException("Dead-letter take must be a positive integer.");
    }
}
