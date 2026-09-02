using System.Collections.Concurrent;
using DotNetCore.CAP;
using DotNetCore.CAP.Filter;
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
    public async Task Concurrent_distinct_events_for_one_ncr_emit_one_created_receipt_after_both_deliveries_succeed()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(
            factory.Services,
            "org-transport",
            "env-transport");

        var firstEvent = NcrReworkRequestedPostgresFixtures.CreateEvent(
            eventId: "evt-rework-transport-001",
            organizationId: "org-transport",
            environmentId: "env-transport",
            idempotencyKey: "quality:rework:org-transport:env-transport:ncr-001");
        var secondEvent = firstEvent with { EventId = "evt-rework-transport-002" };
        await Task.WhenAll(
            PublishAsync(factory, firstEvent),
            PublishAsync(factory, secondEvent));

        var probe = factory.Services.GetRequiredService<ReworkReceiptTransportProbe>();
        var concurrencyGate = factory.Services.GetRequiredService<DistinctNcrDeliveryGate>();
        await Eventually.AssertAsync(
            condition: "both concurrent NCR deliveries succeed before MES exposes one durable receipt",
            assertion: async token =>
            {
                using var assertionScope = factory.Services.CreateScope();
                var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, firstEvent.EventId, token));
                AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, secondEvent.EventId, token));
                Assert.Equal(
                    [firstEvent.EventId, secondEvent.EventId],
                    concurrencyGate.EventIds.Order(StringComparer.Ordinal).ToArray());
                var rework = await db.WorkOrders.AsNoTracking()
                    .SingleAsync(x => x.SourceNcrId == "ncr-001", token);
                Assert.Equal(WorkOrder.ReworkType, rework.WorkOrderType);
                Assert.Equal(2, await db.OperationTasks.CountAsync(x => x.WorkOrderId == rework.WorkOrderIdValue, token));
                Assert.Single(await db.ProcessedIntegrationEvents
                    .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
                    .AsNoTracking()
                    .ToArrayAsync(token));
                Assert.Equal(
                    1,
                    await db.Database.SqlQueryRaw<int>(
                            "SELECT count(*)::int AS \"Value\" FROM cap.published WHERE \"Content\" LIKE '%ReworkWorkOrderCreated%' AND \"Content\" LIKE '%\"SourceNcrId\":\"ncr-001\"%'")
                        .SingleAsync(token));
                var delivered = Assert.Single(probe.Receipts);
                Assert.Equal("ncr-001", delivered.Payload.SourceNcrId);
                Assert.Equal(rework.WorkOrderIdValue, delivered.Payload.ReworkWorkOrderId);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));
    }

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        NcrReworkRequestedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(NcrReworkRequestedIntegrationEvent), integrationEvent);
    }

    private static Task<string[]> ReadReceivedStatusesAsync(
        ApplicationDbContext db,
        string eventId,
        CancellationToken cancellationToken) =>
        db.Database.SqlQuery<string>($"SELECT \"StatusName\" AS \"Value\" FROM cap.received WHERE \"Content\" LIKE {'%' + eventId + '%'}")
            .ToArrayAsync(cancellationToken);

    private static void AssertReceivedSucceeded(string[] statuses)
    {
        Assert.NotEmpty(statuses);
        Assert.All(statuses, status => Assert.Equal("Succeeded", status));
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
                services.AddSingleton<DistinctNcrDeliveryGate>();
                services.AddSingleton<ISubscribeFilter>(provider =>
                    provider.GetRequiredService<DistinctNcrDeliveryGate>());
                services.PostConfigure<CapOptions>(options =>
                {
                    options.SucceedMessageExpiredAfter = 3600;
                    options.CollectorCleaningInterval = 3600;
                    options.FailedRetryInterval = 1;
                    options.ConsumerThreadCount = 2;
                    options.EnableSubscriberParallelExecute = true;
                    options.SubscriberParallelExecuteThreadCount = 2;
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

    private sealed class DistinctNcrDeliveryGate : SubscribeFilter
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<string> eventIds = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> EventIds
        {
            get
            {
                lock (eventIds)
                {
                    return eventIds.ToArray();
                }
            }
        }

        public override async Task OnSubscribeExecutingAsync(ExecutingContext context)
        {
            var integrationEvent = context.Arguments
                .OfType<NcrReworkRequestedIntegrationEvent>()
                .SingleOrDefault();
            if (integrationEvent is null)
            {
                return;
            }

            lock (eventIds)
            {
                eventIds.Add(integrationEvent.EventId);
                if (eventIds.Count == 2)
                {
                    release.SetResult();
                }
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
