using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class OperationLaborSettlementRedisCapTransportTests
{
    [ErpCostPostgresRedisFact]
    public async Task Redis_cap_transport_converges_settle_void_redelivery_and_out_of_order_revisions_in_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:50:00Z");
        var revisionOne = Settled("transport-settle-r1", 1, completedAtUtc, 2 * TimeSpan.TicksPerHour);
        var revisionTwo = Settled("transport-settle-r2", 2, completedAtUtc.AddHours(1), 3 * TimeSpan.TicksPerHour);

        await PublishAsync(factory, publisher =>
            publisher.PublishAsync(nameof(MesOperationActualTimeSettledIntegrationEvent), revisionTwo));
        await AssertEventuallyAsync(factory, async (db, token) =>
        {
            Assert.Equal(2, (await db.OperationLaborSettlementStates.AsNoTracking().SingleAsync(token)).ActiveRevision);
            Assert.Equal(240m, (await db.WorkOrderCosts.Include(x => x.Details).AsNoTracking().SingleAsync(token)).LaborCost);
        });

        var redeliveredCapMessageId = await ForceSameCapMessageRedeliveryAsync(factory, revisionTwo.EventId);

        await PublishAsync(factory, async publisher =>
        {
            await publisher.PublishAsync(
                nameof(MesOperationActualTimeSettlementVoidedIntegrationEvent),
                Voided("transport-void-r1", revisionOne, completedAtUtc.AddHours(2)));
            await publisher.PublishAsync(nameof(MesOperationActualTimeSettledIntegrationEvent), revisionOne);
        });

        await AssertEventuallyAsync(factory, async (db, token) =>
        {
            var state = await db.OperationLaborSettlementStates.AsNoTracking().SingleAsync(token);
            var cost = await db.WorkOrderCosts.Include(x => x.Details).AsNoTracking().SingleAsync(token);
            Assert.Equal(2, state.HighestRevision);
            Assert.Equal(2, state.ActiveRevision);
            Assert.Equal(240m, cost.LaborCost);
            Assert.Equal(2, await db.OperationLaborSettlements.CountAsync(token));
            Assert.Single(await db.OperationLaborSettlementVoids.ToListAsync(token));
            Assert.Single(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation);
            Assert.DoesNotContain(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperationVoid);
            Assert.Single(await db.ProcessedIntegrationEvents.Where(x => x.EventId == revisionTwo.EventId).ToListAsync(token));
            var receivedCount = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.received").SingleAsync(token);
            var publishedCount = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.published").SingleAsync(token);
            Assert.Equal(3, receivedCount);
            Assert.Equal(3, publishedCount);
            var redelivered = await db.Database.SqlQuery<long>($"SELECT \"Id\" AS \"Value\" FROM cap.received WHERE \"Id\" = {redeliveredCapMessageId}").SingleAsync(token);
            Assert.Equal(redeliveredCapMessageId, redelivered);
        });

    }

    private static async Task<long> ForceSameCapMessageRedeliveryAsync(WebApplicationFactory<Program> factory, string eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Database.SqlQuery<long>($"SELECT \"Id\" AS \"Value\" FROM cap.received WHERE \"Content\" LIKE {'%' + eventId + '%'} ORDER BY \"Id\" DESC LIMIT 1").SingleAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE cap.received SET \"StatusName\" = 'Failed', \"Retries\" = 0, \"ExpiresAt\" = NULL WHERE \"Id\" = {id}");
        return id;
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = ErpPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
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
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-transport", "env-transport", "WC-TRANSPORT", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "transport standard labor rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
    }

    private static async Task PublishAsync(WebApplicationFactory<Program> factory, Func<ICapPublisher, Task> publish)
    {
        using var scope = factory.Services.CreateScope();
        await publish(scope.ServiceProvider.GetRequiredService<ICapPublisher>());
    }

    private static async Task AssertEventuallyAsync(
        WebApplicationFactory<Program> factory,
        Func<ApplicationDbContext, CancellationToken, Task> assertion)
        => await Eventually.AssertAsync(
            condition: "ERP operation labor settlement CAP delivery converges in PostgreSQL",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                await assertion(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), token);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

    private static MesOperationActualTimeSettledIntegrationEvent Settled(
        string eventId,
        long revision,
        DateTimeOffset completedAtUtc,
        long actualLaborTicks)
        => new(
            eventId, MesIntegrationEventTypes.OperationActualTimeSettled, 1, completedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes, "correlation-transport", "causation-transport",
            "org-transport", "env-transport", "operator:test",
            $"actual-time:OP-TRANSPORT:{revision}:settled",
            new OperationActualTimeSettledPayload(
                "WO-TRANSPORT", "OP-TRANSPORT", "WC-TRANSPORT", revision, completedAtUtc,
                actualLaborTicks, actualLaborTicks, [$"RPT-TRANSPORT-{revision}"]));

    private static MesOperationActualTimeSettlementVoidedIntegrationEvent Voided(
        string eventId,
        MesOperationActualTimeSettledIntegrationEvent settled,
        DateTimeOffset voidedAtUtc)
        => new(
            eventId, MesIntegrationEventTypes.OperationActualTimeSettlementVoided, 1, voidedAtUtc,
            MesIntegrationEventSources.BusinessMes, settled.CorrelationId, settled.EventId,
            settled.OrganizationId, settled.EnvironmentId, "operator:test",
            $"actual-time:{settled.Payload.OperationTaskId}:{settled.Payload.SettlementRevision}:voided",
            new OperationActualTimeSettlementVoidedPayload(
                settled.Payload.WorkOrderId, settled.Payload.OperationTaskId, settled.Payload.WorkCenterId,
                settled.Payload.SettlementRevision, settled.Payload.CompletedAtUtc, voidedAtUtc,
                settled.Payload.ActualLaborTicks, settled.Payload.ActualMachineTicks,
                settled.Payload.CoveredProductionReportNos));
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ErpCostPostgresRedisFactAttribute : FactAttribute
{
    public ErpCostPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP ERP labor-cost proof.";
    }
}
