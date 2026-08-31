using DotNetCore.CAP;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            db.SchedulePlans.Add(CreatePlanWithAssignment());
            await db.SaveChangesAsync();
        }

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
        factory.Services.GetRequiredService<PoisonState>().Allow = true;
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IntegrationEventDeadLetterReplayExecutor>()
                .ReplayAsync(deadLetter.Id, CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
            Assert.Single(await db.SchedulePlanInvalidations.AsNoTracking().ToArrayAsync());
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
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
