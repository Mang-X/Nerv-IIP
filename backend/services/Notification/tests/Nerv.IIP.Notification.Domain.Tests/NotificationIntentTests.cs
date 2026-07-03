using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Domain.DomainEvents;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Domain.Tests;

public sealed class NotificationIntentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-21T00:00:00Z");

    [Fact]
    public void Create_task_intent_creates_message_and_task_for_each_recipient()
    {
        var intent = CreateIntent("task", ["user:admin", "role:ops-admin"]);

        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(["role:ops-admin", "user:admin"], intent.Messages.Select(x => x.RecipientRef).Order(StringComparer.Ordinal));
        Assert.All(intent.Messages, message =>
        {
            Assert.Equal(NotificationMessageStatuses.Unread, message.Status);
            Assert.Null(message.ReadAtUtc);
        });
        Assert.Equal(2, intent.Tasks.Count);
        Assert.All(intent.Tasks, task =>
        {
            Assert.Equal(NotificationTaskTypes.Review, task.TaskType);
            Assert.Equal(NotificationTaskStatuses.Open, task.Status);
            Assert.Contains(intent.Messages, message => message.Id == task.MessageId && message.RecipientRef == task.RecipientRef);
        });
        Assert.IsType<NotificationIntentSubmittedDomainEvent>(Assert.Single(intent.GetDomainEvents()));
    }

    [Fact]
    public void Create_task_intent_assigns_non_default_unique_message_ids_before_creating_tasks()
    {
        var intent = CreateIntent(NotificationIntentTypes.Task, ["user:admin", "role:ops-admin"]);

        var messageIds = intent.Messages.Select(x => x.Id).ToArray();
        var taskIds = intent.Tasks.Select(x => x.Id).ToArray();
        var taskMessageIds = intent.Tasks.Select(x => x.MessageId).ToArray();

        Assert.All(messageIds, messageId => Assert.NotEqual(new NotificationMessageId(Guid.Empty), messageId));
        Assert.All(taskIds, taskId => Assert.NotEqual(new NotificationTaskId(Guid.Empty), taskId));
        Assert.Equal(messageIds.Length, messageIds.Distinct().Count());
        Assert.Equal(taskIds.Length, taskIds.Distinct().Count());
        Assert.Equal(taskMessageIds.Length, taskMessageIds.Distinct().Count());
        Assert.Equal(messageIds.ToHashSet(), taskMessageIds.ToHashSet());
    }

    [Fact]
    public void Create_message_intent_creates_messages_without_tasks()
    {
        var intent = CreateIntent(NotificationIntentTypes.Message, ["user:admin", "role:ops-admin"]);

        Assert.Equal(2, intent.Messages.Count);
        Assert.Empty(intent.Tasks);
    }

    [Theory]
    [InlineData("Critical", "critical")]
    [InlineData("WARNING", "warning")]
    [InlineData(" info ", "info")]
    public void Create_normalizes_supported_severity_values(string inputSeverity, string expectedSeverity)
    {
        var intent = CreateIntent(NotificationIntentTypes.Message, ["user:admin"], severity: inputSeverity);

        Assert.Equal(expectedSeverity, intent.Severity);
        Assert.All(intent.Messages, message => Assert.Equal(expectedSeverity, message.Severity));
    }

    [Theory]
    [InlineData("")]
    [InlineData("urgent")]
    public void Create_rejects_unsupported_severity_values(string severity)
    {
        var create = () => CreateIntent(NotificationIntentTypes.Message, ["user:admin"], severity: severity);

        Assert.Throws<KnownException>(create);
    }

    [Fact]
    public void MarkRead_unread_message_sets_status_and_read_time()
    {
        var intent = CreateIntent("message", ["user:admin"]);
        intent.ClearDomainEvents();
        var message = Assert.Single(intent.Messages);
        var readAt = Now.AddMinutes(5);

        var readMessage = intent.MarkRead(message.Id, readAt);

        Assert.Same(message, readMessage);
        Assert.True(readMessage.IsRead);
        Assert.Equal(NotificationMessageStatuses.Read, readMessage.Status);
        Assert.Equal(readAt, readMessage.ReadAtUtc);
        var domainEvent = Assert.IsType<NotificationMessageReadDomainEvent>(Assert.Single(intent.GetDomainEvents()));
        Assert.Same(intent, domainEvent.Intent);
        Assert.Same(message, domainEvent.Message);
    }

    [Fact]
    public void MarkRead_read_message_is_idempotent()
    {
        var intent = CreateIntent("message", ["user:admin"]);
        var message = Assert.Single(intent.Messages);
        intent.MarkRead(message.Id, Now.AddMinutes(5));
        intent.ClearDomainEvents();

        var readMessage = intent.MarkRead(message.Id, Now.AddMinutes(10));

        Assert.Same(message, readMessage);
        Assert.Equal(Now.AddMinutes(5), readMessage.ReadAtUtc);
        Assert.Empty(intent.GetDomainEvents());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("summary")]
    [InlineData("organization")]
    [InlineData("environment")]
    [InlineData("source")]
    [InlineData("recipient")]
    public void Create_rejects_blank_required_fields(string blankField)
    {
        var recipients = blankField == "recipient" ? new[] { "user:admin", " " } : new[] { "user:admin" };

        var create = () => CreateIntent(
            "message",
            recipients,
            title: blankField == "title" ? " " : "Restart failed",
            summary: blankField == "summary" ? " " : "Instance restart failed.",
            organizationId: blankField == "organization" ? " " : "org-001",
            environmentId: blankField == "environment" ? " " : "env-dev",
            sourceService: blankField == "source" ? " " : "ops");

        Assert.Throws<KnownException>(create);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("Task")]
    [InlineData("MESSAGE")]
    public void Create_rejects_unknown_or_case_mismatched_intent_type(string intentType)
    {
        var create = () => CreateIntent(intentType, ["user:admin"]);

        Assert.Throws<KnownException>(create);
    }

    private static NotificationIntent CreateIntent(
        string intentType,
        IReadOnlyCollection<string> recipientRefs,
        string title = "Restart failed",
        string summary = "Instance restart failed.",
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string sourceService = "ops",
        string severity = "critical")
    {
        return new NotificationIntent(
            organizationId,
            environmentId,
            sourceService,
            "ops.OperationTaskFailed",
            "event-001",
            intentType,
            severity,
            "ops.OperationTaskFailed:task-001",
            "operation-task",
            "task-001",
            null,
            title,
            summary,
            recipientRefs,
            Now);
    }
}
