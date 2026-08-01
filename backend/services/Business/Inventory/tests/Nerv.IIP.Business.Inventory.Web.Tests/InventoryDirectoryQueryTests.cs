using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryDirectoryQueryTests
{
    private const string Org = "org-directory";
    private const string Env = "env-directory";

    [Fact]
    public async Task Location_directory_filters_scope_and_keyword_before_paging()
    {
        await using var db = CreateDbContext();
        db.StockLocations.AddRange(
            StockLocation.CreateOrUpdate(null, Org, Env, "LOC-A-01", "bin", "SITE-A", null, "active"),
            StockLocation.CreateOrUpdate(null, Org, Env, "LOC-A-02", "bin", "SITE-A", "ZONE-A", "active"),
            StockLocation.CreateOrUpdate(null, Org, Env, "LOC-B-01", "bin", "SITE-B", null, "active"),
            StockLocation.CreateOrUpdate(null, Org, Env, "LOC-A-OFF", "bin", "SITE-A", null, "disabled"),
            StockLocation.CreateOrUpdate(null, "other-org", Env, "LOC-A-OTHER", "bin", "SITE-A", null, "active"));
        await db.SaveChangesAsync();

        var result = await Handle(db, new ListInventoryDirectoryQuery(
            Org,
            Env,
            InventoryDirectoryTypes.Location,
            SiteCode: "SITE-A",
            Keyword: "loc-a-0",
            Skip: 1,
            Take: 1));

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("LOC-A-02", item.Code);
        Assert.Equal("LOC-A-02", item.Display);
        Assert.Equal("SITE-A", item.SiteCode);
        Assert.Equal("ZONE-A", item.ParentCode);
        Assert.Equal("inventory.stock-locations", result.SourceKind);
    }

    [Fact]
    public async Task Batch_directory_deduplicates_locations_and_requires_positive_current_stock()
    {
        await using var db = CreateDbContext();
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-01", "LOT-001", null, 4m);
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-02", "LOT-001", null, 3m);
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-03", "LOT-ZERO", null, 0m);
        AddLedger(db, "SKU-02", "SITE-A", "LOC-A-04", "LOT-001", null, 2m);
        AddLedger(db, "SKU-01", "SITE-B", "LOC-B-01", "LOT-002", null, 2m);
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-05", "OTHER", null, 2m, organizationId: "other-org");
        await db.SaveChangesAsync();

        var result = await Handle(db, new ListInventoryDirectoryQuery(
            Org,
            Env,
            InventoryDirectoryTypes.Batch,
            SiteCode: "SITE-A",
            SkuCode: "SKU-01",
            Keyword: "lot"));

        var item = Assert.Single(result.Items);
        Assert.Equal("SKU-01:LOT-001", item.Id);
        Assert.Equal("LOT-001", item.Code);
        Assert.Equal("LOT-001 · SKU-01", item.Display);
        Assert.Equal("SKU-01", item.SkuCode);
        Assert.Equal(1, result.Total);
        Assert.Equal("inventory.stock-ledgers", result.SourceKind);
    }

    [Fact]
    public async Task Serial_directory_is_deterministic_and_server_paged()
    {
        await using var db = CreateDbContext();
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-01", "LOT-001", "SN-002", 1m);
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-02", "LOT-001", "SN-001", 1m);
        AddLedger(db, "SKU-01", "SITE-A", "LOC-A-03", "LOT-001", null, 1m);
        await db.SaveChangesAsync();

        var result = await Handle(db, new ListInventoryDirectoryQuery(
            Org,
            Env,
            InventoryDirectoryTypes.Serial,
            SiteCode: "SITE-A",
            Skip: 0,
            Take: 1));

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("SN-001", item.Code);
        Assert.Equal("SKU-01:SN-001", item.Id);
        Assert.Equal(0, result.Skip);
        Assert.Equal(1, result.Take);
    }

    [Fact]
    public async Task Unsupported_directory_type_returns_explicit_unavailable_status()
    {
        await using var db = CreateDbContext();

        var result = await Handle(db, new ListInventoryDirectoryQuery(Org, Env, "unknown"));

        Assert.Equal("unsupported", result.Status);
        Assert.Equal("inventory-directory-type-unsupported", result.ReasonCode);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    private static Task<InventoryDirectoryResponse> Handle(ApplicationDbContext db, ListInventoryDirectoryQuery query) =>
        new ListInventoryDirectoryQueryHandler(db).Handle(query, CancellationToken.None);

    private static void AddLedger(
        ApplicationDbContext db,
        string skuCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        decimal quantity,
        string organizationId = Org)
    {
        var ledger = StockLedger.Create(
            organizationId,
            Env,
            skuCode,
            "piece",
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            "unrestricted",
            "company",
            null);
        if (quantity > 0)
        {
            ledger.ApplyMovement(DomainMovementFactoryForDirectory.Inbound(
                organizationId,
                skuCode,
                siteCode,
                locationCode,
                lotNo,
                serialNo,
                quantity));
        }

        db.StockLedgers.Add(ledger);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-directory-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal static class DomainMovementFactoryForDirectory
{
    public static Domain.AggregatesModel.StockMovementAggregate.StockMovement Inbound(
        string organizationId,
        string skuCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        string? serialNo,
        decimal quantity) =>
        Domain.AggregatesModel.StockMovementAggregate.StockMovement.Post(
            organizationId,
            "env-directory",
            "inbound",
            "directory-test",
            $"DOC-{skuCode}-{locationCode}",
            "1",
            $"IDEM-{skuCode}-{locationCode}-{lotNo}-{serialNo}",
            skuCode,
            "piece",
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            "unrestricted",
            "company",
            null,
            quantity);
}
