using System.Data;
using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using StackExchange.Redis;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed partial class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
    private const decimal ClientSuppliedUnitCost = 99.99m;
    private const decimal ErpCapitalizedUnitCost = 12.34m;
    private const decimal ReceiptQuantity = 5m;

    [RealPostgresRedisMesInventoryFact]
    public async Task External_process_proves_exact_produced_lot_link_for_success_and_explicit_failure()
    {
        var mesPostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_MES_POSTGRES")!;
        var inventoryPostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_INVENTORY_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION")!;
        var probeRunId = Environment.GetEnvironmentVariable("NERV_IIP_TEST_PROBE_RUN_ID")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = redis,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(MesInventoryProducedLotPostgresRedisAcceptanceTests).Assembly));
        services.AddDbContext<MesDbContext>(options => options.UseNpgsql(
            mesPostgres,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema)));
        services.AddCap(options =>
        {
            options.Version = capVersion;
            options.UseEntityFramework<MesDbContext>();
            options.UseConfiguredTransport(configuration, "Development");
        });

        var source = await SeedReceiptPairAsync(mesPostgres, probeRunId);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
        await WaitForConsumerGroupsAsync(redis, capVersion);
        await SeedProducedLotsAsync(provider, source);
        var receiptBoundary = await CreateReceiptRequestsAsync(provider, source);
        source = receiptBoundary.Source;
        var publisher = provider.GetRequiredService<ICapPublisher>();
        var successEvent = receiptBoundary.SuccessEvent;
        var successReplayEvent = receiptBoundary.SuccessReplayEvent;
        var failureEvent = receiptBoundary.FailureEvent;
        var failureReplayEvent = receiptBoundary.FailureReplayEvent;
        var pendingEvent = receiptBoundary.PendingEvent;
        Assert.NotEqual(successEvent.EventId, successReplayEvent.EventId);
        Assert.Equal(successEvent.IdempotencyKey, successReplayEvent.IdempotencyKey);
        Assert.NotEqual(failureEvent.EventId, failureReplayEvent.EventId);
        Assert.Equal(failureEvent.IdempotencyKey, failureReplayEvent.IdempotencyKey);
        var allEventIds = new[]
        {
            successEvent.EventId,
            successReplayEvent.EventId,
            failureEvent.EventId,
            failureReplayEvent.EventId,
            pendingEvent.EventId,
        };
        Assert.Equal(5, allEventIds.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(ErpCapitalizedUnitCost, source.ClientSuppliedUnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successReplayEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureReplayEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, pendingEvent.Payload.UnitCost);
        Assert.Equal(
            InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            pendingEvent.Payload.UnitCostAuthorityReference);
        Assert.Equal(source.ClientSuppliedUnitCost, receiptBoundary.SuccessCommandUnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, receiptBoundary.PendingCommandUnitCost);
        Assert.Null(receiptBoundary.PendingUnitCost);
        Assert.Equal(0, receiptBoundary.PendingDomainEventCount);
        var pendingRedisBeforePublish = await ReadPendingRedisFactAsync(
            redis,
            capVersion,
            pendingEvent.EventId,
            pendingEvent.IdempotencyKey);
        Assert.Equal(nameof(InventoryMovementRequestedIntegrationEvent), pendingRedisBeforePublish.StreamName);
        Assert.Equal($"business-inventory.movement-requested.{capVersion}", pendingRedisBeforePublish.ConsumerGroup);
        Assert.False(pendingRedisBeforePublish.IsPresent);

        var eventIdempotencyKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        eventIdempotencyKeys.Add(successEvent.EventId, successEvent.IdempotencyKey);
        eventIdempotencyKeys.Add(successReplayEvent.EventId, successReplayEvent.IdempotencyKey);
        eventIdempotencyKeys.Add(failureEvent.EventId, failureEvent.IdempotencyKey);
        eventIdempotencyKeys.Add(failureReplayEvent.EventId, failureReplayEvent.IdempotencyKey);
        eventIdempotencyKeys.Add(pendingEvent.EventId, pendingEvent.IdempotencyKey);
        var inventoryConsumerGroup = $"business-inventory.movement-requested.{capVersion}";
        await using var receivedMessageSnapshot = await InventoryReceivedMessageSnapshot.StartAsync(
            inventoryPostgres,
            capVersion,
            inventoryConsumerGroup,
            eventIdempotencyKeys);
        Assert.Equal(0L, receivedMessageSnapshot.GetReceivedCount(successEvent.EventId));
        Assert.Equal(0L, receivedMessageSnapshot.GetReceivedCount(successReplayEvent.EventId));
        Assert.Equal(0L, receivedMessageSnapshot.GetReceivedCount(failureEvent.EventId));
        Assert.Equal(0L, receivedMessageSnapshot.GetReceivedCount(failureReplayEvent.EventId));
        Assert.Equal(0L, receivedMessageSnapshot.GetReceivedCount(pendingEvent.EventId));
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), successEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), successReplayEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), failureEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), failureReplayEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), pendingEvent);

        var requiredTransportEventIds = eventIdempotencyKeys.Keys.ToHashSet(StringComparer.Ordinal);
        var observedTransportFacts = new Dictionary<string, EventMessageFact>(StringComparer.Ordinal);
        var observedPendingCapReceivedFacts = new Dictionary<string, CapReceivedEventFact>(StringComparer.Ordinal);

        // Real cross-process Redis CAP transport: stream history is the durable published fact. CAP stores
        // the exact received row before enqueueing the subscriber. A successful consumer callback is
        // XACKed, while RejectAsync leaves a failed subscriber entry in the PEL; depending on CAP's
        // execution mode, the pending authority path can therefore have either an exact PEL entry or
        // only its durable CAP received Failed/Retries row. The snapshot latches the exact row identity
        // and latest status.
        try
        {
            var observed = await Eventually.WaitAsync(
                condition: "MES and Inventory closed both Redis CAP produced-lot paths",
                observe: async token => (
                    Mes: await ReadMesFactsAsync(mesPostgres, source, token),
                    Inventory: await ReadInventoryFactsAsync(inventoryPostgres, source, token),
                    Messaging: await ReadMessagingFactsAsync(
                        inventoryPostgres,
                        redis,
                        capVersion,
                        pendingEvent.EventId,
                        pendingEvent.IdempotencyKey,
                        eventIdempotencyKeys,
                        receivedMessageSnapshot,
                        eventIdempotencyKeys.Keys.ToArray())),
                isSatisfied: state => state.Mes.SuccessStatus == "Posted"
                    && state.Mes.SuccessMovementId is not null
                    && state.Mes.FailureStatus == "InventoryPostingFailed"
                    && !string.IsNullOrWhiteSpace(state.Mes.FailureCode)
                    && state.Mes.SuccessUnitCost == ErpCapitalizedUnitCost
                    && state.Mes.ErpCapitalizedUnitCost == ErpCapitalizedUnitCost
                    && state.Mes.PendingStatus == "Requested"
                    && state.Mes.PendingUnitCost is null
                    && state.Mes.PendingErpCapitalizedUnitCost is null
                    && state.Mes.PendingAuthorityProvenanceCount == 0
                    && state.Inventory.SuccessMovementCount == 1
                    && state.Inventory.FailureMovementCount == 0
                    && state.Inventory.PendingMovementCount == 0
                    && state.Inventory.SuccessRequestedUnitCost == ErpCapitalizedUnitCost
                    && state.Inventory.SuccessUnitCost == ErpCapitalizedUnitCost
                    && state.Inventory.SuccessQuantity == ReceiptQuantity
                    && state.Inventory.SuccessMovementAmount == ReceiptQuantity * ErpCapitalizedUnitCost
                    && state.Inventory.LedgerMovingAverageUnitCost == ErpCapitalizedUnitCost
                    && state.Inventory.LedgerInventoryValue == ReceiptQuantity * ErpCapitalizedUnitCost
                    && CaptureObservedTransportFacts(
                        state.Messaging,
                        requiredTransportEventIds,
                        observedTransportFacts)
                    && CaptureObservedPendingCapReceivedFact(
                        state.Messaging.PendingCapReceivedFact,
                        pendingEvent.EventId,
                        inventoryConsumerGroup,
                        observedPendingCapReceivedFacts),
                describe: state =>
                    $"SuccessStatus={state.Mes.SuccessStatus}, SuccessMovement={state.Mes.SuccessMovementId}, " +
                    $"FailureStatus={state.Mes.FailureStatus}, FailureCode={state.Mes.FailureCode}, " +
                    $"InventorySuccess={state.Inventory.SuccessMovementCount}, " +
                    $"InventoryFailure={state.Inventory.FailureMovementCount}, " +
                    $"PendingStatus={state.Mes.PendingStatus}, " +
                    $"PendingInventory={state.Inventory.PendingMovementCount}, " +
                    $"MesUnitCost={state.Mes.SuccessUnitCost}, " +
                    $"RequestedUnitCost={state.Inventory.SuccessRequestedUnitCost}, " +
                    $"EffectiveUnitCost={state.Inventory.SuccessUnitCost}, " +
                    $"MovementQuantity={state.Inventory.SuccessQuantity}, " +
                    $"MovementAmount={state.Inventory.SuccessMovementAmount}, " +
                    $"LedgerMovingAverage={state.Inventory.LedgerMovingAverageUnitCost}, " +
                    $"LedgerValue={state.Inventory.LedgerInventoryValue}",
                options: new EventuallyOptions(
                    Timeout: TimeSpan.FromSeconds(90),
                    PollInterval: TimeSpan.FromMilliseconds(500),
                    SensitiveValues: [mesPostgres, inventoryPostgres, redis]));

            Assert.Equal(source.SuccessLotNo, observed.Inventory.SuccessLotNo);
            Assert.Equal(source.WorkOrderId, observed.Inventory.SuccessSourceDocumentLineId);
            Assert.Equal("business-mes", observed.Inventory.SuccessSourceService);
            Assert.Equal(0, observed.Inventory.SimilarSourceMovementCount);
            Assert.Equal(ErpCapitalizedUnitCost, observed.Mes.SuccessUnitCost);
            Assert.Equal(ErpCapitalizedUnitCost, observed.Inventory.SuccessRequestedUnitCost);
            Assert.Equal(ErpCapitalizedUnitCost, observed.Inventory.SuccessUnitCost);
            Assert.Equal(ReceiptQuantity, observed.Inventory.SuccessQuantity);
            Assert.Equal(ReceiptQuantity * ErpCapitalizedUnitCost, observed.Inventory.SuccessMovementAmount);
            Assert.Equal(ErpCapitalizedUnitCost, observed.Inventory.LedgerMovingAverageUnitCost);
            Assert.Equal(ReceiptQuantity * ErpCapitalizedUnitCost, observed.Inventory.LedgerInventoryValue);
            Assert.Equal("Requested", observed.Mes.PendingStatus);
            Assert.Null(observed.Mes.PendingUnitCost);
            Assert.Null(observed.Mes.PendingErpCapitalizedUnitCost);
            Assert.Equal(0L, observed.Mes.PendingAuthorityProvenanceCount);
            Assert.Equal(0, observed.Inventory.PendingMovementCount);

            var finalInventory = await ReadInventoryFactsAsync(
                inventoryPostgres,
                source,
                CancellationToken.None);
            Assert.Equal(ReceiptQuantity, finalInventory.SuccessQuantity);
            Assert.True(observedTransportFacts.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(requiredTransportEventIds));

            var transport = await ReadMessagingFactsAsync(
                inventoryPostgres,
                redis,
                capVersion,
                pendingEvent.EventId,
                pendingEvent.IdempotencyKey,
                eventIdempotencyKeys,
                receivedMessageSnapshot,
                eventIdempotencyKeys.Keys.ToArray());
            // The bounded latch only proves that every event was observed at least once during
            // processing. Final exact-once evidence must come from a fresh durable/snapshot read,
            // so a later duplicate cannot be hidden by the latch.
            AssertEventTransport(transport.EventFacts, successEvent.EventId);
            AssertEventTransport(transport.EventFacts, successReplayEvent.EventId);
            AssertEventTransport(transport.EventFacts, failureEvent.EventId);
            AssertEventTransport(transport.EventFacts, failureReplayEvent.EventId);
            AssertEventTransport(transport.EventFacts, pendingEvent.EventId);
            // CAP XACKs only after the consumer callback completes successfully. RejectAsync does not ACK,
            // so a failed authority subscriber may retain the exact event in the PEL. If the callback has
            // already ACKed after enqueueing, the PEL may instead be absent. Both shapes are accepted only
            // with their complete fail-closed identity evidence; neither replaces the CAP received proof.
            var pendingRedisFact = transport.PendingEventRedisFact;
            Assert.Equal(nameof(InventoryMovementRequestedIntegrationEvent), pendingRedisFact.StreamName);
            Assert.Equal(inventoryConsumerGroup, pendingRedisFact.ConsumerGroup);
            var pendingReceivedFact = transport.PendingCapReceivedFact;
            Assert.NotNull(pendingReceivedFact);
            Assert.Equal(pendingEvent.EventId, pendingReceivedFact!.EventId);
            Assert.Equal(nameof(InventoryMovementRequestedIntegrationEvent), pendingReceivedFact.Name);
            Assert.Equal(inventoryConsumerGroup, pendingReceivedFact.Group);
            Assert.Equal("Failed", pendingReceivedFact.StatusName);
            Assert.True(pendingReceivedFact.RetryCount >= 1);
            if (pendingRedisFact.IsPresent)
            {
                Assert.Equal(pendingEvent.EventId, pendingRedisFact.EventId);
                Assert.Equal(pendingEvent.IdempotencyKey, pendingRedisFact.IdempotencyKey);
                Assert.False(string.IsNullOrWhiteSpace(pendingRedisFact.StreamEntryId));
                Assert.False(string.IsNullOrWhiteSpace(pendingRedisFact.ConsumerName));
                Assert.True(pendingRedisFact.DeliveryCount >= 1);
                Assert.Equal(pendingReceivedFact.EventId, pendingRedisFact.EventId);
                Assert.Equal(pendingReceivedFact.Group, pendingRedisFact.ConsumerGroup);
            }
            else
            {
                Assert.Null(pendingRedisFact.EventId);
                Assert.Null(pendingRedisFact.IdempotencyKey);
                Assert.Null(pendingRedisFact.StreamEntryId);
                Assert.Null(pendingRedisFact.ConsumerName);
                Assert.Equal(0, pendingRedisFact.DeliveryCount);
            }
            Assert.Equal("Pending", transport.PendingAuthorityStatus);
            Assert.Equal("erp-capitalization-provenance-not-observed", transport.PendingAuthorityReason);
            Assert.Equal(0L, transport.InventoryDeadLetterCount);
        }
        catch (EventuallyTimeoutException timeout)
        {
            // The final read is diagnostic only. CAP may retain or ACK the PEL depending on where the
            // subscriber failure occurred; the final facts preserve both the exact PEL shape and the CAP
            // received status/retry fact for diagnosis.
            var finalMessagingFacts = await ReadMessagingFactsAsync(
                inventoryPostgres,
                redis,
                capVersion,
                pendingEvent.EventId,
                pendingEvent.IdempotencyKey,
                eventIdempotencyKeys,
                receivedMessageSnapshot,
                eventIdempotencyKeys.Keys.ToArray());
            throw new TimeoutException(
                $"{timeout.Message} " +
                $"EventTransport={string.Join(",", finalMessagingFacts.EventFacts.Select(x => $"{x.Key}:published={x.Value.PublishedCount}/received={x.Value.ReceivedCount}"))}, " +
                $"PendingEntry={finalMessagingFacts.PendingEventRedisFact.StreamEntryId}, " +
                $"PendingCapReceived={finalMessagingFacts.PendingCapReceivedFact?.StatusName}/{finalMessagingFacts.PendingCapReceivedFact?.RetryCount}, " +
                $"InventoryDeadLetters={finalMessagingFacts.InventoryDeadLetterCount}.",
                timeout);
        }
    }


}

internal sealed class RealPostgresRedisMesInventoryFactAttribute : FactAttribute
{
    public RealPostgresRedisMesInventoryFactAttribute()
    {
        var required = new[]
        {
            "NERV_IIP_TEST_MES_POSTGRES",
            "NERV_IIP_TEST_INVENTORY_POSTGRES",
            "NERV_IIP_TEST_REDIS",
            "NERV_IIP_TEST_CAP_VERSION",
            "NERV_IIP_TEST_PROBE_RUN_ID",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-528 MES/Inventory PostgreSQL, Redis, CAP version, and probe-run variables to run the external-process success/failure probe.";
        }
    }
}
