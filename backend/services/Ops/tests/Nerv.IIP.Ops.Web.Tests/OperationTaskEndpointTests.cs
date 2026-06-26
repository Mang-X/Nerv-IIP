using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OperationTaskEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ops_api_endpoints_require_internal_service_authorization()
    {
        var client = factory.CreateClient();
        var createRequest = CreateRestartRequest("idem-unauthorized-001");
        var resultRequest = CreateResult(
            "op-missing",
            "attempt-missing",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "org-001",
            "env-dev",
            "connector-host-001");

        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", createRequest),
            await client.GetAsync("/api/ops/v1/operation-tasks?organizationId=org-001&environmentId=env-dev"),
            await client.GetAsync("/api/ops/v1/operation-tasks/op-missing"),
            await client.GetAsync("/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10"),
            await client.PostAsJsonAsync("/api/ops/v1/operation-tasks/claims", new ClaimOperationTasksRequest("org-001", "env-dev", "connector-host-001", 1)),
            await client.PostAsJsonAsync("/api/ops/v1/operation-tasks/op-missing/lease/abandon", new AbandonOperationTaskLeaseRequest("org-001", "env-dev", "connector-host-001", "lease-001", "test")),
            await client.PostAsJsonAsync("/api/ops/v1/operation-tasks/op-missing/lease/heartbeat", new HeartbeatOperationTaskLeaseRequest("org-001", "env-dev", "connector-host-001", "lease-001")),
            await client.PostAsJsonAsync("/api/ops/v1/operation-results", resultRequest),
            await client.GetAsync("/api/ops/v1/audit-records?organizationId=org-001&environmentId=env-dev"),
            await client.PostAsJsonAsync("/api/ops/v1/audit-intents", new SubmitAuditIntentRequest("org-001", "env-dev", "op-missing", "manual.reviewed", "user:auditor", "corr-audit-unauthorized")),
            await client.PostAsJsonAsync("/api/ops/v1/operation-templates", new CreateOperationTemplateRequest("backup.unauthorized", "Backup", "{}", "medium", 3, 300, false)),
            await client.GetAsync("/api/ops/v1/operation-templates"),
            await client.GetAsync("/api/ops/v1/operation-templates/lifecycle.restart")
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task Operation_task_can_be_created_dispatched_and_completed()
    {
        var client = CreateInternalServiceClient(factory);
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
    public async Task Approval_required_task_is_not_claimable_until_approved()
    {
        var client = CreateAuthorizedClient("org-approval", "env-dev");

        await CreateTemplateAsync(client, new CreateOperationTemplateRequest(
            "lifecycle.high-risk-restart",
            "High-risk restart",
            "{}",
            "high",
            3,
            300,
            RequiresApproval: true));

        var createRequest = CreateRestartRequest("idem-approval-endpoint-001", "org-approval") with
        {
            OperationCode = "lifecycle.high-risk-restart"
        };
        var createResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadResponseDataAsync<OperationTaskResponse>(createResponse);

        Assert.NotNull(created);
        Assert.Equal("approval-pending", created.Status);
        Assert.NotNull(created.Approval);
        Assert.Equal("pending", created.Approval.Status);
        Assert.Contains(created.AuditRecords, x => x.Action == "operation.approval-requested");

        var pendingBeforeApproval = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-tasks/claims",
            new ClaimOperationTasksRequest("org-approval", "env-dev", "connector-host-001", 10));
        Assert.Equal(HttpStatusCode.OK, pendingBeforeApproval.StatusCode);
        var emptyClaims = await ReadResponseDataAsync<PendingOperationTasksResponse>(pendingBeforeApproval);
        Assert.NotNull(emptyClaims);
        Assert.Empty(emptyClaims.Items);

        var approvalResponse = await client.PostAsJsonAsync(
            $"/api/ops/v1/operation-tasks/{created.OperationTaskId}/approval/approve",
            new DecideOperationApprovalRequest("org-approval", "env-dev", "ops-approver", "approved", "corr-approval"));
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        var approved = await ReadResponseDataAsync<OperationTaskResponse>(approvalResponse);
        Assert.NotNull(approved);
        Assert.Equal("queued", approved.Status);
        Assert.NotNull(approved.Approval);
        Assert.Equal("approved", approved.Approval.Status);

        var claimAfterApproval = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-tasks/claims",
            new ClaimOperationTasksRequest("org-approval", "env-dev", "connector-host-001", 10));
        Assert.Equal(HttpStatusCode.OK, claimAfterApproval.StatusCode);
        var claims = await ReadResponseDataAsync<PendingOperationTasksResponse>(claimAfterApproval);
        Assert.NotNull(claims);
        Assert.Equal(created.OperationTaskId, Assert.Single(claims.Items).OperationTaskId);
    }

    [Fact]
    public async Task Approval_actor_is_derived_from_server_context_and_body_actor_is_ignored()
    {
        var client = CreateInternalServiceClient(factory);
        client.DefaultRequestHeaders.Add("X-Actor", "user:trusted-approver");

        await CreateTemplateAsync(client, new CreateOperationTemplateRequest(
            "lifecycle.high-risk-restart.actor-derived",
            "High-risk restart",
            "{}",
            "high",
            DefaultMaxAttempts: 3,
            DefaultLeaseDurationSeconds: 300,
            RequiresApproval: true));
        var created = await PostCreateAsync(
            client,
            CreateRestartRequest("idem-approval-actor-derived-001", "org-approval-actor", "env-dev") with
            {
                OperationCode = "lifecycle.high-risk-restart.actor-derived"
            });

        var approvalResponse = await client.PostAsJsonAsync(
            $"/api/ops/v1/operation-tasks/{created.OperationTaskId}/approval/approve",
            new DecideOperationApprovalRequest(
                "org-approval-actor",
                "env-dev",
                "user:spoofed-body-actor",
                "approved from server context",
                "corr-approval-actor-derived"));

        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        var approved = await ReadResponseDataAsync<OperationTaskResponse>(approvalResponse);
        Assert.NotNull(approved);
        Assert.Equal("user:trusted-approver", approved.Approval?.DecidedBy);
        Assert.Contains(approved.AuditRecords, x =>
            x.Action == "operation.approved"
            && x.Actor == "user:trusted-approver");
        Assert.DoesNotContain(approved.AuditRecords, x => x.Actor == "user:spoofed-body-actor");
    }

    [Fact]
    public async Task Approval_endpoint_rejects_requester_self_approval_from_server_context()
    {
        var client = CreateInternalServiceClient(factory);
        client.DefaultRequestHeaders.Add("X-Actor", "local-admin");

        await CreateTemplateAsync(client, new CreateOperationTemplateRequest(
            "lifecycle.high-risk-restart.self",
            "High-risk restart",
            "{}",
            "high",
            DefaultMaxAttempts: 3,
            DefaultLeaseDurationSeconds: 300,
            RequiresApproval: true));
        var created = await PostCreateAsync(
            client,
            CreateRestartRequest("idem-approval-self-001", "org-approval-self", "env-dev") with
            {
                OperationCode = "lifecycle.high-risk-restart.self"
            });

        var approvalResponse = await client.PostAsJsonAsync(
            $"/api/ops/v1/operation-tasks/{created.OperationTaskId}/approval/approve",
            new DecideOperationApprovalRequest(
                "org-approval-self",
                "env-dev",
                "user:different-body-actor",
                "self approval attempt",
                "corr-approval-self"));

        Assert.Equal(HttpStatusCode.BadRequest, approvalResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/ops/v1/operation-tasks/{created.OperationTaskId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadResponseDataAsync<OperationTaskResponse>(detailResponse);
        Assert.Equal("approval-pending", detail.Status);
        Assert.DoesNotContain(detail.AuditRecords, x => x.Action == "operation.approved");
        Assert.DoesNotContain(detail.AuditRecords, x => x.Actor == "user:different-body-actor");
    }

    [Fact]
    public async Task Missing_operation_task_returns_404_json()
    {
        var client = CreateInternalServiceClient(factory);

        var response = await client.GetAsync("/api/ops/v1/operation-tasks/op-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Pending_and_result_reject_invalid_connector_credentials()
    {
        var client = CreateInternalServiceClient(factory);

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
        var client = CreateInternalServiceClient(configuredFactory);
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
        var expiredClient = CreateInternalServiceClient(expiredFactory);
        AddConnectorHeaders(expiredClient, "expired-test-secret");

        var expiredResponse = await expiredClient.GetAsync(
            "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

        await using var revokedFactory = CreateFactoryWithConnectorCredential(
            "revoked-test-secret",
            new KeyValuePair<string, string?>("ConnectorHostCredential:Revoked", "true"));
        var revokedClient = CreateInternalServiceClient(revokedFactory);
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
                builder.UseSetting("Iam:BaseUrl", "http://127.0.0.1:1");
                builder.UseSetting("InternalService:BearerToken", "production-internal-token");
            });
        var client = CreateInternalServiceClient(productionFactory, "production-internal-token");
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
        var client = CreateInternalServiceClient(loggingFactory);

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

    [Fact]
    public async Task List_operation_tasks_returns_paged_tasks_for_scope()
    {
        await using var listFactory = CreateEfInMemoryFactory("ops-list-tasks");
        var client = CreateInternalServiceClient(listFactory);
        AddConnectorHeaders(client, "local-connector-secret", "org-list", "env-dev");

        var first = await PostCreateAsync(client, CreateRestartRequest("idem-list-001", "org-list", "env-dev"));
        var second = await PostCreateAsync(client, CreateRestartRequest("idem-list-002", "org-list", "env-dev"));
        await PostCreateAsync(client, CreateRestartRequest("idem-list-other-env", "org-list", "env-stage"));

        var response = await client.GetAsync(
            "/api/ops/v1/operation-tasks?organizationId=org-list&environmentId=env-dev&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadResponseDataAsync<PagedOperationTaskListResponse>(response);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, x => x.OperationTaskId == first.OperationTaskId);
        Assert.Contains(page.Items, x => x.OperationTaskId == second.OperationTaskId);
        Assert.All(page.Items, x =>
        {
            Assert.Equal("org-list", x.OrganizationId);
            Assert.Equal("env-dev", x.EnvironmentId);
            Assert.Equal("queued", x.Status);
        });
    }

    [Fact]
    public async Task List_audit_records_returns_task_audit_records_for_scope()
    {
        await using var auditFactory = CreateEfInMemoryFactory("ops-list-audit");
        var client = CreateInternalServiceClient(auditFactory);
        AddConnectorHeaders(client, "local-connector-secret", "org-audit", "env-dev");

        var created = await PostCreateAsync(client, CreateRestartRequest("idem-audit-001", "org-audit", "env-dev"));
        await PostCreateAsync(client, CreateRestartRequest("idem-audit-other-env", "org-audit", "env-stage"));

        var response = await client.GetAsync(
            "/api/ops/v1/audit-records?organizationId=org-audit&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = await ReadResponseDataAsync<AuditRecordListResponse>(response);
        Assert.Contains(records.Items, x =>
            x.OperationTaskId == created.OperationTaskId
            && x.Action == "operation.requested"
            && x.Actor == "local-admin");
        Assert.All(records.Items, x => Assert.Equal(created.OperationTaskId, x.OperationTaskId));
    }

    [Fact]
    public async Task Audit_integrity_endpoint_accepts_contiguous_chain()
    {
        await using var auditFactory = CreateEfInMemoryFactory("ops-audit-chain-valid");
        var client = CreateInternalServiceClient(auditFactory);

        await PostCreateAsync(client, CreateRestartRequest("idem-audit-chain-001", "org-audit-chain", "env-dev"));
        await PostCreateAsync(client, CreateRestartRequest("idem-audit-chain-002", "org-audit-chain", "env-dev"));

        var auditResponse = await client.GetAsync(
            "/api/ops/v1/audit-records?organizationId=org-audit-chain&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var records = await ReadResponseDataAsync<AuditRecordListResponse>(auditResponse);
        Assert.Equal([2L, 1L], records.Items.Select(x => x.SequenceNo).ToArray());
        Assert.Equal(string.Empty, records.Items.Single(x => x.SequenceNo == 1).PreviousIntegrityHash);
        Assert.Equal(
            records.Items.Single(x => x.SequenceNo == 1).IntegrityHash,
            records.Items.Single(x => x.SequenceNo == 2).PreviousIntegrityHash);

        var validationResponse = await client.GetAsync(
            "/api/ops/v1/audit-records/integrity?organizationId=org-audit-chain&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await ReadResponseDataAsync<AuditIntegrityValidationResponse>(validationResponse);
        Assert.True(validation.IsValid);
        Assert.Equal(2, validation.CheckedRecords);
    }

    [Theory]
    [InlineData("tamper", "hash-mismatch")]
    [InlineData("delete", "sequence-gap")]
    [InlineData("reorder", "previous-hash-mismatch")]
    public async Task Audit_integrity_endpoint_detects_tamper_delete_and_reorder(string mutation, string expectedFailureCode)
    {
        var databaseName = $"ops-audit-chain-{mutation}";
        await using var auditFactory = CreateEfInMemoryFactory(databaseName);
        var client = CreateInternalServiceClient(auditFactory);

        await PostCreateAsync(client, CreateRestartRequest($"idem-audit-chain-{mutation}-001", "org-audit-chain-break", "env-dev"));
        await PostCreateAsync(client, CreateRestartRequest($"idem-audit-chain-{mutation}-002", "org-audit-chain-break", "env-dev"));
        await MutateAuditChainAsync(auditFactory, mutation);

        var validationResponse = await client.GetAsync(
            "/api/ops/v1/audit-records/integrity?organizationId=org-audit-chain-break&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await ReadResponseDataAsync<AuditIntegrityValidationResponse>(validationResponse);
        Assert.False(validation.IsValid);
        Assert.Equal(expectedFailureCode, validation.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(validation.FirstInvalidAuditRecordId));
    }

    [Fact]
    public async Task Submit_audit_intent_creates_audit_record_for_task_scope()
    {
        await using var auditFactory = CreateEfInMemoryFactory("ops-submit-audit-intent");
        var client = CreateInternalServiceClient(auditFactory);
        var created = await PostCreateAsync(client, CreateRestartRequest("idem-audit-intent-001", "org-audit-intent", "env-dev"));

        var response = await client.PostAsJsonAsync(
            "/api/ops/v1/audit-intents",
            new SubmitAuditIntentRequest(
                "org-audit-intent",
                "env-dev",
                created.OperationTaskId,
                "manual.reviewed",
                "user:auditor",
                "corr-audit-intent-001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var intent = await ReadResponseDataAsync<AuditIntentResponse>(response);
        Assert.Equal(created.OperationTaskId, intent.OperationTaskId);
        Assert.Equal("manual.reviewed", intent.Action);
        Assert.Equal("user:auditor", intent.Actor);

        var auditResponse = await client.GetAsync(
            "/api/ops/v1/audit-records?organizationId=org-audit-intent&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var records = await ReadResponseDataAsync<AuditRecordListResponse>(auditResponse);
        Assert.Contains(records.Items, x =>
            x.AuditRecordId == intent.AuditRecordId
            && x.Action == "manual.reviewed"
            && x.CorrelationId == "corr-audit-intent-001");
    }

    [Fact]
    public async Task Submit_audit_intent_rejects_operation_task_outside_scope()
    {
        await using var auditFactory = CreateEfInMemoryFactory("ops-submit-audit-intent-scope");
        var client = CreateInternalServiceClient(auditFactory);
        var created = await PostCreateAsync(client, CreateRestartRequest("idem-audit-intent-scope-001", "org-audit-intent", "env-dev"));

        var response = await client.PostAsJsonAsync(
            "/api/ops/v1/audit-intents",
            new SubmitAuditIntentRequest(
                "org-other",
                "env-dev",
                created.OperationTaskId,
                "manual.reviewed",
                "user:auditor",
                "corr-audit-intent-scope-001"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_audit_intent_rejects_missing_or_oversized_audit_fields()
    {
        await using var auditFactory = CreateEfInMemoryFactory("ops-submit-audit-intent-validation");
        var client = CreateInternalServiceClient(auditFactory);
        var created = await PostCreateAsync(client, CreateRestartRequest("idem-audit-intent-validation-001", "org-audit-intent", "env-dev"));

        var missingActor = await client.PostAsJsonAsync(
            "/api/ops/v1/audit-intents",
            new SubmitAuditIntentRequest(
                "org-audit-intent",
                "env-dev",
                created.OperationTaskId,
                "manual.reviewed",
                "",
                "corr-audit-intent-validation-001"));
        var oversizedAction = await client.PostAsJsonAsync(
            "/api/ops/v1/audit-intents",
            new SubmitAuditIntentRequest(
                "org-audit-intent",
                "env-dev",
                created.OperationTaskId,
                new string('a', 129),
                "user:auditor",
                "corr-audit-intent-validation-002"));

        Assert.Equal(HttpStatusCode.BadRequest, missingActor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedAction.StatusCode);
    }

    [Fact]
    public async Task Operation_template_can_be_created_listed_and_read()
    {
        var client = CreateInternalServiceClient(factory);
        var request = new CreateOperationTemplateRequest(
            "backup.snapshot",
            "Backup snapshot",
            """{"type":"object"}""",
            "medium",
            4,
            900,
            RequiresApproval: false);

        var createResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-templates", request);

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadResponseDataAsync<OperationTemplateResponse>(createResponse);
        Assert.Equal("backup.snapshot", created.OperationCode);
        Assert.Equal(4, created.DefaultMaxAttempts);
        Assert.Equal(900, created.DefaultLeaseDurationSeconds);
        Assert.True(created.Enabled);

        var listResponse = await client.GetAsync("/api/ops/v1/operation-templates");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadResponseDataAsync<OperationTemplateListResponse>(listResponse);
        Assert.Contains(list.Items, x => x.OperationCode == "lifecycle.restart");
        Assert.Contains(list.Items, x => x.OperationCode == "backup.snapshot");

        var getResponse = await client.GetAsync("/api/ops/v1/operation-templates/backup.snapshot");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await ReadResponseDataAsync<OperationTemplateResponse>(getResponse);
        Assert.Equal(created.OperationTemplateId, fetched.OperationTemplateId);

        var taskResponse = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-tasks",
            CreateRestartRequest("idem-template-task-001") with { OperationCode = "backup.snapshot" });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);
        var task = await ReadResponseDataAsync<OperationTaskResponse>(taskResponse);
        Assert.Equal("backup.snapshot", task.OperationCode);
    }

    [Fact]
    public async Task Operation_template_get_returns_template_specific_not_found_message()
    {
        var client = CreateInternalServiceClient(factory);

        var response = await client.GetAsync("/api/ops/v1/operation-templates/missing.template");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Operation template was not found", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_template_create_rejects_unknown_risk_level()
    {
        var client = CreateInternalServiceClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-templates",
            new CreateOperationTemplateRequest("backup.snapshot.risk", "Backup snapshot", "{}", "experimental", 3, 300, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("risk level", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operation_template_defaults_are_used_when_claiming_task()
    {
        var client = CreateInternalServiceClient(factory);
        AddConnectorHeaders(client, "local-connector-secret", "org-template-claim", "env-dev");
        await client.PostAsJsonAsync(
            "/api/ops/v1/operation-templates",
            new CreateOperationTemplateRequest("backup.snapshot.claim", "Backup snapshot", "{}", "medium", 4, 900, false));
        var created = await PostCreateAsync(
            client,
            CreateRestartRequest("idem-template-claim-001", "org-template-claim", "env-dev") with { OperationCode = "backup.snapshot.claim" });

        var claimResponse = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-tasks/claims",
            new ClaimOperationTasksRequest("org-template-claim", "env-dev", "connector-host-001", 1, LeaseDurationSeconds: 30, MaxAttempts: 1));

        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var pending = await ReadResponseDataAsync<PendingOperationTasksResponse>(claimResponse);
        var claim = Assert.Single(pending.Items);
        Assert.Equal(created.OperationTaskId, claim.OperationTaskId);
        Assert.Equal(4, claim.MaxAttempts);
        Assert.Equal(900, claim.LeaseDurationSeconds);
        Assert.Equal(TimeSpan.FromSeconds(900), claim.LeasedUntilUtc - claim.LeasedAtUtc);
    }

    [Fact]
    public async Task Operation_template_duplicate_check_uses_normalized_operation_code()
    {
        var client = CreateInternalServiceClient(factory);
        await client.PostAsJsonAsync(
            "/api/ops/v1/operation-templates",
            new CreateOperationTemplateRequest("backup.snapshot.normalized", "Backup", "{}", "low", 3, 300, false));

        var duplicate = await client.PostAsJsonAsync(
            "/api/ops/v1/operation-templates",
            new CreateOperationTemplateRequest(" backup.snapshot.normalized ", "Backup", "{}", "low", 3, 300, false));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task InMemory_list_endpoints_return_tasks_and_audit_records()
    {
        var client = CreateInternalServiceClient(factory);
        var created = await PostCreateAsync(client, CreateRestartRequest("idem-inmemory-list-001", "org-memory", "env-dev"));

        var listResponse = await client.GetAsync(
            "/api/ops/v1/operation-tasks?organizationId=org-memory&environmentId=env-dev&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadResponseDataAsync<PagedOperationTaskListResponse>(listResponse);
        Assert.Contains(list.Items, x => x.OperationTaskId == created.OperationTaskId);

        var auditResponse = await client.GetAsync(
            "/api/ops/v1/audit-records?organizationId=org-memory&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var audit = await ReadResponseDataAsync<AuditRecordListResponse>(auditResponse);
        Assert.Contains(audit.Items, x => x.OperationTaskId == created.OperationTaskId && x.Action == "operation.requested");
    }

    private HttpClient CreateAuthorizedClient(string organizationId = "org-001", string environmentId = "env-dev")
    {
        var client = CreateInternalServiceClient(factory);
        AddConnectorHeaders(client, "local-connector-secret", organizationId, environmentId);
        return client;
    }

    private static HttpClient CreateInternalServiceClient(
        WebApplicationFactory<Program> testFactory,
        string token = InternalServiceAuthentication.DefaultDevelopmentBearerToken)
    {
        var client = testFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    private static WebApplicationFactory<Program> CreateEfInMemoryFactory(string databaseName)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "PostgreSQL",
                        ["ConnectionStrings:OpsDb"] = "Host=localhost;Database=ops_query_tests;Username=nerv;Password=nerv"
                    }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                    services.RemoveAll<IOperationTaskApplicationService>();
                    services.AddScoped<IOperationTaskRepository, OperationTaskRepository>();
                    services.AddScoped<IOperationTemplateRepository, OperationTemplateRepository>();
                    services.AddScoped<EfOperationTaskApplicationService>();
                    services.AddScoped<IOperationTaskApplicationService, SavingOperationTaskApplicationService>();
                });
            });
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

    private static async Task<OperationTemplateResponse> CreateTemplateAsync(
        HttpClient client,
        CreateOperationTemplateRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/ops/v1/operation-templates", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var template = await ReadResponseDataAsync<OperationTemplateResponse>(response);
        return Assert.IsType<OperationTemplateResponse>(template);
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

    private static async Task MutateAuditChainAsync(WebApplicationFactory<Program> testFactory, string mutation)
    {
        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var records = await db.AuditRecords
            .OrderBy(x => x.SequenceNo)
            .ToListAsync();
        Assert.True(records.Count >= 2);

        switch (mutation)
        {
            case "tamper":
                SetPrivateProperty(records[0], nameof(AuditRecord.Action), "operation.tampered");
                break;
            case "delete":
                db.AuditRecords.Remove(records[0]);
                break;
            case "reorder":
                SetPrivateProperty(records[0], nameof(AuditRecord.SequenceNo), records[1].SequenceNo);
                SetPrivateProperty(records[1], nameof(AuditRecord.SequenceNo), 1L);
                break;
            default:
                throw new InvalidOperationException($"Unknown audit chain mutation: {mutation}");
        }

        await db.SaveChangesAsync();
    }

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        target.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
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

    private sealed class SavingOperationTaskApplicationService(
        EfOperationTaskApplicationService inner,
        ApplicationDbContext db) : IOperationTaskApplicationService
    {
        public async Task<OperationTaskResponse> CreateAsync(CreateOperationTaskRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.CreateAsync(request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public Task<OperationTaskResponse> GetAsync(string operationTaskId, CancellationToken cancellationToken)
        {
            return inner.GetAsync(operationTaskId, cancellationToken);
        }

        public async Task<PendingOperationTasksResponse> ClaimPendingAsync(ClaimOperationTasksRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.ClaimPendingAsync(request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<OperationTaskResponse> AbandonLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.AbandonLeaseAsync(operationTaskId, request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<OperationTaskResponse> HeartbeatLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.HeartbeatLeaseAsync(operationTaskId, request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<OperationTaskResponse> RecordResultAsync(OperationResult result, CancellationToken cancellationToken)
        {
            var response = await inner.RecordResultAsync(result, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<AuditIntentResponse> SubmitAuditIntentAsync(SubmitAuditIntentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.SubmitAuditIntentAsync(request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<OperationTaskResponse> ApproveAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.ApproveAsync(operationTaskId, request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        public async Task<OperationTaskResponse> RejectAsync(string operationTaskId, DecideOperationApprovalRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var response = await inner.RejectAsync(operationTaskId, request, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }
    }

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
