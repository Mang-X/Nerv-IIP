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
using Npgsql;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests
{
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
        int receivedBeforeReplay;
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
            receivedBeforeReplay = await CountSiblingConsumerReceiptsAsync(postgres, processed.EventId);
        }

        var publisher = provider.GetRequiredService<ICapPublisher>();
        await publisher.PublishAsync(nameof(WmsIntegrationEvent), replay);
        await publisher.PublishAsync(nameof(WmsIntegrationEvent), replay);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await CountSiblingConsumerReceiptsAsync(postgres, replay.EventId) >= receivedBeforeReplay + 2)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        var receivedAfterReplay = await CountSiblingConsumerReceiptsAsync(postgres, replay.EventId);
        Assert.True(
            receivedAfterReplay >= receivedBeforeReplay + 2,
            $"ERP did not receive both repeated WMS completion envelopes through the real Redis CAP transport. " +
            $"Sibling-consumer receipt count before={receivedBeforeReplay}, after={receivedAfterReplay}.");

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

    private static async Task<int> CountSiblingConsumerReceiptsAsync(string connectionString, string eventId)
    {
        // Successful Redis CAP deliveries are not retained in cap_received_messages in this profile.
        // The sibling WMS consumer durably rejects this outbound event type once per physical envelope.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT COUNT(*)
            FROM erp.integration_event_dead_letters
            WHERE consumer_name = @consumer_name
              AND event_id = @event_id;
            """;
        command.Parameters.AddWithValue(
            "consumer_name",
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName);
        command.Parameters.AddWithValue("event_id", eventId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
