using MediatR;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventConverters;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.AppHub.Web.Application.Commands;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubIntegrationEventTests
{
    [Fact]
    public void Non_collection_health_update_error_is_not_classified_as_retryable()
    {
        var error = new DbUpdateException("foreign key violation", new InvalidOperationException("FK_state_history"));

        Assert.False(RecordInstanceStateSnapshotCommandHandler.IsCollectionHealthUniqueConflict(error));
    }
    [Fact]
    public async Task Concurrent_first_collection_health_insert_reloads_and_monotonically_merges()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"apphub-concurrent-health-{Guid.CreateVersion7():N}";
        var options = CreateDbContextOptions(databaseName, root);
        await using (var seed = CreateDbContext(options))
        {
            seed.ApplicationInstances.Add(new ApplicationInstance("org", "env", "host", "collector", "1", "node", "opcua-main", "OPC", new Dictionary<string, string>(), []));
            await seed.SaveChangesAsync();
        }

        var competing = new ConnectorCollectionHealth("opcua-main", "opcua", Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 5, 1, 0, null);
        var interceptor = new ConflictOnceSaveChangesInterceptor(async () =>
        {
            await using var other = CreateDbContext(options);
            var instance = await other.ApplicationInstances.Include(x => x.CollectionHealth).SingleAsync();
            instance.RecordCollectionHealth(competing);
            await other.SaveChangesAsync();
        });
        var conflictOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .AddInterceptors(interceptor)
            .Options;
        await using var conflictDb = CreateDbContext(conflictOptions);
        using var services = new ServiceCollection()
            .AddSingleton<ApplicationDbContext>(conflictDb)
            .AddSingleton<IApplicationInstanceRepository, ApplicationInstanceRepository>()
            .BuildServiceProvider();
        var incoming = competing with { ReportedAtUtc = DateTimeOffset.Parse("2026-07-13T01:01:00Z"), ReceivedCount = 8 };
        var snapshot = new InstanceStateSnapshot(new ConnectorRequestContext("1", "1", "corr", incoming.ReportedAtUtc, "org", "env", "host"), "opcua-main", incoming.ReportedAtUtc, "running", "healthy", "ok", new Dictionary<string, string>(), new Dictionary<string, decimal>(), new Dictionary<string, string>(), incoming);

        await new RecordInstanceStateSnapshotCommandHandler(services).Handle(new RecordInstanceStateSnapshotCommand(snapshot), CancellationToken.None);

        await using var verify = CreateDbContext(options);
        var saved = await verify.ConnectorCollectionHealth.SingleAsync();
        Assert.Equal(8, saved.ReceivedCount);
        Assert.Equal(incoming.ReportedAtUtc, saved.ReportedAtUtc);
    }
    [Fact]
    public async Task Same_connector_identity_is_independent_across_organization_environment_scopes()
    {
        await using var db = CreateDbContext();
        var first = new ApplicationInstance("org-a", "env", "host", "collector", "1", "node", "opcua-main", "OPC A", new Dictionary<string, string>(), []);
        var second = new ApplicationInstance("org-b", "env", "host", "collector", "1", "node", "opcua-main", "OPC B", new Dictionary<string, string>(), []);
        first.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", Guid.CreateVersion7(), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 1, 0, 0, null));
        second.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", Guid.CreateVersion7(), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 2, 0, 0, null));
        db.ApplicationInstances.AddRange(first, second);

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ConnectorCollectionHealth.CountAsync());
    }
    [Fact]
    public async Task Collection_health_projection_survives_db_context_reload()
    {
        var root = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-health-{Guid.CreateVersion7():N}", root);
        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using (var writer = CreateDbContext(options))
        {
            var instance = new ApplicationInstance("org", "env", "host", "collector", "1", "node", "opcua-main", "OPC", new Dictionary<string, string>(), []);
            instance.RecordCollectionHealth(new ConnectorCollectionHealth("opcua-main", "opcua", epoch, DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 12, 2, 1, DateTimeOffset.Parse("2026-07-13T00:59:59Z")));
            writer.ApplicationInstances.Add(instance);
            await writer.SaveChangesAsync();
        }

        await using var reader = CreateDbContext(options);
        var reloaded = await reader.ApplicationInstances.Include(x => x.CollectionHealth).SingleAsync(x => x.InstanceKey == "opcua-main");

        Assert.Equal(epoch, reloaded.CollectionHealth!.CounterEpoch);
        Assert.Equal(12, reloaded.CollectionHealth.ReceivedCount);
        Assert.Equal(2, reloaded.CollectionHealth.DroppedCount);
        Assert.Equal(1, reloaded.CollectionHealth.ErrorCount);
    }
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
    public void Connector_host_unreachable_and_restored_converters_map_heartbeat_lifecycle_events()
    {
        var unreachableConverter = new ConnectorHostUnreachableIntegrationEventConverter();
        var restoredConverter = new ConnectorHostRestoredIntegrationEventConverter();
        var unreachableDomainEvent = new ConnectorHostUnreachableDomainEvent(
            "org-001",
            "env-dev",
            "connector-host-001",
            "demo-api-001",
            DateTimeOffset.Parse("2026-07-06T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
            TimeSpan.FromMinutes(5));
        var restoredDomainEvent = new ConnectorHostRestoredDomainEvent(
            "org-001",
            "env-dev",
            "connector-host-001",
            "demo-api-001",
            DateTimeOffset.Parse("2026-07-06T01:08:00Z"));

        var unreachable = unreachableConverter.Convert(unreachableDomainEvent);
        var restored = restoredConverter.Convert(restoredDomainEvent);

        Assert.Equal(AppHubIntegrationEventTypes.ConnectorHostUnreachable, unreachable.EventType);
        Assert.Equal(AppHubIntegrationEventSources.AppHub, unreachable.SourceService);
        Assert.Equal("org-001", unreachable.OrganizationId);
        Assert.Equal("env-dev", unreachable.EnvironmentId);
        Assert.Equal("connector-host-001", unreachable.Payload.ConnectorHostId);
        Assert.Equal("demo-api-001", unreachable.Payload.InstanceKey);
        Assert.Equal(300, unreachable.Payload.HeartbeatTimeoutSeconds);
        Assert.False(string.IsNullOrWhiteSpace(unreachable.CorrelationId));
        Assert.Equal("demo-api-001", unreachable.CausationId);
        Assert.Equal("apphub:connector-host-unreachable:org-001:env-dev:connector-host-001:demo-api-001:2026-07-06T01:06:00.0000000+00:00", unreachable.IdempotencyKey);

        Assert.Equal(AppHubIntegrationEventTypes.ConnectorHostRestored, restored.EventType);
        Assert.False(string.IsNullOrWhiteSpace(restored.CorrelationId));
        Assert.Equal("demo-api-001", restored.CausationId);
        Assert.Equal("connector-host-001", restored.Payload.ConnectorHostId);
        Assert.Equal("demo-api-001", restored.Payload.InstanceKey);
        Assert.Equal(DateTimeOffset.Parse("2026-07-06T01:08:00Z"), restored.Payload.RestoredAtUtc);
    }

    [Fact]
    public async Task Heartbeat_timeout_scan_marks_stale_instance_unreachable_once()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-heartbeat-scan-{Guid.CreateVersion7():N}", databaseRoot);
        await using var dbContext = CreateDbContext(options);
        var instance = new ApplicationInstance(
            "org-001",
            "env-dev",
            "connector-host-001",
            "demo-api",
            "1.0.0",
            "node-001",
            "demo-api-001",
            "demo-api",
            new Dictionary<string, string>(),
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())]);
        instance.RecordHeartbeat(DateTimeOffset.Parse("2026-07-06T01:00:00Z"), true, 12);
        dbContext.ApplicationInstances.Add(instance);
        await dbContext.SaveChangesAsync();
        instance.ClearDomainEvents();
        var scanner = new AppHubHeartbeatTimeoutScanner(dbContext);

        var first = await scanner.ScanAsync(
            DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
            TimeSpan.FromMinutes(5),
            take: 10,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await scanner.ScanAsync(
            DateTimeOffset.Parse("2026-07-06T01:07:00Z"),
            TimeSpan.FromMinutes(5),
            take: 10,
            CancellationToken.None);

        Assert.Equal(1, first.MarkedUnreachableCount);
        Assert.Equal(0, second.MarkedUnreachableCount);
        await using var assertionDbContext = CreateDbContext(options);
        var persisted = await assertionDbContext.ApplicationInstances
            .Include(x => x.Heartbeat)
            .SingleAsync(x => x.InstanceKey == "demo-api-001");
        Assert.NotNull(persisted.Heartbeat);
        Assert.False(persisted.Heartbeat.Reachable);
    }

    [Fact]
    public async Task Heartbeat_timeout_scan_command_publishes_unreachable_integration_event_through_unit_of_work()
    {
        var databaseName = $"apphub-heartbeat-command-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        using var provider = CreateHeartbeatScanCommandProvider(databaseName, databaseRoot);

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var instance = new ApplicationInstance(
                "org-001",
                "env-dev",
                "connector-host-001",
                "demo-api",
                "1.0.0",
                "node-001",
                "demo-api-command-001",
                "demo-api",
                new Dictionary<string, string>(),
                [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())]);
            instance.RecordHeartbeat(DateTimeOffset.Parse("2026-07-06T01:00:00Z"), true, 12);
            dbContext.ApplicationInstances.Add(instance);
            await dbContext.SaveChangesAsync();
        }

        AppHubHeartbeatTimeoutScanResult result;
        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            result = await sender.Send(new AppHubHeartbeatTimeoutScanCommand(
                DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
                TimeSpan.FromMinutes(5),
                Take: 10));
        }

        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        var integrationEvent = Assert.IsType<ConnectorHostUnreachableIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(1, result.MarkedUnreachableCount);
        Assert.Equal(AppHubIntegrationEventTypes.ConnectorHostUnreachable, integrationEvent.EventType);
        Assert.Equal("connector-host-001", integrationEvent.Payload.ConnectorHostId);
        Assert.False(string.IsNullOrWhiteSpace(integrationEvent.CorrelationId));
    }

    [Fact]
    public async Task Heartbeat_timeout_scan_skips_legacy_instances_without_connector_host()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-heartbeat-scan-legacy-{Guid.CreateVersion7():N}", databaseRoot);
        await using var dbContext = CreateDbContext(options);
        var instance = new ApplicationInstance(
            "org-001",
            "env-dev",
            "",
            "demo-api",
            "1.0.0",
            "node-001",
            "legacy-demo-api-001",
            "demo-api",
            new Dictionary<string, string>(),
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())]);
        instance.RecordHeartbeat(DateTimeOffset.Parse("2026-07-06T01:00:00Z"), true, 12);
        dbContext.ApplicationInstances.Add(instance);
        await dbContext.SaveChangesAsync();
        var scanner = new AppHubHeartbeatTimeoutScanner(dbContext);

        var result = await scanner.ScanAsync(
            DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
            TimeSpan.FromMinutes(5),
            take: 10,
            CancellationToken.None);

        Assert.Equal(0, result.MarkedUnreachableCount);
        Assert.True(instance.Heartbeat!.Reachable);
        Assert.Empty(instance.GetDomainEvents().OfType<ConnectorHostUnreachableDomainEvent>());
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
        await using var dbContext = CreateDbContext();
        using var services = CreateServiceProvider(dbContext);
        var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
            sender,
            services,
            deadLetterStore,
            new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>());

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
    public async Task Operation_completed_consumer_skips_duplicate_event_before_sending_command()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-completed-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateCompletedEvent(eventVersion: 1);

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(sender.RequestTypes);
        Assert.Equal(typeof(RefreshInstanceStateAfterOperationCommand), sender.RequestTypes.Single());
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Operation_completed_consumer_skips_released_event_with_same_idempotency_key()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-completed-idem-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateCompletedEvent(eventVersion: 1);
        var releasedEvent = integrationEvent with { EventId = "evt-ops-completed-guard-released" };

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(releasedEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(sender.RequestTypes);
        await using var assertionDbContext = CreateDbContext(options);
        var processed = Assert.Single(await assertionDbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(integrationEvent.EventId, processed.EventId);
        Assert.Equal(integrationEvent.IdempotencyKey, processed.IdempotencyKey);
    }

    [Fact]
    public async Task Operation_failed_consumer_skips_duplicate_event_before_sending_command()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"apphub-failed-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateFailedEvent(eventVersion: 1);

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            using var services = CreateServiceProvider(dbContext);
            var handler = new OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(
                sender,
                services,
                deadLetterStore,
                new RecordingLogger<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(sender.RequestTypes);
        Assert.Equal(typeof(RefreshInstanceStateAfterFailedOperationCommand), sender.RequestTypes.Single());
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Operation_failed_consumer_dead_letters_unsupported_version_without_sending_command()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        await using var dbContext = CreateDbContext();
        using var services = CreateServiceProvider(dbContext);
        var handler = new OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState(
            sender,
            services,
            deadLetterStore,
            new RecordingLogger<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>());

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
    public async Task Operation_completed_consumer_logs_warning_when_db_context_is_unavailable()
    {
        var sender = new RecordingSender();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var logger = new RecordingLogger<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>();
        var handler = new OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState(
            sender,
            services,
            deadLetterStore,
            logger);

        await handler.HandleAsync(CreateCompletedEvent(eventVersion: 1), CancellationToken.None);

        Assert.Single(sender.RequestTypes);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("ApplicationDbContext is not registered", StringComparison.Ordinal));
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

    [Fact]
    public void Apphub_published_events_have_local_cap_sink_subscriptions_for_inmemory_transport()
    {
        var subscriptions = typeof(AppHubPublishedEventSink)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(CapSubscribeAttribute), inherit: false).Cast<CapSubscribeAttribute>())
            .Select(attribute => new { attribute.Name, attribute.Group })
            .ToArray();

        Assert.Contains(
            subscriptions,
            subscription =>
                subscription.Name == nameof(ApplicationRegisteredIntegrationEvent) &&
                subscription.Group == AppHubPublishedEventSink.ConsumerName);
        Assert.Contains(
            subscriptions,
            subscription =>
                subscription.Name == nameof(ApplicationInstanceStatusChangedIntegrationEvent) &&
                subscription.Group == AppHubPublishedEventSink.ConsumerName);
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = CreateDbContextOptions($"apphub-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        return CreateDbContext(options);
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
    }

    private static ServiceProvider CreateHeartbeatScanCommandProvider(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseInMemoryDatabase(databaseName, databaseRoot)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<AppHubHeartbeatTimeoutScanner>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateServiceProvider(ApplicationDbContext dbContext)
    {
        return new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            _ = state;
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            _ = logLevel;
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = eventId;
            _ = exception;
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }

    private sealed class ConflictOnceSaveChangesInterceptor(Func<Task> createCompetingProjection) : SaveChangesInterceptor
    {
        private bool _thrown;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_thrown && eventData.Context!.ChangeTracker.Entries<ConnectorCollectionHealthProjection>().Any(x => x.State == EntityState.Added))
            {
                _thrown = true;
                await createCompetingProjection();
                throw new DbUpdateException("simulated unique insert conflict", new RecordInstanceStateSnapshotCommandHandler.CollectionHealthUniqueConstraintException());
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}
