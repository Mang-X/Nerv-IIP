extern alias WmsWeb;

using MediatR;
using Microsoft.EntityFrameworkCore;
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
        var salesOrder = SalesOrder.CreateFromQuotation(salesOrderNo, quotation);
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
        var outboundRequested = new DeliveryOrderOutboundOrderRequestedIntegrationEventConverter()
            .Convert(new DeliveryOrderReleasedDomainEvent(delivery));
        return BuildWmsCompletedEvent(
            outboundRequested.Payload.DeliveryOrderNo,
            outboundRequested.Payload.SalesOrderNo,
            outboundRequested.Payload.Lines.OrderBy(x => x.SourceLineNo, StringComparer.Ordinal).First().SourceLineNo);
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
            sourceDocumentId,
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
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-wms-outbound-ar-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed record SalesDeliveryLineSpec(string LineNo, decimal DeliveryQuantity, decimal UnitPrice);

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
