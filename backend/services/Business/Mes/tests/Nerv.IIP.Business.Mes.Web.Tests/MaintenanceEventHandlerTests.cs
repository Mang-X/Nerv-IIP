using Microsoft.AspNetCore.Mvc.Testing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MaintenanceEventHandlerTests
{
    [Fact]
    public async Task AssetUnavailableHandler_RecordsOpenUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        await using var dbContext = CreateDbContext();

        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true },
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateUnavailableEvent(now), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal("WC-A", window.WorkCenterId);
        Assert.Null(window.ToUtc);
        Assert.Equal("breakdown", window.Reason);
        Assert.Equal(RescheduleTrigger.AssetUnavailable, Assert.Single(store.ScheduleResults).Trigger);
    }

    [Fact]
    public async Task AssetUnavailableHandler_SkipsDuplicateEventBeforeRecordingWindowOrRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-unavailable-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateUnavailableEvent(now);

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(store.Unavailabilities);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task AssetRestoredHandler_ClosesUnavailableWindowAndAutoReschedules()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));

        var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
            CreateDbContext(),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateRestoredEvent(now.AddHours(2)), CancellationToken.None);

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal(now.AddHours(2), window.ToUtc);
        Assert.Equal(RescheduleTrigger.AssetRestored, Assert.Single(store.ScheduleResults).Trigger);
    }

    [Fact]
    public async Task AssetRestoredHandler_SkipsDuplicateEventBeforeClosingWindowOrRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"mes-restored-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = CreateRestoredEvent(now.AddHours(2));

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
                store,
                new RuleScheduler(),
                new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var window = Assert.Single(store.Unavailabilities);
        Assert.Equal(now.AddHours(2), window.ToUtc);
        Assert.Single(store.ScheduleResults);
        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task AssetUnavailableHandler_DeadLettersUnsupportedEventVersionWithoutRescheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetUnavailableIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetUnavailable = true },
            CreateDbContext(),
            deadLetterStore);

        await handler.HandleAsync(CreateUnavailableEvent(DateTimeOffset.Parse("2026-05-22T08:00:00Z"), eventVersion: 2), CancellationToken.None);

        Assert.Empty(store.Unavailabilities);
        Assert.Empty(store.ScheduleResults);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public async Task AssetRestoredHandler_DeadLettersUnsupportedEventVersionWithoutClosingWindow()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        store.AddUnavailability(new WorkCenterUnavailability("WC-A", now, null, "breakdown", "ASSET-CNC-01"));
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new AssetRestoredIntegrationEventHandlerForReschedule(
            store,
            new RuleScheduler(),
            new MesRescheduleOptions { AutoRescheduleOnAssetRestored = true },
            CreateDbContext(),
            deadLetterStore);

        await handler.HandleAsync(CreateRestoredEvent(now.AddHours(2), eventVersion: 2), CancellationToken.None);

        Assert.Null(Assert.Single(store.Unavailabilities).ToUtc);
        Assert.Empty(store.ScheduleResults);
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            AssetRestoredIntegrationEventHandlerForReschedule.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public void PostgreSQL_profile_uses_persistent_dead_letter_store()
    {
        using var factory = new MesPostgreSqlWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();

        Assert.IsType<PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>(store);
    }

    private static AssetUnavailableIntegrationEvent CreateUnavailableEvent(DateTimeOffset fromUtc, int eventVersion = MaintenanceIntegrationEventVersions.V1)
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            eventVersion,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetUnavailable:ASSET-CNC-01:20260522080000",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", fromUtc));
    }

    private static AssetRestoredIntegrationEvent CreateRestoredEvent(DateTimeOffset restoredAtUtc, int eventVersion = MaintenanceIntegrationEventVersions.V1)
    {
        return new AssetRestoredIntegrationEvent(
            "evt-002",
            MaintenanceIntegrationEventTypes.AssetRestored,
            eventVersion,
            restoredAtUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-001",
            "evt-001",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetRestored:ASSET-CNC-01:20260522100000",
            new AssetRestoredPayload("ASSET-CNC-01", restoredAtUtc));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = CreateDbContextOptions($"mes-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
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

    private sealed class MesPostgreSqlWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=nerv_iip_mes_dead_letter_test;Username=nerv;Password=nerv",
                    ["InternalService:BearerToken"] = "test-internal-token",
                });
            });
        }
    }
}
