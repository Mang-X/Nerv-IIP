using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialRequirementSnapshotProviderTests
{
    [Fact]
    public async Task Http_provider_captures_mbom_requirements_with_inventory_availability()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions?", StringComparison.Ordinal))
            {
                Assert.Contains("status=active", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(new
                {
                    items = new[]
                    {
                        new
                        {
                            productionVersionId = "PV-001",
                            organizationId = "org-001",
                            environmentId = "env-dev",
                            skuCode = "FG-FSA",
                            mbomVersionId = "MBOM-1000:A",
                            routingVersionId = "ROUTE-1000:A",
                            validFrom = "2026-06-01",
                            validTo = (string?)null,
                            lotSizeMin = (decimal?)null,
                            lotSizeMax = (decimal?)null,
                            priority = 10,
                            isDefault = true,
                            status = "active",
                        },
                    },
                    total = 1,
                });
            }

            if (pathAndQuery.StartsWith("/api/business/v1/engineering/manufacturing-boms/MBOM-1000/A?", StringComparison.Ordinal))
            {
                return JsonEnvelope(new
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
                            skuCode = "MAT-OIL",
                            quantity = 1.2m,
                            unitOfMeasureCode = "L",
                            scrapRate = 0m,
                            isPhantom = false,
                            alternateGroup = (string?)null,
                            alternatePriority = (int?)null,
                            substituteSkuCodes = (string?)null,
                            referenceDesignators = (string?)null,
                            yieldRate = 1m,
                            backflush = false,
                        },
                        new
                        {
                            skuCode = "MAT-OIL",
                            quantity = 0.3m,
                            unitOfMeasureCode = "L",
                            scrapRate = 0m,
                            isPhantom = false,
                            alternateGroup = (string?)null,
                            alternatePriority = (int?)null,
                            substituteSkuCodes = (string?)null,
                            referenceDesignators = (string?)null,
                            yieldRate = 1m,
                            backflush = false,
                        },
                        new
                        {
                            skuCode = "MAT-ALT-A",
                            quantity = 5m,
                            unitOfMeasureCode = "KG",
                            scrapRate = 0m,
                            isPhantom = false,
                            alternateGroup = "ALT-1",
                            alternatePriority = 2,
                            substituteSkuCodes = "MAT-ALT-B",
                            referenceDesignators = (string?)null,
                            yieldRate = 1m,
                            backflush = false,
                        },
                        new
                        {
                            skuCode = "MAT-ALT-B",
                            quantity = 2m,
                            unitOfMeasureCode = "KG",
                            scrapRate = 0m,
                            isPhantom = false,
                            alternateGroup = "ALT-1",
                            alternatePriority = 1,
                            substituteSkuCodes = "MAT-ALT-A",
                            referenceDesignators = (string?)null,
                            yieldRate = 1m,
                            backflush = false,
                        },
                    },
                    recipeLines = Array.Empty<object>(),
                });
            }

            throw new InvalidOperationException($"Unexpected ProductEngineering request: {pathAndQuery}");
        });
        var inventoryRequests = new List<string>();
        var inventoryHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            inventoryRequests.Add(pathAndQuery);
            if (pathAndQuery.Contains("skuCode=MAT-OIL", StringComparison.Ordinal))
            {
                Assert.Contains("uomCode=L", pathAndQuery, StringComparison.Ordinal);
                Assert.Contains("siteCode=production", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(Availability("MAT-OIL", "L", 12m));
            }

            if (pathAndQuery.Contains("skuCode=MAT-ALT-B", StringComparison.Ordinal))
            {
                Assert.Contains("uomCode=KG", pathAndQuery, StringComparison.Ordinal);
                Assert.Contains("siteCode=production", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(Availability("MAT-ALT-B", "KG", 3m));
            }

            throw new InvalidOperationException($"Unexpected Inventory request: {pathAndQuery}");
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

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

        Assert.Equal(MesMaterialRequirementSnapshotStatus.Captured, result.Status);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(2, inventoryRequests.Count);
        var oil = Assert.Single(result.Lines, x => x.MaterialId == "MAT-OIL");
        Assert.Equal(15m, oil.RequiredQuantity);
        Assert.Equal(12m, oil.AvailableQuantity);
        Assert.Equal(0m, oil.StagedQuantity);
        Assert.Equal("MBOM-1000:A:MAT-OIL", oil.SourceSnapshotId);
        var alternate = Assert.Single(result.Lines, x => x.MaterialId == "MAT-ALT-B");
        Assert.Equal(20m, alternate.RequiredQuantity);
        Assert.Equal(3m, alternate.AvailableQuantity);
    }

    private static HttpResponseMessage JsonEnvelope<T>(T data)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, success = true, message = "OK", code = 0 }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
    }

    private static object Availability(string skuCode, string uomCode, decimal availableQuantity)
    {
        return new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuCode,
            uomCode,
            siteCode = "production",
            locationCode = (string?)null,
            lotNo = (string?)null,
            serialNo = (string?)null,
            qualityStatus = (string?)null,
            ownerType = (string?)null,
            ownerId = (string?)null,
            onHandQuantity = availableQuantity,
            reservedQuantity = 0m,
            availableQuantity,
            inventoryValue = 0m,
            items = Array.Empty<object>(),
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
