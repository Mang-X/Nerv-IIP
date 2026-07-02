using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class PlanningInputAdapterTests
{
    [Fact]
    public async Task Fixture_adapter_returns_snapshots_without_cross_service_table_access()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await new DemandPlanningFixtureInputSnapshotProvider(dbContext).GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("fixture-production-engineering-snapshot", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("fixture-inventory-availability-snapshot", snapshot.InventorySnapshotSource);
        Assert.Single(snapshot.Demands);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("ProductEngineering", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("Inventory", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Upstream_adapter_uses_product_engineering_and_inventory_snapshots_for_mrp_inputs()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var engineering = new FakePlanningProductEngineeringClient();
        var inventory = new FakePlanningInventoryClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(dbContext, engineering, inventory);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("product-engineering-http:2", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:none", snapshot.InventorySnapshotSource);
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000" && x.ProductionVersionReference == "PV-REAL-001");
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000" && x.LotSizeMin == 10m && x.LotSizeMax == 50m);
        Assert.Contains(snapshot.BomComponents, x => x.ParentSkuCode == "SKU-FG-1000" && x.ComponentSkuCode == "SKU-RM-1000" && x.QuantityPerParent == 3m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-RM-1000" && x.AvailableQuantity == 5m);
        Assert.Empty(snapshot.ScheduledReceipts);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], engineering.RequestedParentSkuCodes);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], inventory.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_includes_only_released_mps_buckets_as_mrp_inputs_with_source_type()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var released = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 10),
            12m);
        released.MarkReviewed("planner.li");
        released.Release("planning.manager");
        var draft = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-2000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 12),
            8m);
        dbContext.MasterProductionSchedules.AddRange(released, draft);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var engineering = new FakePlanningProductEngineeringClient();
        var inventory = new FakePlanningInventoryClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(dbContext, engineering, inventory);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        var demand = Assert.Single(snapshot.Demands);
        Assert.Equal("mps", demand.SourceType);
        Assert.StartsWith("MPS:", demand.DemandSourceReference, StringComparison.Ordinal);
        Assert.Equal("SKU-FG-1000", demand.SkuCode);
        Assert.Equal(12m, demand.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 10), demand.DueDate);
        Assert.DoesNotContain(snapshot.Demands, x => x.SkuCode == "SKU-FG-2000");
        Assert.Contains("SKU-FG-1000", engineering.RequestedParentSkuCodes);
        Assert.Contains("SKU-FG-1000", inventory.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_adds_master_data_planning_parameters_for_requested_items()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var planningParameters = new FakePlanningParameterClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            planningParameters);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:2;master-data-uom-conversions:0", snapshot.InventorySnapshotSource);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-FG-1000"
            && x.LeadTimeDays == 6
            && x.SafetyStockQuantity == 4m
            && x.LotSizeMin == 10m
            && x.LotSizeMax == 50m
            && x.LotSizeMultiple == 5m);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-RM-1000"
            && x.LeadTimeDays == 3
            && x.SafetyStockQuantity == 2m
            && x.LotSizeMultiple == 10m);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], planningParameters.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_adds_erp_open_purchase_order_lines_as_scheduled_receipts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var erp = new FakePlanningErpScheduledReceiptClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            erp);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        var receipt = Assert.Single(snapshot.ScheduledReceipts);
        Assert.Equal("SKU-RM-1000", receipt.SkuCode);
        Assert.Equal("pcs", receipt.UomCode);
        Assert.Equal("SITE-01", receipt.SiteCode);
        Assert.Equal(7m, receipt.Quantity);
        Assert.Equal("erp", receipt.SourceSystem);
        Assert.Equal("purchase-order", receipt.SourceDocumentType);
        Assert.Equal("PO-1000:10", receipt.SourceDocumentId);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], erp.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_degrades_optional_planning_sources_when_they_fail()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            new ThrowingPlanningErpScheduledReceiptClient(),
            new ThrowingPlanningParameterClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("inventory-http:2;scheduled-receipts:error;master-data-planning-parameters:error", snapshot.InventorySnapshotSource);
        Assert.Empty(snapshot.ScheduledReceipts);
        Assert.Empty(snapshot.PlanningParameters);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000");
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000");
    }

    [Fact]
    public async Task Upstream_adapter_propagates_truncated_uom_conversion_source()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "box", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            new TruncatedUomConversionPlanningParameterClient());

        var exception = await Assert.ThrowsAsync<RequiredPlanningSnapshotException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upstream_adapter_propagates_unexpected_erp_invalid_operation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            new BuggyPlanningErpScheduledReceiptClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Equal("ERP optional source bug", exception.Message);
    }

    [Fact]
    public async Task Upstream_adapter_propagates_unexpected_master_data_invalid_operation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "sales-order", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            new BuggyPlanningParameterClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Equal("MasterData optional source bug", exception.Message);
    }

    [Fact]
    public async Task Erp_scheduled_receipt_client_wraps_empty_response_envelope_as_optional_snapshot_failure()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "success": true,
              "message": "ok",
              "code": 0,
              "data": null
            }
            """));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var client = new HttpPlanningErpScheduledReceiptSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<OptionalPlanningSnapshotException>(() => client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("SKU-RM-1000", "pcs", "SITE-01")]),
            CancellationToken.None));

        Assert.Contains("ERP", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_wraps_invalid_json_as_optional_snapshot_failure()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<OptionalPlanningSnapshotException>(() => client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
            CancellationToken.None));

        Assert.Contains("MasterData", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_skips_empty_single_sku_envelope()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sku/SKU-MISSING", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": null
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-RM-1000",
                    "displayName": "Raw material",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 3,
                    "safetyStockQuantity": 2
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [
                    new PlanningParameterSnapshotItem("SKU-MISSING", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-RM-1000", "pcs", "SITE-01"),
                ]),
            CancellationToken.None);

        var parameter = Assert.Single(snapshot.PlanningParameters);
        Assert.Equal("master-data-planning-parameters:1;master-data-uom-conversions:0", snapshot.SnapshotSource);
        Assert.Equal("SKU-RM-1000", parameter.SkuCode);
    }

    [Fact]
    public async Task Erp_scheduled_receipt_client_maps_open_purchase_order_lines_across_pages()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Contains("status=Released", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("take=500", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            if (request.RequestUri.Query.Contains("skip=500", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 501,
                        "items": [
                          {
                            "purchaseOrderNo": "PO-501",
                            "supplierCode": "SUP-001",
                            "siteCode": "SITE-01",
                            "status": "Released",
                            "totalAmount": 0,
                            "lines": [
                              { "lineNo": "10", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 9, "receivedQuantity": 2, "unitPrice": 1, "promisedDate": "2026-05-24" }
                            ]
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "total": 501,
                    "items": [
                      {
                        "purchaseOrderNo": "PO-100",
                        "supplierCode": "SUP-001",
                        "siteCode": "SITE-01",
                        "status": "Released",
                        "totalAmount": 0,
                        "lines": [
                          { "lineNo": "10", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 12, "receivedQuantity": 5, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "20", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 5, "receivedQuantity": 5, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "30", "skuCode": "SKU-RM-1000", "uomCode": "kg", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "40", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-07-01" },
                          { "lineNo": "50", "skuCode": "SKU-OTHER", "uomCode": "pcs", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-05-30" }
                        ]
                      }
                    ]
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var client = new HttpPlanningErpScheduledReceiptSnapshotClient(httpClient);

        var snapshot = await client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("sku-rm-1000", "PCS", "site-01")]),
            CancellationToken.None);

        Assert.Equal("erp-purchase-orders:2", snapshot.SnapshotSource);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([7m, 7m], snapshot.ScheduledReceipts.Select(x => x.Quantity).ToArray());
        Assert.Contains(snapshot.ScheduledReceipts, x =>
            x.SourceDocumentId == "PO-100:10"
            && x.ExpectedReceiptDate == new DateOnly(2026, 5, 30));
        Assert.Contains(snapshot.ScheduledReceipts, x =>
            x.SourceDocumentId == "PO-501:10"
            && x.ExpectedReceiptDate == new DateOnly(2026, 5, 24));
    }

    [Fact]
    public async Task Mes_scheduled_receipt_client_maps_open_work_order_remaining_output()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/api/business/v1/mes/work-orders", request.RequestUri!.AbsolutePath);
            Assert.Contains("organizationId=org-001", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("environmentId=env-dev", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "total": 4,
                    "items": [
                      {
                        "workOrderId": "WO-OPEN-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 10,
                        "completedQuantity": 4,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "started",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-CLOSED-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 12,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "closed",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-LATE-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 5,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-07-01T00:00:00Z",
                        "status": "released",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-OTHER-001",
                        "skuId": "SKU-OTHER",
                        "skuCode": "SKU-OTHER",
                        "uomCode": "pcs",
                        "quantity": 5,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "released",
                        "operationTasks": []
                      }
                    ]
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var client = new HttpPlanningMesScheduledReceiptSnapshotClient(httpClient);

        var snapshot = await client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("sku-fg-1000", "PCS", "SITE-01")]),
            CancellationToken.None);

        var receipt = Assert.Single(snapshot.ScheduledReceipts);
        Assert.Equal("mes-work-orders:1", snapshot.SnapshotSource);
        Assert.Equal("SKU-FG-1000", receipt.SkuCode);
        Assert.Equal("pcs", receipt.UomCode);
        Assert.Equal("SITE-01", receipt.SiteCode);
        Assert.Equal(6m, receipt.Quantity);
        Assert.Equal(new DateOnly(2026, 5, 31), receipt.ExpectedReceiptDate);
        Assert.Equal("mes", receipt.SourceSystem);
        Assert.Equal("work-order", receipt.SourceDocumentType);
        Assert.Equal("WO-OPEN-001", receipt.SourceDocumentId);
    }

    [Fact]
    public async Task Scheduled_receipt_source_registration_keeps_erp_and_mes_base_addresses_isolated()
    {
        var requests = new List<Uri>();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new CaptureHttpMessageHandlerBuilderFilter(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath.Contains("/erp/", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse("""{"success":true,"message":"ok","code":0,"data":{"total":0,"items":[]}}""")
                : JsonResponse("""{"success":true,"message":"ok","code":0,"data":{"total":0,"items":[]}}""");
        }));
        services.AddPlanningScheduledReceiptSourceClients(
            new Uri("http://erp.test"),
            new Uri("http://mes.test"));
        await using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IPlanningScheduledReceiptSourceClient>().ToArray();

        foreach (var source in sources)
        {
            await source.GetScheduledReceiptsAsync(
                "token",
                new PlanningScheduledReceiptSnapshotRequest(
                    "org-001",
                    "env-dev",
                    new DateOnly(2026, 5, 25),
                    new DateOnly(2026, 6, 30),
                    [new PlanningScheduledReceiptSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
                CancellationToken.None);
        }

        Assert.Equal(2, sources.Length);
        Assert.Contains(requests, x => x.Host == "erp.test" && x.AbsolutePath == "/api/business/v1/erp/purchase-orders");
        Assert.Contains(requests, x => x.Host == "mes.test" && x.AbsolutePath == "/api/business/v1/mes/work-orders");
    }

    [Fact]
    public async Task Composite_scheduled_receipts_uses_explicit_source_name_when_source_degrades()
    {
        var composite = new CompositePlanningScheduledReceiptSnapshotClient(
            [new ThrowingNamedScheduledReceiptSourceClient("mes-work-orders")]);

        var snapshot = await composite.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
            CancellationToken.None);

        Assert.Equal("mes-work-orders:error", snapshot.SnapshotSource);
        Assert.Empty(snapshot.ScheduledReceipts);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_maps_sku_planning_attributes()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            Assert.Contains("organizationId=org-001", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("environmentId=env-dev", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            if (request.RequestUri.AbsolutePath.EndsWith("/sku/SKU-FG-1000", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "resourceType": "sku",
                        "code": "SKU-FG-1000",
                        "displayName": "Finished good",
                        "active": true,
                        "snapshotVersion": "v1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "baseUomCode": "pcs",
                        "procurementType": "make",
                        "mrpType": "mrp",
                        "lotSizingPolicy": "fixed-lot",
                        "plannedDeliveryTimeDays": 4,
                        "inHouseProductionTimeDays": 5,
                        "goodsReceiptProcessingTimeDays": 1,
                        "safetyStockQuantity": 4,
                        "reorderPointQuantity": 6,
                        "minimumLotSize": 10,
                        "maximumLotSize": 50,
                        "lotSizeMultiple": 5
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/sku/SKU-BLOCKED", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "resourceType": "sku",
                        "code": "SKU-BLOCKED",
                        "displayName": "Blocked item",
                        "active": true,
                        "snapshotVersion": "v1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "baseUomCode": "pcs",
                        "lifecycleStatus": "blocked",
                        "plannedDeliveryTimeDays": 30,
                        "safetyStockQuantity": 99,
                        "lotSizeMultiple": 99
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-RM-1000",
                    "displayName": "Raw material",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "procurementType": "buy",
                    "mrpType": "mrp",
                    "lotSizingPolicy": "fixed-lot",
                    "plannedDeliveryTimeDays": 3,
                    "goodsReceiptProcessingTimeDays": 0,
                    "safetyStockQuantity": 2,
                    "reorderPointQuantity": 8,
                    "lotSizeMultiple": 10
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [
                    new PlanningParameterSnapshotItem("sku-fg-1000", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-RM-1000", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-FG-1000", "pcs", "SITE-02"),
                    new PlanningParameterSnapshotItem("SKU-BLOCKED", "pcs", "SITE-01"),
                ]),
            CancellationToken.None);

        Assert.Equal("master-data-planning-parameters:3;master-data-uom-conversions:0", snapshot.SnapshotSource);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(snapshot.PlanningParameters, x => x.SkuCode == "sku-fg-1000" && x.SiteCode == "SITE-01" && x.LeadTimeDays == 6);
        Assert.Contains(snapshot.PlanningParameters, x => x.SkuCode == "SKU-FG-1000" && x.SiteCode == "SITE-02" && x.LotSizeMultiple == 5m);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-RM-1000"
            && x.LeadTimeDays == 3
            && x.SafetyStockQuantity == 2m
            && x.ProcurementType == "buy"
            && x.MrpType == "mrp"
            && x.LotSizingPolicy == "fixed-lot"
            && x.ReorderPointQuantity == 8m
            && x.PlannedDeliveryTimeDays == 3
            && x.InHouseProductionTimeDays is null
            && x.GoodsReceiptProcessingTimeDays == 0);
        Assert.DoesNotContain(snapshot.PlanningParameters, x => x.SkuCode == "SKU-BLOCKED");
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_maps_required_uom_conversions_to_planning_uom()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("resourceType=uom-conversion", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("all=True", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 1,
                        "resources": [
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "box to pcs",
                            "active": true,
                            "snapshotVersion": "v1",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 12,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-01-01"
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 1
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")]),
            CancellationToken.None);

        Assert.Equal("master-data-planning-parameters:1;master-data-uom-conversions:1", snapshot.SnapshotSource);
        var parameter = Assert.Single(snapshot.PlanningParameters);
        Assert.Equal("pcs", parameter.UomCode);
        var conversion = Assert.Single(snapshot.UomConversions);
        Assert.Equal("box", conversion.FromUomCode);
        Assert.Equal("pcs", conversion.ToUomCode);
        Assert.Equal(12m, conversion.Factor);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_selects_uom_conversion_by_as_of_date()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 4,
                        "resources": [
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "expired",
                            "active": true,
                            "snapshotVersion": "v1",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 10,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-01-01",
                            "effectiveTo": "2026-05-31"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "current",
                            "active": true,
                            "snapshotVersion": "v2",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 12,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-06-01"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "invalid latest",
                            "active": true,
                            "snapshotVersion": "v3",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 0,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-06-15"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "future",
                            "active": true,
                            "snapshotVersion": "v4",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 24,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-07-01"
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs"
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")],
                new DateOnly(2026, 6, 20)),
            CancellationToken.None);

        var conversion = Assert.Single(snapshot.UomConversions);
        Assert.Equal(12m, conversion.Factor);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_reports_truncated_uom_conversion_list()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 5001,
                        "truncated": true,
                        "limit": 5000,
                        "resources": []
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs"
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<RequiredPlanningSnapshotException>(() => client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")],
                new DateOnly(2026, 6, 20)),
            CancellationToken.None));

        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_limits_sku_detail_concurrency()
    {
        var current = 0;
        var observedMax = 0;
        var handler = new StubHttpMessageHandler(async request =>
        {
            var running = Interlocked.Increment(ref current);
            int snapshot;
            while (running > (snapshot = Volatile.Read(ref observedMax)))
            {
                Interlocked.CompareExchange(ref observedMax, running, snapshot);
            }

            await Task.Delay(50);
            Interlocked.Decrement(ref current);
            var skuCode = request.RequestUri!.AbsolutePath.Split('/').Last();
            return JsonResponse($$"""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "{{skuCode}}",
                    "displayName": "{{skuCode}}",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 1
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);
        var items = Enumerable.Range(1, 20)
            .Select(x => new PlanningParameterSnapshotItem($"SKU-{x:000}", "pcs", "SITE-01"))
            .ToArray();

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest("org-001", "env-dev", items),
            CancellationToken.None);

        Assert.Equal(20, snapshot.PlanningParameters.Count);
        Assert.True(observedMax <= 8, $"Expected at most 8 concurrent MasterData SKU requests, observed {observedMax}.");
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"demand-planning-adapter-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class FakePlanningProductEngineeringClient : IPlanningProductEngineeringSnapshotClient
    {
        private readonly List<string> requestedParentSkuCodes = [];

        public IReadOnlyCollection<string> RequestedParentSkuCodes => requestedParentSkuCodes;

        public Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
            string internalBearerToken,
            PlanningProductEngineeringSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            requestedParentSkuCodes.AddRange(request.ParentSkuCodes);
            return Task.FromResult(new PlanningProductEngineeringSnapshot(
                "product-engineering-http:2",
                request.ParentSkuCodes.Contains("SKU-FG-1000", StringComparer.OrdinalIgnoreCase)
                    ? [new ProductionVersionSnapshot("SKU-FG-1000", "PV-REAL-001", "MBOM-REAL-001", "ROUTING-REAL-001", 10m, 50m, null)]
                    : [],
                request.ParentSkuCodes.Contains("SKU-FG-1000", StringComparer.OrdinalIgnoreCase)
                    ? [new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m)]
                    : []));
        }
    }

    private sealed class FakePlanningInventoryClient : IPlanningInventorySnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
            string internalBearerToken,
            PlanningInventorySnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningInventorySnapshot(
                "inventory-http:2",
                [
                    new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                    new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
                ]));
        }
    }

    private sealed class FakePlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningScheduledReceiptSnapshot(
                "erp-purchase-orders:1",
                [
                    new ScheduledReceiptSnapshot("SKU-RM-1000", "pcs", "SITE-01", 7m, new DateOnly(2026, 5, 30), "erp", "purchase-order", "PO-1000:10"),
                ]));
        }
    }

    private sealed class FakePlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningParameterSnapshotResult(
                "master-data-planning-parameters:2;master-data-uom-conversions:0",
                [
                    new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 6, 4m, 10m, 50m, 5m),
                    new PlanningParameterSnapshot("SKU-RM-1000", "pcs", "SITE-01", 3, 2m, null, null, 10m),
                ],
                []));
        }
    }

    private sealed class ThrowingPlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("ERP unavailable");
        }
    }

    private sealed class ThrowingPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new OptionalPlanningSnapshotException("MasterData unavailable");
        }
    }

    private sealed class TruncatedUomConversionPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new RequiredPlanningSnapshotException("MasterData UOM conversion list was truncated at 5000 of 5001.");
        }
    }

    private sealed class BuggyPlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("ERP optional source bug");
        }
    }

    private sealed class BuggyPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("MasterData optional source bug");
        }
    }

    private sealed class ThrowingNamedScheduledReceiptSourceClient(string sourceName) : IPlanningScheduledReceiptSourceClient
    {
        public string SourceName { get; } = sourceName;

        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new OptionalPlanningSnapshotException($"{SourceName} unavailable");
        }
    }

    private sealed class CaptureHttpMessageHandlerBuilderFilter(Func<HttpRequestMessage, HttpResponseMessage> send)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                builder.AdditionalHandlers.Add(new CaptureHttpMessageHandler(send));
            };
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this((request, _) => Task.FromResult(send(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
            : this((request, _) => send(request))
        {
        }

        private StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return send(request, cancellationToken);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
