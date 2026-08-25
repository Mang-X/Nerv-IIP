using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Inventory.Web.Application.Valuation;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryMesCostAuthorityTests
{
    [Fact]
    public async Task Mes_finished_goods_event_uses_authority_cost_instead_of_payload_unit_cost()
    {
        var sender = new CapturingSender();
        var handler = CreateHandler(
            sender,
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Available(12.34m)));

        await handler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None);

        var command = Assert.IsType<PostStockMovementCommand>(sender.Request);
        Assert.Equal(12.34m, command.UnitCost);
        Assert.NotEqual(99.99m, command.UnitCost);
    }

    [Fact]
    public async Task Mes_finished_goods_event_with_missing_authority_stays_pending_without_posting()
    {
        await using var dbContext = CreateContext();
        var sender = new CapturingSender();
        var handler = CreateHandler(
            sender,
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Pending("authority-not-ready")),
            dbContext);

        var observedBefore = DateTimeOffset.UtcNow;
        var pending = await Assert.ThrowsAsync<InventoryUnitCostAuthorityPendingException>(
            () => handler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None));
        var observedAfter = DateTimeOffset.UtcNow;

        Assert.Null(sender.Request);
        Assert.Equal("authority-not-ready", pending.ReasonCode);
        var audit = Assert.Single(dbContext.AuthorityResolutionPendingAudits);
        Assert.Equal("evt-mes-authority-001", audit.EventId);
        Assert.Equal("mes:finished-goods-receipt:org-001:env-dev:FGR-001", audit.IdempotencyKey);
        Assert.Equal("authority-not-ready", audit.ReasonCode);
        Assert.Equal(InventoryAuthorityResolutionPendingAudit.PendingStatus, audit.Status);
        Assert.InRange(audit.ObservedAtUtc, observedBefore, observedAfter);
    }

    [Fact]
    public async Task Ordinary_posting_rejection_does_not_write_pending_audit()
    {
        await using var dbContext = CreateContext();
        var handler = CreateHandler(
            new CapturingSender(),
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Rejected("authority-rejected")),
            dbContext);

        await handler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None);

        Assert.Empty(dbContext.AuthorityResolutionPendingAudits);
    }

    [Fact]
    public async Task Unconfigured_authority_resolver_does_not_write_pending_audit()
    {
        await using var dbContext = CreateContext();
        var handler = CreateHandler(
            new CapturingSender(),
            new UnavailableInventoryUnitCostAuthorityResolver(),
            dbContext);

        await handler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None);

        Assert.Empty(dbContext.AuthorityResolutionPendingAudits);
    }

    [Fact]
    public async Task Unknown_authority_failure_does_not_write_pending_audit()
    {
        await using var dbContext = CreateContext();
        var expected = new InvalidOperationException("authority configuration failure");
        var handler = CreateHandler(new CapturingSender(), new ThrowingAuthorityResolver(expected), dbContext);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Empty(dbContext.AuthorityResolutionPendingAudits);
    }

    [Fact]
    public async Task Same_pending_event_replay_is_idempotent_and_remains_unacknowledged()
    {
        await using var dbContext = CreateContext();
        var handler = CreateHandler(
            new CapturingSender(),
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Pending("authority-not-ready")),
            dbContext);
        var integrationEvent = CreateMesFinishedGoodsEvent(99.99m);

        await Assert.ThrowsAsync<InventoryUnitCostAuthorityPendingException>(
            () => handler.HandleAsync(integrationEvent, CancellationToken.None));
        var audit = Assert.Single(dbContext.AuthorityResolutionPendingAudits);

        await Assert.ThrowsAsync<InventoryUnitCostAuthorityPendingException>(
            () => handler.HandleAsync(integrationEvent, CancellationToken.None));

        Assert.Same(audit, Assert.Single(dbContext.AuthorityResolutionPendingAudits));
    }

    [Fact]
    public async Task Same_event_with_conflicting_identity_fails_closed()
    {
        await using var dbContext = CreateContext();
        var firstHandler = CreateHandler(
            new CapturingSender(),
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Pending("authority-not-ready")),
            dbContext);
        var secondHandler = CreateHandler(
            new CapturingSender(),
            new FixedAuthorityResolver(InventoryUnitCostAuthorityResolution.Pending("authority-timeout")),
            dbContext);

        await Assert.ThrowsAsync<InventoryUnitCostAuthorityPendingException>(
            () => firstHandler.HandleAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None));

        var conflict = await Assert.ThrowsAsync<InventoryAuthorityResolutionPendingAuditConflictException>(
            () => secondHandler.HandleAsync(
                CreateMesFinishedGoodsEvent(
                    99.99m,
                    idempotencyKey: "mes:finished-goods-receipt:org-001:env-dev:FGR-002"),
                CancellationToken.None));

        Assert.Equal("evt-mes-authority-001", conflict.EventId);
        Assert.Equal("mes:finished-goods-receipt:org-001:env-dev:FGR-001", conflict.StoredIdempotencyKey);
        Assert.Equal("authority-not-ready", conflict.StoredReasonCode);
        Assert.Equal("mes:finished-goods-receipt:org-001:env-dev:FGR-002", conflict.ReceivedIdempotencyKey);
        Assert.Equal("authority-timeout", conflict.ReceivedReasonCode);
        Assert.Equal(InventoryAuthorityResolutionPendingAudit.PendingStatus, conflict.ReceivedStatus);
        Assert.Single(dbContext.AuthorityResolutionPendingAudits);
    }

    [Fact]
    public async Task Mes_finished_goods_event_without_authority_marker_does_not_post_payload_unit_cost()
    {
        await using var dbContext = CreateContext();
        var sender = new CapturingSender();
        var handler = CreateHandler(sender, new UnavailableInventoryUnitCostAuthorityResolver(), dbContext);

        await Assert.ThrowsAsync<InventoryUnitCostAuthorityPendingException>(
            () => handler.HandleAsync(
                CreateMesFinishedGoodsEvent(99.99m, authorityReference: null),
                CancellationToken.None));

        Assert.Null(sender.Request);
    }

    [Fact]
    public async Task Http_authority_resolver_forwards_exact_mes_scope_and_reads_erp_cost()
    {
        var httpHandler = new CapturingHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status = "available",
                    capitalizedUnitCost = 12.34m,
                    provenanceEventId = "erp-cost-event-001",
                }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            },
        };
        var resolver = new HttpInventoryUnitCostAuthorityResolver(
            new HttpClient(httpHandler) { BaseAddress = new Uri("http://mes.local") },
            new FixedInternalServiceTokenProvider());

        var result = await resolver.ResolveAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None);

        Assert.Equal(InventoryUnitCostAuthorityStatuses.Available, result.Status);
        Assert.Equal(12.34m, result.UnitCost);
        Assert.Equal("Bearer test-internal-token", httpHandler.Request!.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(httpHandler.RequestBody!);
        Assert.Equal("FGR-001", body.RootElement.GetProperty("receiptRequestNo").GetString());
        Assert.Equal("WO-001", body.RootElement.GetProperty("workOrderId").GetString());
        Assert.Equal(
            "mes:finished-goods-receipt:org-001:env-dev:FGR-001",
            body.RootElement.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task Http_authority_resolver_does_not_treat_response_data_envelope_as_direct_authority()
    {
        var httpHandler = new CapturingHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        status = "available",
                        capitalizedUnitCost = 99.99m,
                        provenanceEventId = "untrusted-envelope-event",
                    },
                    success = true,
                    message = "OK",
                    code = 200,
                }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            },
        };
        var resolver = new HttpInventoryUnitCostAuthorityResolver(
            new HttpClient(httpHandler) { BaseAddress = new Uri("http://mes.local") },
            new FixedInternalServiceTokenProvider());

        var result = await resolver.ResolveAsync(CreateMesFinishedGoodsEvent(99.99m), CancellationToken.None);

        Assert.Equal(InventoryUnitCostAuthorityStatuses.Rejected, result.Status);
        Assert.Equal("authority-rejected", result.ReasonCode);
        Assert.Null(result.UnitCost);
    }

    private static InventoryMovementRequestedIntegrationEventHandlerForPostingMovement CreateHandler(
        CapturingSender sender,
        IInventoryUnitCostAuthorityResolver authorityResolver,
        ApplicationDbContext? dbContext = null)
    {
        return new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher(),
            authorityResolver,
            dbContext);
    }

    private static InventoryMovementRequestedIntegrationEvent CreateMesFinishedGoodsEvent(
        decimal unitCost,
        string? authorityReference = InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
        string eventId = "evt-mes-authority-001",
        string idempotencyKey = "mes:finished-goods-receipt:org-001:env-dev:FGR-001")
    {
        return new InventoryMovementRequestedIntegrationEvent(
            eventId,
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessMes,
            "FGR-001",
            "WO-001",
            "org-001",
            "env-dev",
            "system:mes",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                "FGR-001",
                "WO-001",
                idempotencyKey,
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-001",
                null,
                "Unrestricted",
                "production",
                null,
                5m,
                DateTimeOffset.UtcNow,
                UnitCost: unitCost,
                UnitCostAuthorityReference: authorityReference));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-mes-authority-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FixedAuthorityResolver(InventoryUnitCostAuthorityResolution resolution)
        : IInventoryUnitCostAuthorityResolver
    {
        public Task<InventoryUnitCostAuthorityResolution> ResolveAsync(
            InventoryMovementRequestedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.FromResult(resolution);
    }

    private sealed class ThrowingAuthorityResolver(Exception exception) : IInventoryUnitCostAuthorityResolver
    {
        public Task<InventoryUnitCostAuthorityResolution> ResolveAsync(
            InventoryMovementRequestedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.FromException<InventoryUnitCostAuthorityResolution>(exception);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("Test mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot stream requests.");
    }

    private sealed class CapturingSender : ISender
    {
        public object? Request { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Request = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Request = request as IRequest;
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public HttpResponseMessage Response { get; init; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
