using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class ErpSalesReturnAuthorizedConsumerTests
{
    [Fact]
    public async Task Authorized_rma_creates_one_quality_gated_wms_inbound_and_replay_is_idempotent()
    {
        var root = new InMemoryDatabaseRoot();
        await using var dbContext = CreateContext(root);
        var handler = new ErpSalesReturnAuthorizedIntegrationEventHandler(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = new SalesReturnAuthorizedIntegrationEvent(
            "evt-rma-001",
            ErpIntegrationEventTypes.SalesReturnAuthorized,
            ErpIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            ErpIntegrationEventSources.BusinessErp,
            "corr-rma-001",
            "cause-rma-001",
            "org-001",
            "env-dev",
            "system:erp",
            "sales-return-authorized:org-001:env-dev:RMA-001",
            new SalesReturnAuthorizedPayload(
                "RMA-001",
                "SO-001",
                "CUST-001",
                "SITE-01",
                [new SalesReturnAuthorizedLinePayload("LINE-001", "SKU-001", "EA", 2m, "LOC-RETURN", "LOT-001")]));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var inbound = Assert.Single(await dbContext.InboundOrders.Include(x => x.Lines).ToListAsync());
        Assert.Equal("RMA-001", inbound.InboundOrderNo);
        Assert.Equal("sales-return-rma", inbound.SourceDocumentType);
        var line = Assert.Single(inbound.Lines);
        Assert.Equal("quality", line.QualityStatus);
        Assert.Equal(2m, line.ReceivedQuantity);
    }

    private static ApplicationDbContext CreateContext(InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(ErpSalesReturnAuthorizedConsumerTests), root)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
