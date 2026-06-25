using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialRequirementSnapshotProviderTests
{
    [Fact]
    public async Task Http_provider_floors_converted_availability_for_material_readiness()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler("MAT-BOXED", "ea");
        var masterDataHandler = new StubHttpMessageHandler(_ => JsonEnvelope(new
        {
            resources = new[]
            {
                new
                {
                    resourceType = "uom-conversion",
                    code = "box->ea",
                    displayName = "box to ea",
                    active = true,
                    snapshotVersion = "2026-06-01T00:00:00Z",
                    effectiveFrom = "2026-01-01",
                    effectiveTo = (string?)null,
                    fromUomCode = "box",
                    toUomCode = "ea",
                    factor = 2.5m,
                    offset = 0m,
                    precision = 0,
                    roundingMode = "ceiling",
                },
            },
            total = 1,
            truncated = false,
            limit = (int?)null,
        }));
        var inventoryHandler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.Contains("uomCode=box", StringComparison.Ordinal))
            {
                return JsonEnvelope(Availability("MAT-BOXED", "box", "production", 1m));
            }

            return JsonEnvelope(Availability("MAT-BOXED", "ea", "production", 0m));
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMasterDataHttpClient(new HttpClient(masterDataHandler) { BaseAddress = new Uri("http://master-data") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

        var result = await provider.GetSnapshotAsync(NewSnapshotRequest(), CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Equal("ea", line.UomCode);
        Assert.Equal(2m, line.AvailableQuantity);
    }

    [Fact]
    public async Task Http_provider_logs_warning_when_all_inventory_candidates_return_zero()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler("MAT-MISSING", "kg");
        var masterDataHandler = new StubHttpMessageHandler(_ => JsonEnvelope(new
        {
            resources = Array.Empty<object>(),
            total = 0,
            truncated = false,
            limit = (int?)null,
        }));
        var inventoryHandler = new StubHttpMessageHandler(_ => JsonEnvelope(Availability("MAT-MISSING", "kg", "production", 0m)));
        var logger = new RecordingLogger<HttpMesProductEngineeringMaterialRequirementSnapshotProvider>();
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMasterDataHttpClient(new HttpClient(masterDataHandler) { BaseAddress = new Uri("http://master-data") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" },
            logger: logger);

        await provider.GetSnapshotAsync(NewSnapshotRequest(), CancellationToken.None);

        Assert.Contains(logger.Messages, x => x.LogLevel == LogLevel.Warning && x.Message.Contains("returned zero availability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Http_provider_aggregates_inventory_sites_and_converts_available_quantities_to_bom_uom()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                Assert.Contains("effectiveDate=2026-06-19", pathAndQuery, StringComparison.Ordinal);
                Assert.Contains("lotSize=10", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(new
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
                });
            }

            throw new InvalidOperationException($"Unexpected ProductEngineering request: {pathAndQuery}");
        });
        var masterDataHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            Assert.StartsWith("/api/business/v1/master-data/resources?", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("resourceType=uom-conversion", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("all=True", pathAndQuery, StringComparison.Ordinal);
            return JsonEnvelope(new
            {
                resources = new[]
                {
                    new
                    {
                        resourceType = "uom-conversion",
                        code = "kg->g",
                        displayName = "kg to g",
                        active = true,
                        snapshotVersion = "2026-06-01T00:00:00Z",
                        effectiveFrom = "2026-01-01",
                        effectiveTo = (string?)null,
                        fromUomCode = "kg",
                        toUomCode = "g",
                        factor = 1000m,
                        offset = 0m,
                        precision = 3,
                        roundingMode = "half-up",
                    },
                },
                total = 1,
                truncated = false,
                limit = (int?)null,
            });
        });
        var inventoryRequests = new List<string>();
        var inventoryHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            inventoryRequests.Add(pathAndQuery);
            if (pathAndQuery.Contains("skuCode=MAT-POWDER", StringComparison.Ordinal) &&
                pathAndQuery.Contains("uomCode=g", StringComparison.Ordinal) &&
                pathAndQuery.Contains("siteCode=SITE-A", StringComparison.Ordinal))
            {
                return JsonEnvelope(Availability("MAT-POWDER", "g", "SITE-A", 5000m));
            }

            if (pathAndQuery.Contains("skuCode=MAT-POWDER", StringComparison.Ordinal) &&
                pathAndQuery.Contains("uomCode=g", StringComparison.Ordinal) &&
                pathAndQuery.Contains("siteCode=SITE-B", StringComparison.Ordinal))
            {
                return JsonEnvelope(Availability("MAT-POWDER", "g", "SITE-B", 7000m));
            }

            return JsonEnvelope(Availability("MAT-POWDER", "kg", "production", 0m));
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMasterDataHttpClient(new HttpClient(masterDataHandler) { BaseAddress = new Uri("http://master-data") }),
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
        Assert.Equal(20m, line.RequiredQuantity);
        Assert.Equal(12m, line.AvailableQuantity);
        Assert.Contains(inventoryRequests, x => x.Contains("uomCode=g", StringComparison.Ordinal) && x.Contains("siteCode=SITE-A", StringComparison.Ordinal));
        Assert.Contains(inventoryRequests, x => x.Contains("uomCode=g", StringComparison.Ordinal) && x.Contains("siteCode=SITE-B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Http_provider_captures_mbom_requirements_with_inventory_availability()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                Assert.Contains("effectiveDate=2026-06-19", pathAndQuery, StringComparison.Ordinal);
                Assert.Contains("lotSize=10", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(new
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

    [Fact]
    public async Task Http_provider_wraps_downstream_failures_as_known_material_readiness_errors()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { message = "maintenance" }),
            });
        var inventoryHandler = new StubHttpMessageHandler(request =>
            throw new InvalidOperationException($"Inventory should not be called after ProductEngineering fails: {request.RequestUri}"));
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetSnapshotAsync(
            new MesMaterialRequirementSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "FG-FSA",
                "PV-001",
                10m,
                DateTimeOffset.Parse("2026-06-19T08:00:00Z")),
            CancellationToken.None));

        Assert.Contains("MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE", exception.Message);
        Assert.Contains("ProductEngineering", exception.Message);
    }

    [Fact]
    public async Task Http_provider_treats_unresolved_production_version_as_missing_snapshot()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { message = "No active production version matched." }),
            });
        var inventoryHandler = new StubHttpMessageHandler(request =>
            throw new InvalidOperationException($"Inventory should not be called when ProductEngineering has no matching production version: {request.RequestUri}"));
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
                "PV-MISSING",
                10m,
                DateTimeOffset.Parse("2026-06-19T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal(MesMaterialRequirementSnapshotStatus.Missing, result.Status);
        Assert.Equal("product-engineering:production-version:PV-MISSING", result.SourceSystem);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task Http_provider_wraps_http_timeouts_as_known_material_readiness_errors()
    {
        var productEngineeringHandler = new StubHttpMessageHandler(_ =>
            throw new TaskCanceledException("The request timed out."));
        var inventoryHandler = new StubHttpMessageHandler(request =>
            throw new InvalidOperationException($"Inventory should not be called after ProductEngineering times out: {request.RequestUri}"));
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetSnapshotAsync(
            new MesMaterialRequirementSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "FG-FSA",
                "PV-001",
                10m,
                DateTimeOffset.Parse("2026-06-19T08:00:00Z")),
            CancellationToken.None));

        Assert.Contains("MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE", exception.Message);
        Assert.Contains("ProductEngineering", exception.Message);
    }

    private static HttpResponseMessage JsonEnvelope<T>(T data)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { data, success = true, message = "OK", code = 0 }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
    }

    private static object Availability(string skuCode, string uomCode, decimal availableQuantity) =>
        Availability(skuCode, uomCode, "production", availableQuantity);

    private static object Availability(string skuCode, string uomCode, string siteCode, decimal availableQuantity)
    {
        return new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuCode,
            uomCode,
            siteCode,
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

    private static MesMaterialRequirementSnapshotRequest NewSnapshotRequest()
    {
        return new MesMaterialRequirementSnapshotRequest(
            "org-001",
            "env-dev",
            "WO-001",
            "FG-FSA",
            "PV-001",
            10m,
            DateTimeOffset.Parse("2026-06-19T08:00:00Z"));
    }

    private static StubHttpMessageHandler SingleMaterialProductEngineeringHandler(string materialId, string uomCode)
    {
        return new StubHttpMessageHandler(request =>
        {
            Assert.NotNull(request.RequestUri);
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                return JsonEnvelope(new
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
                            skuCode = materialId,
                            quantity = 1m,
                            unitOfMeasureCode = uomCode,
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
                });
            }

            throw new InvalidOperationException($"Unexpected ProductEngineering request: {pathAndQuery}");
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}
