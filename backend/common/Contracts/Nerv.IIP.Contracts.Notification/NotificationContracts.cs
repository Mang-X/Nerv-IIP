namespace Nerv.IIP.Contracts.Notification;

public sealed record SubmitNotificationIntentRequest(
    string SourceService,
    string SourceEventType,
    string SourceEventId,
    string IntentType,
    string Severity,
    string DedupeKey,
    NotificationResourceRef? Resource,
    string Title,
    string Summary,
    IReadOnlyCollection<string> SuggestedRecipientRefs);

public sealed record NotificationResourceRef(string ResourceType, string ResourceId, string? FileId);

public sealed record NotificationIntentResponse(
    string IntentId,
    bool Duplicate,
    IReadOnlyCollection<NotificationMessageResponse> Messages);

public sealed record NotificationMessageResponse(
    string MessageId,
    string IntentId,
    string RecipientRef,
    string Status,
    string Severity,
    string Title,
    string Summary,
    NotificationResourceRef? Resource,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record NotificationMessageListResponse(IReadOnlyCollection<NotificationMessageResponse> Items);

public sealed record NotificationTaskResponse(
    string TaskId,
    string MessageId,
    string RecipientRef,
    string TaskType,
    string Status,
    string? ActionRef,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationTaskListResponse(IReadOnlyCollection<NotificationTaskResponse> Items);

public sealed record MarkNotificationMessagesReadRequest(IReadOnlyCollection<string> MessageIds);

public sealed record MarkNotificationMessageReadResponse(string MessageId, string Status, DateTimeOffset ReadAtUtc);

public sealed record NotificationDeadLetterListResponse(IReadOnlyCollection<NotificationDeadLetterResponse> Items);

public sealed record NotificationDeadLetterResponse(
    Guid Id,
    string ConsumerName,
    string? EventId,
    string? EventType,
    int? EventVersion,
    string? SourceService,
    string? IdempotencyKey,
    string FailureCode,
    string FailureMessage,
    string Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc);

public sealed record NotificationDeadLetterDetailResponse(
    Guid Id,
    string ConsumerName,
    string? EventId,
    string? EventType,
    int? EventVersion,
    string? SourceService,
    string? IdempotencyKey,
    string EventClrType,
    string EventJson,
    string FailureCode,
    string FailureMessage,
    string Status,
    DateTimeOffset DeadLetteredAtUtc,
    DateTimeOffset? ReplayedAtUtc);

public sealed record ReplayNotificationDeadLetterBatchRequest(
    string? ConsumerName,
    string? EventType,
    string? Status,
    int? Take);

public sealed record IgnoreNotificationDeadLetterRequest(string Reason);

public sealed record NotificationDeadLetterReplayResponse(
    Guid Id,
    bool Succeeded,
    string Status,
    string? Message);

public sealed record NotificationDeadLetterBatchReplayResponse(
    IReadOnlyCollection<NotificationDeadLetterReplayResponse> Items);

public sealed record NotificationDeadLetterMetricsResponse(
    int ActionableCount,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount,
    IReadOnlyCollection<NotificationDeadLetterEventTypeMetricsResponse> EventTypes);

public sealed record NotificationDeadLetterEventTypeMetricsResponse(
    string EventType,
    int ActionableCount,
    int PendingCount,
    int FailedCount,
    int IgnoredCount,
    int ReplayedCount);

public sealed record UpsertNotificationRecipientChannelBindingRequest(
    string RecipientRef,
    string Channel,
    string RecipientAddress,
    bool Enabled);

public sealed record NotificationRecipientChannelBindingResponse(
    string RecipientRef,
    string Channel,
    string RecipientAddress,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertNotificationPreferenceRequest(
    string RecipientRef,
    string NotificationType,
    string Channel,
    bool Enabled);

public sealed record NotificationPreferenceResponse(
    string RecipientRef,
    string NotificationType,
    string Channel,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertNotificationSubscriptionRequest(
    string RecipientRef,
    string NotificationType,
    string Channel,
    bool Enabled);

public sealed record NotificationSubscriptionResponse(
    string RecipientRef,
    string NotificationType,
    string Channel,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public static class NotificationContractConstants
{
    public const string IntentTypeMessage = "message";
    public const string IntentTypeTask = "task";
    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";
}
