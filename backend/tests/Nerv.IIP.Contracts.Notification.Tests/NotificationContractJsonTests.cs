using System.Text.Json;
using Nerv.IIP.Contracts.Notification;

namespace Nerv.IIP.Contracts.Notification.Tests;

public sealed class NotificationContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Submit_notification_intent_request_serializes_with_web_json_names()
    {
        var request = new SubmitNotificationIntentRequest(
            SourceService: "ops",
            SourceEventType: "ops.OperationTaskFailed",
            SourceEventId: "event-001",
            IntentType: "task",
            Severity: "critical",
            DedupeKey: "ops.OperationTaskFailed:task-001",
            Resource: new NotificationResourceRef("operation-task", "task-001", null),
            Title: "Restart failed",
            Summary: "Instance restart failed with timeout.",
            SuggestedRecipientRefs: ["role:ops-admin"]);

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ops", root.GetProperty("sourceService").GetString());
        Assert.Equal("ops.OperationTaskFailed", root.GetProperty("sourceEventType").GetString());
        Assert.Equal("ops.OperationTaskFailed:task-001", root.GetProperty("dedupeKey").GetString());
        Assert.Equal("task-001", root.GetProperty("resource").GetProperty("resourceId").GetString());
        Assert.Equal("role:ops-admin", root.GetProperty("suggestedRecipientRefs")[0].GetString());
    }
}
