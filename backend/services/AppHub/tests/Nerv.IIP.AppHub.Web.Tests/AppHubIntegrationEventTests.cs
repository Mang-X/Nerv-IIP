using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubIntegrationEventTests
{
    [Fact]
    public void Application_registered_converter_maps_domain_event_to_apphub_integration_event()
    {
        var converter = new ApplicationRegisteredIntegrationEventConverter();
        var domainEvent = new ApplicationRegisteredDomainEvent("org-001", "env-dev", "demo-api", "1.0.0");

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("apphub.ApplicationRegistered", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("apphub", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("apphub:application-registered:org-001:env-dev:demo-api:1.0.0", integrationEvent.IdempotencyKey);
        Assert.Equal("demo-api", integrationEvent.Payload.ApplicationKey);
        Assert.Equal("1.0.0", integrationEvent.Payload.Version);
    }

    [Fact]
    public void Instance_status_changed_converter_maps_status_transition()
    {
        var converter = new ApplicationInstanceStatusChangedIntegrationEventConverter();
        var domainEvent = new ApplicationInstanceStatusChangedDomainEvent(
            "demo-api-001",
            "starting",
            "running",
            DateTimeOffset.Parse("2026-05-15T00:00:10Z"));

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("apphub.InstanceStatusChanged", integrationEvent.EventType);
        Assert.Equal("apphub:instance-status-changed:demo-api-001:2026-05-15T00:00:10.0000000+00:00", integrationEvent.IdempotencyKey);
        Assert.Equal("demo-api-001", integrationEvent.Payload.InstanceKey);
        Assert.Equal("starting", integrationEvent.Payload.PreviousStatus);
        Assert.Equal("running", integrationEvent.Payload.CurrentStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-05-15T00:00:10Z"), integrationEvent.Payload.ChangedAtUtc);
    }

    [Fact]
    public void Cap_retry_duplicate_operation_completed_event_refreshes_instance_state_once()
    {
        var instance = new ApplicationInstance(
            "org-001",
            "env-dev",
            "demo-api",
            "1.0.0",
            "node-001",
            "docker-container-local-demo-001",
            "demo-api",
            new Dictionary<string, string>(),
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())]);
        instance.RecordStateSnapshot(
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            "running",
            "healthy",
            "initial state",
            new Dictionary<string, string>());
        var integrationEvent = new OperationTaskCompletedIntegrationEvent(
            "evt-ops-completed-001",
            "ops.OperationTaskCompleted",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "ops",
            "corr-ops-001",
            "op-001",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-completed:op-001:attempt-001",
            new OperationTaskCompletedPayload(
                "op-001",
                "attempt-001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z")));

        var first = instance.RecordOperationTaskCompletedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId);
        var duplicate = instance.RecordOperationTaskCompletedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId);

        Assert.True(first);
        Assert.False(duplicate);
        Assert.Equal(2, instance.StateHistory.Count);
        Assert.Equal("ops:operation-task-completed:op-001:attempt-001", instance.Metadata["ops.lastCompletedOperationIdempotencyKey"]);
        Assert.True(instance.Metadata.ContainsKey("ops.completed.ops:operation-task-completed:op-001:attempt-001"));
        Assert.IsType<InstanceStateSnapshotRecordedDomainEvent>(instance.GetDomainEvents().Last());
    }

    [Fact]
    public void Cap_retry_duplicate_operation_failed_event_refreshes_instance_state_once()
    {
        var instance = new ApplicationInstance(
            "org-001",
            "env-dev",
            "demo-api",
            "1.0.0",
            "node-001",
            "docker-container-local-demo-001",
            "demo-api",
            new Dictionary<string, string>(),
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())]);
        instance.RecordStateSnapshot(
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            "running",
            "healthy",
            "initial state",
            new Dictionary<string, string>());
        var integrationEvent = new OperationTaskFailedIntegrationEvent(
            "evt-ops-failed-001",
            "ops.OperationTaskFailed",
            1,
            DateTimeOffset.Parse("2026-05-15T00:00:03Z"),
            "ops",
            "corr-ops-002",
            "op-002",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-failed:op-002:attempt-001",
            new OperationTaskFailedPayload(
                "op-002",
                "attempt-001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:03Z"),
                "docker-daemon-unavailable"));

        var first = instance.RecordOperationTaskFailedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId,
            integrationEvent.Payload.FailureCode);
        var duplicate = instance.RecordOperationTaskFailedRefresh(
            integrationEvent.IdempotencyKey,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.OperationCode,
            integrationEvent.Payload.FinishedAtUtc,
            integrationEvent.CorrelationId,
            integrationEvent.Payload.FailureCode);

        Assert.True(first);
        Assert.False(duplicate);
        Assert.Equal(2, instance.StateHistory.Count);
        Assert.Equal("ops:operation-task-failed:op-002:attempt-001", instance.Metadata["ops.lastFailedOperationIdempotencyKey"]);
        Assert.Equal("docker-daemon-unavailable", instance.Metadata["ops.lastFailedOperationFailureCode"]);
        Assert.True(instance.Metadata.ContainsKey("ops.failed.ops:operation-task-failed:op-002:attempt-001"));
        Assert.IsType<InstanceStateSnapshotRecordedDomainEvent>(instance.GetDomainEvents().Last());
    }

    [Fact]
    public async Task Operation_completed_consumer_dead_letters_unsupported_version_without_sending_command()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(sender, deadLetterStore);

        await handler.HandleAsync(CreateCompletedEvent(eventVersion: 2), CancellationToken.None);

        Assert.Empty(sender.RequestTypes);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public async Task Operation_failed_consumer_dead_letters_unsupported_version_without_sending_command()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(sender, deadLetterStore);

        await handler.HandleAsync(CreateFailedEvent(eventVersion: 2), CancellationToken.None);

        Assert.Empty(sender.RequestTypes);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public void PostgreSQL_profile_uses_persistent_dead_letter_store()
    {
        var services = new ServiceCollection();

        services.AddAppHubIntegrationEventDeadLetterStore(usePostgreSql: true);

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IIntegrationEventDeadLetterStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>), descriptor.ImplementationType);
    }

    private static OperationTaskCompletedIntegrationEvent CreateCompletedEvent(int eventVersion)
    {
        return new OperationTaskCompletedIntegrationEvent(
            "evt-ops-completed-guard",
            "ops.OperationTaskCompleted",
            eventVersion,
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "ops",
            "corr-ops-guard",
            "op-guard",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-completed:op-guard:attempt-001",
            new OperationTaskCompletedPayload(
                "op-guard",
                "attempt-001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:02Z")));
    }

    private static OperationTaskFailedIntegrationEvent CreateFailedEvent(int eventVersion)
    {
        return new OperationTaskFailedIntegrationEvent(
            "evt-ops-failed-guard",
            "ops.OperationTaskFailed",
            eventVersion,
            DateTimeOffset.Parse("2026-05-15T00:00:03Z"),
            "ops",
            "corr-ops-failed-guard",
            "op-guard",
            "org-001",
            "env-dev",
            "connector-host-001",
            "ops:operation-task-failed:op-guard:attempt-001",
            new OperationTaskFailedPayload(
                "op-guard",
                "attempt-001",
                "docker-container-local-demo-001",
                "lifecycle.restart",
                DateTimeOffset.Parse("2026-05-15T00:00:03Z"),
                "docker-daemon-unavailable"));
    }

    private sealed class RecordingSender : ISender
    {
        public List<Type> RequestTypes { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            RequestTypes.Add(request.GetType());
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            RequestTypes.Add(request.GetType());
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            RequestTypes.Add(request.GetType());
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Streams are not used by these tests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Streams are not used by these tests.");
        }
    }

}
