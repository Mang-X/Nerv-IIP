using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialRequirementSnapshotProviderTests
{
    // Contract: ProviderBehavior + Regression. Authority: Issue #2223 review 5036479152.
    // Serial candidate/UOM/site awaits keep peak concurrency at one; removing the shared bound lets it exceed eight.
    [Fact]
    public async Task Http_provider_bounds_parallel_inventory_queries_across_candidates_uoms_and_sites()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler(
            "MAT-PRIMARY",
            "ea",
            "MAT-ALT-A;MAT-ALT-B;MAT-ALT-C;MAT-ALT-D");
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
                    factor = 2m,
                    offset = 0m,
                    precision = 0,
                    roundingMode = "half-up",
                },
            },
            total = 1,
            truncated = false,
            limit = (int?)null,
        }));
        var releaseInventory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoRequestsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inventoryRequests = new List<string>();
        var concurrencyLock = new object();
        var activeRequests = 0;
        var peakConcurrency = 0;
        var inventoryHandler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            lock (concurrencyLock)
            {
                inventoryRequests.Add(pathAndQuery);
                activeRequests++;
                peakConcurrency = Math.Max(peakConcurrency, activeRequests);
                if (activeRequests == 1)
                {
                    firstRequestStarted.TrySetResult();
                }

                if (activeRequests >= 2)
                {
                    twoRequestsStarted.TrySetResult();
                }
            }

            try
            {
                await releaseInventory.Task.WaitAsync(cancellationToken);
                return JsonEnvelope(Availability("candidate", "ea", "production", 1m));
            }
            finally
            {
                lock (concurrencyLock)
                {
                    activeRequests--;
                }
            }
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMasterDataHttpClient(new HttpClient(masterDataHandler) { BaseAddress = new Uri("http://master-data") }),
            new MesMaterialRequirementInventoryOptions { SiteCodes = ["SITE-A", "SITE-B"] });

        var snapshotTask = provider.GetSnapshotAsync(NewSnapshotRequest(), CancellationToken.None);
        try
        {
            await TestTimeout.RunAsync(
                operation: "observe the first Inventory availability request",
                action: token => new ValueTask(firstRequestStarted.Task.WaitAsync(token)),
                timeout: TimeSpan.FromSeconds(5));
            await TestTimeout.RunAsync(
                operation: "observe overlapping Inventory availability requests",
                action: token => new ValueTask(twoRequestsStarted.Task.WaitAsync(token)),
                timeout: TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseInventory.TrySetResult();
        }

        var result = await snapshotTask;

        Assert.InRange(peakConcurrency, 2, 8);
        Assert.Equal(20, inventoryRequests.Count);
        Assert.All(inventoryRequests, pathAndQuery =>
        {
            Assert.Contains("organizationId=org-001", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("environmentId=env-dev", pathAndQuery, StringComparison.Ordinal);
            Assert.True(
                pathAndQuery.Contains("siteCode=SITE-A", StringComparison.Ordinal) ||
                pathAndQuery.Contains("siteCode=SITE-B", StringComparison.Ordinal));
            Assert.True(
                pathAndQuery.Contains("uomCode=ea", StringComparison.Ordinal) ||
                pathAndQuery.Contains("uomCode=box", StringComparison.Ordinal));
        });
        Assert.Equal(30m, Assert.Single(result.Lines).AvailableQuantity);
    }

    // Contract: ProviderBehavior + Regression. Authority: Issue #2223 review 5036805693.
    // Treating one failed candidate/UOM/site branch as zero would publish a partial aggregate as a complete snapshot.
    [Fact]
    public async Task Http_provider_fails_the_whole_snapshot_when_any_inventory_candidate_branch_fails()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler(
            "MAT-PRIMARY",
            "PCS",
            "MAT-ALT-A");
        var inventoryRequests = new List<string>();
        var inventoryHandler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            inventoryRequests.Add(pathAndQuery);
            return pathAndQuery.Contains("skuCode=MAT-ALT-A", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new { message = "candidate unavailable" }),
                }
                : JsonEnvelope(Availability("MAT-PRIMARY", "PCS", "production", 5m));
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetSnapshotAsync(
            NewSnapshotRequest(),
            CancellationToken.None));

        Assert.Contains("MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE", exception.Message);
        Assert.Contains("Inventory", exception.Message);
        Assert.Contains("503", exception.Message);
        Assert.Equal(2, inventoryRequests.Count);
    }

    // Contract: DomainInvariant + Regression. Authority: Issue #2223 acceptance 1-2.
    // Removing substitute queries, counting the primary requirement once per candidate, or skipping candidate normalization makes this test fail.
    [Fact]
    public async Task Http_provider_counts_the_normalized_substitute_pool_without_repeating_the_primary_requirement()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler(
            "MAT-PRIMARY",
            "PCS",
            " MAT-ALT-B ; MAT-PRIMARY ; mat-alt-a ; MAT-ALT-B ; ; ");
        var inventoryRequests = new List<string>();
        var inventoryHandler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            inventoryRequests.Add(pathAndQuery);
            Assert.Contains("organizationId=org-001", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("environmentId=env-dev", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("uomCode=PCS", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("siteCode=production", pathAndQuery, StringComparison.Ordinal);
            return pathAndQuery switch
            {
                var value when value.Contains("skuCode=MAT-PRIMARY", StringComparison.Ordinal) =>
                    JsonEnvelope(Availability("MAT-PRIMARY", "PCS", "production", 3m)),
                var value when value.Contains("skuCode=mat-alt-a", StringComparison.Ordinal) =>
                    JsonEnvelope(Availability("mat-alt-a", "PCS", "production", 4m)),
                var value when value.Contains("skuCode=MAT-ALT-B", StringComparison.Ordinal) =>
                    JsonEnvelope(Availability("MAT-ALT-B", "PCS", "production", 5m)),
                _ => throw new InvalidOperationException($"Unexpected Inventory request: {pathAndQuery}"),
            };
        });
        var provider = new HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://product-engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new MesMaterialRequirementInventoryOptions { DefaultSiteCode = "production" });

        var result = await provider.GetSnapshotAsync(NewSnapshotRequest(), CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Equal(["mat-alt-a", "MAT-ALT-B"], line.SubstituteMaterialIds);
        Assert.Equal(10m, line.RequiredQuantity);
        Assert.Equal(12m, line.AvailableQuantity);
        Assert.Equal(3, inventoryRequests.Count);
        Assert.Single(inventoryRequests, x => x.Contains("skuCode=MAT-PRIMARY", StringComparison.Ordinal));
        Assert.Single(inventoryRequests, x => x.Contains("skuCode=mat-alt-a", StringComparison.Ordinal));
        Assert.Single(inventoryRequests, x => x.Contains("skuCode=MAT-ALT-B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Http_provider_floors_converted_availability_for_material_readiness()
    {
        var productEngineeringHandler = SingleMaterialProductEngineeringHandler("MAT-BOXED", "ea", "MAT-ALT-BOXED");
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
            Assert.Contains("organizationId=org-001", pathAndQuery, StringComparison.Ordinal);
            Assert.Contains("environmentId=env-dev", pathAndQuery, StringComparison.Ordinal);
            if (pathAndQuery.Contains("skuCode=MAT-ALT-BOXED", StringComparison.Ordinal) &&
                pathAndQuery.Contains("uomCode=box", StringComparison.Ordinal))
            {
                return JsonEnvelope(Availability("MAT-ALT-BOXED", "box", "production", 1m));
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
        Assert.Equal(["MAT-ALT-BOXED"], line.SubstituteMaterialIds);
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

    // Contract: DomainInvariant + Regression. Authority: Issue #2246 acceptance 1 and PR #2238 review 5025552710,
    // which confirmed that duplicate MBOM rows must merge every normalized substitute candidate, not only the first row.
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
                            substituteSkuCodes = "mat-alt-a ; MAT-ALT-B ; MAT-OIL",
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
                            substituteSkuCodes = " MAT-ALT-B ; MAT-OIL ; mat-alt-shared ",
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

            if (pathAndQuery.Contains("skuCode=MAT-ALT-B", StringComparison.Ordinal) &&
                pathAndQuery.Contains("uomCode=KG", StringComparison.Ordinal))
            {
                Assert.Contains("siteCode=production", pathAndQuery, StringComparison.Ordinal);
                return JsonEnvelope(Availability("MAT-ALT-B", "KG", 3m));
            }

            if (pathAndQuery.Contains("skuCode=mat-alt-a", StringComparison.Ordinal) ||
                pathAndQuery.Contains("skuCode=MAT-ALT-A", StringComparison.Ordinal) ||
                pathAndQuery.Contains("skuCode=MAT-ALT-B", StringComparison.Ordinal) ||
                pathAndQuery.Contains("skuCode=mat-alt-shared", StringComparison.Ordinal))
            {
                Assert.Contains("siteCode=production", pathAndQuery, StringComparison.Ordinal);
                var uomCode = pathAndQuery.Contains("uomCode=KG", StringComparison.Ordinal) ? "KG" : "L";
                return JsonEnvelope(Availability("candidate", uomCode, 0m));
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
        Assert.Equal(6, inventoryRequests.Count);
        var oil = Assert.Single(result.Lines, x => x.MaterialId == "MAT-OIL");
        Assert.Equal(15m, oil.RequiredQuantity);
        Assert.Equal(12m, oil.AvailableQuantity);
        Assert.Equal(0m, oil.StagedQuantity);
        Assert.Equal("MBOM-1000:A:MAT-OIL", oil.SourceSnapshotId);
        Assert.Equal(["mat-alt-a", "MAT-ALT-B", "mat-alt-shared"], oil.SubstituteMaterialIds);
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
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { data = (object?)null, success = false, message = "No active production version matched.", code = 0 }),
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

    private static StubHttpMessageHandler SingleMaterialProductEngineeringHandler(
        string materialId,
        string uomCode,
        string? substituteSkuCodes = null)
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
                            substituteSkuCodes,
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
            : this((request, _) => Task.FromResult(handle(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        {
            this.handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handle(request, cancellationToken);
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
