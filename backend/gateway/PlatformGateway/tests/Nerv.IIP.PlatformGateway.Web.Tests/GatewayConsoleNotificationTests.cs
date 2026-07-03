using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.NotificationClient;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleNotificationTests
{
    [Fact]
    public async Task List_messages_forwards_route_query_headers_and_bearer()
    {
        var notification = new FakeGatewayNotificationClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(notification, auth);
        var bearerToken = GatewayTestTokens.ValidAccessToken();
        using var request = AuthorizedRequest(
            HttpMethod.Get,
            "/api/console/v1/notifications/messages?recipientRef=user%3Aadmin&status=unread",
            "corr-notify-list",
            "idem-notify-list",
            bearerToken);

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<NotificationMessageListResponse>(response);
        Assert.Single(body.Items);
        Assert.Equal("/api/notifications/v1/messages?recipientRef=user%3Aadmin&status=unread", notification.LastRequest!.RequestUri);
        Assert.Equal("org-001", notification.LastRequest.OrganizationId);
        Assert.Equal("env-dev", notification.LastRequest.EnvironmentId);
        Assert.Equal(bearerToken, notification.LastRequest.BearerToken);
        Assert.Equal("corr-notify-list", notification.LastRequest.CorrelationId);
        Assert.Equal("idem-notify-list", notification.LastRequest.IdempotencyKey);
        Assert.Equal(GatewayPermissions.NotificationMessagesRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task List_tasks_forwards_query_to_notification_service()
    {
        var notification = new FakeGatewayNotificationClient();
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(
            HttpMethod.Get,
            "/api/console/v1/notifications/tasks?recipientRef=user%3Aadmin&status=open");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<NotificationTaskListResponse>(response);
        Assert.Single(body.Items);
        Assert.Equal("/api/notifications/v1/tasks?recipientRef=user%3Aadmin&status=open", notification.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task Submit_intent_forwards_resource_reference_without_file_storage_lookup()
    {
        var notification = new FakeGatewayNotificationClient();
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/intents");
        request.Content = JsonContent.Create(CreateIntent());

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<NotificationIntentResponse>(response);
        Assert.False(body.Duplicate);
        Assert.Equal("/api/notifications/v1/intents", notification.LastRequest!.RequestUri);
        Assert.Equal("file-123", notification.LastIntentRequest!.Resource!.FileId);
        Assert.Equal(GatewayPermissions.NotificationIntentsSubmit, notification.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Mark_single_read_forwards_message_id()
    {
        var notification = new FakeGatewayNotificationClient();
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/messages/msg-001/read");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<MarkNotificationMessageReadResponse>(response);
        Assert.Equal("msg-001", body.MessageId);
        Assert.Equal("/api/notifications/v1/messages/msg-001/read", notification.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task Mark_batch_read_forwards_payload()
    {
        var notification = new FakeGatewayNotificationClient();
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/messages/read-batch");
        request.Content = JsonContent.Create(new MarkNotificationMessagesReadRequest(["msg-001", "msg-002"]));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<IReadOnlyCollection<MarkNotificationMessageReadResponse>>(response);
        Assert.Equal(["msg-001", "msg-002"], body.Select(x => x.MessageId));
        Assert.Equal(["msg-001", "msg-002"], notification.LastBatchReadRequest!.MessageIds);
        Assert.Equal(GatewayPermissions.NotificationMessagesMarkRead, notification.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task List_dead_letters_forwards_query_with_read_permission()
    {
        var notification = new FakeGatewayNotificationClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(notification, auth);
        using var request = AuthorizedRequest(
            HttpMethod.Get,
            "/api/console/v1/notifications/dlq?eventType=ops.OperationTaskFailed&status=Pending");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<NotificationDeadLetterListResponse>(response);
        Assert.Single(body.Items);
        Assert.Equal("/api/notifications/v1/dlq?eventType=ops.OperationTaskFailed&status=Pending", notification.LastRequest!.RequestUri);
        Assert.Equal(GatewayPermissions.NotificationDeadLettersRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Replay_and_ignore_dead_letters_use_manage_permission_and_forward_payloads()
    {
        var notification = new FakeGatewayNotificationClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(notification, auth);
        using var replayRequest = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/dlq/018f8b65-32d1-7111-9cde-0242ac120002/replay");

        var replayResponse = await factory.CreateClient().SendAsync(replayRequest);

        replayResponse.EnsureSuccessStatusCode();
        var replay = await ReadResponseDataAsync<NotificationDeadLetterReplayResponse>(replayResponse);
        Assert.True(replay.Succeeded);
        Assert.Equal("/api/notifications/v1/dlq/018f8b65-32d1-7111-9cde-0242ac120002/replay", notification.LastRequest!.RequestUri);
        Assert.Equal(GatewayPermissions.NotificationDeadLettersManage, auth.LastRequirement!.PermissionCode);

        using var ignoreRequest = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/dlq/018f8b65-32d1-7111-9cde-0242ac120002/ignore");
        ignoreRequest.Content = JsonContent.Create(new IgnoreNotificationDeadLetterRequest("known replacement processed"));

        var ignoreResponse = await factory.CreateClient().SendAsync(ignoreRequest);

        ignoreResponse.EnsureSuccessStatusCode();
        var ignored = await ReadResponseDataAsync<NotificationDeadLetterDetailResponse>(ignoreResponse);
        Assert.Equal("Ignored", ignored.Status);
        Assert.Equal("known replacement processed", notification.LastIgnoreRequest!.Reason);
    }

    [Fact]
    public async Task Replay_dead_letter_batch_forwards_filter_payload()
    {
        var notification = new FakeGatewayNotificationClient();
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/notifications/dlq/replay-batch");
        request.Content = JsonContent.Create(new ReplayNotificationDeadLetterBatchRequest(null, "ops.OperationTaskFailed", null, 50));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<NotificationDeadLetterBatchReplayResponse>(response);
        Assert.Single(body.Items);
        Assert.Equal("ops.OperationTaskFailed", notification.LastReplayBatchRequest!.EventType);
        Assert.Equal(50, notification.LastReplayBatchRequest.Take);
        Assert.Equal(GatewayPermissions.NotificationDeadLettersManage, notification.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Notification_unavailable_returns_response_data_bad_gateway()
    {
        var notification = new FakeGatewayNotificationClient
        {
            ExceptionToThrow = new HttpRequestException("Notification down")
        };
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/notifications/messages");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(502, envelope.Code);
        Assert.Contains("Notification unavailable", envelope.Message);
    }

    [Fact]
    public async Task Notification_validation_error_preserves_downstream_status_and_message()
    {
        var notification = new FakeGatewayNotificationClient
        {
            ExceptionToThrow = new GatewayNotificationException(HttpStatusCode.BadRequest, "message id is invalid")
        };
        await using var factory = CreateFactory(notification);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/notifications/messages");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(400, envelope.Code);
        Assert.Equal("message id is invalid", envelope.Message);
    }

    [Fact]
    public async Task Notification_client_sends_context_headers_and_bearer_to_downstream()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Envelope(new
            {
                items = Array.Empty<object>()
            }))
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://notification.local")
        };
        var notification = new HttpGatewayNotificationClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new GatewayNotificationRequestContext(
            "/api/notifications/v1/messages?recipientRef=user%3Aadmin",
            "org-001",
            "env-dev",
            "access-token",
            "corr-client",
            "idem-client",
            new GatewayPermissionRequirement(GatewayPermissions.NotificationMessagesRead, "org-001", "env-dev", "notification-message", null));

        await notification.ListMessagesAsync(context, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/notifications/v1/messages?recipientRef=user%3Aadmin", request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("internal-test-token", request.Authorization.Parameter);
        Assert.Equal("org-001", request.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", request.Headers["X-Environment-Id"]);
        Assert.Equal("corr-client", request.Headers["X-Correlation-Id"]);
        Assert.Equal("idem-client", request.Headers["Idempotency-Key"]);
    }

    [Fact]
    public async Task Notification_client_preserves_downstream_response_data_error()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                data = (object?)null,
                success = false,
                message = "message ids are required",
                code = 400,
                errorData = Array.Empty<object>()
            })
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://notification.local")
        };
        var notification = new HttpGatewayNotificationClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new GatewayNotificationRequestContext(
            "/api/notifications/v1/messages/read-batch",
            "org-001",
            "env-dev",
            "access-token",
            null,
            null,
            new GatewayPermissionRequirement(GatewayPermissions.NotificationMessagesMarkRead, "org-001", "env-dev", "notification-message", null));

        var exception = await Assert.ThrowsAsync<GatewayNotificationException>(() =>
            notification.MarkMessagesReadAsync(context, new MarkNotificationMessagesReadRequest([]), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("message ids are required", exception.Message);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayNotificationClient notification,
        FakeGatewayAuthorizationClient? auth = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayNotificationClient>();
                services.AddSingleton<IGatewayNotificationClient>(notification);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth ?? FakeGatewayAuthorizationClient.Allowed());
            }));
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string requestUri,
        string? correlationId = null,
        string? idempotencyKey = null,
        string? bearerToken = null)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", bearerToken ?? GatewayTestTokens.ValidAccessToken());
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static SubmitNotificationIntentRequest CreateIntent() =>
        new(
            "ops",
            "ops.OperationTaskFailed",
            "event-001",
            NotificationContractConstants.IntentTypeTask,
            NotificationContractConstants.SeverityCritical,
            "dedupe-001",
            new NotificationResourceRef("operation-task", "op-001", "file-123"),
            "Restart failed",
            "Instance restart failed.",
            ["user:admin"]);

    private sealed class FakeGatewayNotificationClient : IGatewayNotificationClient
    {
        public GatewayNotificationRequestContext? LastRequest { get; private set; }
        public GatewayPermissionRequirement? LastRequirement { get; private set; }
        public SubmitNotificationIntentRequest? LastIntentRequest { get; private set; }
        public MarkNotificationMessagesReadRequest? LastBatchReadRequest { get; private set; }
        public ReplayNotificationDeadLetterBatchRequest? LastReplayBatchRequest { get; private set; }
        public IgnoreNotificationDeadLetterRequest? LastIgnoreRequest { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task<NotificationMessageListResponse> ListMessagesAsync(
            GatewayNotificationRequestContext context,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(new NotificationMessageListResponse([
                new NotificationMessageResponse("msg-001", "intent-001", "user:admin", "unread", "critical", "Restart failed", "Instance restart failed.", null, DateTimeOffset.UtcNow, null)
            ]));
        }

        public Task<NotificationTaskListResponse> ListTasksAsync(
            GatewayNotificationRequestContext context,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(new NotificationTaskListResponse([
                new NotificationTaskResponse("task-001", "msg-001", "user:admin", "review", "open", null, DateTimeOffset.UtcNow)
            ]));
        }

        public Task<NotificationDeadLetterListResponse> ListDeadLettersAsync(
            GatewayNotificationRequestContext context,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(new NotificationDeadLetterListResponse([DeadLetterResponse("Pending")]));
        }

        public Task<NotificationDeadLetterDetailResponse> GetDeadLetterAsync(
            GatewayNotificationRequestContext context,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(DeadLetterDetailResponse("Pending"));
        }

        public Task<NotificationDeadLetterReplayResponse> ReplayDeadLetterAsync(
            GatewayNotificationRequestContext context,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(new NotificationDeadLetterReplayResponse(DeadLetterId, true, "Replayed", null));
        }

        public Task<NotificationDeadLetterBatchReplayResponse> ReplayDeadLettersAsync(
            GatewayNotificationRequestContext context,
            ReplayNotificationDeadLetterBatchRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            LastReplayBatchRequest = request;
            return Task.FromResult(new NotificationDeadLetterBatchReplayResponse([
                new NotificationDeadLetterReplayResponse(DeadLetterId, true, "Replayed", null)
            ]));
        }

        public Task<NotificationDeadLetterDetailResponse> IgnoreDeadLetterAsync(
            GatewayNotificationRequestContext context,
            IgnoreNotificationDeadLetterRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            LastIgnoreRequest = request;
            return Task.FromResult(DeadLetterDetailResponse("Ignored"));
        }

        public Task<NotificationIntentResponse> SubmitIntentAsync(
            GatewayNotificationRequestContext context,
            SubmitNotificationIntentRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            LastIntentRequest = request;
            return Task.FromResult(new NotificationIntentResponse("intent-001", false, []));
        }

        public Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
            GatewayNotificationRequestContext context,
            string messageId,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            return Task.FromResult(new MarkNotificationMessageReadResponse(messageId, "read", DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyCollection<MarkNotificationMessageReadResponse>> MarkMessagesReadAsync(
            GatewayNotificationRequestContext context,
            MarkNotificationMessagesReadRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastRequest = context;
            LastRequirement = context.PermissionRequirement;
            LastBatchReadRequest = request;
            return Task.FromResult<IReadOnlyCollection<MarkNotificationMessageReadResponse>>(
                request.MessageIds.Select(messageId => new MarkNotificationMessageReadResponse(messageId, "read", DateTimeOffset.UtcNow)).ToArray());
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }

        private static readonly Guid DeadLetterId = Guid.Parse("018f8b65-32d1-7111-9cde-0242ac120002");

        private static NotificationDeadLetterResponse DeadLetterResponse(string status) =>
            new(
                DeadLetterId,
                "notification.operation-task-failed",
                "event-001",
                "ops.OperationTaskFailed",
                1,
                "ops",
                "operation-task-failed:task-001",
                "handler-retry-exhausted",
                "Handler failed.",
                status,
                DateTimeOffset.UtcNow,
                status == "Pending" ? null : DateTimeOffset.UtcNow);

        private static NotificationDeadLetterDetailResponse DeadLetterDetailResponse(string status) =>
            new(
                DeadLetterId,
                "notification.operation-task-failed",
                "event-001",
                "ops.OperationTaskFailed",
                1,
                "ops",
                "operation-task-failed:task-001",
                "Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent",
                "{\"eventId\":\"event-001\"}",
                status == "Ignored" ? "ignored" : "handler-retry-exhausted",
                status == "Ignored" ? "known replacement processed" : "Handler failed.",
                status,
                DateTimeOffset.UtcNow,
                status == "Pending" ? null : DateTimeOffset.UtcNow);
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static object Envelope<T>(T data) => new
    {
        data,
        success = true,
        message = string.Empty,
        code = 0,
        errorData = Array.Empty<object>()
    };

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.Ordinal)));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
