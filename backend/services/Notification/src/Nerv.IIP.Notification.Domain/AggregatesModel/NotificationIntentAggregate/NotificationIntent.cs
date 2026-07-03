using Nerv.IIP.Notification.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;

public partial record NotificationIntentId : IGuidStronglyTypedId;
public partial record NotificationMessageId : IGuidStronglyTypedId;
public partial record NotificationTaskId : IGuidStronglyTypedId;

public static class NotificationIntentTypes
{
    public const string Message = "message";
    public const string Task = "task";
}

public static class NotificationMessageStatuses
{
    public const string Unread = "unread";
    public const string Read = "read";
}

public static class NotificationSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public static class NotificationTaskStatuses
{
    public const string Open = "open";
}

public static class NotificationTaskTypes
{
    public const string Review = "review";
}

public class NotificationIntent : Entity<NotificationIntentId>, IAggregateRoot
{
    private readonly List<NotificationMessage> _messages = [];
    private readonly List<NotificationTask> _tasks = [];

    protected NotificationIntent()
    {
    }

    public NotificationIntent(
        string organizationId,
        string environmentId,
        string sourceService,
        string sourceEventType,
        string sourceEventId,
        string intentType,
        string severity,
        string dedupeKey,
        string? resourceType,
        string? resourceId,
        string? fileId,
        string title,
        string summary,
        IReadOnlyCollection<string> recipientRefs,
        DateTimeOffset createdAtUtc)
    {
        OrganizationId = Required(organizationId, "Organization is required.");
        EnvironmentId = Required(environmentId, "Environment is required.");
        SourceService = Required(sourceService, "Source service is required.");
        SourceEventType = Required(sourceEventType, "Source event type is required.");
        SourceEventId = Required(sourceEventId, "Source event id is required.");
        IntentType = RequiredIntentType(intentType);
        Severity = RequiredSeverity(severity);
        DedupeKey = Required(dedupeKey, "Dedupe key is required.");
        ResourceType = string.IsNullOrWhiteSpace(resourceType) ? null : resourceType;
        ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId;
        FileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId;
        Title = Required(title, "Title is required.");
        Summary = Required(summary, "Summary is required.");
        CreatedAtUtc = createdAtUtc;

        if (recipientRefs is null || recipientRefs.Count == 0)
        {
            throw new KnownException("Recipient refs are required.");
        }

        foreach (var recipientRef in recipientRefs)
        {
            var messageId = new NotificationMessageId(Guid.CreateVersion7());
            var message = new NotificationMessage(
                messageId,
                Required(recipientRef, "Recipient ref is required."),
                Severity,
                Title,
                Summary,
                ResourceType,
                ResourceId,
                FileId,
                createdAtUtc);
            _messages.Add(message);

            if (string.Equals(IntentType, NotificationIntentTypes.Task, StringComparison.Ordinal))
            {
                _tasks.Add(new NotificationTask(
                    new NotificationTaskId(Guid.CreateVersion7()),
                    messageId,
                    message.RecipientRef,
                    NotificationTaskTypes.Review,
                    createdAtUtc));
            }
        }

        this.AddDomainEvent(new NotificationIntentSubmittedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceEventType { get; private set; } = string.Empty;
    public string SourceEventId { get; private set; } = string.Empty;
    public string IntentType { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string DedupeKey { get; private set; } = string.Empty;
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public string? FileId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);
    public IReadOnlyCollection<NotificationMessage> Messages => _messages;
    public IReadOnlyCollection<NotificationTask> Tasks => _tasks;

    public NotificationMessage MarkRead(NotificationMessageId messageId, DateTimeOffset now)
    {
        var message = _messages.SingleOrDefault(x => x.Id == messageId)
            ?? throw new KnownException($"Notification message was not found: {messageId}");

        if (message.MarkRead(now))
        {
            this.AddDomainEvent(new NotificationMessageReadDomainEvent(this, message));
        }

        return message;
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException(message);
        }

        return value;
    }

    private static string RequiredIntentType(string? intentType)
    {
        var value = Required(intentType, "Intent type is required.");
        if (!string.Equals(value, NotificationIntentTypes.Message, StringComparison.Ordinal)
            && !string.Equals(value, NotificationIntentTypes.Task, StringComparison.Ordinal))
        {
            throw new KnownException($"Unsupported notification intent type: {value}");
        }

        return value;
    }

    private static string RequiredSeverity(string? severity)
    {
        var value = Required(severity, "Severity is required.").Trim().ToLowerInvariant();
        if (value is not NotificationSeverities.Info
            and not NotificationSeverities.Warning
            and not NotificationSeverities.Critical)
        {
            throw new KnownException($"Unsupported notification severity: {value}");
        }

        return value;
    }
}

public class NotificationMessage : Entity<NotificationMessageId>
{
    protected NotificationMessage()
    {
    }

    internal NotificationMessage(
        NotificationMessageId id,
        string recipientRef,
        string severity,
        string title,
        string summary,
        string? resourceType,
        string? resourceId,
        string? fileId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        RecipientRef = recipientRef;
        Severity = severity;
        Title = title;
        Summary = summary;
        ResourceType = resourceType;
        ResourceId = resourceId;
        FileId = fileId;
        CreatedAtUtc = createdAtUtc;
        Status = NotificationMessageStatuses.Unread;
    }

    public NotificationIntentId NotificationIntentId { get; private set; } = null!;
    public string RecipientRef { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public string? FileId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public bool IsRead => string.Equals(Status, NotificationMessageStatuses.Read, StringComparison.Ordinal);

    internal bool MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return false;
        }

        Status = NotificationMessageStatuses.Read;
        ReadAtUtc = now;
        return true;
    }
}

public class NotificationTask : Entity<NotificationTaskId>
{
    protected NotificationTask()
    {
    }

    internal NotificationTask(
        NotificationTaskId id,
        NotificationMessageId messageId,
        string recipientRef,
        string taskType,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        MessageId = messageId;
        RecipientRef = recipientRef;
        TaskType = taskType;
        Status = NotificationTaskStatuses.Open;
        CreatedAtUtc = createdAtUtc;
    }

    public NotificationIntentId NotificationIntentId { get; private set; } = null!;
    public NotificationMessageId MessageId { get; private set; } = null!;
    public string RecipientRef { get; private set; } = string.Empty;
    public string TaskType { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string? ActionRef { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
