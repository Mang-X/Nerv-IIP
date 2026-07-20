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
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using OutboundOrderCancelledIntegrationEventConverter = WmsWeb::Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters.OutboundOrderCancelledIntegrationEventConverter;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WmsOutboundCancelledDeliveryProjectionConsumerTests
{
    [Fact]
    public async Task OutboundOrderCancelledHandler_ProjectsCancellationToErpDeliveryOrderOnce()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-CANCEL-001", "SO-CANCEL-001", "SO-LINE-001", 2m, 80m);
        var integrationEvent = BuildWmsCancelledEvent(delivery, "customer-requested-cancel");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persisted = await dbContext.DeliveryOrders.SingleAsync(x => x.DeliveryOrderNo == "DO-CANCEL-001", CancellationToken.None);
        Assert.Equal("cancelled", persisted.Status);
        Assert.Equal("customer-requested-cancel", persisted.CancellationReason);
        Assert.NotNull(persisted.CancelledAtUtc);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(dbContext.AccountReceivables);
        Assert.Empty(dbContext.JournalVouchers);

        var response = await new ListDeliveryOrdersQueryHandler(dbContext).Handle(
            new ListDeliveryOrdersQuery("org-001", "env-dev", "cancelled", "DO-CANCEL-001"),
            CancellationToken.None);
        var item = Assert.Single(response.Items);
        Assert.Equal("cancelled", item.Status);
        Assert.Empty(await deadLetters.ListAsync(
            WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task OutboundOrderCancelledHandler_DeadLettersWhenDeliveryAlreadyHasAccountReceivable()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-CANCEL-AR-001", "SO-CANCEL-AR-001", "SO-LINE-AR-001", 2m, 80m);
        await new CreateAccountReceivableCommandHandler(dbContext).Handle(
            new CreateAccountReceivableCommand(
                "org-001",
                "env-dev",
                "AR-CANCEL-001",
                delivery.DeliveryOrderNo,
                "CUS-001",
                160m,
                "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var integrationEvent = BuildWmsCancelledEvent(delivery, "customer-requested-cancel");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persisted = await dbContext.DeliveryOrders.SingleAsync(x => x.DeliveryOrderNo == "DO-CANCEL-AR-001", CancellationToken.None);
        Assert.Equal("released", persisted.Status);
        Assert.Null(persisted.CancelledAtUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("delivery-already-accrued", deadLetter.FailureCode);
    }

    [Fact]
    public async Task OutboundOrderCancelledHandler_DeadLettersPartiallyShippedDeliveryWithoutPoisonRetry()
    {
        await using var dbContext = CreateDbContext();
        var delivery = await ReleaseDeliveryOrderAsync(dbContext, "DO-CANCEL-PARTIAL", "SO-CANCEL-PARTIAL", "SO-LINE-PARTIAL", 2m, 80m);
        delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("SO-LINE-PARTIAL", 1m)],
            DateTime.Parse("2026-07-20T02:00:00Z").ToUniversalTime());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var integrationEvent = BuildWmsCancelledEvent(delivery, "late-cancellation");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persisted = await dbContext.DeliveryOrders
            .Include(x => x.Lines)
            .SingleAsync(x => x.DeliveryOrderNo == "DO-CANCEL-PARTIAL", CancellationToken.None);
        Assert.Equal("partially-shipped", persisted.Status);
        Assert.Equal(1m, Assert.Single(persisted.Lines).ShippedQuantity);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("stale-delivery-state", deadLetter.FailureCode);
    }

    private static WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection CreateHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
    {
        return new WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection(
            dbContext,
            deadLetterStore,
            new TestLogger<WmsOutboundOrderCancelledIntegrationEventHandlerForCancelDeliveryProjection>());
    }

    private static async Task<DeliveryOrder> ReleaseDeliveryOrderAsync(
        ApplicationDbContext dbContext,
        string deliveryOrderNo,
        string salesOrderNo,
        string lineNo,
        decimal deliveryQuantity,
        decimal unitPrice)
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            $"Q-{salesOrderNo}",
            "CUS-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            [new QuotationLineDraft(lineNo, "SKU-FG-1000", "kg", 5m, unitPrice, new DateOnly(2026, 7, 1))]);
        quotation.Approve();
        var salesOrder = SalesOrder.CreateFromQuotation(salesOrderNo, "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(
            salesOrder,
            deliveryOrderNo,
            [new DeliveryOrderLineDraft(lineNo, deliveryQuantity, "LOC-A-01", "LOT-001")]);
        dbContext.Quotations.Add(quotation);
        dbContext.SalesOrders.Add(salesOrder);
        dbContext.DeliveryOrders.Add(delivery);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return delivery;
    }

    private static WmsIntegrationEvent BuildWmsCancelledEvent(DeliveryOrder delivery, string reason)
    {
        var outboundRequested = new DeliveryOrderOutboundOrderRequestedIntegrationEventConverter()
            .Convert(new DeliveryOrderReleasedDomainEvent(delivery));
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            outboundRequested.Payload.DeliveryOrderNo,
            "erp-delivery-order",
            outboundRequested.Payload.SalesOrderNo,
            "SITE-01",
            [
                new OutboundOrderLineDraft(
                    outboundRequested.Payload.Lines.Single().SourceLineNo,
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
        outbound.Cancel(reason);
        return new OutboundOrderCancelledIntegrationEventConverter()
            .Convert(new OutboundOrderCancelledDomainEvent(outbound));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-wms-outbound-cancel-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot stream requests.");
    }
}
