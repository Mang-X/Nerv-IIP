using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Web.Infrastructure;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceIntegrationEventHandlerTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [Fact]
    public async Task Device_disabled_consumer_pauses_matching_plans_once_on_replay()
    {
        await using var dbContext = CreateDbContext();
        var matching = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-MATCH", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        var otherDevice = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-02", "PM-OTHER", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        dbContext.MaintenancePlans.AddRange(matching, otherDevice);
        await dbContext.SaveChangesAsync();
        var sender = new CommandOnlySender(dbContext);
        var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(sender, dbContext, new InMemoryIntegrationEventDeadLetterStore());
        var disabled = CreateDeviceAssetChangedEvent("disabled");

        await handler.HandleAsync(disabled, CancellationToken.None);
        await handler.HandleAsync(disabled, CancellationToken.None);

        Assert.True(matching.Paused);
        Assert.False(otherDevice.Paused);
        Assert.Equal(1, sender.ApplyDeviceStateCommandCount);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.True((await dbContext.MaintenanceDeviceStates.SingleAsync()).Disabled);
    }

    [Fact]
    public async Task Device_changed_consumer_projects_active_status_without_resuming_paused_plans()
    {
        await using var dbContext = CreateDbContext();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-ACTIVE", "P7D", new DateOnly(2026, 6, 1), "maintenance");
        plan.Pause();
        dbContext.MaintenancePlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var sender = new CommandOnlySender(dbContext);
        var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(sender, dbContext, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDeviceAssetChangedEvent("active"), CancellationToken.None);

        Assert.True(plan.Paused);
        Assert.Equal(1, sender.ApplyDeviceStateCommandCount);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.False((await dbContext.MaintenanceDeviceStates.SingleAsync()).Disabled);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Device_changed_consumer_keeps_disabled_state_when_active_event_is_not_newer(int activeOffsetMinutes)
    {
        await using var dbContext = CreateDbContext();
        var sender = new CommandOnlySender(dbContext);
        var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(sender, dbContext, new InMemoryIntegrationEventDeadLetterStore());
        var disabledAtUtc = new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(CreateDeviceAssetChangedEvent("disabled", changedAtUtc: disabledAtUtc), CancellationToken.None);
        await handler.HandleAsync(CreateDeviceAssetChangedEvent("active", eventId: "evt-device-delayed-active", changedAtUtc: disabledAtUtc.AddMinutes(activeOffsetMinutes)), CancellationToken.None);

        var state = await dbContext.MaintenanceDeviceStates.SingleAsync();
        Assert.True(state.Disabled);
        Assert.Equal("evt-device-001", state.SourceEventId);
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Device_disabled_consumer_accepts_missing_plan_without_poison_message()
    {
        await using var dbContext = CreateDbContext();
        var sender = new CommandOnlySender(dbContext);
        var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(sender, dbContext, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDeviceAssetChangedEvent("disabled"), CancellationToken.None);

        Assert.Equal(1, sender.ApplyDeviceStateCommandCount);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());

        var planId = await new CreateMaintenancePlanCommandHandler(dbContext).Handle(
            new CreateMaintenancePlanCommand(
                "org-001", "env-dev", "DEV-CNC-01", "PM-LATE", "P7D", new DateOnly(2026, 6, 1), "maintenance", null, null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var latePlan = await dbContext.MaintenancePlans.SingleAsync(x => x.Id == planId);
        Assert.True(latePlan.Paused);
    }

    [Fact]
    public async Task Device_changed_consumer_dead_letters_unsupported_version()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new MaintenanceIntegrationEventDeadLetterStore(dbContext);
        var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(new CommandOnlySender(dbContext), dbContext, deadLetterStore);

        await handler.HandleAsync(CreateDeviceAssetChangedEvent("disabled", eventVersion: 2), CancellationToken.None);

        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToArrayAsync());
        var deadLetter = Assert.Single(await dbContext.IntegrationEventDeadLetters.ToArrayAsync());
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
    }

    [MaintenanceRealPostgresFact]
    public async Task Device_disabled_consumer_durably_blocks_pm_generation_on_postgres()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(baseConnectionString, "maintenance_device_pause");
        await using (var dbContext = CreatePostgresDbContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
            dbContext.MaintenancePlans.Add(MaintenancePlan.Create(
                "org-001", "env-dev", "DEV-CNC-01", "PM-POSTGRES", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
            await dbContext.SaveChangesAsync();

            var handler = new PauseMaintenancePlansWhenDeviceDisabledHandler(
                new CommandOnlySender(dbContext),
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            var disabled = CreateDeviceAssetChangedEvent("disabled");
            await handler.HandleAsync(disabled, CancellationToken.None);
            await handler.HandleAsync(disabled, CancellationToken.None);
        }

        await using var assertionContext = CreatePostgresDbContext(database.ConnectionString);
        var persistedPlan = await assertionContext.MaintenancePlans.SingleAsync();
        Assert.True(persistedPlan.Paused);
        Assert.Equal(1, await assertionContext.ProcessedIntegrationEvents.CountAsync());

        var generation = await new GenerateDueMaintenanceWorkOrdersCommandHandler(assertionContext).Handle(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"),
            CancellationToken.None);
        await assertionContext.SaveChangesAsync();
        Assert.Equal(0, generation.GeneratedCount);
        Assert.Empty(await assertionContext.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal(new DateOnly(2026, 6, 1), persistedPlan.NextDueOn);

        assertionContext.MaintenancePlans.Add(MaintenancePlan.Create(
            "org-001", "env-dev", "DEV-CNC-02", "PM-POSTGRES-RACE", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        await assertionContext.SaveChangesAsync();

        var applyCommand = new ApplyMaintenanceDeviceStateCommand(
            "org-001", "env-dev", "DEV-CNC-02", true, DateTimeOffset.UtcNow, "evt-device-postgres-race");
        var generateCommand = new GenerateDueMaintenanceWorkOrdersCommand(
            "org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm");
        var applyLock = await new ApplyMaintenanceDeviceStateCommandLock().GetLockKeysAsync(applyCommand, CancellationToken.None);
        var generateLock = await new GenerateDueMaintenanceWorkOrdersCommandLock().GetLockKeysAsync(generateCommand, CancellationToken.None);
        var sharedLockKey = applyLock.LockKey ?? throw new InvalidOperationException("Device-state command lock key is required.");
        Assert.Equal(sharedLockKey, generateLock.LockKey);
        var distributedLock = new RedisMaintenanceDistributedLock(new InMemoryRedisCommandLockStore(), TimeProvider.System);
        var applyHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowApplyCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var applyTask = Task.Run(async () =>
        {
            await using var handle = await distributedLock.AcquireAsync(sharedLockKey, TimeSpan.FromSeconds(5), CancellationToken.None);
            applyHasLock.SetResult();
            await allowApplyCommit.Task;
            await using var applyContext = CreatePostgresDbContext(database.ConnectionString);
            await new ApplyMaintenanceDeviceStateCommandHandler(applyContext).Handle(applyCommand, CancellationToken.None);
            await applyContext.SaveChangesAsync();
        });
        await applyHasLock.Task;

        var generateTask = Task.Run(async () =>
        {
            generationAttempted.SetResult();
            await using var handle = await distributedLock.AcquireAsync(sharedLockKey, TimeSpan.FromSeconds(5), CancellationToken.None);
            await using var generationContext = CreatePostgresDbContext(database.ConnectionString);
            var result = await new GenerateDueMaintenanceWorkOrdersCommandHandler(generationContext).Handle(generateCommand, CancellationToken.None);
            await generationContext.SaveChangesAsync();
            return result;
        });
        await generationAttempted.Task;
        await using (var blockedProbe = await distributedLock.TryAcquireAsync(sharedLockKey, TimeSpan.Zero, CancellationToken.None))
        {
            Assert.Null(blockedProbe);
        }

        allowApplyCommit.SetResult();
        await applyTask;
        var concurrentGeneration = await generateTask;

        Assert.Equal(0, concurrentGeneration.GeneratedCount);
        await using var raceAssertionContext = CreatePostgresDbContext(database.ConnectionString);
        Assert.True((await raceAssertionContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-POSTGRES-RACE")).Paused);
        Assert.True((await raceAssertionContext.MaintenanceDeviceStates.SingleAsync(x => x.DeviceAssetId == "DEV-CNC-02")).Disabled);
        Assert.Empty(await raceAssertionContext.MaintenanceWorkOrders.ToArrayAsync());
    }

    [Fact]
    public async Task Alarm_consumer_creates_one_work_order_per_source_alarm_id()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var alarm = CreateAlarmRaisedEvent();

        await handler.HandleAsync(alarm, CancellationToken.None);
        await handler.HandleAsync(alarm, CancellationToken.None);

        var workOrders = await dbContext.MaintenanceWorkOrders.ToArrayAsync();
        Assert.Single(workOrders);
        Assert.Equal("alarm-001", workOrders[0].SourceAlarmId);
        Assert.Equal("p1", workOrders[0].Priority);
        Assert.Equal("OVER_TEMP", workOrders[0].FailureModeCode);
        Assert.Equal("temperature", workOrders[0].FailureCauseCode);
        Assert.Contains("96.5", workOrders[0].DiagnosticDescription, StringComparison.Ordinal);
        Assert.True(workOrders[0].AssetUnavailable);
        Assert.Equal(1, sender.CreateWorkOrderCommandCount);
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
        Assert.Empty(await deadLetterStore.ListAsync(OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName, IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Alarm_consumer_skips_released_event_with_same_idempotency_key()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var alarm = CreateAlarmRaisedEvent();
        var releasedAlarm = alarm with { EventId = "evt-alarm-001-released" };

        await handler.HandleAsync(alarm, CancellationToken.None);
        await handler.HandleAsync(releasedAlarm, CancellationToken.None);

        Assert.Single(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        Assert.Equal(1, sender.CreateWorkOrderCommandCount);
        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(alarm.EventId, processed.EventId);
        Assert.Equal(alarm.IdempotencyKey, processed.IdempotencyKey);
    }

    [Fact]
    public async Task Alarm_consumer_dead_letters_unsupported_event_version_without_creating_work_order()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new MaintenanceIntegrationEventDeadLetterStore(dbContext);
        var handler = new OpenWorkOrderWhenAlarmRaisedHandler(new CommandOnlySender(dbContext), dbContext, deadLetterStore);

        await handler.HandleAsync(CreateAlarmRaisedEvent(eventVersion: 2), CancellationToken.None);

        Assert.Empty(await dbContext.MaintenanceWorkOrders.ToArrayAsync());
        var deadLetter = Assert.Single(await dbContext.IntegrationEventDeadLetters.ToArrayAsync());
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, deadLetter.Status);
    }

    [Fact]
    public async Task Dead_letter_store_truncates_failed_and_ignored_messages_to_column_limit()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new MaintenanceIntegrationEventDeadLetterStore(dbContext);
        var failed = await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName,
                CreateAlarmRaisedEvent("evt-alarm-failed-long-message", "alarm-failed-long-message", DateTimeOffset.UtcNow),
                "manual-test",
                "Stored for replay."),
            CancellationToken.None);
        var ignored = await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName,
                CreateAlarmRaisedEvent("evt-alarm-ignored-long-message", "alarm-ignored-long-message", DateTimeOffset.UtcNow),
                "manual-test",
                "Stored for replay."),
            CancellationToken.None);
        var longMessage = new string('x', 1200);

        await deadLetterStore.MarkFailedAsync(failed.Id, "replay-handler-failed", longMessage, DateTimeOffset.UtcNow, CancellationToken.None);
        await deadLetterStore.MarkIgnoredAsync(ignored.Id, longMessage, DateTimeOffset.UtcNow, CancellationToken.None);

        var failedMessage = await deadLetterStore.GetAsync(failed.Id, CancellationToken.None);
        var ignoredMessage = await deadLetterStore.GetAsync(ignored.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed, failedMessage?.Status);
        Assert.Equal(1000, failedMessage?.FailureMessage.Length);
        Assert.Equal(IntegrationEventDeadLetterStatus.Ignored, ignoredMessage?.Status);
        Assert.Equal(1000, ignoredMessage?.FailureMessage.Length);
    }

    [Fact]
    public async Task Alarm_cleared_consumer_marks_matching_open_work_order_without_completing_it()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var raisedHandler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var clearedHandler = new MarkWorkOrderAlarmClearedHandler(sender, dbContext, deadLetterStore);
        var clearedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent(), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent(clearedAtUtc), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent(clearedAtUtc), CancellationToken.None);

        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.True(workOrder.AlarmCleared);
        Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
        Assert.Equal(1, sender.ClearAlarmCommandCount);
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Stable_rule_alarm_events_open_one_work_order_and_clear_runtime_window()
    {
        await using var dbContext = CreateDbContext();
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new CommandOnlySender(dbContext);
        var raisedHandler = new OpenWorkOrderWhenAlarmRaisedHandler(sender, dbContext, deadLetterStore);
        var clearedHandler = new MarkWorkOrderAlarmClearedHandler(sender, dbContext, deadLetterStore);
        var raisedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var clearedAtUtc = raisedAtUtc.AddHours(1);

        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent("evt-alarm-537-1", "TEMP_RULE", raisedAtUtc), CancellationToken.None);
        await raisedHandler.HandleAsync(CreateAlarmRaisedEvent("evt-alarm-537-2", "TEMP_RULE", raisedAtUtc.AddMinutes(1)), CancellationToken.None);
        await clearedHandler.HandleAsync(CreateAlarmClearedEvent("evt-alarm-clear-537", "TEMP_RULE", raisedAtUtc, clearedAtUtc), CancellationToken.None);

        var workOrder = await dbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal("TEMP_RULE", workOrder.SourceAlarmId);
        Assert.True(workOrder.AssetUnavailable);
        Assert.True(workOrder.AlarmCleared);
        Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        dbContext.Entry(workOrder).Property(x => x.AssetUnavailableFromUtc).CurrentValue = raisedAtUtc;
        await dbContext.SaveChangesAsync();

        var availability = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest(
                "org-001",
                "env-dev",
                raisedAtUtc,
                raisedAtUtc.AddHours(4),
                ["DEV-CNC-01"],
                null)),
            CancellationToken.None);
        var activeAlarm = Assert.Single(availability.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Equal(raisedAtUtc, activeAlarm.StartUtc);
        Assert.Equal(clearedAtUtc, activeAlarm.EndUtc);

        var runtime = await new MaintenanceUnavailableWindowRuntimeHoursProvider(sender).CalculateFallbackAsync(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            raisedAtUtc,
            raisedAtUtc.AddHours(4),
            CancellationToken.None);
        Assert.Equal(3m, runtime.RuntimeHours);
        Assert.Equal(AssetRuntimeSources.Fallback, runtime.RuntimeSource);
    }

    [Fact]
    public async Task Alarm_clear_command_marks_all_matching_open_work_orders_when_duplicate_alarm_facts_exist()
    {
        await using var dbContext = CreateDbContext();
        var clearedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var first = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        var second = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
        dbContext.MaintenanceWorkOrders.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        await new MarkMaintenanceWorkOrderAlarmClearedCommandHandler(dbContext).Handle(
            new MarkMaintenanceWorkOrderAlarmClearedCommand("org-001", "env-dev", "alarm-001", clearedAtUtc),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var workOrders = await dbContext.MaintenanceWorkOrders.OrderBy(x => x.OpenedAtUtc).ToArrayAsync();
        Assert.All(workOrders, workOrder =>
        {
            Assert.True(workOrder.AlarmCleared);
            Assert.Equal(clearedAtUtc, workOrder.AlarmClearedAtUtc);
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(int eventVersion = 1)
    {
        return CreateAlarmRaisedEvent("evt-alarm-001", "alarm-001", DateTimeOffset.UtcNow, eventVersion);
    }

    private static DeviceAssetChangedIntegrationEvent CreateDeviceAssetChangedEvent(
        string status,
        int eventVersion = 1,
        string eventId = "evt-device-001",
        DateTimeOffset? changedAtUtc = null)
    {
        var effectiveChangedAtUtc = changedAtUtc ?? new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);
        return new DeviceAssetChangedIntegrationEvent(
            eventId,
            MasterDataIntegrationEventTypes.DeviceAssetChanged,
            eventVersion,
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-device-001",
            "cause-device-001",
            "org-001",
            "env-dev",
            "user:masterdata-admin",
            $"masterdata:device-asset-changed:org-001:env-dev:DEV-CNC-01:{eventId}",
            new MasterDataChangedPayload("device-asset", "DEV-CNC-01", status, effectiveChangedAtUtc));
    }

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(
        string eventId,
        string externalAlarmId,
        DateTimeOffset raisedAtUtc,
        int eventVersion = 1)
    {
        return new AlarmRaisedIntegrationEvent(
            eventId,
            "industrialTelemetry.AlarmRaised",
            eventVersion,
            raisedAtUtc,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-raised:org-001:env-dev:DEV-CNC-01:OVER_TEMP:{externalAlarmId}:{eventId}",
            new AlarmRaisedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                raisedAtUtc,
                externalAlarmId,
                "p1",
                "temperature",
                96.5m,
                90m,
                "celsius"));
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(DateTimeOffset clearedAtUtc, int eventVersion = 1)
    {
        var raisedAtUtc = clearedAtUtc.AddHours(-1);
        return CreateAlarmClearedEvent("evt-alarm-clear-001", "alarm-001", raisedAtUtc, clearedAtUtc, eventVersion);
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(
        string eventId,
        string externalAlarmId,
        DateTimeOffset raisedAtUtc,
        DateTimeOffset clearedAtUtc,
        int eventVersion = 1)
    {
        return new AlarmClearedIntegrationEvent(
            eventId,
            "industrialTelemetry.AlarmCleared",
            eventVersion,
            clearedAtUtc,
            "industrialTelemetry",
            "corr-alarm-001",
            "alarm-event-001",
            "org-001",
            "env-dev",
            "system:industrial-telemetry",
            $"industrialTelemetry:alarm-cleared:org-001:env-dev:DEV-CNC-01:OVER_TEMP:{externalAlarmId}:{eventId}",
            new AlarmClearedPayload(
                "alarm-event-001",
                "DEV-CNC-01",
                "OVER_TEMP",
                "critical",
                raisedAtUtc,
                clearedAtUtc,
                externalAlarmId));
    }

    private sealed class CommandOnlySender(ApplicationDbContext dbContext) : ISender
    {
        public int CreateWorkOrderCommandCount { get; private set; }
        public int ClearAlarmCommandCount { get; private set; }
        public int ApplyDeviceStateCommandCount { get; private set; }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateMaintenanceWorkOrderCommand command)
            {
                CreateWorkOrderCommandCount++;
                var handler = new CreateMaintenanceWorkOrderCommandHandler(dbContext);
                var id = await handler.Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)id;
            }

            if (request is QueryMaintenanceAvailabilityWindowsQuery query)
            {
                var handler = new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext);
                var response = await handler.Handle(query, cancellationToken);
                return (TResponse)(object)response;
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is ApplyMaintenanceDeviceStateCommand deviceStateCommand)
            {
                return SendDeviceStateAsync(deviceStateCommand, cancellationToken);
            }

            if (request is MarkMaintenanceWorkOrderAlarmClearedCommand clearCommand)
            {
                return SendClearAsync(clearCommand, cancellationToken);
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        private async Task SendDeviceStateAsync(ApplyMaintenanceDeviceStateCommand command, CancellationToken cancellationToken)
        {
            ApplyDeviceStateCommandCount++;
            var handler = new ApplyMaintenanceDeviceStateCommandHandler(dbContext);
            await handler.Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task SendClearAsync(MarkMaintenanceWorkOrderAlarmClearedCommand command, CancellationToken cancellationToken)
        {
            ClearAlarmCommandCount++;
            var handler = new MarkMaintenanceWorkOrderAlarmClearedCommandHandler(dbContext);
            await handler.Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only typed commands are supported by this test sender.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported by this test sender.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported by this test sender.");
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

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
            };
            var databaseName = $"nerv_iip_{prefix}_{Guid.CreateVersion7():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();",
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class MaintenanceRealPostgresFactAttribute : FactAttribute
    {
        public MaintenanceRealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run the real PostgreSQL Maintenance device-pause acceptance test.";
            }
        }
    }
}
