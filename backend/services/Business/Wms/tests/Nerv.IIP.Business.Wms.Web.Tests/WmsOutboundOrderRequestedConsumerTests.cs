using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsOutboundOrderRequestedConsumerTests
{
    [Fact]
    public async Task Outbound_order_requested_consumer_creates_wms_outbound_order_idempotently()
    {
        var databaseName = $"wms-outbound-order-requested-{Guid.NewGuid():N}";
        var handler = new WmsOutboundOrderRequestedIntegrationEventHandler(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreateRequestedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var order = await assertionContext.OutboundOrders
            .Include(x => x.Lines)
            .SingleAsync(CancellationToken.None);
        var line = Assert.Single(order.Lines);
        Assert.Equal("DO-001", order.OutboundOrderNo);
        Assert.Equal("erp-delivery-order", order.SourceDocumentType);
        Assert.Equal("SO-001", order.SourceDocumentId);
        Assert.Equal("SITE-01", order.SiteCode);
        Assert.Equal("SO-LINE-001", line.LineNo);
        Assert.Equal("SKU-FG-1000", line.SkuCode);
        Assert.Equal("kg", line.UomCode);
        Assert.Equal(4m, line.RequestedQuantity);
        Assert.Equal("LOC-A-01", line.PickLocationCode);
        Assert.Equal("LOT-001", line.LotNo);
        Assert.Equal("customer-001", line.OwnerId);
    }

    [Fact]
    public async Task Outbound_order_requested_consumer_ignores_non_erp_sources()
    {
        var databaseName = $"wms-outbound-order-requested-{Guid.NewGuid():N}";
        var handler = new WmsOutboundOrderRequestedIntegrationEventHandler(
            new CommandExecutingSender(databaseName),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateRequestedEvent(sourceService: WmsIntegrationEventSources.BusinessWms), CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        Assert.False(await assertionContext.OutboundOrders.AnyAsync(CancellationToken.None));
    }

    private static WmsOutboundOrderRequestedIntegrationEvent CreateRequestedEvent(string sourceService = WmsIntegrationEventSources.BusinessErp)
    {
        return new WmsOutboundOrderRequestedIntegrationEvent(
            "evt-outbound-requested-001",
            WmsIntegrationEventTypes.OutboundOrderRequested,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            sourceService,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:erp",
            "delivery-order-wms-outbound-requested:org-001:env-dev:DO-001",
            new WmsOutboundOrderRequestedPayload(
                "DO-001",
                "SO-001",
                "customer-001",
                "SITE-01",
                [
                    new WmsOutboundOrderRequestedLine(
                        "SO-LINE-001",
                        "SKU-FG-1000",
                        "kg",
                        "LOC-A-01",
                        "LOT-001",
                        4m)
                ]));
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CommandExecutingSender(string databaseName) : ISender
    {
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test sender only supports command requests with responses.");
        }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateOutboundOrderCommand command)
            {
                await using var dbContext = CreateContext(databaseName);
                var id = await new CreateOutboundOrderCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)id;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request?.GetType().FullName}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports typed command requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }
}
