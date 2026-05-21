using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Infrastructure;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationEndpointTests
{
    [Fact]
    public async Task Submit_intent_creates_messages_for_recipients()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-create", "user:admin", "user:operator"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationIntentResponse>(response);
        Assert.False(data.Duplicate);
        Assert.NotEqual(string.Empty, data.IntentId);
        Assert.Equal(["user:admin", "user:operator"], data.Messages.Select(x => x.RecipientRef).Order());
        Assert.All(data.Messages, message => Assert.Equal("unread", message.Status));
    }

    [Fact]
    public async Task Submit_intent_with_duplicate_dedupe_key_returns_existing_intent()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-duplicate", "user:admin"));
        var second = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-duplicate", "user:admin"));

        var firstData = await ReadDataAsync<NotificationIntentResponse>(first);
        var secondData = await ReadDataAsync<NotificationIntentResponse>(second);
        Assert.False(firstData.Duplicate);
        Assert.True(secondData.Duplicate);
        Assert.Equal(firstData.IntentId, secondData.IntentId);
        Assert.Equal(firstData.Messages.Single().MessageId, secondData.Messages.Single().MessageId);
    }

    [Fact]
    public async Task Submit_intent_with_same_dedupe_key_but_different_source_is_not_duplicate()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-source", "user:admin"));
        var differentEventType = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-source", ["user:admin"], sourceEventType: "ops.OtherFailure"));
        var differentService = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-source", ["user:admin"], sourceService: "iam"));

        var firstData = await ReadDataAsync<NotificationIntentResponse>(first);
        var eventTypeData = await ReadDataAsync<NotificationIntentResponse>(differentEventType);
        var serviceData = await ReadDataAsync<NotificationIntentResponse>(differentService);
        Assert.False(firstData.Duplicate);
        Assert.False(eventTypeData.Duplicate);
        Assert.False(serviceData.Duplicate);
        Assert.NotEqual(firstData.IntentId, eventTypeData.IntentId);
        Assert.NotEqual(firstData.IntentId, serviceData.IntentId);
    }

    [Fact]
    public async Task List_messages_filters_by_recipient_and_status()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-admin", "user:admin"));
        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-operator", "user:operator"));

        var response = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationMessageListResponse>(response);
        var message = Assert.Single(data.Items);
        Assert.Equal("user:admin", message.RecipientRef);
        Assert.Equal("unread", message.Status);
    }

    [Fact]
    public async Task List_messages_is_limited_to_request_organization_and_environment()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");

        var orgOneCreated = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-org-1", "user:admin"));
        await orgTwoClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-org-2", "user:admin"));
        var orgOneMessageId = (await ReadDataAsync<NotificationIntentResponse>(orgOneCreated)).Messages.Single().MessageId;

        var response = await orgOneClient.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationMessageListResponse>(response);
        var message = Assert.Single(data.Items);
        Assert.Equal(orgOneMessageId, message.MessageId);
    }

    [Fact]
    public async Task Mark_message_read_sets_status_and_read_time()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await client.PostAsync($"/api/notifications/v1/messages/{messageId}/read", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<MarkNotificationMessageReadResponse>(response);
        Assert.Equal(messageId, data.MessageId);
        Assert.Equal("read", data.Status);
        Assert.True(data.ReadAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Mark_message_read_from_another_organization_returns_error_and_does_not_mutate()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");
        var created = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-cross-org-read", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await orgTwoClient.PostAsync($"/api/notifications/v1/messages/{messageId}/read", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadResponse = await orgOneClient.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Contains(unread.Items, message => message.MessageId == messageId);
    }

    [Fact]
    public async Task Mark_message_read_with_invalid_message_id_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsync("/api/notifications/v1/messages/not-a-guid/read", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Read_batch_marks_multiple_messages_read()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-1", "user:admin"));
        var second = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-2", "user:admin"));
        var messageIds = new[]
        {
            (await ReadDataAsync<NotificationIntentResponse>(first)).Messages.Single().MessageId,
            (await ReadDataAsync<NotificationIntentResponse>(second)).Messages.Single().MessageId,
        };

        var response = await client.PostAsJsonAsync("/api/notifications/v1/messages/read-batch", new { messageIds });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<IReadOnlyCollection<MarkNotificationMessageReadResponse>>(response);
        Assert.Equal(messageIds.Order(), data.Select(x => x.MessageId).Order());
        Assert.All(data, item => Assert.Equal("read", item.Status));
        var unreadResponse = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Empty(unread.Items);
    }

    [Fact]
    public async Task Read_batch_with_invalid_id_does_not_mark_any_messages_read()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-invalid", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await client.PostAsJsonAsync(
            "/api/notifications/v1/messages/read-batch",
            new { messageIds = new[] { messageId, Guid.NewGuid().ToString() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadResponse = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Contains(unread.Items, message => message.MessageId == messageId);
    }

    [Fact]
    public async Task Read_batch_with_empty_ids_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsJsonAsync("/api/notifications/v1/messages/read-batch", new { messageIds = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_tasks_filters_by_recipient_and_status()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;
        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-other", "user:operator"));

        var response = await client.GetAsync("/api/notifications/v1/tasks?recipientRef=user:admin&status=open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationTaskListResponse>(response);
        var task = Assert.Single(data.Items);
        Assert.Equal(messageId, task.MessageId);
        Assert.Equal("user:admin", task.RecipientRef);
        Assert.Equal("open", task.Status);
    }

    [Fact]
    public async Task List_tasks_is_limited_to_request_organization_and_environment()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");

        var orgOneCreated = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-org-1", "user:admin"));
        await orgTwoClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-org-2", "user:admin"));
        var orgOneMessageId = (await ReadDataAsync<NotificationIntentResponse>(orgOneCreated)).Messages.Single().MessageId;

        var response = await orgOneClient.GetAsync("/api/notifications/v1/tasks?recipientRef=user:admin&status=open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationTaskListResponse>(response);
        var task = Assert.Single(data.Items);
        Assert.Equal(orgOneMessageId, task.MessageId);
    }

    [Fact]
    public async Task Missing_organization_or_environment_header_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var missingOrganization = factory.CreateNotificationClient(organizationId: null, environmentId: "env-001");
        using var missingEnvironment = factory.CreateNotificationClient(organizationId: "org-001", environmentId: null);

        var missingOrganizationResponse = await missingOrganization.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-missing-org", "user:admin"));
        var missingEnvironmentResponse = await missingEnvironment.GetAsync("/api/notifications/v1/messages");

        Assert.Equal(HttpStatusCode.BadRequest, missingOrganizationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingEnvironmentResponse.StatusCode);
    }

    private static SubmitNotificationIntentRequest CreateIntent(string dedupeKey, params string[] recipients)
    {
        return CreateIntent(dedupeKey, (IReadOnlyCollection<string>)recipients);
    }

    private static SubmitNotificationIntentRequest CreateIntent(
        string dedupeKey,
        IReadOnlyCollection<string> recipients,
        string sourceService = "ops",
        string sourceEventType = "ops.OperationTaskFailed")
    {
        return new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: sourceEventType,
            SourceEventId: dedupeKey,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", dedupeKey, null),
            Title: "Restart failed",
            Summary: "Instance restart failed with timeout.",
            SuggestedRecipientRefs: recipients);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var data = document.RootElement.GetProperty("data");
        return data.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed class NotificationWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                });
            });
        }

        public HttpClient CreateNotificationClient(string? organizationId = "org-001", string? environmentId = "env-001")
        {
            var client = CreateClient();
            if (organizationId is not null)
            {
                client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId);
            }

            if (environmentId is not null)
            {
                client.DefaultRequestHeaders.Add("X-Environment-Id", environmentId);
            }

            return client;
        }
    }
}
