using System.Collections.Concurrent;
using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesReworkReceiptRedisCapTransportTests
{
    private const string DeploymentProfile = "Issue3010Acceptance";

    [MesReworkReceiptPostgresRedisFact]
    public async Task Redis_cap_production_subscriber_emits_one_created_receipt_for_replayed_ncr_request()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(
            factory.Services,
            "org-transport",
            "env-transport");

        var integrationEvent = NcrReworkRequestedPostgresFixtures.CreateEvent(
            eventId: "evt-rework-transport-001",
            organizationId: "org-transport",
            environmentId: "env-transport",
            idempotencyKey: "quality:rework:org-transport:env-transport:ncr-001");
        using (var publicationScope = factory.Services.CreateScope())
        {
            var publisher = publicationScope.ServiceProvider.GetRequiredService<ICapPublisher>();
            await publisher.PublishAsync(nameof(NcrReworkRequestedIntegrationEvent), integrationEvent);
            await publisher.PublishAsync(nameof(NcrReworkRequestedIntegrationEvent), integrationEvent);
        }

        var probe = factory.Services.GetRequiredService<ReworkReceiptTransportProbe>();
        await Eventually.AssertAsync(
            condition: "MES consumes the replayed NCR request and transports one created rework receipt",
            assertion: async token =>
            {
                using var assertionScope = factory.Services.CreateScope();
                var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rework = await db.WorkOrders.AsNoTracking()
                    .SingleAsync(x => x.SourceNcrId == "ncr-001", token);
                Assert.Equal(WorkOrder.ReworkType, rework.WorkOrderType);
                Assert.Equal(2, await db.OperationTasks.CountAsync(x => x.WorkOrderId == rework.WorkOrderIdValue, token));
                Assert.Single(await db.ProcessedIntegrationEvents
                    .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
                    .AsNoTracking()
                    .ToArrayAsync(token));
                var delivered = Assert.Single(probe.Receipts);
                Assert.Equal("ncr-001", delivered.Payload.SourceNcrId);
                Assert.Equal(rework.WorkOrderIdValue, delivered.Payload.ReworkWorkOrderId);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

        await Consistently.StaysAsync(
            condition: "NCR replay does not emit a duplicate created rework receipt",
            observe: _ => ValueTask.FromResult(probe.Receipts.Count),
            isSatisfied: count => count == 1,
            describe: count => $"deliveredReceipts={count}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(200), []));
    }

    private static WebApplicationFactory<Program> CreateFactory()
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
            ["InternalService:BearerToken"] = "test-internal-token",
            ["ProductEngineering:BaseUrl"] = "https://product-engineering.test",
            ["Inventory:BaseUrl"] = "https://inventory.test",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["Quality:BaseUrl"] = "https://quality.test",
            ["Approval:BaseUrl"] = "https://approval.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IMesMaterialRequirementSnapshotProvider>(_ => NoRequirementsSnapshotProvider.Instance);
                services.AddSingleton<ReworkReceiptTransportProbe>();
                services.PostConfigure<CapOptions>(options =>
                {
                    options.SucceedMessageExpiredAfter = 3600;
                    options.CollectorCleaningInterval = 3600;
                    options.FailedRetryInterval = 1;
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

        var candidates = scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        Assert.Contains(candidates, candidate =>
            candidate.ImplTypeInfo.AsType() == typeof(NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder));
        var observer = Assert.Single(candidates, candidate =>
            candidate.ImplTypeInfo.AsType() == typeof(ReworkReceiptTransportProbe));
        var prefix = scope.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        Assert.Equal($"{prefix}.{nameof(ReworkWorkOrderCreatedIntegrationEvent)}", observer.TopicName);
    }

    public sealed class ReworkReceiptTransportProbe : ICapSubscribe
    {
        private readonly ConcurrentQueue<ReworkWorkOrderCreatedIntegrationEvent> receipts = new();

        public IReadOnlyCollection<ReworkWorkOrderCreatedIntegrationEvent> Receipts => receipts.ToArray();

        [CapSubscribe(nameof(ReworkWorkOrderCreatedIntegrationEvent), Group = "business-mes.issue3010-rework-receipt-probe")]
        public Task ObserveAsync(
            ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            receipts.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class NoRequirementsSnapshotProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsSnapshotProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MesReworkReceiptPostgresRedisFactAttribute : FactAttribute
{
    public MesReworkReceiptPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP MES rework-receipt producer proof.";
        }
    }
}
