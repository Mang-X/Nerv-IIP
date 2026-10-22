using FastEndpoints;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class AssetUnavailableRedisCapTransportTests
{
    private const string Profile = "Issue2967Acceptance";
    private const string Topic = "nerv-iip.issue2967acceptance.business-maintenance.maintenance.asset-unavailable.v2";

    [SchedulingPostgresRedisFact]
    public async Task Redis_cap_poison_exhausts_to_dlq_and_replay_preserves_identity_without_duplicate_claim()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var migrationServices = new ServiceCollection();
        migrationServices.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        migrationServices.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using (var migrationProvider = migrationServices.BuildServiceProvider())
        {
            await using var scope = migrationProvider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        var subscription = new FirstPublishSubscription();
        await using var factory = CreateFactory(subscription);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SchedulePlans.Add(CreatePlanWithAssignment());
            await db.SaveChangesAsync();
        }

        await TestTimeout.RunAsync("Scheduling target Subscribe entered", async token =>
            await subscription.Entered.Task.WaitAsync(token), TimeSpan.FromSeconds(30));
        var ready = TestTimeout.RunAsync("Scheduling before-first-publish: actual target Subscribe completed", async token =>
            await subscription.Completed.Task.WaitAsync(token), TimeSpan.FromSeconds(30));
        try
        {
            Assert.False(ready.IsCompleted);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM cap.published").SingleAsync());
        }
        finally
        {
            subscription.Release.TrySetResult();
        }
        await ready;

        var integrationEvent = Event("evt-poison", "asset-unavailable:wo-1:2026-06-01T09:00:00.0000000+00:00");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(Topic, integrationEvent);

        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION");
        var transportConsumerName = string.IsNullOrWhiteSpace(capVersion)
            ? AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName
            : $"{AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName}.{capVersion}";
        IntegrationEventDeadLetterMessage deadLetter = null!;
        await Eventually.AssertAsync("Scheduling CAP poison reaches persistent DLQ", async token =>
        {
            using var scope = factory.Services.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                .ListAsync(transportConsumerName, IntegrationEventDeadLetterStatus.Pending, token);
            deadLetter = Assert.Single(rows, x => x.FailureCode == IntegrationEventCapFailureDeadLetterer.HandlerRetryExhaustedFailureCode);
        }, new EventuallyOptions(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), []));

        Assert.Equal(integrationEvent.EventId, deadLetter.EventId);
        Assert.Equal(integrationEvent.EventVersion, deadLetter.EventVersion);
        Assert.Equal(integrationEvent.IdempotencyKey, deadLetter.IdempotencyKey);
        var replayEnvelope = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(
            deadLetter.EventJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(replayEnvelope);
        Assert.Equal(integrationEvent.EventId, replayEnvelope.EventId);
        Assert.Equal(integrationEvent.EventVersion, replayEnvelope.EventVersion);
        Assert.Equal(integrationEvent.IdempotencyKey, replayEnvelope.IdempotencyKey);
        Assert.Equal(integrationEvent.Payload, replayEnvelope.Payload);
        factory.Services.GetRequiredService<PoisonState>().Allow = true;
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IntegrationEventDeadLetterReplayExecutor>()
                .ReplayAsync(deadLetter.Id, CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        await AssertCountsEventuallyAsync(factory, 1, 1, "real CAP replay reaches the canonical processor");

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(
                AssetUnavailableIntegrationEventTopics.V1LegacyAlias,
                V1Event("evt-v1-companion", integrationEvent.IdempotencyKey));
        await AssertCountsEventuallyAsync(factory, 1, 1, "cross-version companion is deduplicated by business key");

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(
                AssetUnavailableIntegrationEventTopics.V1LegacyAlias,
                V1Event("evt-v1-wrong-key", integrationEvent.IdempotencyKey + ":mutated"));
        await AssertCountsEventuallyAsync(factory, 2, 2, "wrong-key mutation defeats business deduplication");
    }

    private static WebApplicationFactory<Program> CreateFactory(FirstPublishSubscription subscription)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = SchedulingPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["ProductEngineering:BaseUrl"] = "https://product-engineering.test",
            ["Mes:BaseUrl"] = "https://mes.test",
            ["IndustrialTelemetry:BaseUrl"] = "https://industrial-telemetry.test",
            ["Maintenance:BaseUrl"] = "https://maintenance.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Profile);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(Program).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.IncludeAbstractValidators = true;
                });
                var consumerFactory = services.Single(x => x.ServiceType == typeof(IConsumerClientFactory));
                services.Remove(consumerFactory);
                services.AddSingleton<IConsumerClientFactory>(provider => new FirstPublishConsumerFactory(
                    (IConsumerClientFactory)ActivatorUtilities.CreateInstance(provider, consumerFactory.ImplementationType!),
                    subscription, provider.GetRequiredService<IOptions<CapOptions>>().Value));
                services.AddSingleton<PoisonState>();
                services.Replace(ServiceDescriptor.Scoped<IAssetUnavailableCanonicalProcessor>(sp =>
                    new PoisonProcessor(sp.GetRequiredService<AssetUnavailableCanonicalProcessor>(), sp.GetRequiredService<PoisonState>())));
                services.PostConfigure<CapOptions>(options =>
                {
                    options.FailedRetryCount = 2;
                    options.FailedRetryInterval = 1;
                    options.FallbackWindowLookbackSeconds = 30;
                });
            });
        });
    }

    private static AssetUnavailableV2IntegrationEvent Event(string eventId, string key) => new(
        eventId, MaintenanceIntegrationEventTypes.AssetUnavailable, MaintenanceIntegrationEventVersions.V2,
        DateTimeOffset.Parse("2026-06-01T09:00:00Z"), MaintenanceIntegrationEventSources.BusinessMaintenance,
        "corr-2967", "cause-2967", "org-001", "env-dev", "system:test", key,
        new AssetUnavailableV2Payload("ASSET-CNC-01", "breakdown", DateTimeOffset.Parse("2026-06-01T09:00:00Z")));

    private static AssetUnavailableIntegrationEvent V1Event(string eventId, string key) => new(
        eventId, MaintenanceIntegrationEventTypes.AssetUnavailable, MaintenanceIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-06-01T09:00:00Z"), MaintenanceIntegrationEventSources.Maintenance,
        "corr-2967", "cause-2967", "org-001", "env-dev", "system:test", key,
        new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", DateTimeOffset.Parse("2026-06-01T09:00:00Z")));

    private static ValueTask AssertCountsEventuallyAsync(
        WebApplicationFactory<Program> factory,
        int expectedInbox,
        int expectedInvalidations,
        string operation) => Eventually.AssertAsync(operation, async _ =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(expectedInbox, await db.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
            Assert.Equal(expectedInvalidations, await db.SchedulePlanInvalidations.AsNoTracking().CountAsync());
        }, new EventuallyOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250), []));

    private static SchedulePlan CreatePlanWithAssignment() => SchedulePlan.FromGeneratedPlan(
        "org-001",
        "env-dev",
        SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
            ContractVersion: 1,
            PlanId: "plan-2967",
            ProblemId: "problem-2967",
            ProblemFingerprint: "fingerprint-plan-2967",
            AlgorithmVersion: "aps-lite-v1",
            Status: SchedulePlanStatusContract.Generated,
            GeneratedAtUtc: DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
            Metrics: new SchedulePlanMetricsContract(1, 0, 60, 60, 0, 0, 1m, 0m),
            Assignments:
            [
                new ScheduleAssignmentContract(
                    AssignmentId: "assign-plan-2967",
                    OrderId: "WO-2967",
                    OperationId: "OP-2967",
                    OperationSequence: 10,
                    ResourceId: "ASSET-CNC-01",
                    WorkCenterId: "WC-CNC",
                    StartUtc: DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                    EndUtc: DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
                    IsLocked: false,
                    ExplanationCode: "scheduled")
            ],
            ResourceLoads: [],
            Conflicts: [],
            UnscheduledOperations: [],
            ChangeSummary: [],
            GanttItems: [])));

    private sealed class FirstPublishSubscription
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FirstPublishConsumerFactory(IConsumerClientFactory inner, FirstPublishSubscription subscription, CapOptions options)
        : IConsumerClientFactory
    {
        public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            var client = await inner.CreateAsync(groupName, groupConcurrent);
            var prefix = string.IsNullOrEmpty(options.GroupNamePrefix) ? "" : options.GroupNamePrefix + ".";
            var target = prefix + AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName + "." + options.Version;
            return groupName == target ? new FirstPublishConsumer(client, subscription, options) : client;
        }
    }

    private sealed class FirstPublishConsumer(IConsumerClient inner, FirstPublishSubscription subscription, CapOptions options)
        : IConsumerClient
    {
        public BrokerAddress BrokerAddress => inner.BrokerAddress;
        public Func<TransportMessage, object?, Task>? OnMessageCallback { get => inner.OnMessageCallback; set => inner.OnMessageCallback = value; }
        public Action<LogMessageEventArgs>? OnLogCallback { get => inner.OnLogCallback; set => inner.OnLogCallback = value; }
        public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topics) => inner.FetchTopicsAsync(topics);
        public async Task SubscribeAsync(IEnumerable<string> topics)
        {
            var actualTopics = topics.ToArray();
            var prefix = string.IsNullOrEmpty(options.TopicNamePrefix) ? "" : options.TopicNamePrefix + ".";
            Assert.Contains(prefix + Topic, actualTopics);
            subscription.Entered.TrySetResult();
            await TestTimeout.RunAsync("Scheduling controlled actual Subscribe release", async token =>
                await subscription.Release.Task.WaitAsync(token), TimeSpan.FromSeconds(30));
            await inner.SubscribeAsync(actualTopics);
            subscription.Completed.TrySetResult();
        }
        public Task ListeningAsync(TimeSpan timeout, CancellationToken token) => inner.ListeningAsync(timeout, token);
        public Task CommitAsync(object? sender) => inner.CommitAsync(sender);
        public Task RejectAsync(object? sender) => inner.RejectAsync(sender);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class PoisonState { public volatile bool Allow; }
    private sealed class PoisonProcessor(IAssetUnavailableCanonicalProcessor inner, PoisonState state) : IAssetUnavailableCanonicalProcessor
    {
        public Task ProcessAsync(AssetUnavailableCanonicalInput input, CancellationToken cancellationToken)
        {
            if (!state.Allow) throw new InvalidOperationException("deterministic poison before Scheduling side effects");
            return inner.ProcessAsync(input, cancellationToken);
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SchedulingPostgresRedisFactAttribute : FactAttribute
{
    public SchedulingPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run Scheduling PostgreSQL + Redis CAP tests.";
    }
}
