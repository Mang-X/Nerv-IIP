using System.Data;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests
{
    private const string ReplayIdentityHeader = "man527-replay-identity";

    [RealPostgresRedisErpWmsDeliveryFact]
    public async Task External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts()
    {
        var postgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION")!;
        var deliveryOrderNo = Environment.GetEnvironmentVariable("NERV_IIP_TEST_DELIVERY_ORDER_NO")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = redis,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests).Assembly));
        services.AddDbContext<ErpDbContext>(options => options.UseNpgsql(
            postgres,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema)));
        services.AddCap(options =>
        {
            options.Version = capVersion;
            options.UseEntityFramework<ErpDbContext>();
            options.UseConfiguredTransport(configuration, "Development");
        });

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);

        WmsIntegrationEvent replay;
        await using (var sourceScope = provider.CreateAsyncScope())
        {
            var dbContext = sourceScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var delivery = await dbContext.DeliveryOrders
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.DeliveryOrderNo == deliveryOrderNo);
            var processed = await dbContext.ProcessedIntegrationEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ConsumerName == WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName
                    && x.EventType == WmsIntegrationEventTypes.OutboundOrderCompleted
                    && x.IdempotencyKey.Contains(deliveryOrderNo));
            var payloadLines = delivery.Lines
                .OrderBy(x => x.SalesOrderLineNo, StringComparer.Ordinal)
                .Select(deliveryLine => new WmsIntegrationPayloadLine(
                    deliveryLine.SalesOrderLineNo,
                    deliveryLine.SkuCode,
                    deliveryLine.UomCode,
                    "SITE-001",
                    deliveryLine.LocationCode,
                    deliveryLine.Quantity,
                    "issued"))
                .ToArray();
            var firstLine = payloadLines[0];
            replay = new WmsIntegrationEvent(
                processed.EventId,
                WmsIntegrationEventTypes.OutboundOrderCompleted,
                WmsIntegrationEventVersions.V1,
                DateTimeOffset.UtcNow,
                WmsIntegrationEventSources.BusinessWms,
                "corr-man527-cross-process",
                $"probe:{deliveryOrderNo}",
                delivery.OrganizationId,
                delivery.EnvironmentId,
                "system:acceptance-probe",
                processed.IdempotencyKey,
                new WmsIntegrationPayload(
                    deliveryOrderNo,
                    firstLine.LineReference,
                    firstLine.SkuCode,
                    firstLine.UomCode,
                    firstLine.SiteCode,
                    firstLine.LocationCode,
                    payloadLines.Sum(x => x.Quantity),
                    "completed",
                    null,
                    null,
                    payloadLines,
                    "erp-delivery-order",
                    deliveryOrderNo));
        }

        var publisher = provider.GetRequiredService<ICapPublisher>();
        var replayRun = Guid.NewGuid().ToString("N");
        string[] replayIdentities = [$"{replayRun}:1", $"{replayRun}:2"];
        foreach (var identity in replayIdentities)
        {
            await publisher.PublishAsync(nameof(WmsIntegrationEvent), replay,
                new Dictionary<string, string?> { [ReplayIdentityHeader] = identity });
        }

        // CAP persists Succeeded only after the target subscriber returns. Both physical replays must
        // complete before unchanged business facts can prove idempotency; a sibling receipt cannot do so.
        var receivedAfterReplay = await Eventually.WaitAsync(
            condition: "ERP target consumer completed both repeated WMS envelopes through the real Redis CAP transport",
            observe: async token => await CountCompletedTargetReplaysAsync(postgres, capVersion, replayIdentities, token),
            isSatisfied: count => count == 2,
            describe: count => $"targetSucceededReplayIdentities={count}; expected=2; capVersion={capVersion}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(45),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [postgres, redis]));
        Assert.Equal(2, receivedAfterReplay);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var persistedDelivery = await verificationDbContext.DeliveryOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == deliveryOrderNo);
        Assert.Equal("completed", persistedDelivery.Status);
        Assert.NotNull(persistedDelivery.ShippedAtUtc);
        Assert.NotNull(persistedDelivery.CompletedAtUtc);
        Assert.All(persistedDelivery.Lines, line => Assert.Equal(line.Quantity, line.ShippedQuantity));
        Assert.Equal(1, await verificationDbContext.AccountReceivables.CountAsync(x => x.SourceDocumentNo == deliveryOrderNo));
        Assert.Equal(1, await verificationDbContext.ProcessedIntegrationEvents.CountAsync(x =>
            x.ConsumerName == WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName
            && x.EventId == replay.EventId));
        Assert.Equal(0, await verificationDbContext.Set<IntegrationEventDeadLetter>().CountAsync(x =>
            x.ConsumerName == WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName
            && x.EventId == replay.EventId));
    }

    private static async Task<int> CountCompletedTargetReplaysAsync(
        string connectionString,
        string capVersion,
        string[] replayIdentities,
        CancellationToken cancellationToken)
    {
        // UseEntityFramework writes CAP transport state to cap.received, not the ERP ORM-mapped tables.
        // DISTINCT prevents redelivery of one identity from standing in for completion of the other.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT COUNT(DISTINCT "Content"::jsonb -> 'Headers' ->> @replay_header)
            FROM cap.received
            WHERE "Group" = @consumer_group
              AND "Version" = @cap_version
              AND "Name" = @event_name
              AND "StatusName" = 'Succeeded'
              AND "Content"::jsonb -> 'Headers' ->> @replay_header = ANY(@replay_identities);
            """;
        command.Parameters.AddWithValue(
            "consumer_group",
            $"{WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName}.{capVersion}");
        command.Parameters.AddWithValue("cap_version", capVersion);
        command.Parameters.AddWithValue("event_name", nameof(WmsIntegrationEvent));
        command.Parameters.AddWithValue("replay_header", ReplayIdentityHeader);
        command.Parameters.AddWithValue("replay_identities", replayIdentities);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}

internal sealed class RealPostgresRedisErpWmsDeliveryFactAttribute : FactAttribute
{
    public RealPostgresRedisErpWmsDeliveryFactAttribute()
    {
        var required = new[]
        {
            "NERV_IIP_TEST_POSTGRES",
            "NERV_IIP_TEST_REDIS",
            "NERV_IIP_TEST_CAP_VERSION",
            "NERV_IIP_TEST_DELIVERY_ORDER_NO",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-527 PostgreSQL, Redis, CAP version, and delivery-order variables to run the external-process replay probe.";
        }
    }
}
