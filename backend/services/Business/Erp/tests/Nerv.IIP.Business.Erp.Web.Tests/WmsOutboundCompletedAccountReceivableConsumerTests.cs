extern alias WmsWeb;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using OutboundOrderCompletedIntegrationEventConverter = WmsWeb::Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters.OutboundOrderCompletedIntegrationEventConverter;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WmsOutboundCompletedAccountReceivableConsumerTests
{
    [Fact]
    public async Task OutboundOrderCompletedCapHandler_PersistsProjectionWithoutExternalUnitOfWorkSave()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-CAP", "SO-AR-CAP", "SO-LINE-001", 2m, 80m);
        var handler = CreateHandler(dbContext, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleCapAsync(BuildWmsCompletedEvent(delivery), CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var persistedDelivery = await dbContext.DeliveryOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == "DO-AR-CAP", CancellationToken.None);
        Assert.Equal("completed", persistedDelivery.Status);
        Assert.Equal(2m, Assert.Single(persistedDelivery.Lines).ShippedQuantity);
        Assert.Equal(1, await dbContext.AccountReceivables.CountAsync(CancellationToken.None));
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_CreatesReceivableFromErpDeliveryAndSalesOrderFactsOnce()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-001", "SO-AR-001", "SO-LINE-001", 2m, 80m);
        var completedAtUtc = DateTimeOffset.Parse("2026-07-03T16:30:00Z");
        var integrationEvent = BuildWmsCompletedEvent(delivery) with { OccurredAtUtc = completedAtUtc };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receivable = await new GetAccountReceivableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountReceivableBySourceDocumentQuery("org-001", "env-dev", "DO-AR-001"),
            CancellationToken.None);
        Assert.Equal("DO-AR-001", receivable.SourceDocumentNo);
        Assert.Equal("CUS-001", receivable.CustomerCode);
        Assert.Equal(160m, receivable.Amount);
        Assert.Equal("CNY", receivable.CurrencyCode);
        Assert.Equal(DateOnly.FromDateTime(completedAtUtc.UtcDateTime), dbContext.AccountReceivables.Single().InvoiceDate);
        Assert.Single(dbContext.AccountReceivables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        var persistedDelivery = await dbContext.DeliveryOrders
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == "DO-AR-001", CancellationToken.None);
        Assert.Equal("completed", persistedDelivery.Status);
        Assert.Equal(completedAtUtc.UtcDateTime, persistedDelivery.ShippedAtUtc);
        Assert.Equal(completedAtUtc.UtcDateTime, persistedDelivery.CompletedAtUtc);
        Assert.Equal(2m, Assert.Single(persistedDelivery.Lines).ShippedQuantity);
        var deliveryResponse = await new ListDeliveryOrdersQueryHandler(dbContext).Handle(
            new ListDeliveryOrdersQuery("org-001", "env-dev", "completed", "DO-AR-001"),
            CancellationToken.None);
        var deliveryItem = Assert.Single(deliveryResponse.Items);
        Assert.Equal(completedAtUtc.UtcDateTime, deliveryItem.ShippedAtUtc);
        Assert.Equal(completedAtUtc.UtcDateTime, deliveryItem.CompletedAtUtc);
        Assert.Equal(2m, Assert.Single(deliveryItem.Lines).ShippedQuantity);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_CalculatesReceivableFromAllErpDeliveryLines()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(
            dbContext,
            "DO-AR-MULTI",
            "SO-AR-MULTI",
            [
                new SalesDeliveryLineSpec("SO-LINE-001", 2m, 80m),
                new SalesDeliveryLineSpec("SO-LINE-002", 3m, 12.5m),
            ]);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(BuildWmsCompletedEvent(delivery), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var receivable = await new GetAccountReceivableBySourceDocumentQueryHandler(dbContext).Handle(
            new GetAccountReceivableBySourceDocumentQuery("org-001", "env-dev", "DO-AR-MULTI"),
            CancellationToken.None);
        Assert.Equal(197.5m, receivable.Amount);
        Assert.Single(dbContext.AccountReceivables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_AccumulatesDistinctPartialShipmentsAndAccruesOnlyOnCompletion()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(
            dbContext,
            "DO-AR-PARTIAL",
            "SO-AR-PARTIAL",
            [
                new SalesDeliveryLineSpec("SO-LINE-001", 2m, 80m),
                new SalesDeliveryLineSpec("SO-LINE-002", 2m, 20m),
            ]);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var firstAtUtc = DateTimeOffset.Parse("2026-07-03T16:30:00Z");
        var firstEvent = BuildWmsCompletedEvent(
            delivery,
            "SPLIT-001",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["SO-LINE-001"] = 1m,
                ["SO-LINE-002"] = 0m,
            }) with { OccurredAtUtc = firstAtUtc };

        await handler.HandleAsync(firstEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var partiallyShipped = await dbContext.DeliveryOrders
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == "DO-AR-PARTIAL", CancellationToken.None);
        Assert.Equal("partially-shipped", partiallyShipped.Status);
        Assert.Equal(firstAtUtc.UtcDateTime, partiallyShipped.ShippedAtUtc);
        Assert.Null(partiallyShipped.CompletedAtUtc);
        Assert.Equal([1m, 0m], partiallyShipped.Lines.OrderBy(x => x.SalesOrderLineNo).Select(x => x.ShippedQuantity).ToArray());
        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);

        var completedAtUtc = firstAtUtc.AddMinutes(5);
        var secondEvent = BuildWmsCompletedEvent(
            delivery,
            "SPLIT-002",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["SO-LINE-001"] = 1m,
                ["SO-LINE-002"] = 2m,
            }) with { OccurredAtUtc = completedAtUtc };
        await handler.HandleAsync(secondEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(secondEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var completed = await dbContext.DeliveryOrders
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == "DO-AR-PARTIAL", CancellationToken.None);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(completedAtUtc.UtcDateTime, completed.CompletedAtUtc);
        Assert.All(completed.Lines, line => Assert.Equal(line.Quantity, line.ShippedQuantity));
        Assert.Single(dbContext.AccountReceivables);
        Assert.Single(dbContext.JournalVouchers);
        Assert.Equal(2, dbContext.ProcessedIntegrationEvents.Count());
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersCancelledDeliveryWithoutPoisonRetry()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-CANCELLED", "SO-AR-CANCELLED", "SO-LINE-001", 2m, 80m);
        delivery.Cancel("cancelled-before-delivery", DateTime.Parse("2026-07-20T02:00:00Z").ToUniversalTime());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(BuildWmsCompletedEvent(delivery), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persisted = await dbContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        Assert.Equal("cancelled", persisted.Status);
        Assert.Equal(0m, Assert.Single(persisted.Lines).ShippedQuantity);
        await AssertDeadLetteredWithoutReceivableAsync(dbContext, deadLetters, "stale-delivery-state");
    }

    [Fact]
    public async Task OutboundOrderCompletedCapHandler_RetriesConcurrentDistinctPartialShipmentWithoutLostUpdate()
    {
        var databaseName = $"erp-wms-outbound-ar-concurrency-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseName, databaseRoot);
        DeliveryOrder releasedDelivery;
        await using (var seedContext = new ApplicationDbContext(options, new NoopMediator()))
        {
            releasedDelivery = await ReleaseDeliveryOrderAsync(
                seedContext,
                "DO-AR-CONCURRENT",
                "SO-AR-CONCURRENT",
                "SO-LINE-001",
                3m,
                80m);
        }

        await using (var winnerContext = new ApplicationDbContext(options, new NoopMediator()))
        {
            var winner = await winnerContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
            winner.ApplyShipment(
                [new DeliveryOrderShipmentLine("SO-LINE-001", 1m)],
                DateTime.Parse("2026-07-20T02:01:00Z").ToUniversalTime());
            await winnerContext.SaveChangesAsync(CancellationToken.None);
        }

        var integrationEvent = BuildWmsCompletedEvent(
            releasedDelivery,
            "CONCURRENT-LOSER",
            new Dictionary<string, decimal>(StringComparer.Ordinal) { ["SO-LINE-001"] = 1m });
        var interceptor = new ForceFirstConcurrencyConflictInterceptor();
        var handlerOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(interceptor)
            .Options;
        await using var staleContext = new ApplicationDbContext(handlerOptions, new NoopMediator());
        var handler = CreateHandler(staleContext, new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleCapAsync(integrationEvent, CancellationToken.None);

        await using var assertionContext = new ApplicationDbContext(options, new NoopMediator());
        var persisted = await assertionContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        Assert.Equal("partially-shipped", persisted.Status);
        Assert.Equal(2m, Assert.Single(persisted.Lines).ShippedQuantity);
        Assert.Empty(assertionContext.AccountReceivables);
        Assert.Single(assertionContext.ProcessedIntegrationEvents);
        Assert.Equal(2, interceptor.SaveAttempts);
    }

    [Fact]
    public async Task DeliveryOrderConcurrencyToken_RejectsStaleCumulativeShipmentUpdate()
    {
        var databaseName = $"erp-delivery-concurrency-token-{Guid.CreateVersion7():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseName, databaseRoot);
        await using (var seedContext = new ApplicationDbContext(options, new NoopMediator()))
        {
            await ReleaseDeliveryOrderAsync(seedContext, "DO-CONCURRENCY-TOKEN", "SO-CONCURRENCY-TOKEN", "SO-LINE-001", 2m, 80m);
        }

        await using var staleContext = new ApplicationDbContext(options, new NoopMediator());
        await using var winnerContext = new ApplicationDbContext(options, new NoopMediator());
        var stale = await staleContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        var winner = await winnerContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        stale.ApplyShipment([new DeliveryOrderShipmentLine("SO-LINE-001", 1m)], DateTime.UtcNow);
        winner.ApplyShipment([new DeliveryOrderShipmentLine("SO-LINE-001", 1m)], DateTime.UtcNow);

        await winnerContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersDuplicatePayloadLinesWithoutMutatingDelivery()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-DUP-LINES", "SO-AR-DUP-LINES", "SO-LINE-001", 2m, 80m);
        var baseEvent = BuildWmsCompletedEvent(delivery);
        var line = Assert.Single(baseEvent.Payload.Lines!);
        var integrationEvent = baseEvent with
        {
            Payload = baseEvent.Payload with { Lines = [line with { Quantity = 1m }, line with { Quantity = 1m }] },
        };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persisted = await dbContext.DeliveryOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        Assert.Equal("released", persisted.Status);
        Assert.Equal(0m, Assert.Single(persisted.Lines).ShippedQuantity);
        await AssertDeadLetteredWithoutReceivableAsync(dbContext, deadLetters, "invalid-shipment-lines");
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersLineReferenceMismatchWithoutCreatingReceivable()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-LINE-MISMATCH", "SO-AR-LINE-MISMATCH", "SO-LINE-001", 2m, 80m);
        var integrationEvent = BuildWmsCompletedEvent(delivery) with
        {
            Payload = BuildWmsCompletedEvent(delivery).Payload with { LineReference = "SO-LINE-MISSING" },
        };
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-source-facts", deadLetter.FailureCode);
        Assert.Equal(integrationEvent.IdempotencyKey, deadLetter.IdempotencyKey);
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersUnexpectedSourceServiceWithoutCreatingReceivable()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var integrationEvent = BuildWmsCompletedEvent("DO-AR-UNEXPECTED-SOURCE", "SO-AR-UNEXPECTED-SOURCE", "SO-LINE-001") with
        {
            SourceService = "business-inventory",
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetter = await AssertDeadLetteredWithoutReceivableAsync(dbContext, deadLetters, "unexpected-source-service");
        Assert.Equal("business-inventory", deadLetter.SourceService);
    }

    [Theory]
    [InlineData(WmsIntegrationEventTypes.InboundOrderCompleted)]
    [InlineData(WmsIntegrationEventTypes.CountExecutionCompleted)]
    [InlineData(WmsIntegrationEventTypes.WcsTaskDispatched)]
    [InlineData(WmsIntegrationEventTypes.WcsTaskFailed)]
    [InlineData(WmsIntegrationEventTypes.WcsTaskCompleted)]
    public async Task OutboundOrderCompletedHandler_IgnoresSharedWmsTopicEventsForOtherEventTypesWithoutDeadLetter(
        string eventType)
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var integrationEvent = BuildWmsCompletedEvent("WMS-SHARED-TOPIC", "SO-SHARED-TOPIC", "LINE-001") with
        {
            EventType = eventType,
            IdempotencyKey = $"wms:{eventType}:WMS-SHARED-TOPIC",
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_IgnoresSharedWmsTopicEventWithOtherEventTypeBeforePayloadValidation()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var integrationEvent = BuildWmsCompletedEvent("WMS-SHARED-MISSING-PAYLOAD", "SO-SHARED-MISSING-PAYLOAD", "LINE-001") with
        {
            EventType = WmsIntegrationEventTypes.WcsTaskCompleted,
            Payload = null!
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersUnsupportedOutboundVersionWithoutCreatingReceivable()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var integrationEvent = BuildWmsCompletedEvent("DO-AR-UNSUPPORTED-VERSION", "SO-AR-UNSUPPORTED-VERSION", "SO-LINE-001") with
        {
            EventVersion = WmsIntegrationEventVersions.V1 + 1,
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var deadLetter = await AssertDeadLetteredWithoutReceivableAsync(
            dbContext,
            deadLetters,
            IntegrationEventEnvelopeValidator.UnsupportedVersionFailureCode);
        Assert.Equal(WmsIntegrationEventTypes.OutboundOrderCompleted, deadLetter.EventType);
        Assert.Equal(WmsIntegrationEventVersions.V1 + 1, deadLetter.EventVersion);
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersMissingPublicReferenceWithoutCreatingReceivable()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var baseEvent = BuildWmsCompletedEvent("DO-AR-MISSING-PUBLIC-REFERENCE", "SO-AR-MISSING-PUBLIC-REFERENCE", "SO-LINE-001");
        var integrationEvent = baseEvent with
        {
            Payload = baseEvent.Payload with { PublicReference = string.Empty },
        };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await AssertDeadLetteredWithoutReceivableAsync(dbContext, deadLetters, "missing-payload-field");
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_DeadLettersMissingSalesOrderWithoutCreatingReceivable()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-AR-MISSING-SALES", "SO-AR-MISSING-SALES", "SO-LINE-001", 2m, 80m);
        dbContext.SalesOrders.Remove(await dbContext.SalesOrders.SingleAsync(CancellationToken.None));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(BuildWmsCompletedEvent(delivery), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await AssertDeadLetteredWithoutReceivableAsync(dbContext, deadLetters, "missing-source-facts");
    }

    [Fact]
    public async Task OutboundOrderCompletedHandler_IgnoresUnmatchedOutboundWithoutSideEffects()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var logger = new TestLogger<WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable>();
        var handler = CreateHandler(dbContext, deadLetters, logger);
        var integrationEvent = BuildWmsCompletedEvent("WMS-OUT-UNRELATED", "SO-UNRELATED", "LINE-001");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Contains(logger.Messages, x =>
            x.Level == LogLevel.Debug
            && x.Message.Contains("WMS outbound completion", StringComparison.Ordinal)
            && x.Message.Contains("WMS-OUT-UNRELATED", StringComparison.Ordinal));
    }

    private static WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable CreateHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        ILogger<WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable>? logger = null)
    {
        return new WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable(
            dbContext,
            deadLetterStore,
            new ErpCodingService(),
            logger ?? new TestLogger<WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable>());
    }

    private static async Task<IntegrationEventDeadLetterMessage> AssertDeadLetteredWithoutReceivableAsync(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetters,
        string expectedFailureCode)
    {
        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal(expectedFailureCode, deadLetter.FailureCode);
        return deadLetter;
    }

    private static async Task<DeliveryOrder> ReleaseDeliveryOrderAsync(
        ApplicationDbContext dbContext,
        string deliveryOrderNo,
        string salesOrderNo,
        string lineNo,
        decimal deliveryQuantity,
        decimal unitPrice)
    {
        return await ReleaseDeliveryOrderAsync(
            dbContext,
            deliveryOrderNo,
            salesOrderNo,
            [new SalesDeliveryLineSpec(lineNo, deliveryQuantity, unitPrice)]);
    }

    private static async Task<DeliveryOrder> ReleaseDeliveryOrderAsync(
        ApplicationDbContext dbContext,
        string deliveryOrderNo,
        string salesOrderNo,
        IReadOnlyCollection<SalesDeliveryLineSpec> lines)
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            $"Q-{salesOrderNo}",
            "CUS-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            lines.Select(x => new QuotationLineDraft(x.LineNo, "SKU-FG-1000", "kg", 5m, x.UnitPrice, new DateOnly(2026, 7, 1))).ToArray());
        quotation.Approve();
        var salesOrder = SalesOrder.CreateFromQuotation(salesOrderNo, "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(
            salesOrder,
            deliveryOrderNo,
            lines.Select(x => new DeliveryOrderLineDraft(x.LineNo, x.DeliveryQuantity, "LOC-A-01", "LOT-001")).ToArray());
        dbContext.Quotations.Add(quotation);
        dbContext.SalesOrders.Add(salesOrder);
        dbContext.DeliveryOrders.Add(delivery);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return delivery;
    }

    private static WmsIntegrationEvent BuildWmsCompletedEvent(DeliveryOrder delivery)
    {
        return BuildWmsCompletedEvent(delivery, null, null);
    }

    private static WmsIntegrationEvent BuildWmsCompletedEvent(
        DeliveryOrder delivery,
        string? outboundSuffix,
        IReadOnlyDictionary<string, decimal>? executedQuantitiesByLine)
    {
        var outboundRequested = new DeliveryOrderOutboundOrderRequestedIntegrationEventConverter()
            .Convert(new DeliveryOrderReleasedDomainEvent(delivery));
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            outboundSuffix is null ? delivery.DeliveryOrderNo : $"{delivery.DeliveryOrderNo}-{outboundSuffix}",
            "erp-delivery-order",
            delivery.DeliveryOrderNo,
            "SITE-01",
            outboundRequested.Payload.Lines
                .Select(line => new OutboundOrderLineDraft(
                    line.SourceLineNo,
                    line.SkuCode,
                    line.UomCode,
                    line.Quantity,
                    line.LocationCode,
                    line.LotNo,
                    null,
                    "qualified",
                    "company",
                    outboundRequested.Payload.CustomerCode))
                .ToArray());
        outbound.CompletePackReview(
            "PACK-001",
            true,
            $"pack-review:{outbound.OutboundOrderNo}",
            executedQuantitiesByLine);
        return new OutboundOrderCompletedIntegrationEventConverter()
            .Convert(new OutboundOrderCompletedDomainEvent(outbound));
    }

    private static WmsIntegrationEvent BuildWmsCompletedEvent(
        string outboundOrderNo,
        string sourceDocumentId,
        string lineNo)
    {
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            outboundOrderNo,
            "erp-delivery-order",
            outboundOrderNo,
            "SITE-01",
            [
                new OutboundOrderLineDraft(
                    lineNo,
                    "SKU-FG-1000",
                    "kg",
                    2m,
                    "LOC-A-01",
                    "LOT-001",
                    null,
                    "qualified",
                    "company",
                    "CUS-001")
            ]);
        outbound.CompletePackReview("PACK-001", true, $"pack-review:{outboundOrderNo}:{lineNo}");
        return new OutboundOrderCompletedIntegrationEventConverter()
            .Convert(new OutboundOrderCompletedDomainEvent(outbound));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = CreateOptions(
            $"erp-wms-outbound-ar-{Guid.CreateVersion7():N}",
            new InMemoryDatabaseRoot());
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
    }

    private sealed record SalesDeliveryLineSpec(string LineNo, decimal DeliveryQuantity, decimal UnitPrice);

    private sealed class ForceFirstConcurrencyConflictInterceptor : SaveChangesInterceptor
    {
        public int SaveAttempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (SaveAttempts == 1)
            {
                throw new DbUpdateConcurrencyException("forced delivery projection concurrency conflict");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
