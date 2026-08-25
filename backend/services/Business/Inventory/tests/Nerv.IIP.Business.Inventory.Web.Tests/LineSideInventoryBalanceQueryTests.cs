using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class LineSideInventoryBalanceQueryTests
{
    [Fact]
    public async Task Query_aggregates_only_positive_line_side_stock_and_reports_partial_age()
    {
        await using var db = CreateDbContext();
        db.StockLocations.AddRange(
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "LINE-01", "line-side", "SITE-01", null, "active"),
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "RAW-01", "warehouse", "SITE-01", null, "active"),
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "LINE-SITE-OTHER", "line-side", "SITE-02", null, "active"),
            StockLocation.CreateOrUpdate(null, "org-001", "env-other", "LINE-ENV-OTHER", "line-side", "SITE-01", null, "active"),
            StockLocation.CreateOrUpdate(null, "org-other", "env-dev", "LINE-OTHER", "line-side", "SITE-01", null, "active"));

        AddLedger(db, "org-001", "env-dev", "RM-001", "EA", "SITE-01", "LINE-01", "LOT-OLD", 8m, 2m, new DateOnly(2026, 8, 1));
        AddLedger(db, "org-001", "env-dev", "RM-001", "EA", "SITE-01", "LINE-01", "LOT-UNKNOWN", 4m, 1m, null);
        AddLedger(db, "org-001", "env-dev", "RM-002", "KG", "SITE-01", "LINE-01", "LOT-ZERO", 0m, 0m, new DateOnly(2026, 7, 1));
        AddLedger(db, "org-001", "env-dev", "RM-003", "EA", "SITE-01", "RAW-01", "LOT-WH", 9m, 0m, new DateOnly(2026, 7, 1));
        AddLedger(db, "org-001", "env-dev", "RM-SITE-OTHER", "EA", "SITE-02", "LINE-SITE-OTHER", "LOT-SITE-OTHER", 13m, 0m, new DateOnly(2026, 7, 1));
        AddLedger(db, "org-001", "env-other", "RM-ENV-OTHER", "EA", "SITE-01", "LINE-ENV-OTHER", "LOT-ENV-OTHER", 11m, 0m, new DateOnly(2026, 7, 1));
        AddLedger(db, "org-other", "env-dev", "RM-004", "EA", "SITE-01", "LINE-OTHER", "LOT-OTHER", 7m, 0m, new DateOnly(2026, 7, 1));
        await db.SaveChangesAsync();

        var result = await new ListLineSideInventoryBalancesQueryHandler(db, TimeProvider.System).Handle(
            new ListLineSideInventoryBalancesQuery(
                "org-001",
                "env-dev",
                SiteCode: "SITE-01",
                AsOfDate: new DateOnly(2026, 8, 25)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("RM-001", item.SkuCode);
        Assert.Equal(12m, item.OnHandQuantity);
        Assert.Equal(3m, item.ReservedQuantity);
        Assert.Equal(9m, item.AvailableQuantity);
        Assert.Equal(2, item.LotCount);
        Assert.Equal(new DateOnly(2026, 8, 1), item.OldestProductionDate);
        Assert.Equal(24, item.AgeDays);
        Assert.Equal(LineSideInventoryAgeCompleteness.Partial, item.AgeCompleteness);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(new DateOnly(2026, 8, 25), result.AsOfDate);
    }

    [Fact]
    public async Task Query_distinguishes_complete_and_unavailable_age_without_using_ledger_update_time()
    {
        await using var db = CreateDbContext();
        db.StockLocations.Add(
            StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "LINE-01", "line-side", "SITE-01", null, "active"));
        AddLedger(db, "org-001", "env-dev", "RM-COMPLETE", "EA", "SITE-01", "LINE-01", "LOT-1", 5m, 0m, new DateOnly(2026, 8, 20));
        AddLedger(db, "org-001", "env-dev", "RM-UNKNOWN", "EA", "SITE-01", "LINE-01", "LOT-2", 6m, 0m, null);
        await db.SaveChangesAsync();

        var result = await new ListLineSideInventoryBalancesQueryHandler(db, TimeProvider.System).Handle(
            new ListLineSideInventoryBalancesQuery(
                "org-001",
                "env-dev",
                AsOfDate: new DateOnly(2026, 8, 25)),
            CancellationToken.None);

        var complete = Assert.Single(result.Items, x => x.SkuCode == "RM-COMPLETE");
        Assert.Equal(5, complete.AgeDays);
        Assert.Equal(LineSideInventoryAgeCompleteness.Complete, complete.AgeCompleteness);

        var unavailable = Assert.Single(result.Items, x => x.SkuCode == "RM-UNKNOWN");
        Assert.Null(unavailable.OldestProductionDate);
        Assert.Null(unavailable.AgeDays);
        Assert.Equal(LineSideInventoryAgeCompleteness.Unavailable, unavailable.AgeCompleteness);
    }

    [Fact]
    public async Task Http_endpoint_serializes_authoritative_balance_and_age_fields()
    {
        var databaseName = $"line-side-http-{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                });
            });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.StockLocations.Add(
                StockLocation.CreateOrUpdate(null, "org-001", "env-dev", "LINE-01", "line-side", "SITE-01", null, "active"));
            AddLedger(db, "org-001", "env-dev", "RM-001", "EA", "SITE-01", "LINE-01", "LOT-001", 12m, 3m, new DateOnly(2026, 8, 1));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        using var response = await client.GetAsync(
            "/api/inventory/v1/line-side-balances?organizationId=org-001&environmentId=env-dev&asOfDate=2026-08-25&page=1&pageSize=20");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(12m, item.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(3m, item.GetProperty("reservedQuantity").GetDecimal());
        Assert.Equal(9m, item.GetProperty("availableQuantity").GetDecimal());
        Assert.Equal(24, item.GetProperty("ageDays").GetInt32());
        Assert.Equal("complete", item.GetProperty("ageCompleteness").GetString());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"line-side-balances-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static void AddLedger(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string lotNo,
        decimal onHandQuantity,
        decimal reservedQuantity,
        DateOnly? productionDate)
    {
        var ledger = StockLedger.Create(
            organizationId,
            environmentId,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            lotNo,
            null,
            "unrestricted",
            "company",
            null,
            productionDate);
        if (onHandQuantity > 0m)
        {
            ledger.ApplyMovement(StockMovement.Post(
                organizationId,
                environmentId,
                "inbound",
                "test",
                $"IN-{skuCode}-{lotNo}",
                null,
                $"idem-{skuCode}-{lotNo}",
                skuCode,
                uomCode,
                siteCode,
                locationCode,
                lotNo,
                null,
                "unrestricted",
                "company",
                null,
                onHandQuantity,
                ProductionDate: productionDate));
        }

        if (reservedQuantity > 0m)
        {
            var reservation = StockReservation.Reserve(
                ledger,
                "test",
                $"RES-{skuCode}-{lotNo}",
                null,
                $"reserve-{skuCode}-{lotNo}",
                reservedQuantity);
            ledger.Reserve(reservation);
        }

        db.StockLedgers.Add(ledger);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
