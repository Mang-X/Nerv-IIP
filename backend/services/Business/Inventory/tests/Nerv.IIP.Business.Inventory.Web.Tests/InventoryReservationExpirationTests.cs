using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MediatR;
using Nerv.IIP.Testing;
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
        var timeProvider = CreateReservationClock();
        var registry = Metrics.NewCustomRegistry();
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var expiringReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-METRIC-EXPIRED-001",
            "LINE-001",
            "reservation-metric-expired",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1));
        var validReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-METRIC-VALID-001",
            "LINE-001",
            "reservation-metric-valid",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddHours(1));
        ledger.Reserve(expiringReservation);
        ledger.Reserve(validReservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.AddRange(expiringReservation, validReservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var metrics = new InventoryReservationMetrics(timeProvider, registry);
        await metrics.RefreshHangingReservationsAsync(dbContext, CancellationToken.None);
        var sample = await ExportMetricsAsync(registry);

        Assert.Contains("nerv_iip_inventory_hanging_stock_reservations 1", sample, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reservation_metrics_do_not_share_collectors_or_samples_between_registries()
    {
        var timeProvider = CreateReservationClock();
        var firstRegistry = Metrics.NewCustomRegistry();
        var secondRegistry = Metrics.NewCustomRegistry();
        var firstMetrics = new InventoryReservationMetrics(timeProvider, firstRegistry);
        var secondMetrics = new InventoryReservationMetrics(timeProvider, secondRegistry);
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstLedger = CreateLedger();
        var secondLedger = CreateLedger();
        var firstReservation = StockReservation.Reserve(
            firstLedger,
            "wms",
            "OUT-METRIC-REGISTRY-001",
            "LINE-001",
            "reservation-metric-registry-001",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1));
        var secondReservationOne = StockReservation.Reserve(
            secondLedger,
            "wms",
            "OUT-METRIC-REGISTRY-002",
            "LINE-001",
            "reservation-metric-registry-002",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1));
        var secondReservationTwo = StockReservation.Reserve(
            secondLedger,
            "wms",
            "OUT-METRIC-REGISTRY-002",
            "LINE-002",
            "reservation-metric-registry-003",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1));
        firstLedger.Reserve(firstReservation);
        secondLedger.Reserve(secondReservationOne);
        secondLedger.Reserve(secondReservationTwo);
        firstContext.StockLedgers.Add(firstLedger);
        firstContext.StockReservations.Add(firstReservation);
        secondContext.StockLedgers.Add(secondLedger);
        secondContext.StockReservations.AddRange(secondReservationOne, secondReservationTwo);
        await firstContext.SaveChangesAsync(CancellationToken.None);
        await secondContext.SaveChangesAsync(CancellationToken.None);
        // A reservation cannot be created already expired; advance the shared fake clock past the
        // expiry instead of back-dating it.
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        await firstMetrics.RefreshHangingReservationsAsync(firstContext, CancellationToken.None);
        await secondMetrics.RefreshHangingReservationsAsync(secondContext, CancellationToken.None);
        var firstSamples = GetMetricSamples(await ExportMetricsAsync(firstRegistry), "nerv_iip_inventory_hanging_stock_reservations");
        var secondSamples = GetMetricSamples(await ExportMetricsAsync(secondRegistry), "nerv_iip_inventory_hanging_stock_reservations");

        Assert.Equal("nerv_iip_inventory_hanging_stock_reservations 1", Assert.Single(firstSamples));
        Assert.Equal("nerv_iip_inventory_hanging_stock_reservations 2", Assert.Single(secondSamples));
    }

    [Fact]
    public async Task Expiration_worker_runs_a_second_pass_after_the_configured_scan_interval()
    {
        var timeProvider = CreateReservationClock();
        var options = Options.Create(new StockReservationExpirationOptions
        {
            Enabled = true,
            ScanInterval = TimeSpan.FromMinutes(1),
        });
        await using var dbContext = CreateContext();
        var ledger = CreateLedger();
        var firstPassReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-EXP-WORKER-001",
            "LINE-001",
            "reservation-expiry-worker-first-pass",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddSeconds(30));
        var secondPassReservation = StockReservation.Reserve(
            ledger,
            "wms",
            "OUT-EXP-WORKER-002",
            "LINE-002",
            "reservation-expiry-worker-second-pass",
            1m,
            timeProvider.GetUtcNow().UtcDateTime.AddSeconds(90));
        ledger.Reserve(firstPassReservation);
        ledger.Reserve(secondPassReservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.AddRange(firstPassReservation, secondPassReservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var registry = Metrics.NewCustomRegistry();
        await using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .AddSingleton<IOptions<StockReservationExpirationOptions>>(options)
            .AddScoped<ExpiredStockReservationService>()
            .AddSingleton(new InventoryReservationMetrics(timeProvider, registry))
            .BuildServiceProvider();
        var worker = new ExpiredStockReservationHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<ExpiredStockReservationHostedService>.Instance,
            timeProvider);

        // Both reservations are created in the future because the aggregate forbids a past expiry;
        // the first one becomes due only after this explicit advance.
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Eventually.WaitAsync(
                "Inventory expiration worker completes its first pass",
                async _ => await ExportMetricsAsync(registry),
                exposition => exposition.Contains(
                    "nerv_iip_inventory_stock_reservations_expired_total 1",
                    StringComparison.Ordinal),
                exposition => exposition,
                new EventuallyOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), []));
            Assert.Equal(1m, secondPassReservation.OpenQuantity);

            // Advancing a fake clock is only safe once the timer that must observe the advance actually
            // exists. "The first pass published expired_total 1" is not that fact: the worker registers its
            // PeriodicTimer only after RunOnceAsync returns, so the metric is visible strictly *before* the
            // registration. An advance that lands in that window re-bases the tick on the advanced now,
            // nothing advances the clock again, and the second pass never happens — which is exactly the
            // intermittent CI failure this test showed (every observation stuck at openQuantity=1, i.e. the
            // worker never ran again, rather than ran slowly). Widening the Eventually budget cannot help:
            // the tick is lost permanently. The registration itself is the observable edge, and unlike
            // "which statement comes first in ExecuteAsync" it stays true however the worker is rewritten.
            // Measured once by hand (the experiment is not in the tree; it is also recorded in
            // docs/architecture/backend-test-determinism.md): widening the registration window with a 1.5 s
            // delay in the worker between the first pass and the PeriodicTimer construction fails this test
            // with the metric barrier alone — with the exact CI message, openQuantity=1 after 2 s — and
            // passes it with the barrier below. The first Advance above needs no barrier at all: it happens
            // before StartAsync, when no timer exists yet.
            //
            // The count is this clock's *total* registrations, not "the worker's timer". It pins the right
            // fact only because nothing else in this host registers a timer on this clock: the worker is the
            // sole registrant, InventoryReservationMetrics holds the same TimeProvider but only ever reads
            // GetUtcNow() from it, and Eventually polls on TimeProvider.System. Handing this clock to a
            // second component that owns a timer would let that component's registration satisfy this
            // barrier vacuously — re-derive the count then. That premise is pinned by the exact-count
            // assertion after the second pass below, so breaking it fails deterministically instead of
            // degrading into an intermittent red.
            await timeProvider.WaitForTimerCountAsync(1);
            timeProvider.Advance(TimeSpan.FromMinutes(1));

            await Eventually.WaitAsync(
                "Inventory expiration worker completes its second pass",
                _ => ValueTask.FromResult(secondPassReservation.OpenQuantity),
                openQuantity => openQuantity == 0m,
                openQuantity => $"openQuantity={openQuantity}",
                new EventuallyOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), []));

            Assert.Equal("expired", secondPassReservation.Status);

            // Executable guard for the barrier's premise above: the worker's single PeriodicTimer is this
            // clock's only registrant, so the total is exactly 1 for the whole test. A second registrant on
            // this clock (or a worker loop rewritten to register per iteration) moves this number and fails
            // every run, pointing straight at the broken premise — instead of letting WaitForTimerCountAsync(1)
            // be satisfied vacuously and turning the test intermittently red somewhere else.
            Assert.Equal(1, timeProvider.TimersCreated);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
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

    /// <summary>
    /// StockReservation.Reserve validates "expiration must be in the future" against the process wall
    /// clock, so the fake clock must be anchored to real now — a fixed calendar date would make these
    /// tests pass on the day they were written and throw every day after. Every assertion below is
    /// relative to this anchor and advances the fake clock explicitly, so nothing waits on real time. The
    /// anchored constructor of <see cref="TimerRegistrationObservingTimeProvider"/> carries the general form
    /// of that rule; this clock additionally publishes timer registrations, so a test that advances it while
    /// a worker is running can wait for the edge that makes the advance observable.
    /// </summary>
    private static TimerRegistrationObservingTimeProvider CreateReservationClock() => new(DateTimeOffset.UtcNow);

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

    private static async Task<string> ExportMetricsAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream, CancellationToken.None);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string[] GetMetricSamples(string exposition, string metricName) =>
        exposition.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith($"{metricName} ", StringComparison.Ordinal))
            .ToArray();

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
