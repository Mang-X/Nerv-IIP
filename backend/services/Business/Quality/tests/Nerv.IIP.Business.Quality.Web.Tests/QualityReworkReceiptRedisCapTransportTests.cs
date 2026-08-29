using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class QualityReworkReceiptRedisCapTransportTests
{
    private const string DeploymentProfile = "Issue2809Acceptance";

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Redis_cap_transport_recovers_replayed_receipt_and_preserves_the_first_system_binding_in_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        var ncrId = await AddOpenNcrAsync(factory);
        var receipt = await BuildReceiptAsync(factory, ncrId, "RW-TRANSPORT-001", "receipt-early");

        await PublishAsync(factory, receipt);
        await AssertEventuallyAsync(factory, async (db, deadLetters, token) =>
        {
            Assert.Null((await db.NonconformanceReports.AsNoTracking().SingleAsync(token)).ReworkWorkOrderId);
            Assert.Contains(
                await deadLetters.ListAsync(ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName, null, token),
                message => message.FailureCode == "quality.reworkWorkOrderCreated.bindingConflict");
        });

        await SubmitReworkDispositionAsync(factory, ncrId);
        await PublishAsync(factory, receipt with { EventId = "receipt-replayed" });
        await PublishAsync(factory, receipt with { EventId = "receipt-duplicate" });
        await PublishAsync(factory, receipt with
        {
            EventId = "receipt-conflict",
            Payload = receipt.Payload with { ReworkWorkOrderId = "RW-TRANSPORT-CONFLICT" },
        });
        await PublishAsync(factory, receipt with
        {
            EventId = "receipt-cross-scope",
            OrganizationId = "org-other",
        });

        await AssertEventuallyAsync(factory, async (db, deadLetters, token) =>
        {
            var ncr = await db.NonconformanceReports.AsNoTracking().SingleAsync(token);
            Assert.Equal("RW-TRANSPORT-001", ncr.ReworkWorkOrderId);
            Assert.Equal("created", ncr.ReworkWorkOrderCreationStatus);

            var failures = await deadLetters.ListAsync(
                ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName,
                null,
                token);
            Assert.Contains(failures, message => message.FailureCode == "quality.reworkWorkOrderCreated.bindingConflict");
            Assert.Contains(failures, message => message.FailureCode == "quality.reworkWorkOrderCreated.ncrNotFoundInScope");

            var receivedCount = await db.Database
                .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.received")
                .SingleAsync(token);
            Assert.True(receivedCount >= 5, $"Expected at least five delivered receipts, observed {receivedCount}.");
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

        var candidate = Assert.Single(
            scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates(),
            x => x.ImplTypeInfo.AsType() == typeof(ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr));
        var prefix = scope.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        var expectedTopic = string.IsNullOrWhiteSpace(prefix)
            ? nameof(ReworkWorkOrderCreatedIntegrationEvent)
            : $"{prefix}.{nameof(ReworkWorkOrderCreatedIntegrationEvent)}";
        Assert.Equal(expectedTopic, candidate.TopicName);
    }

    private static async Task<NonconformanceReportId> AddOpenNcrAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-transport",
            "env-transport",
            "NCR-TRANSPORT-001",
            "in-process",
            "DEF-TRANSPORT-001",
            "SKU-TRANSPORT-001",
            3m,
            "surface-defect",
            "LOT-TRANSPORT-001",
            "SN-TRANSPORT-001",
            []);
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        return ncr.Id;
    }

    private static async Task SubmitReworkDispositionAsync(
        WebApplicationFactory<Program> factory,
        NonconformanceReportId ncrId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = await db.NonconformanceReports.SingleAsync(x => x.Id == ncrId);
        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.Rework,
            "approval-chain-transport",
            [],
            [MrbReviewInput.Approve("qa-manager", "approved", DateTimeOffset.Parse("2026-08-29T10:00:00Z"))]);
        await db.SaveChangesAsync();
    }

    private static async Task<ReworkWorkOrderCreatedIntegrationEvent> BuildReceiptAsync(
        WebApplicationFactory<Program> factory,
        NonconformanceReportId ncrId,
        string reworkWorkOrderId,
        string eventId)
    {
        using var scope = factory.Services.CreateScope();
        var ncr = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .NonconformanceReports.AsNoTracking().SingleAsync(x => x.Id == ncrId);
        return new ReworkWorkOrderCreatedIntegrationEvent(
            eventId,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            QualityIntegrationEventSources.BusinessMes,
            "corr-rework-created",
            "evt-rework-requested",
            ncr.OrganizationId,
            ncr.EnvironmentId,
            "system:business-mes",
            $"mes:rework-work-order-created:{ncr.OrganizationId}:{ncr.EnvironmentId}:{ncr.Id}",
            new ReworkWorkOrderCreatedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                reworkWorkOrderId,
                "WO-SOURCE-TRANSPORT",
                "OP-10",
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.BatchNo,
                ncr.SerialNo,
                DateTimeOffset.Parse("2026-08-29T12:00:00Z")));
    }

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(ReworkWorkOrderCreatedIntegrationEvent), integrationEvent);
    }

    private static async Task AssertEventuallyAsync(
        WebApplicationFactory<Program> factory,
        Func<ApplicationDbContext, IIntegrationEventDeadLetterStore, CancellationToken, Task> assertion) =>
        await Eventually.AssertAsync(
            condition: "Quality rework receipt CAP delivery converges in PostgreSQL",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                await assertion(
                    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>(),
                    token);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class QualityReworkReceiptPostgresRedisFactAttribute : FactAttribute
{
    public QualityReworkReceiptPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP Quality rework-receipt proof.";
    }
}
