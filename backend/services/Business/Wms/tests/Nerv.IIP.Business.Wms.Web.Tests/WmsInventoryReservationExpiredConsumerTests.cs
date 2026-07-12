using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInventoryReservationExpiredConsumerTests
{
    [Fact]
    public async Task Expired_reservation_cancels_the_open_picking_and_wcs_tasks_and_clears_the_public_reservation_id()
    {
        var options = CreateOptions();
        var mediator = new RecordingMediator();
        await using var dbContext = CreateContext(options, mediator);
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-EXP-001",
            "sales-order",
            "SO-EXP-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        var pickingTask = outbound.CreatePickingTask(
            "TASK-EXP-001",
            "LINE-001",
            "LOC-A-01",
            "PACK-01",
            4m,
            "reservation-expired-001");
        dbContext.OutboundOrders.Add(outbound);
        dbContext.WarehouseTasks.Add(pickingTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var wcsTask = WcsTask.Dispatch(
            "org-001",
            "env-dev",
            pickingTask.Id,
            "agv",
            "WCS-EXP-001",
            """{"mission":"pick"}""");
        dbContext.WcsTasks.Add(wcsTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateExpiredEvent(), CancellationToken.None);
        await using var persistedDbContext = CreateContext(options);
        var persistedOutbound = await persistedDbContext.OutboundOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        var persistedPickingTask = await persistedDbContext.WarehouseTasks.SingleAsync(CancellationToken.None);
        var persistedWcsTask = await persistedDbContext.WcsTasks.SingleAsync(CancellationToken.None);

        Assert.Null(persistedOutbound.Lines.Single().InventoryReservationId);
        Assert.Equal(WarehouseTaskStatus.Cancelled, persistedPickingTask.Status);
        Assert.Equal(WcsTaskStatus.Cancelled, persistedWcsTask.Status);
        Assert.Single(persistedDbContext.ProcessedIntegrationEvents);
        Assert.Contains(mediator.PublishedNotifications, notification => notification is WcsTaskCancelledDomainEvent);
    }

    [Fact]
    public async Task Stale_expiration_event_does_not_cancel_a_picking_task_replaced_by_a_new_reservation()
    {
        var options = CreateOptions();
        await using var dbContext = CreateContext(options);
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-EXP-001",
            "sales-order",
            "SO-EXP-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        var pickingTask = outbound.CreatePickingTask(
            "TASK-EXP-002",
            "LINE-001",
            "LOC-A-01",
            "PACK-01",
            4m,
            "reservation-current-001");
        dbContext.OutboundOrders.Add(outbound);
        dbContext.WarehouseTasks.Add(pickingTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateExpiredEvent("reservation-expired-001"), CancellationToken.None);
        await using var persistedDbContext = CreateContext(options);
        var persistedOutbound = await persistedDbContext.OutboundOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        var persistedPickingTask = await persistedDbContext.WarehouseTasks.SingleAsync(CancellationToken.None);

        Assert.Equal("reservation-current-001", persistedOutbound.Lines.Single().InventoryReservationId);
        Assert.Equal(WarehouseTaskStatus.Open, persistedPickingTask.Status);
        Assert.Single(persistedDbContext.ProcessedIntegrationEvents);
    }

    private static InventoryReservationExpiredIntegrationEvent CreateExpiredEvent(string reservationId = "reservation-expired-001") => new(
        "evt-reservation-expired-001",
        InventoryIntegrationEventTypes.StockReservationExpired,
        InventoryIntegrationEventVersions.V1,
        DateTimeOffset.UtcNow,
        InventoryIntegrationEventSources.BusinessInventory,
        "corr-expiry",
        "cause-expiry",
        "org-001",
        "env-dev",
        "system:business-inventory",
        $"inventory:reservation-expired:{reservationId}",
        new InventoryReservationExpiredPayload(
            reservationId,
            "wms",
            "OUT-EXP-001",
            "LINE-001",
            4m,
            DateTimeOffset.UtcNow));

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-reservation-expired-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static ApplicationDbContext CreateContext(
        DbContextOptions<ApplicationDbContext> options,
        IMediator? mediator = null) =>
        new(options, mediator ?? new NoopMediator());

    private sealed class RecordingMediator : IMediator
    {
        public List<object> PublishedNotifications { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishedNotifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            PublishedNotifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Recording mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("Recording mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Recording mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Recording mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Recording mediator cannot stream requests.");
    }
}
