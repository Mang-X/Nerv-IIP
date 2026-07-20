using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventorySourceLookupTests
{
    [Fact]
    public async Task Exact_mes_source_returns_only_its_movements_and_current_balances_for_the_posted_dimensions()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = Movement("FGR-001", "WO-001", "mes:fgr:1", "receiving", "unrestricted", 8m);
        var retry = Movement("FGR-001", "WO-001", "mes:fgr:retry", "receiving", "unrestricted", 2m);
        var postedLedger = Ledger("receiving", "unrestricted");
        postedLedger.ApplyMovement(first);
        postedLedger.ApplyMovement(retry);

        var restrictedLedger = Ledger("receiving", "restricted");
        var restriction = Movement("IR-001", "10", "quality:restrict:1", "receiving", "restricted", 3m, "business-quality");
        restrictedLedger.ApplyMovement(restriction);

        var unrelated = Movement("FGR-OTHER", "WO-OTHER", "mes:fgr:other", "other-location", "unrestricted", 99m);
        var unrelatedLedger = Ledger("other-location", "unrestricted");
        unrelatedLedger.ApplyMovement(unrelated);

        dbContext.StockMovements.AddRange(first, retry, restriction, unrelated);
        dbContext.StockLedgers.AddRange(postedLedger, restrictedLedger, unrelatedLedger);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new GetStockBySourceQueryHandler(dbContext).Handle(
            new GetStockBySourceQuery("org-001", "env-dev", "business-mes", "FGR-001", "WO-001"),
            CancellationToken.None);

        Assert.True(result.IsEstablished);
        Assert.Equal(new[] { "mes:fgr:1", "mes:fgr:retry" }, result.Movements.Select(x => x.IdempotencyKey).ToArray());
        Assert.All(result.Movements, movement =>
        {
            Assert.Equal("FGR-001", movement.SourceDocumentId);
            Assert.Equal("WO-001", movement.SourceDocumentLineId);
            Assert.Equal("LOT-FG-001", movement.LotNo);
        });
        Assert.Equal(new[] { "restricted", "unrestricted" }, result.Balances.Select(x => x.QualityStatus).Order().ToArray());
        Assert.DoesNotContain(result.Balances, x => x.LocationCode == "other-location");
    }

    [Fact]
    public async Task Missing_exact_source_returns_an_explicit_unestablished_result_without_similar_lot_fallback()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var similar = Movement("FGR-SIMILAR", "WO-001", "mes:fgr:similar", "receiving", "unrestricted", 5m);
        var ledger = Ledger("receiving", "unrestricted");
        ledger.ApplyMovement(similar);
        dbContext.StockMovements.Add(similar);
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new GetStockBySourceQueryHandler(dbContext).Handle(
            new GetStockBySourceQuery("org-001", "env-dev", "business-mes", "FGR-001", "WO-001"),
            CancellationToken.None);

        Assert.False(result.IsEstablished);
        Assert.Empty(result.Movements);
        Assert.Empty(result.Balances);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"inventory-source-link-{Guid.CreateVersion7():N}"));
        return services.BuildServiceProvider();
    }

    private static StockLedger Ledger(string locationCode, string qualityStatus) =>
        StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-001",
            "EA",
            "finished-goods",
            locationCode,
            "LOT-FG-001",
            null,
            qualityStatus,
            "production",
            null);

    private static StockMovement Movement(
        string sourceDocumentId,
        string sourceDocumentLineId,
        string idempotencyKey,
        string locationCode,
        string qualityStatus,
        decimal quantity,
        string sourceService = "business-mes") =>
        StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            idempotencyKey,
            "SKU-FG-001",
            "EA",
            "finished-goods",
            locationCode,
            "LOT-FG-001",
            null,
            qualityStatus,
            "production",
            null,
            quantity);
}
