using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 回填通道在**真实 Redis/CAP transport** 上的投递（#3000）。此前两端只靠 <c>nameof(同一契约类型)</c>
/// 这个字面量对齐——「接线≠通路」：没有任何证据表明消息真的离开发布方、真的路由到回填消费组。
/// 本用例证明三件事：路由绑定成立、独立消费组确实独立（直投组不吃这条 topic）、重投幂等。
/// </summary>
[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseProjectionBackfillRedisCapTransportTests
{
    private const string DeploymentProfile = "Issue3000Backfill";
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    [QualityBackfillPostgresRedisFact]
    public async Task Redis_cap_transport_routes_the_backfill_topic_to_its_own_consumer_group_and_stays_idempotent_in_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        await PublishAsync(factory, Backfill("evt-backfill-transport-1"));
        await AssertEventuallyAsync(factory, async (db, token) =>
        {
            var operation = await db.PeriodicInspectionOperations.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-001", token);
            Assert.Equal("SKU-FG-1000", operation.SkuCode);
            Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);

            // 独立消费组确实独立：只有回填组登记了这封事件，直投组没有吃到这条 topic。
            var consumers = await db.ProcessedIntegrationEvents.AsNoTracking()
                .Select(x => x.ConsumerName)
                .Distinct()
                .ToArrayAsync(token);
            Assert.Equal(
                [WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName],
                consumers);
        });

        // 重跑回填：新 EventId 走完整投递链，投影内容不变、不开首件任务。
        await PublishAsync(factory, Backfill("evt-backfill-transport-2"));
        await AssertEventuallyAsync(factory, async (db, token) =>
        {
            var delivered = await db.Database
                .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.received")
                .SingleAsync(token);
            Assert.True(delivered >= 2, $"Expected both backfill deliveries, observed {delivered}.");

            var operation = await db.PeriodicInspectionOperations.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-001", token);
            Assert.Equal("SKU-FG-1000", operation.SkuCode);
            Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
            Assert.Empty(await db.InspectionTasks.AsNoTracking()
                .Where(x => x.SourceType == FirstArticleInspection.SourceType)
                .ToArrayAsync(token));
        });
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = QualityPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["Approval:BaseUrl"] = "https://approval.test",
            ["Erp:BaseUrl"] = "https://erp.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services => services.PostConfigure<CapOptions>(options =>
            {
                options.SucceedMessageExpiredAfter = 3600;
                options.CollectorCleaningInterval = 3600;
                options.FailedRetryInterval = 1;
            }));
        });
    }

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);

        // 路由绑定：回填 handler 在真实 selector 上确实绑到回填 topic，且与直投 handler 是两个不同的 topic。
        var candidates = scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        var prefix = scope.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        var backfill = Assert.Single(
            candidates,
            x => x.ImplTypeInfo.AsType()
                == typeof(WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts));
        var live = Assert.Single(
            candidates,
            x => x.ImplTypeInfo.AsType()
                == typeof(WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts));
        Assert.Equal(Topic(prefix, nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent)), backfill.TopicName);
        Assert.Equal(Topic(prefix, nameof(WorkOrderReleasedIntegrationEvent)), live.TopicName);
        Assert.NotEqual(live.TopicName, backfill.TopicName);
        Assert.NotEqual(live.Attribute.Group, backfill.Attribute.Group);

        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "IQP-FA-TRANSPORT-001", "first-article", "SKU-FG-1000", null, "WC-001", null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", true, "100%");
        plan.Activate();
        db.InspectionPlans.Add(plan);
        await db.SaveChangesAsync();
    }

    private static string Topic(string? prefix, string shortName) =>
        string.IsNullOrWhiteSpace(prefix) ? shortName : $"{prefix}.{shortName}";

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), integrationEvent);
    }

    private static async Task AssertEventuallyAsync(
        WebApplicationFactory<Program> factory,
        Func<ApplicationDbContext, CancellationToken, Task> assertion) =>
        await Eventually.AssertAsync(
            condition: "Quality release-projection backfill CAP delivery converges in PostgreSQL",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                await assertion(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), token);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

    private static WorkOrderReleaseProjectionBackfilledIntegrationEvent Backfill(string eventId) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            ReleasedAtUtc,
            [new ReleasedOperationPayload("OP-001", 10, "WC-001")]));
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class QualityBackfillPostgresRedisFactAttribute : FactAttribute
{
    public QualityBackfillPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP release-projection backfill proof.";
    }
}
