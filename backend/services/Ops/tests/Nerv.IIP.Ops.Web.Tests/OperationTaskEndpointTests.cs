using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OperationTaskEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Operation_task_can_be_created_dispatched_and_completed()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", "connector-host-001");
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
        client.DefaultRequestHeaders.Add("X-Organization-Id", "org-001");
        client.DefaultRequestHeaders.Add("X-Environment-Id", "env-dev");

        var createRequest = new CreateOperationTaskRequest(
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "idem-restart-001",
            "local-admin",
            "manual smoke restart",
            "corr-ops-test",
            new Dictionary<string, string>());

        var created = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", createRequest);

        created.EnsureSuccessStatusCode();
        var createdTask = await ReadResponseDataAsync<OperationTaskResponse>(created);
        Assert.NotNull(createdTask);
        Assert.Equal("queued", createdTask.Status);
        Assert.Contains(createdTask.AuditRecords, x => x.Action == "operation.requested");

        var pendingResponse = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = await ReadResponseDataAsync<PendingOperationTasksResponse>(pendingResponse);
        Assert.NotNull(pending);
        var dispatch = Assert.Single(pending.Items);
        Assert.Equal(createdTask.OperationTaskId, dispatch.OperationTaskId);
        Assert.Equal("connector-host-001", dispatch.ConnectorHostId);

        var dispatchedResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}");

        Assert.Equal(HttpStatusCode.OK, dispatchedResponse.StatusCode);
        var dispatched = await ReadResponseDataAsync<OperationTaskResponse>(dispatchedResponse);
        Assert.NotNull(dispatched);
        Assert.Equal("dispatched", dispatched.Status);
        var startedAttempt = Assert.Single(dispatched.Attempts);
        Assert.Equal(dispatch.AttemptId, startedAttempt.AttemptId);
        Assert.Equal("started", startedAttempt.Status);

        var now = DateTimeOffset.UtcNow;
        var result = new OperationResult(
            new ConnectorRequestContext(
                "2026-05-ops-test",
                "test-sdk",
                dispatch.CorrelationId,
                now,
                dispatch.OrganizationId,
                dispatch.EnvironmentId,
                dispatch.ConnectorHostId),
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            dispatch.InstanceKey,
            dispatch.OperationCode,
            now,
            now.AddSeconds(1),
            "succeeded",
            null,
            new Dictionary<string, string>());

        var resultResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", result);
        resultResponse.EnsureSuccessStatusCode();

        var completedResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}");

        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        var completed = await ReadResponseDataAsync<OperationTaskResponse>(completedResponse);
        Assert.NotNull(completed);
        Assert.Equal("completed", completed.Status);
        var completedAttempt = Assert.Single(completed.Attempts);
        Assert.Equal(dispatch.AttemptId, completedAttempt.AttemptId);
        Assert.Equal("completed", completedAttempt.Status);
        Assert.Contains(completed.AuditRecords, x => x.Action == "operation.claimed");
        Assert.Contains(completed.AuditRecords, x => x.Action == "operation.completed");
    }

    [Fact]
    public async Task Missing_operation_task_returns_404_json()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ops/v1/operation-tasks/op-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Pending_and_result_reject_invalid_connector_credentials()
    {
        var client = factory.CreateClient();

        var pendingResponse = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, pendingResponse.StatusCode);
        Assert.Equal("application/json", pendingResponse.Content.Headers.ContentType?.MediaType);

        var resultResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", CreateResult(
            "op-missing",
            "attempt-missing",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "org-001",
            "env-dev",
            "connector-host-001"));

        Assert.Equal(HttpStatusCode.Unauthorized, resultResponse.StatusCode);
        Assert.Equal("application/json", resultResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Development_fake_connector_credential_uses_configured_secret()
    {
        await using var configuredFactory = CreateFactoryWithConnectorCredential("rotated-test-secret");
        var client = configuredFactory.CreateClient();
        AddConnectorHeaders(client, "rotated-test-secret");

        var response = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_fake_connector_credential_rejects_expired_or_revoked_secret()
    {
        await using var expiredFactory = CreateFactoryWithConnectorCredential(
            "expired-test-secret",
            new KeyValuePair<string, string?>("ConnectorHostCredential:ValidToUtc", "2000-01-01T00:00:00Z"));
        var expiredClient = expiredFactory.CreateClient();
        AddConnectorHeaders(expiredClient, "expired-test-secret");

        var expiredResponse = await expiredClient.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

        await using var revokedFactory = CreateFactoryWithConnectorCredential(
            "revoked-test-secret",
            new KeyValuePair<string, string?>("ConnectorHostCredential:Revoked", "true"));
        var revokedClient = revokedFactory.CreateClient();
        AddConnectorHeaders(revokedClient, "revoked-test-secret");

        var revokedResponse = await revokedClient.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    [Fact]
    public async Task Production_does_not_accept_development_fake_connector_credential()
    {
        await using var productionFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Iam:BaseUrl"] = "http://127.0.0.1:1"
                    }));
            });
        var client = productionFactory.CreateClient();
        AddConnectorHeaders(client, "local-connector-secret");

        var response = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_auth_failure_writes_structured_audit_log()
    {
        var loggerProvider = new RecordingLoggerProvider();
        await using var loggingFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider)));
        var client = loggingFactory.CreateClient();

        var response = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var auditLog = Assert.Single(loggerProvider.Entries, x => x.Message.Contains("ConnectorCredentialRejected", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, auditLog.Level);
        Assert.Contains(auditLog.State, x => x.Key == "ConnectorHostId" && x.Value == "connector-host-001");
        Assert.Contains(auditLog.State, x => x.Key == "OrganizationId" && x.Value == "org-001");
        Assert.Contains(auditLog.State, x => x.Key == "EnvironmentId" && x.Value == "env-dev");
        Assert.Contains(auditLog.State, x => x.Key == "Reason" && !string.IsNullOrWhiteSpace(x.Value));
    }

    [Fact]
    public async Task Pending_rejects_mismatched_connector_scope_headers()
    {
        var client = CreateAuthorizedClient("org-other", "env-dev");

        var response = await client.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Invalid_operation_result_context_returns_400_without_mutating_task()
    {
        var client = CreateAuthorizedClient("org-invalid-result", "env-dev");
        var createdTask = await CreateRestartTaskAsync(client, "idem-invalid-result-001", "org-invalid-result");
        var dispatch = await DispatchSingleAsync(client, "org-invalid-result");

        var invalidResult = CreateResult(
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            "wrong-instance-key",
            dispatch.OperationCode,
            dispatch.OrganizationId,
            dispatch.EnvironmentId,
            dispatch.ConnectorHostId);

        var resultResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", invalidResult);

        Assert.Equal(HttpStatusCode.BadRequest, resultResponse.StatusCode);
        Assert.Equal("application/json", resultResponse.Content.Headers.ContentType?.MediaType);

        var detailResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadResponseDataAsync<OperationTaskResponse>(detailResponse);

        Assert.NotNull(detail);
        Assert.Equal("dispatched", detail.Status);
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("started", attempt.Status);
        Assert.DoesNotContain(detail.AuditRecords, x => x.Action == "operation.completed" || x.Action == "operation.failed");
    }

    [Fact]
    public async Task Missing_or_mismatched_operation_result_attempt_returns_400_json()
    {
        var client = CreateAuthorizedClient("org-missing-attempt", "env-dev");
        await CreateRestartTaskAsync(client, "idem-missing-attempt-001", "org-missing-attempt");
        var dispatch = await DispatchSingleAsync(client, "org-missing-attempt");

        var missingAttemptResult = CreateResult(
            dispatch.OperationTaskId,
            "attempt-missing",
            dispatch.InstanceKey,
            dispatch.OperationCode,
            dispatch.OrganizationId,
            dispatch.EnvironmentId,
            dispatch.ConnectorHostId);

        var response = await client.PostAsJsonAsync("/api/ops/v1/operation-results", missingAttemptResult);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_operation_task_is_idempotent_by_idempotency_key()
    {
        var client = CreateAuthorizedClient();
        var request = CreateRestartRequest("idem-repeat-001", "org-idempotent");

        var firstResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", request);
        var secondResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", request);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await ReadResponseDataAsync<OperationTaskResponse>(firstResponse);
        var second = await ReadResponseDataAsync<OperationTaskResponse>(secondResponse);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.OperationTaskId, second.OperationTaskId);
        Assert.Equal("queued", second.Status);
        Assert.Single(second.AuditRecords, x => x.Action == "operation.requested");
    }

    [Fact]
    public async Task Unsupported_operation_code_returns_400_json()
    {
        var client = CreateAuthorizedClient("org-unsupported", "env-dev");
        var request = CreateRestartRequest("idem-unsupported-001", "org-unsupported") with
        {
            OperationCode = "lifecycle.unsupported"
        };

        var response = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Replayed_terminal_operation_result_returns_400_without_mutating_task()
    {
        var client = CreateAuthorizedClient("org-replay-result", "env-dev");
        var createdTask = await CreateRestartTaskAsync(client, "idem-replay-result-001", "org-replay-result");
        var dispatch = await DispatchSingleAsync(client, "org-replay-result");
        var successResult = CreateResult(
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            dispatch.InstanceKey,
            dispatch.OperationCode,
            dispatch.OrganizationId,
            dispatch.EnvironmentId,
            dispatch.ConnectorHostId);

        var firstResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", successResult);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var replayedFailedResult = successResult with
        {
            ExecutionStatus = "failed",
            Failure = new FailureReason(
                "replayed-failure",
                "replayed result should be rejected",
                "validation",
                false,
                new Dictionary<string, string>())
        };

        var secondResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", replayedFailedResult);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        Assert.Equal("application/json", secondResponse.Content.Headers.ContentType?.MediaType);

        var detailResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadResponseDataAsync<OperationTaskResponse>(detailResponse);

        Assert.NotNull(detail);
        Assert.Equal("completed", detail.Status);
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal("completed", attempt.Status);
        Assert.Single(detail.AuditRecords, x => x.Action == "operation.completed");
        Assert.DoesNotContain(detail.AuditRecords, x => x.Action == "operation.failed");
    }

    [Fact]
    public async Task Idempotency_key_is_scoped_by_organization_and_environment()
    {
        var client = CreateAuthorizedClient();

        var orgOneFirst = await PostCreateAsync(client, CreateRestartRequest("idem-shared-scope-001", "org-scope-a", "env-dev"));
        var orgOneSecond = await PostCreateAsync(client, CreateRestartRequest("idem-shared-scope-001", "org-scope-a", "env-dev"));
        var orgTwo = await PostCreateAsync(client, CreateRestartRequest("idem-shared-scope-001", "org-scope-b", "env-dev"));
        var envTwo = await PostCreateAsync(client, CreateRestartRequest("idem-shared-scope-001", "org-scope-a", "env-stage"));

        Assert.Equal(orgOneFirst.OperationTaskId, orgOneSecond.OperationTaskId);
        Assert.NotEqual(orgOneFirst.OperationTaskId, orgTwo.OperationTaskId);
        Assert.NotEqual(orgOneFirst.OperationTaskId, envTwo.OperationTaskId);
        Assert.Equal("queued", orgOneSecond.Status);
        Assert.Equal("queued", orgTwo.Status);
        Assert.Equal("queued", envTwo.Status);
    }

    [Fact]
    public async Task Claim_operation_tasks_prevents_concurrent_duplicate_leases()
    {
        var organizationId = "org-claim-concurrent";
        var client = CreateAuthorizedClient(organizationId, "env-dev");
        var createdTask = await CreateRestartTaskAsync(client, "idem-claim-concurrent-001", organizationId);

        var claimRequests = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync(
                "/api/ops/v1/operation-tasks/claims",
                new ClaimOperationTasksRequest(organizationId, "env-dev", "connector-host-001", 1)))
            .ToArray();

        var responses = await Task.WhenAll(claimRequests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var claims = await Task.WhenAll(responses.Select(x => ReadResponseDataAsync<PendingOperationTasksResponse>(x)));
        var claimedItems = claims
            .SelectMany(x => x?.Items ?? [])
            .ToList();

        var claim = Assert.Single(claimedItems);
        Assert.Equal(createdTask.OperationTaskId, claim.OperationTaskId);
        Assert.Equal("connector-host-001", claim.ConnectorHostId);
        Assert.False(string.IsNullOrWhiteSpace(claim.LeaseId));
        Assert.Equal(1, claim.AttemptNo);
        Assert.True(claim.MaxAttempts >= claim.AttemptNo);
        Assert.True(claim.LeasedUntilUtc > claim.LeasedAtUtc);

        var detailResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadResponseDataAsync<OperationTaskResponse>(detailResponse);

        Assert.NotNull(detail);
        Assert.Equal("dispatched", detail.Status);
        var attempt = Assert.Single(detail.Attempts);
        Assert.Equal(claim.AttemptId, attempt.AttemptId);
        Assert.Equal(claim.LeaseId, attempt.LeaseId);
        Assert.Null(attempt.AbandonReason);
    }

    [Fact]
    public async Task Heartbeat_and_abandon_update_active_lease_idempotently()
    {
        var organizationId = "org-lease-updates";
        var client = CreateAuthorizedClient(organizationId, "env-dev");
        var createdTask = await CreateRestartTaskAsync(client, "idem-lease-updates-001", organizationId);
        var claim = await ClaimSingleAsync(client, organizationId);

        var heartbeatResponse = await client.PostAsJsonAsync(
            $"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}/lease/heartbeat",
            new HeartbeatOperationTaskLeaseRequest(
                organizationId,
                "env-dev",
                "connector-host-001",
                claim.LeaseId,
                600));

        Assert.Equal(HttpStatusCode.OK, heartbeatResponse.StatusCode);
        var heartbeated = await ReadResponseDataAsync<OperationTaskResponse>(heartbeatResponse);
        Assert.NotNull(heartbeated);
        var heartbeatedAttempt = Assert.Single(heartbeated.Attempts);
        Assert.Equal(claim.LeaseId, heartbeatedAttempt.LeaseId);
        Assert.True(heartbeatedAttempt.LeasedUntilUtc > claim.LeasedUntilUtc);

        var abandonResponse = await client.PostAsJsonAsync(
            $"/api/ops/v1/operation-tasks/{createdTask.OperationTaskId}/lease/abandon",
            new AbandonOperationTaskLeaseRequest(
                organizationId,
                "env-dev",
                "connector-host-001",
                claim.LeaseId,
                "connector-shutdown"));

        Assert.Equal(HttpStatusCode.OK, abandonResponse.StatusCode);
        var abandoned = await ReadResponseDataAsync<OperationTaskResponse>(abandonResponse);
        Assert.NotNull(abandoned);
        Assert.Equal("queued", abandoned.Status);
        var abandonedAttempt = Assert.Single(abandoned.Attempts);
        Assert.Equal("abandoned", abandonedAttempt.Status);
        Assert.Equal("connector-shutdown", abandonedAttempt.AbandonReason);

        var nextClaim = await ClaimSingleAsync(client, organizationId);

        Assert.Equal(createdTask.OperationTaskId, nextClaim.OperationTaskId);
        Assert.NotEqual(claim.LeaseId, nextClaim.LeaseId);
        Assert.Equal(2, nextClaim.AttemptNo);
    }

    private HttpClient CreateAuthorizedClient(string organizationId = "org-001", string environmentId = "env-dev")
    {
        var client = factory.CreateClient();
        AddConnectorHeaders(client, "local-connector-secret", organizationId, environmentId);
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactoryWithConnectorCredential(
        string secret,
        params KeyValuePair<string, string?>[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectorHostCredential:Secret"] = secret
        };
        foreach (var item in overrides)
        {
            settings[item.Key] = item.Value;
        }

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings)));
    }

    private static void AddConnectorHeaders(
        HttpClient client,
        string secret,
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string connectorHostId = "connector-host-001")
    {
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", connectorHostId);
        client.DefaultRequestHeaders.Add("X-Connector-Secret", secret);
        client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", environmentId);
    }

    private static CreateOperationTaskRequest CreateRestartRequest(string idempotencyKey, string organizationId = "org-001", string environmentId = "env-dev")
    {
        return new CreateOperationTaskRequest(
            organizationId,
            environmentId,
            "docker-container-local-demo-001",
            "lifecycle.restart",
            idempotencyKey,
            "local-admin",
            "manual smoke restart",
            $"corr-{idempotencyKey}",
            new Dictionary<string, string>());
    }

    private static OperationResult CreateResult(
        string operationTaskId,
        string attemptId,
        string instanceKey,
        string operationCode,
        string organizationId,
        string environmentId,
        string connectorHostId)
    {
        var now = DateTimeOffset.UtcNow;
        return new OperationResult(
            new ConnectorRequestContext(
                "2026-05-ops-test",
                "test-sdk",
                $"corr-result-{attemptId}",
                now,
                organizationId,
                environmentId,
                connectorHostId),
            operationTaskId,
            attemptId,
            instanceKey,
            operationCode,
            now,
            now.AddSeconds(1),
            "succeeded",
            null,
            new Dictionary<string, string>());
    }

    private async Task<OperationTaskResponse> CreateRestartTaskAsync(HttpClient client, string idempotencyKey, string organizationId)
    {
        var response = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", CreateRestartRequest(idempotencyKey, organizationId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await ReadResponseDataAsync<OperationTaskResponse>(response);
        Assert.NotNull(task);
        return task;
    }

    private static async Task<OperationTaskResponse> PostCreateAsync(HttpClient client, CreateOperationTaskRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await ReadResponseDataAsync<OperationTaskResponse>(response);
        Assert.NotNull(task);
        return task;
    }

    private static async Task<OperationTaskDispatchItem> DispatchSingleAsync(HttpClient client, string organizationId)
    {
        var response = await client.GetAsync(
            $"/api/ops/v1/operation-tasks/pending?organizationId={organizationId}&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pending = await ReadResponseDataAsync<PendingOperationTasksResponse>(response);
        Assert.NotNull(pending);
        return Assert.Single(pending.Items);
    }

    private static async Task<OperationTaskDispatchItem> ClaimSingleAsync(HttpClient client, string organizationId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-tasks/claims",
            new ClaimOperationTasksRequest(organizationId, "env-dev", "connector-host-001", 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pending = await ReadResponseDataAsync<PendingOperationTasksResponse>(response);
        Assert.NotNull(pending);
        return Assert.Single(pending.Items);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> entries = [];
        public IReadOnlyList<LogEntry> Entries => entries;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(entries);
        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>
                ?? [];
            entries.Add(new LogEntry(
                logLevel,
                eventId,
                formatter(state, exception),
                structuredState
                    .Where(x => x.Key != "{OriginalFormat}")
                    .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString()))
                    .ToArray()));
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, string?>> State);
}
