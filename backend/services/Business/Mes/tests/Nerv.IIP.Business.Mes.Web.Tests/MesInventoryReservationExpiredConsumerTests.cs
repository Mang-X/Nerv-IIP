using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesInventoryReservationExpiredConsumerTests
{
    [Fact]
    public async Task Expired_mes_reservation_marks_the_matching_material_issue_request_expired_once()
    {
        var options = CreateOptions();
        await using var dbContext = CreateContext(options);
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-EXP-001",
            "WO-EXP-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            DateTimeOffset.UtcNow);
        dbContext.MaterialIssueRequests.Add(materialRequest);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new InventoryReservationExpiredIntegrationEventHandlerForMarkMesRequestExpired(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = new InventoryReservationExpiredIntegrationEvent(
            "evt-mes-reservation-expired-001",
            InventoryIntegrationEventTypes.StockReservationExpired,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-expiry",
            "cause-expiry",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:reservation-expired:mes-001",
            new InventoryReservationExpiredPayload(
                "reservation-mes-001",
                InventoryIntegrationEventSources.BusinessMes,
                "MIR-EXP-001",
                "MIR-EXP-001",
                5m,
                DateTimeOffset.UtcNow));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await using var persistedDbContext = CreateContext(options);
        var persistedMaterialRequest = await persistedDbContext.MaterialIssueRequests.SingleAsync(CancellationToken.None);

        Assert.Equal(MaterialIssueRequest.ReservationExpiredStatus, persistedMaterialRequest.Status);
        Assert.Single(persistedDbContext.ProcessedIntegrationEvents);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-reservation-expired-{Guid.NewGuid():N}")
            .Options;
    }

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());
}
