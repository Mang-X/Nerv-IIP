using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ManualDispatchConcurrencyTests
{
    [Fact]
    public async Task Concurrent_manual_dispatch_assignments_reject_the_stale_revision()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedTaskAsync(options, activeDeviceAssetId: null);
        await using var firstContext = CreateContext(options);
        await using var staleContext = CreateContext(options);
        var first = await firstContext.OperationTasks.SingleAsync();
        var stale = await staleContext.OperationTasks.SingleAsync();

        first.Assign("operator-001", "device-001", "shift-a", At(1), "user:dispatcher-001");
        stale.Assign("operator-002", "device-002", "shift-b", At(2), "user:dispatcher-002");
        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_manual_dispatch_clear_and_cancellation_reject_the_stale_revision()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedTaskAsync(options, activeDeviceAssetId: "device-001");
        await using var clearContext = CreateContext(options);
        await using var staleCancellationContext = CreateContext(options);
        var clear = await clearContext.OperationTasks.SingleAsync();
        var staleCancellation = await staleCancellationContext.OperationTasks.SingleAsync();

        clear.Assign(null, null, null, At(2), "user:dispatcher-001");
        staleCancellation.Cancel(At(3), "user:planner-001");
        await clearContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleCancellationContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Dispatch_pipeline_retries_a_stale_assignment_with_the_next_revision_once()
    {
        var options = CreateInMemoryOptions();
        await SeedTaskAsync(options, activeDeviceAssetId: null);
        await using var staleContext = CreateContext(options);
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(1));
        var command = new AssignDispatchTaskCommand(
            "org-001", "env-dev", "OP-CONCURRENCY-10", "operator-retry", "device-retry", "shift-b", At(2), "user:dispatcher-retry");
        var handler = new AssignDispatchTaskCommandHandler(staleContext);
        var behavior = new ManualDispatchConcurrencyRetryBehavior<AssignDispatchTaskCommand, MesAcceptedResponse>(staleContext);
        var attempts = 0;

        await behavior.Handle(command, async cancellationToken =>
        {
            attempts++;
            var response = await handler.Handle(command, cancellationToken);
            await staleContext.SaveChangesAsync(cancellationToken);
            return response;
        }, CancellationToken.None);

        await using var assertionContext = CreateContext(options);
        var persisted = await assertionContext.OperationTasks.SingleAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(2, persisted.ManualDispatchRevision);
        Assert.Equal("device-retry", persisted.DeviceAssetId);
        var dispatched = Assert.IsType<OperationTaskManuallyDispatchedDomainEvent>(
            Assert.Single(Assert.Single(staleContext.OperationTasks.Local).GetDomainEvents()));
        Assert.Equal(2, dispatched.Dispatch.DispatchRevision);
    }

    [Fact]
    public async Task Dispatch_pipeline_retries_a_stale_device_clear_with_the_next_revision_once()
    {
        var options = CreateInMemoryOptions();
        await SeedTaskAsync(options, activeDeviceAssetId: "device-seed");
        await using var staleContext = CreateContext(options);
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(2));
        var command = new AssignDispatchTaskCommand(
            "org-001", "env-dev", "OP-CONCURRENCY-10", null, null, null, At(3), "user:dispatcher-clear");
        var handler = new AssignDispatchTaskCommandHandler(staleContext);
        var behavior = new ManualDispatchConcurrencyRetryBehavior<AssignDispatchTaskCommand, MesAcceptedResponse>(staleContext);
        var attempts = 0;

        await behavior.Handle(command, async cancellationToken =>
        {
            attempts++;
            var response = await handler.Handle(command, cancellationToken);
            await staleContext.SaveChangesAsync(cancellationToken);
            return response;
        }, CancellationToken.None);

        await using var assertionContext = CreateContext(options);
        var persisted = await assertionContext.OperationTasks.SingleAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(3, persisted.ManualDispatchRevision);
        Assert.False(persisted.HasActiveManualDispatch);
        Assert.Null(persisted.DeviceAssetId);
        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(Assert.Single(staleContext.OperationTasks.Local).GetDomainEvents()));
        Assert.Equal(3, cleared.Dispatch.DispatchRevision);
        Assert.Equal("device-winner", cleared.Dispatch.ResourceId);
    }

    [Fact]
    public async Task Cancellation_pipeline_retries_stale_tasks_and_emits_each_clear_at_its_persisted_revision()
    {
        var options = CreateInMemoryOptions();
        await SeedTaskAsync(options, activeDeviceAssetId: "device-seed");
        await using var staleContext = CreateContext(options);
        _ = await staleContext.WorkOrders.SingleAsync();
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(2));
        var command = new CancelWorkOrderCommand(
            "org-001", "env-dev", "WO-CONCURRENCY-001", "plan cancelled", At(3), "user:planner-001");
        var handler = new CancelWorkOrderCommandHandler(staleContext);
        var behavior = new ManualDispatchConcurrencyRetryBehavior<CancelWorkOrderCommand, MesAcceptedResponse>(staleContext);
        var attempts = 0;

        await behavior.Handle(command, async cancellationToken =>
        {
            attempts++;
            var response = await handler.Handle(command, cancellationToken);
            await staleContext.SaveChangesAsync(cancellationToken);
            return response;
        }, CancellationToken.None);

        await using var assertionContext = CreateContext(options);
        var persisted = await assertionContext.OperationTasks.SingleAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(3, persisted.ManualDispatchRevision);
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled, persisted.Status);
        var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(Assert.Single(staleContext.OperationTasks.Local).GetDomainEvents()));
        Assert.Equal(3, cleared.Dispatch.DispatchRevision);
        Assert.Equal("device-winner", cleared.Dispatch.ResourceId);
    }

    [Fact]
    public async Task Real_unit_of_work_pipeline_publishes_only_the_successful_retried_assignment()
    {
        var databaseName = $"mes-manual-dispatch-pipeline-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateInMemoryOptions(databaseName, databaseRoot);
        await SeedTaskAsync(options, activeDeviceAssetId: null);
        await using var provider = CreatePipelineProvider(databaseName, databaseRoot);
        await using var scope = provider.CreateAsyncScope();
        var staleContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(1));

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AssignDispatchTaskCommand(
            "org-001", "env-dev", "OP-CONCURRENCY-10", "operator-retry", "device-retry", "shift-b", At(2), "user:dispatcher-retry"));

        var published = Assert.Single(provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published);
        var dispatched = Assert.IsType<MesOperationTaskManuallyDispatchedIntegrationEvent>(published);
        Assert.Equal(2, dispatched.Payload.DispatchRevision);
        Assert.Equal("device-retry", dispatched.Payload.ResourceId);
    }

    [Fact]
    public async Task Real_unit_of_work_pipeline_publishes_only_the_successful_retried_clear()
    {
        var databaseName = $"mes-manual-dispatch-pipeline-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateInMemoryOptions(databaseName, databaseRoot);
        await SeedTaskAsync(options, activeDeviceAssetId: "device-seed");
        await using var provider = CreatePipelineProvider(databaseName, databaseRoot);
        await using var scope = provider.CreateAsyncScope();
        var staleContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(2));

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AssignDispatchTaskCommand(
            "org-001", "env-dev", "OP-CONCURRENCY-10", null, null, null, At(3), "user:dispatcher-clear"));

        var published = Assert.Single(provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published);
        var cleared = Assert.IsType<MesOperationTaskManualDispatchClearedIntegrationEvent>(published);
        Assert.Equal(3, cleared.Payload.DispatchRevision);
        Assert.Equal("device-winner", cleared.Payload.ResourceId);
    }

    [Fact]
    public async Task Real_unit_of_work_pipeline_publishes_only_the_successful_retried_cancellation_clear()
    {
        var databaseName = $"mes-manual-dispatch-pipeline-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateInMemoryOptions(databaseName, databaseRoot);
        await SeedTaskAsync(options, activeDeviceAssetId: "device-seed");
        await using var provider = CreatePipelineProvider(databaseName, databaseRoot);
        await using var scope = provider.CreateAsyncScope();
        var staleContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _ = await staleContext.WorkOrders.SingleAsync();
        _ = await staleContext.OperationTasks.SingleAsync();
        await WinConcurrentAssignmentAsync(options, "device-winner", At(2));

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CancelWorkOrderCommand(
            "org-001", "env-dev", "WO-CONCURRENCY-001", "plan cancelled", At(3), "user:planner-001"));

        var clears = provider.GetRequiredService<RecordingIntegrationEventPublisher>().Published
            .OfType<MesOperationTaskManualDispatchClearedIntegrationEvent>()
            .ToArray();
        var cleared = Assert.Single(clears);
        Assert.Equal(3, cleared.Payload.DispatchRevision);
        Assert.Equal("device-winner", cleared.Payload.ResourceId);
    }

    [Fact]
    public async Task Manual_dispatch_retry_is_bounded_to_three_conflicting_attempts()
    {
        var options = CreateInMemoryOptions();
        await SeedTaskAsync(options, activeDeviceAssetId: null);
        await using var staleContext = CreateContext(options);
        var command = new AssignDispatchTaskCommand(
            "org-001", "env-dev", "OP-CONCURRENCY-10", null, "device-loser", null, At(1), "user:loser");
        var behavior = new ManualDispatchConcurrencyRetryBehavior<AssignDispatchTaskCommand, MesAcceptedResponse>(staleContext);
        var attempts = 0;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => behavior.Handle(
            command,
            async cancellationToken =>
            {
                attempts++;
                var losingTask = await staleContext.OperationTasks.SingleAsync(cancellationToken);
                losingTask.Assign(null, $"device-loser-{attempts}", null, At(attempts), "user:loser");
                losingTask.ClearDomainEvents();
                await WinConcurrentAssignmentAsync(options, $"device-winner-{attempts}", At(attempts));
                await staleContext.SaveChangesAsync(cancellationToken);
                return new MesAcceptedResponse("unreachable", command.OperationTaskId, command.AssignedAtUtc);
            },
            CancellationToken.None));

        Assert.Equal(3, attempts);
    }

    private static async Task SeedTaskAsync(
        DbContextOptions<ApplicationDbContext> options,
        string? activeDeviceAssetId)
    {
        await using var context = CreateContext(options);
        await context.Database.EnsureCreatedAsync();
        context.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-CONCURRENCY-001",
            "SKU-001",
            "PV-001",
            10m,
            10,
            At(30)));
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-CONCURRENCY-001",
            "OP-CONCURRENCY-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-001",
            [],
            At(0),
            TimeSpan.FromMinutes(30),
            null,
            null);
        if (activeDeviceAssetId is not null)
        {
            task.Assign("operator-seed", activeDeviceAssetId, "shift-a", At(1), "user:seed-001");
        }

        context.OperationTasks.Add(task);
        await context.SaveChangesAsync();
    }

    private static async Task WinConcurrentAssignmentAsync(
        DbContextOptions<ApplicationDbContext> options,
        string deviceAssetId,
        DateTimeOffset assignedAtUtc)
    {
        await using var winnerContext = CreateContext(options);
        var winner = await winnerContext.OperationTasks.SingleAsync();
        winner.Assign("operator-winner", deviceAssetId, "shift-a", assignedAtUtc, "user:dispatcher-winner");
        winner.ClearDomainEvents();
        await winnerContext.SaveChangesAsync();
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(
        string? databaseName = null,
        InMemoryDatabaseRoot? databaseRoot = null) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                databaseName ?? $"mes-manual-dispatch-concurrency-{Guid.CreateVersion7():N}",
                databaseRoot ?? new InMemoryDatabaseRoot())
            .Options;

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static ServiceProvider CreatePipelineProvider(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IMesIntegrationEventContextAccessor, StubMesIntegrationEventContextAccessor>();
        services.AddScoped<OperationTaskManuallyDispatchedIntegrationEventConverter>();
        services.AddScoped<OperationTaskManualDispatchClearedIntegrationEventConverter>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddOpenBehavior(typeof(ManualDispatchConcurrencyRetryBehavior<,>))
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(builder => builder
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private static DateTimeOffset At(int minute) =>
        DateTimeOffset.Parse("2026-07-15T08:00:00Z").AddMinutes(minute);

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

    private sealed class StubMesIntegrationEventContextAccessor : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() =>
            new("corr-manual-dispatch-concurrency", "cause-manual-dispatch-concurrency");
    }

}
