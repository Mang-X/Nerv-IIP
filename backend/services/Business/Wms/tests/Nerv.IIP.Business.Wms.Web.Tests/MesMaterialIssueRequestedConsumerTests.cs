using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class MesMaterialIssueRequestedConsumerTests
{
    [Fact]
    public async Task Material_issue_requested_consumer_creates_outbound_order_and_picking_task_idempotently()
    {
        var databaseName = $"wms-mes-material-issue-{Guid.CreateVersion7():N}";
        var handler = CreateHandler(databaseName, out _);
        var integrationEvent = CreateRequestedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        // Replay: CAP redelivery must not create a second 出库单 or a duplicate picking task.
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        var order = await assertionContext.OutboundOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        Assert.Equal("MI-MIR-001", order.OutboundOrderNo);
        Assert.Equal(WmsSourceDocumentTypes.MesMaterialIssueRequest, order.SourceDocumentType);
        Assert.Equal("MIR-001", order.SourceDocumentId);
        Assert.Equal("SITE-001", order.SiteCode);
        var line = Assert.Single(order.Lines);
        Assert.Equal("MAT-OIL", line.SkuCode);
        Assert.Equal("L", line.UomCode);
        Assert.Equal(7m, line.RequestedQuantity);
        Assert.Equal("WO-001", line.OwnerId);

        var task = await assertionContext.WarehouseTasks.SingleAsync(CancellationToken.None);
        Assert.Equal(WarehouseTaskType.Picking, task.TaskType);
        Assert.Equal("MI-MIR-001-P1", task.TaskNo);
        Assert.Equal("MI-MIR-001", task.SourceOrderNo);
        Assert.Equal(7m, task.PlannedQuantity);
        Assert.Equal(MesMaterialIssueRequestedIntegrationEventHandler.DefaultLineSideLocationCode, task.ToLocationCode);
    }

    [Fact]
    public async Task Material_issue_requested_consumer_ignores_events_from_other_services()
    {
        var databaseName = $"wms-mes-material-issue-{Guid.CreateVersion7():N}";
        var handler = CreateHandler(databaseName, out _);

        await handler.HandleAsync(CreateRequestedEvent(sourceService: WmsIntegrationEventSources.BusinessErp), CancellationToken.None);

        await using var assertionContext = CreateContext(databaseName);
        Assert.False(await assertionContext.OutboundOrders.AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Material_issue_requested_consumer_dead_letters_unusable_payload_without_throwing()
    {
        var databaseName = $"wms-mes-material-issue-{Guid.CreateVersion7():N}";
        var handler = CreateHandler(databaseName, out var deadLetters);

        var exception = await Record.ExceptionAsync(
            () => handler.HandleAsync(CreateRequestedEvent(quantity: 0m), CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesMaterialIssueRequestedIntegrationEventHandler.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-payload-field", deadLetter.FailureCode);
        await using var assertionContext = CreateContext(databaseName);
        Assert.False(await assertionContext.OutboundOrders.AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Material_issue_requested_consumer_dead_letters_when_the_site_cannot_be_resolved()
    {
        var databaseName = $"wms-mes-material-issue-{Guid.CreateVersion7():N}";
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await using var context = CreateContext(databaseName);
        var handler = new MesMaterialIssueRequestedIntegrationEventHandler(
            context,
            new CommandExecutingSender(databaseName),
            deadLetters);

        // No payload site code and no warehouse facts to derive one from: guessing a site would put the
        // picking work in the wrong warehouse, so the message is parked instead.
        var exception = await Record.ExceptionAsync(
            () => handler.HandleAsync(CreateRequestedEvent(siteCode: null), CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            MesMaterialIssueRequestedIntegrationEventHandler.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unresolved-site", deadLetter.FailureCode);
    }

    [Fact]
    public void Prepared_acknowledgement_converter_carries_the_outbound_document_back_to_mes()
    {
        var order = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "MI-MIR-001",
            WmsSourceDocumentTypes.MesMaterialIssueRequest,
            "MIR-001",
            "SITE-001",
            [new OutboundOrderLineDraft("1", "MAT-OIL", "L", 7m, "WH-WB-RM-01", null, null, "unrestricted", "production", "WO-001")]);
        var preparedAtUtc = DateTimeOffset.Parse("2026-06-15T07:50:00Z");
        order.AnnounceMaterialIssuePrepared("MIR-001", "MI-MIR-001-P1", preparedAtUtc);

        var domainEvent = Assert.Single(order.GetDomainEvents().OfType<MaterialIssueOutboundPreparedDomainEvent>());
        var integrationEvent = new MaterialIssueOutboundPreparedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(WmsIntegrationEventTypes.MaterialIssueOutboundPrepared, integrationEvent.EventType);
        Assert.Equal(WmsIntegrationEventSources.BusinessWms, integrationEvent.SourceService);
        Assert.Equal("MIR-001", integrationEvent.Payload.MaterialIssueRequestNo);
        Assert.Equal("MI-MIR-001", integrationEvent.Payload.OutboundOrderNo);
        Assert.Equal("MI-MIR-001-P1", integrationEvent.Payload.PickingTaskNo);
        Assert.Equal("SITE-001", integrationEvent.Payload.SiteCode);
        Assert.Equal(7m, integrationEvent.Payload.Quantity);
        Assert.Equal("wms:material-issue-outbound-prepared:org-001:env-dev:MIR-001", integrationEvent.IdempotencyKey);
    }

    [Fact]
    public void Prepared_acknowledgement_refuses_a_foreign_material_issue_request()
    {
        var order = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "MI-MIR-001",
            WmsSourceDocumentTypes.MesMaterialIssueRequest,
            "MIR-001",
            "SITE-001",
            [new OutboundOrderLineDraft("1", "MAT-OIL", "L", 7m, "WH-WB-RM-01", null, null, "unrestricted", "production", "WO-001")]);

        Assert.Throws<InvalidOperationException>(
            () => order.AnnounceMaterialIssuePrepared("MIR-999", null, DateTimeOffset.UtcNow));
    }

    private static MesMaterialIssueRequestedIntegrationEventHandler CreateHandler(
        string databaseName,
        out InMemoryIntegrationEventDeadLetterStore deadLetters)
    {
        deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        return new MesMaterialIssueRequestedIntegrationEventHandler(
            CreateContext(databaseName),
            new CommandExecutingSender(databaseName),
            deadLetters);
    }

    private static MesMaterialIssueRequestedIntegrationEvent CreateRequestedEvent(
        string sourceService = MesIntegrationEventSources.BusinessMes,
        decimal quantity = 7m,
        string? siteCode = "SITE-001") =>
        new(
            "evt-material-issue-requested-001",
            MesIntegrationEventTypes.MaterialIssueRequested,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T07:45:00Z"),
            sourceService,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:material-issue-requested:org-001:env-dev:MIR-001",
            new MesMaterialIssueRequestedPayload(
                "MIR-001",
                "WO-001",
                "OP-10",
                "MAT-OIL",
                "L",
                quantity,
                DateTimeOffset.Parse("2026-06-15T07:45:00Z"),
                siteCode));

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
            where TRequest : IRequest =>
            throw new NotSupportedException("This test sender only supports command requests with responses.");

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PrepareMesMaterialIssueOutboundCommand command)
            {
                await using var dbContext = CreateContext(databaseName);
                var result = await new PrepareMesMaterialIssueOutboundCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request?.GetType().FullName}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender only supports typed command requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender does not support streams.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender does not support streams.");
    }
}
