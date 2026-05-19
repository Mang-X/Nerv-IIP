using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Domain.DomainEvents;
using Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OperationTaskIntegrationEventConverterTests
{
    [Fact]
    public void Completed_converter_maps_terminal_domain_event_to_ops_integration_contract()
    {
        var domainEvent = CreateCompletedDomainEvent();
        var converter = new OperationTaskCompletedIntegrationEventConverter();

        OperationTaskCompletedIntegrationEvent integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("ops.OperationTaskCompleted", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("ops", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("connector-host-001", integrationEvent.Actor);
        Assert.Equal("corr-result-attempt-001", integrationEvent.CorrelationId);
        Assert.Equal("op-001", integrationEvent.CausationId);
        Assert.Equal("ops:operation-task-completed:op-001:attempt-001", integrationEvent.IdempotencyKey);
        Assert.Equal("op-001", integrationEvent.Payload.OperationTaskId);
        Assert.Equal("attempt-001", integrationEvent.Payload.AttemptId);
        Assert.Equal("docker-container-local-demo-001", integrationEvent.Payload.InstanceKey);
        Assert.Equal("lifecycle.restart", integrationEvent.Payload.OperationCode);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:02Z"), integrationEvent.Payload.FinishedAtUtc);
    }

    [Fact]
    public void Failed_converter_maps_failure_code_to_ops_integration_contract()
    {
        var domainEvent = CreateFailedDomainEvent();
        var converter = new OperationTaskFailedIntegrationEventConverter();

        OperationTaskFailedIntegrationEvent integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("ops.OperationTaskFailed", integrationEvent.EventType);
        Assert.Equal("ops:operation-task-failed:op-002:attempt-002", integrationEvent.IdempotencyKey);
        Assert.Equal("container-exited", integrationEvent.Payload.FailureCode);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:02Z"), integrationEvent.Payload.FinishedAtUtc);
    }

    private static OperationTaskCompletedDomainEvent CreateCompletedDomainEvent()
    {
        var task = CreateClaimedTask("op-001", "attempt-001");
        task.RecordResult(CreateResult("op-001", "attempt-001", "succeeded", null), new AuditRecordId("audit-completed-001"));

        return Assert.IsType<OperationTaskCompletedDomainEvent>(task.GetDomainEvents().Last());
    }

    private static OperationTaskFailedDomainEvent CreateFailedDomainEvent()
    {
        var task = CreateClaimedTask("op-002", "attempt-002");
        task.RecordResult(
            CreateResult(
                "op-002",
                "attempt-002",
                "failed",
                new FailureReason(
                    "container-exited",
                    "Container exited before restart completed.",
                    "connector",
                    true,
                    new Dictionary<string, string>())),
            new AuditRecordId("audit-failed-001"));

        return Assert.IsType<OperationTaskFailedDomainEvent>(task.GetDomainEvents().Last());
    }

    private static OperationTask CreateClaimedTask(string taskId, string attemptId)
    {
        var task = OperationTask.Create(
            new OperationTaskId(taskId),
            new CreateOperationTaskRequest(
                "org-001",
                "env-dev",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                $"idem-{taskId}",
                "local-admin",
                "manual smoke restart",
                "corr-create-001",
                new Dictionary<string, string>()),
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"));

        task.AssignInitialAuditId(new AuditRecordId($"audit-{taskId}"));
        task.Claim(
            new OperationAttemptId(attemptId),
            new AuditRecordId($"audit-claim-{taskId}"),
            $"lease-{attemptId}",
            "connector-host-001",
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            TimeSpan.FromMinutes(5),
            3);

        return task;
    }

    private static OperationResult CreateResult(
        string taskId,
        string attemptId,
        string status,
        FailureReason? failure)
    {
        return new OperationResult(
            new ConnectorRequestContext(
                "2026-05-ops-test",
                "test-sdk",
                $"corr-result-{attemptId}",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
                "org-001",
                "env-dev",
                "connector-host-001"),
            taskId,
            attemptId,
            "docker-container-local-demo-001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            status,
            failure,
            new Dictionary<string, string>());
    }
}
