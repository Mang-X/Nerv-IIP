using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesAssetUnavailableRedisCapTransportTests
{
    private const string DeploymentProfile = "Issue2966Acceptance";
    private const string V2Topic = "nerv-iip.issue2966acceptance.business-maintenance.maintenance.asset-unavailable.v2";

    [MesAssetUnavailablePostgresRedisFact]
    public async Task Redis_cap_poison_exhaustion_persists_and_replays_the_original_v2_identity_without_duplicate_effects()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var poison = new PoisonSwitch();
        await using var factory = CreateFactory(poison);
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        var fromUtc = DateTimeOffset.Parse("2026-08-31T08:00:00Z");
        const string idempotencyKey = "maintenance.AssetUnavailable:ASSET-CNC-01:20260831080000";
        var v2 = new AssetUnavailableV2IntegrationEvent(
            "evt-2966-poison-v2",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2,
            fromUtc,
            MaintenanceIntegrationEventSources.BusinessMaintenance,
            "corr-2966",
            "cause-2966",
            "org-001",
            "env-dev",
            "maintenance",
            idempotencyKey,
            new AssetUnavailableV2Payload("ASSET-CNC-01", "CUSTOM-DOWNTIME-CODE", fromUtc));

        await PublishAsync(factory, V2Topic, v2);
        IntegrationEventDeadLetterMessage? deadLetter = null;
        await Eventually.AssertAsync(
            condition: "MES CAP poison exhausts bounded retries into the persistent dead-letter store",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                deadLetter = (await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                        .ListAsync(consumerName: null, status: null, cancellationToken: token))
                    .SingleOrDefault(message =>
                        message.EventId == v2.EventId &&
                        message.ConsumerName.StartsWith(
                            AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
                            StringComparison.Ordinal));
                Assert.NotNull(deadLetter);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

        Assert.NotNull(deadLetter);
        Assert.Equal(IntegrationEventCapFailureDeadLetterer.HandlerRetryExhaustedFailureCode, deadLetter.FailureCode);
        Assert.True(poison.Attempts >= 2, $"Expected initial delivery plus bounded retry, observed {poison.Attempts} attempts.");
        var preserved = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(
            deadLetter.EventJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        Assert.NotNull(preserved);
        Assert.Equal(v2.EventId, preserved.EventId);
        Assert.Equal(MaintenanceIntegrationEventVersions.V2, preserved.EventVersion);
        Assert.Equal(idempotencyKey, preserved.IdempotencyKey);

        poison.Enabled = false;
        var v1 = new AssetUnavailableIntegrationEvent(
            "evt-2966-v1-winner",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-2966",
            "cause-2966",
            "org-001",
            "env-dev",
            "maintenance",
            idempotencyKey,
            new AssetUnavailablePayload("ASSET-CNC-01", "legacy-breakdown", fromUtc));
        await PublishAsync(factory, nameof(AssetUnavailableIntegrationEvent), v1);
        await AssertOneEffectEventuallyAsync(factory, idempotencyKey);

        IntegrationEventDeadLetterReplayResult replay;
        using (var scope = factory.Services.CreateScope())
        {
            replay = await scope.ServiceProvider.GetRequiredService<IntegrationEventDeadLetterReplayExecutor>()
                .ReplayAsync(deadLetter.Id, CancellationToken.None);
        }
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed.ToString(), replay.Status);
        await AssertOneEffectEventuallyAsync(factory, idempotencyKey);
    }

    private static WebApplicationFactory<Program> CreateFactory(PoisonSwitch poison)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["Cap:FailedRetryCount"] = "2",
            ["Cap:FailedRetryInterval"] = "1",
            ["InternalService:BearerToken"] = "test-internal-token",
            ["Approval:BaseUrl"] = "https://approval.test",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["Quality:BaseUrl"] = "https://quality.test",
            ["ProductEngineering:BaseUrl"] = "https://product-engineering.test",
            ["Inventory:BaseUrl"] = "https://inventory.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.Configure<CapOptions>(options =>
                {
                    options.FailedRetryCount = 2;
                    options.FailedRetryInterval = 1;
                });
                services.RemoveAll<IMesPlanningStore>();
                services.AddSingleton(poison);
                services.AddScoped<IMesPlanningStore>(provider =>
                {
                    var db = provider.GetRequiredService<ApplicationDbContext>();
                    return new PoisoningPlanningStore(
                        new PersistentMesPlanningStore(db, provider.GetRequiredService<IOperationTaskRepository>()),
                        poison);
                });
            });
        });
    }

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
    }

    private static async Task PublishAsync<TEvent>(WebApplicationFactory<Program> factory, string topic, TEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(topic, integrationEvent!);
    }

    private static async Task AssertOneEffectEventuallyAsync(WebApplicationFactory<Program> factory, string idempotencyKey) =>
        await Eventually.AssertAsync(
            condition: "MES replay preserves cross-version idempotency through real Redis CAP transport",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var inbox = await db.ProcessedIntegrationEvents.AsNoTracking().SingleAsync(token);
                Assert.Equal(idempotencyKey, inbox.IdempotencyKey);
                Assert.Equal(1, await db.WorkCenterUnavailabilities.AsNoTracking().CountAsync(token));
                Assert.Equal(1, await db.ScheduleResults.AsNoTracking().CountAsync(token));
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

    private sealed class PoisonSwitch
    {
        private int attempts;
        public bool Enabled { get; set; } = true;
        public int Attempts => Volatile.Read(ref attempts);
        public void RecordAttempt() => Interlocked.Increment(ref attempts);
    }

    private sealed class PoisoningPlanningStore(IMesPlanningStore inner, PoisonSwitch poison) : IMesPlanningStore
    {
        public void AddWorkOrder(PlannedWorkOrder workOrder) => inner.AddWorkOrder(workOrder);
        public void AddOperationTask(PlannedOperationTask operationTask) => inner.AddOperationTask(operationTask);
        public void AddUnavailability(WorkCenterUnavailability unavailability) => inner.AddUnavailability(unavailability);
        public void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId) => inner.MapDeviceAssetToWorkCenter(deviceAssetId, workCenterId);
        public Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default) => inner.GetWorkOrdersAsync(cancellationToken);
        public Task<bool> WorkOrderExistsAsync(string organizationId, string environmentId, string workOrderId, CancellationToken cancellationToken = default) => inner.WorkOrderExistsAsync(organizationId, environmentId, workOrderId, cancellationToken);
        public Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default) => inner.GetOperationTasksAsync(cancellationToken);
        public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetUnavailabilitiesAsync(cancellationToken);
        public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default) => inner.GetUnavailabilitiesAsync(organizationId, environmentId, cancellationToken);
        public Task<IReadOnlyCollection<MesScheduleResult>> GetScheduleResultsAsync(CancellationToken cancellationToken = default) => inner.GetScheduleResultsAsync(cancellationToken);
        public Task CloseUnavailabilityAsync(string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default) => inner.CloseUnavailabilityAsync(deviceAssetId, restoredAtUtc, cancellationToken);
        public Task CloseUnavailabilityAsync(string organizationId, string environmentId, string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default) => inner.CloseUnavailabilityAsync(organizationId, environmentId, deviceAssetId, restoredAtUtc, cancellationToken);
        public Task<string> ResolveWorkCenterIdAsync(string deviceAssetId, CancellationToken cancellationToken = default) => ResolveAsync(() => inner.ResolveWorkCenterIdAsync(deviceAssetId, cancellationToken));
        public Task<string> ResolveWorkCenterIdAsync(string organizationId, string environmentId, string deviceAssetId, CancellationToken cancellationToken = default) => ResolveAsync(() => inner.ResolveWorkCenterIdAsync(organizationId, environmentId, deviceAssetId, cancellationToken));
        public Task<MesScheduleResult> AddScheduleResultAsync(RescheduleTrigger trigger, DateTimeOffset scheduledAtUtc, RuleSchedulePlan plan, IReadOnlyCollection<ScheduledOperation>? compareAssignments = null, CancellationToken cancellationToken = default) => inner.AddScheduleResultAsync(trigger, scheduledAtUtc, plan, compareAssignments, cancellationToken);
        public Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default) => inner.GetScheduleOperationsAsync(organizationId, environmentId, cancellationToken);

        private Task<string> ResolveAsync(Func<Task<string>> next)
        {
            if (poison.Enabled)
            {
                poison.RecordAttempt();
                throw new InvalidOperationException("issue-2966 controlled poison");
            }
            return next();
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MesAssetUnavailablePostgresRedisFactAttribute : FactAttribute
{
    public MesAssetUnavailablePostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP MES asset-unavailable proof.";
        }
    }
}
