using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using MasterDataDbContext = Nerv.IIP.Business.MasterData.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesMaterialAvailabilityBoundaryAcceptanceTests
{
    [Fact]
    public async Task Mes_material_snapshot_uses_masterdata_uom_conversion_and_inventory_availability_boundaries()
    {
        await using var masterDataDbContext = CreateMasterDataDbContext();
        masterDataDbContext.UomConversions.Add(UomConversion.Create(
            "org-001",
            "env-dev",
            "kg",
            "g",
            1000m,
            0m,
            3,
            "half-up",
            new DateOnly(2026, 1, 1)));
        await masterDataDbContext.SaveChangesAsync(CancellationToken.None);

        await using var inventoryDbContext = CreateInventoryDbContext();
        AddInventoryLedger(inventoryDbContext, "SITE-A", "LOC-A", 5000m);
        AddInventoryLedger(inventoryDbContext, "SITE-B", "LOC-B", 7000m);
        await inventoryDbContext.SaveChangesAsync(CancellationToken.None);

        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(new ProductEngineeringBoundaryHandler()) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(new InventoryAvailabilityBoundaryHandler(inventoryDbContext)) { BaseAddress = new Uri("http://inventory") }),
            new MesMasterDataHttpClient(new HttpClient(new MasterDataResourceBoundaryHandler(masterDataDbContext)) { BaseAddress = new Uri("http://master-data") }),
            new MesMaterialRequirementInventoryOptions { SiteCodes = ["SITE-A", "SITE-B"] });

        var result = await provider.GetSnapshotAsync(
            new MesMaterialRequirementSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "FG-FSA",
                "PV-001",
                10m,
                DateTimeOffset.Parse("2026-06-19T08:00:00Z")),
            CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Equal("MAT-POWDER", line.MaterialId);
        Assert.Equal("kg", line.UomCode);
        Assert.Equal(20m, line.RequiredQuantity);
        Assert.Equal(12m, line.AvailableQuantity);
    }

    private static MasterDataDbContext CreateMasterDataDbContext()
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseInMemoryDatabase($"mes-material-masterdata-{Guid.CreateVersion7():N}")
            .Options;
        return new MasterDataDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"mes-material-inventory-{Guid.CreateVersion7():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private static void AddInventoryLedger(InventoryDbContext dbContext, string siteCode, string locationCode, decimal availableGrams)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "MAT-POWDER",
            "g",
            siteCode,
            locationCode,
            null,
            null,
            "qualified",
            "company",
            null);
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "acceptance",
            $"receipt-{siteCode}",
            null,
            $"idem-{siteCode}",
            "MAT-POWDER",
            "g",
            siteCode,
            locationCode,
            null,
            null,
            "qualified",
            "company",
            null,
            availableGrams));
        dbContext.StockLedgers.Add(ledger);
    }

    private static HttpResponseMessage JsonEnvelope<T>(T data)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, success = true, message = "OK", code = 0 }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
    }

    private sealed class ProductEngineeringBoundaryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonEnvelope(new
                {
                    productionVersionId = "PV-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "FG-FSA",
                    mbomVersionId = "MBOM-1000:A",
                    routingVersionId = "ROUTE-1000:A",
                    effectiveDate = "2026-06-19",
                    lotSize = 10m,
                    status = "active",
                }));
            }

            if (pathAndQuery.StartsWith("/api/business/v1/engineering/manufacturing-boms/MBOM-1000/A?", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonEnvelope(new
                {
                    bomCode = "MBOM-1000",
                    revision = "A",
                    skuCode = "FG-FSA",
                    engineeringBomVersionId = "EBOM-1000:A",
                    status = "Published",
                    effectiveDate = "2026-06-01",
                    materialLines = new object[]
                    {
                        new
                        {
                            skuCode = "MAT-POWDER",
                            quantity = 2m,
                            unitOfMeasureCode = "kg",
                            scrapRate = 0m,
                            isPhantom = false,
                            alternateGroup = (string?)null,
                            alternatePriority = (int?)null,
                            substituteSkuCodes = (string?)null,
                            referenceDesignators = (string?)null,
                            yieldRate = 1m,
                            backflush = false,
                        },
                    },
                    recipeLines = Array.Empty<object>(),
                }));
            }

            throw new InvalidOperationException($"Unexpected ProductEngineering request: {pathAndQuery}");
        }
    }

    private sealed class MasterDataResourceBoundaryHandler(MasterDataDbContext dbContext) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var response = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
                new ListMasterDataResourcesQuery(
                    query["organizationId"].ToString(),
                    query["environmentId"].ToString(),
                    query["resourceType"].ToString(),
                    All: bool.TryParse(query["all"].ToString(), out var all) && all),
                cancellationToken);
            return JsonEnvelope(response);
        }
    }

    private sealed class InventoryAvailabilityBoundaryHandler(InventoryDbContext dbContext) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var response = await new GetStockAvailabilityQueryHandler(dbContext).Handle(
                new GetStockAvailabilityQuery(
                    query["organizationId"].ToString(),
                    query["environmentId"].ToString(),
                    query["skuCode"].ToString(),
                    query["uomCode"].ToString(),
                    query["siteCode"].ToString(),
                    Optional(query, "locationCode"),
                    Optional(query, "lotNo"),
                    Optional(query, "serialNo"),
                    Optional(query, "qualityStatus"),
                    Optional(query, "ownerType"),
                    Optional(query, "ownerId")),
                cancellationToken);
            return JsonEnvelope(response);
        }

        private static string? Optional(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
        {
            return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.ToString())
                ? value.ToString()
                : null;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("Test mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test mediator cannot stream requests.");
    }
}
