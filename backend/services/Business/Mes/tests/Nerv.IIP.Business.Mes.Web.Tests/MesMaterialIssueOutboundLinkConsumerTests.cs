using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #1324: closes the 领料 loop — the warehouse outbound document must land on the MES request so the
/// console shows a real 出库单 instead of a permanently empty cell.
/// </summary>
public sealed class MesMaterialIssueOutboundLinkConsumerTests
{
    [Fact]
    public async Task Warehouse_acknowledgement_links_the_outbound_document_once()
    {
        var options = CreateOptions();
        await using var dbContext = CreateContext(options);
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-001", "WO-001", "OP-10", "MAT-OIL", "L", 7m, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreatePreparedEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await using var persisted = CreateContext(options);
        var request = await persisted.MaterialIssueRequests.SingleAsync(CancellationToken.None);
        Assert.Equal("MI-MIR-001", request.WmsRequestId);
        Assert.Equal("MI-MIR-001-P1", request.WmsPickingTaskNo);
        Assert.Single(persisted.ProcessedIntegrationEvents);
    }

    [Fact]
    public async Task Acknowledgement_for_an_unknown_request_is_skipped_without_throwing()
    {
        var options = CreateOptions();
        await using var dbContext = CreateContext(options);
        var handler = new WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Record.ExceptionAsync(
            () => handler.HandleAsync(CreatePreparedEvent(requestNo: "MIR-UNKNOWN"), CancellationToken.None));

        Assert.Null(exception);
        await using var persisted = CreateContext(options);
        Assert.False(await persisted.MaterialIssueRequests.AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledgement_without_an_outbound_number_is_dead_lettered()
    {
        var options = CreateOptions();
        await using var dbContext = CreateContext(options);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound(dbContext, deadLetters);

        var exception = await Record.ExceptionAsync(
            () => handler.HandleAsync(CreatePreparedEvent(outboundOrderNo: " "), CancellationToken.None));

        Assert.Null(exception);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-payload-field", deadLetter.FailureCode);
    }

    private static WmsMaterialIssueOutboundPreparedIntegrationEvent CreatePreparedEvent(
        string requestNo = "MIR-001",
        string outboundOrderNo = "MI-MIR-001") =>
        new(
            "evt-material-issue-prepared-001",
            WmsIntegrationEventTypes.MaterialIssueOutboundPrepared,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-31T08:00:00Z"),
            WmsIntegrationEventSources.BusinessWms,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:wms",
            $"wms:material-issue-outbound-prepared:org-001:env-dev:{requestNo}",
            new WmsMaterialIssueOutboundPreparedPayload(
                requestNo,
                outboundOrderNo,
                "MI-MIR-001-P1",
                "SITE-001",
                "MAT-OIL",
                "L",
                7m,
                DateTimeOffset.Parse("2026-07-31T08:00:00Z")));

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-material-issue-link-{Guid.CreateVersion7():N}")
            .Options;

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());
}
