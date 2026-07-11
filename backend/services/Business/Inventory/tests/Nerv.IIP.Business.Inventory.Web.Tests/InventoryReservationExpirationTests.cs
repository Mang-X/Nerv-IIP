using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using MediatR;
using Prometheus;
using System.Text;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryReservationExpirationTests
{
    [Fact]
    public void Reservation_expiration_options_keep_wms_and_mes_default_lifetimes_separate()
    {
        var options = new StockReservationExpirationOptions
        {
            WmsDefaultLifetime = TimeSpan.FromHours(2),
            MesDefaultLifetime = TimeSpan.FromHours(8),
        };

        Assert.Equal(TimeSpan.FromHours(2), options.ResolveLifetime("wms"));
        Assert.Equal(TimeSpan.FromHours(2), options.ResolveLifetime(InventoryIntegrationEventSources.BusinessWms));
        Assert.Equal(TimeSpan.FromHours(8), options.ResolveLifetime("mes"));
        Assert.Equal(TimeSpan.FromHours(8), options.ResolveLifetime(InventoryIntegrationEventSources.BusinessMes));
    }

    [Fact]
    public void Reservation_renewal_endpoint_is_an_internal_inventory_contract()
    {
        Assert.Contains(
            InventoryEndpointContracts.All,
            x => x.EndpointType == typeof(RenewStockReservationEndpoint)
                && x.Route == "/api/inventory/v1/reservations/{reservationId}/renew"
                && x.OperationId == "renewInventoryReservation");
    }

    [Fact]
    public async Task Renewed_reservation_is_not_expired_by_a_scan_after_its_original_deadline()
    {
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var originalExpiry = DateTime.UtcNow.AddMinutes(5);
        var reservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-EXP-001",
            "LINE-001",
            "reservation-expiry-renewal",
            4m,
            originalExpiry);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var expirationOptions = Options.Create(new StockReservationExpirationOptions
        {
            WmsDefaultLifetime = TimeSpan.FromHours(2),
            MesDefaultLifetime = TimeSpan.FromHours(8),
        });
        var renewed = await new RenewStockReservationCommandHandler(dbContext, expirationOptions)
            .Handle(new RenewStockReservationCommand(reservation.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var scanner = new ExpiredStockReservationService(dbContext, expirationOptions);
        var expiredCount = await scanner.ExpireOpenReservationsAsync(originalExpiry.AddMinutes(1), CancellationToken.None);

        Assert.Equal(0, expiredCount);
        Assert.True(renewed.ExpiresAtUtc > originalExpiry);
        Assert.Equal(4m, reservation.OpenQuantity);
        Assert.Equal(4m, ledger.ReservedQuantity);
        Assert.Equal(6m, ledger.AvailableQuantity);
    }

    [Fact]
    public async Task Expired_reservation_releases_the_ledger_and_restores_availability()
    {
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(1);
        var reservation = StockReservation.Reserve(
            ledger,
            "business-mes",
            "MIR-EXP-001",
            "MIR-EXP-001",
            "reservation-expiry-release",
            4m,
            expiresAtUtc);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var scanner = new ExpiredStockReservationService(
            dbContext,
            Options.Create(new StockReservationExpirationOptions()));
        var expiredCount = await scanner.ExpireOpenReservationsAsync(expiresAtUtc.AddMinutes(1), CancellationToken.None);

        Assert.Equal(1, expiredCount);
        Assert.Equal(0m, reservation.OpenQuantity);
        Assert.Equal(0m, ledger.ReservedQuantity);
        Assert.Equal(10m, ledger.AvailableQuantity);
        Assert.Equal("expired", reservation.Status);
    }

    [Fact]
    public async Task Expiration_scan_dispatches_the_reservation_expired_domain_event_without_a_caller_save()
    {
        var mediator = new RecordingMediator();
        await using var dbContext = CreateContext(mediator);
        var ledger = CreateLedger();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(1);
        var reservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-EXP-DISPATCH-001",
            "LINE-001",
            "reservation-expiry-dispatch",
            2m,
            expiresAtUtc);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var scanner = new ExpiredStockReservationService(
            dbContext,
            Options.Create(new StockReservationExpirationOptions()));

        await scanner.ExpireOpenReservationsAsync(expiresAtUtc.AddMinutes(1), CancellationToken.None);

        Assert.Contains(mediator.Published, x => x is StockReservationExpiredDomainEvent);
    }

    [Fact]
    public async Task Hanging_reservation_metric_excludes_unexpired_open_reservations()
    {
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var expiringReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-METRIC-EXPIRED-001",
            "LINE-001",
            "reservation-metric-expired",
            1m,
            DateTime.UtcNow.AddMilliseconds(100));
        var validReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-METRIC-VALID-001",
            "LINE-001",
            "reservation-metric-valid",
            1m,
            DateTime.UtcNow.AddHours(1));
        ledger.Reserve(expiringReservation);
        ledger.Reserve(validReservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.AddRange(expiringReservation, validReservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        var metrics = new InventoryReservationMetrics();
        await metrics.RefreshHangingReservationsAsync(dbContext, CancellationToken.None);
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream, CancellationToken.None);
        var sample = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("nerv_iip_inventory_hanging_stock_reservations 1", sample, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_reservation_event_uses_the_public_inventory_contract_with_source_document_identity()
    {
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(1);
        var reservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-EXP-002",
            "LINE-002",
            "reservation-expiry-converter",
            3m,
            expiresAtUtc);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        ledger.ExpireReservation(reservation, expiresAtUtc.AddMinutes(1));
        var domainEvent = Assert.IsType<StockReservationExpiredDomainEvent>(reservation.GetDomainEvents().Last());

        var integrationEvent = new StockReservationExpiredIntegrationEventConverter(
            new StaticInventoryEventContextAccessor())
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.StockReservationExpired, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventSources.BusinessInventory, integrationEvent.SourceService);
        Assert.Equal(reservation.Id.ToString(), integrationEvent.Payload.ReservationId);
        Assert.Equal("wms", integrationEvent.Payload.ReservationSourceService);
        Assert.Equal("OUT-EXP-002", integrationEvent.Payload.SourceDocumentId);
        Assert.Equal("LINE-002", integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal(3m, integrationEvent.Payload.ReleasedQuantity);
    }

    private static StockLedger CreateLedger()
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-EXP-001",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-EXP-001",
            "LINE-001",
            "reservation-expiry-inbound",
            "SKU-EXP-001",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            10m));
        return ledger;
    }

    private static ApplicationDbContext CreateContext(IMediator? mediator = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-reservation-expiry-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, mediator ?? new ReservationExpiryNoopMediator());
    }

    private sealed class ReservationExpiryNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMediator : IMediator
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticInventoryEventContextAccessor : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext() => new("corr-expiry", "cause-expiry", "system:test");
    }
}
