using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayProxyTests
{
    [Fact]
    public async Task List_skus_uses_internal_service_token_for_downstream_business_service()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev&take=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 25), masterData.LastListResourcesRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SKU-001", document.RootElement.GetProperty("data").GetProperty("resources")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_skus_does_not_call_downstream_when_iam_denies_permission()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Forbidden(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, masterData.ListResourcesCallCount);
    }

    [Fact]
    public async Task Inventory_availability_uses_internal_service_token_for_downstream_business_service()
    {
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/inventory/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", inventory.LastInternalToken);
        Assert.Equal("SKU-001", inventory.LastAvailabilityRequest!.SkuCode);
        Assert.Equal("S1", inventory.LastAvailabilityRequest.SiteCode);
    }

    [Fact]
    public async Task Quality_ncr_list_uses_internal_service_token_for_downstream_business_service()
    {
        var quality = new RecordingQualityClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", quality.LastInternalToken);
        Assert.Equal(new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 20), quality.LastNcrListRequest);
    }

    [Fact]
    public async Task Mes_work_order_list_uses_internal_service_token_for_downstream_business_service()
    {
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&take=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.Equal(new BusinessConsoleMesListRequest("org-001", "env-dev", "released", 15), mes.LastWorkOrderListRequest);
    }

    [Fact]
    public async Task Mes_production_plan_facade_passes_through_demand_planning_source_reference_fields()
    {
        var mes = new RecordingMesClient
        {
            ProductionPlans =
            [
                new BusinessConsoleMesProductionPlanRow(
                    "SUG-001",
                    "DemandPlanning",
                    "PlanningSuggestion",
                    "SUG-001",
                    "DEMAND-001",
                    "SKU-FG-1000",
                    12m,
                    "PCS",
                    "Converted",
                    "Ready",
                    [],
                    DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                    DateTimeOffset.Parse("2026-06-03T08:00:00Z")),
            ],
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/mes/production-plans?organizationId=org-001&environmentId=env-dev&status=Converted&take=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.Equal(new BusinessConsoleMesListRequest("org-001", "env-dev", "Converted", 15), mes.LastProductionPlanListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plan = document.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("DemandPlanning", plan.GetProperty("sourceSystem").GetString());
        Assert.Equal("PlanningSuggestion", plan.GetProperty("sourceDocumentType").GetString());
        Assert.Equal("SUG-001", plan.GetProperty("sourceDocumentId").GetString());
        Assert.Equal("DEMAND-001", plan.GetProperty("sourceDemandReference").GetString());
        Assert.Equal("PCS", plan.GetProperty("uomCode").GetString());
        Assert.Equal("Converted", plan.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Mes_production_plan_readiness_returns_blocking_item_when_downstream_is_unavailable()
    {
        var mes = new RecordingMesClient
        {
            ProductionPlanReadinessFailure = BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "mes-unavailable"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/mes/production-plans/PLAN-001/readiness?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("Blocked", data.GetProperty("status").GetString());
        var issue = data.GetProperty("blockingIssues")[0];
        Assert.Equal("SOURCE_SERVICE_UNAVAILABLE", issue.GetProperty("code").GetString());
        Assert.Equal("BusinessMes", issue.GetProperty("sourceSystem").GetString());
        Assert.Equal("PLAN-001", issue.GetProperty("referenceId").GetString());
    }

    [Fact]
    public async Task Mes_foundation_readiness_area_returns_blocking_item_when_downstream_is_unavailable()
    {
        var mes = new RecordingMesClient
        {
            FoundationReadinessFailure = new HttpRequestException("connection refused"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/mes/foundation-readiness/equipment?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("Blocked", data.GetProperty("status").GetString());
        Assert.Equal("SOURCE_SERVICE_UNAVAILABLE", data.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Mes_release_preserves_safe_downstream_readiness_reason_code()
    {
        var mes = new RecordingMesClient
        {
            ReleaseFailure = BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.Conflict,
                "QUALITY_PLAN_MISSING"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/work-orders/WO-001/release?organizationId=org-001&environmentId=env-dev",
            new { confirmWarnings = false, idempotencyKey = "release-001" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QUALITY_PLAN_MISSING", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Mes_operation_start_preserves_safe_downstream_readiness_reason_code()
    {
        var mes = new RecordingMesClient
        {
            StartOperationFailure = BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.Conflict,
                "EQUIPMENT_MAINTENANCE_CONFLICT"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/operation-tasks/OP-001/start?organizationId=org-001&environmentId=env-dev",
            new { reasonCode = "start", idempotencyKey = "start-001" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("EQUIPMENT_MAINTENANCE_CONFLICT", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Engineering_production_version_resolve_uses_internal_service_token_for_downstream_business_service()
    {
        var engineering = new RecordingProductEngineeringClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(engineering);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/engineering/production-versions/resolve?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&effectiveDate=2025-01-15&lotSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", engineering.LastInternalToken);
        Assert.Equal(new BusinessConsoleResolveProductionVersionRequest("org-001", "env-dev", "FG-FRONT-SHOCK", DateOnly.Parse("2025-01-15"), 100), engineering.LastResolveRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pv-front-001", document.RootElement.GetProperty("data").GetProperty("productionVersionId").GetString());
        Assert.Equal("active", document.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Planning_mrp_run_uses_internal_service_token_for_downstream_business_service()
    {
        var planning = new RecordingPlanningClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(planning);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/planning/mrp-runs?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            horizonStart = "2026-05-25",
            horizonEnd = "2026-06-30",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", planning.LastInternalToken);
        Assert.Equal(new BusinessConsoleRunMrpRequest("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), planning.LastRunMrpRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mrp-run-001", document.RootElement.GetProperty("data").GetProperty("runId").GetString());
    }

    [Fact]
    public async Task Erp_procurement_purchase_order_list_uses_internal_service_token_for_downstream_business_service()
    {
        var erp = new RecordingErpClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessErpClient>();
            services.AddSingleton<IBusinessErpClient>(erp);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/erp/procurement/purchase-orders?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpContextRequest("org-001", "env-dev"), erp.LastPurchaseOrderListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PO-001", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("purchaseOrderNo").GetString());
        Assert.False(document.RootElement.GetProperty("data").GetProperty("items")[0].TryGetProperty("supplierName", out _));
        Assert.Equal("partially-received", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("receiptReadiness").GetString());
    }

    [Fact]
    public async Task Erp_sales_and_finance_facades_use_domain_specific_downstream_clients()
    {
        var erp = new RecordingErpClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessErpClient>();
            services.AddSingleton<IBusinessErpClient>(erp);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var sales = await client.GetAsync("/api/business-console/v1/erp/sales/sales-orders?organizationId=org-001&environmentId=env-dev");
        var payable = await client.GetAsync("/api/business-console/v1/erp/finance/payables/by-source?organizationId=org-001&environmentId=env-dev&sourceDocumentNo=PR-001");

        Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
        Assert.Equal(HttpStatusCode.OK, payable.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpContextRequest("org-001", "env-dev"), erp.LastSalesOrderListRequest);
        Assert.Equal(new BusinessConsoleErpSourceDocumentRequest("org-001", "env-dev", "PR-001"), erp.LastFinanceSourceDocumentRequest);
    }

    [Fact]
    public async Task Approval_center_facade_uses_internal_service_token_for_downstream_business_service()
    {
        var approval = new RecordingApprovalClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessApprovalClient>();
            services.AddSingleton<IBusinessApprovalClient>(approval);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/approval/templates?organizationId=org-001&environmentId=env-dev&documentType=purchase-order");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", approval.LastInternalToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("purchase-order-default", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("templateCode").GetString());
    }

    [Fact]
    public async Task Barcode_facade_uses_internal_service_token_for_print_and_scan_actions()
    {
        var barcode = new RecordingBarcodeLabelClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessBarcodeLabelClient>();
            services.AddSingleton<IBusinessBarcodeLabelClient>(barcode);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var print = await client.PostAsJsonAsync("/api/business-console/v1/barcode/print-batches?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            barcodeRuleId = "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8",
            labelTemplateId = "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9",
            sourceDocumentType = "work-order",
            sourceDocumentId = "WO-001",
            idempotencyKey = "print-001",
            labelValuesJson = "{}",
            requestedQuantity = 1,
        });
        var scan = await client.PostAsJsonAsync("/api/business-console/v1/barcode/scans?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceCode = "PDA-01",
            scannedValue = "BC-001",
            sourceWorkflow = "wms-receipt",
            sourceDocumentId = "WO-001",
            idempotencyKey = "scan-001",
            result = "accepted",
        });

        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal("internal-test-token", barcode.LastInternalToken);
        Assert.Equal("WO-001", barcode.LastPrintBatchRequest?.SourceDocumentId);
        Assert.Equal("BC-001", barcode.LastScanRequest?.ScannedValue);
    }

    [Fact]
    public async Task Scheduling_facade_uses_internal_service_token_and_forwards_stable_dtos()
    {
        var scheduling = new RecordingSchedulingClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(scheduling);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var preview = await client.PostAsJsonAsync("/api/business-console/v1/scheduling/plans/preview?organizationId=org-001&environmentId=env-dev", new
        {
            problem = CreateSchedulingProblem(),
        });
        var create = await client.PostAsJsonAsync("/api/business-console/v1/scheduling/plans?organizationId=org-001&environmentId=env-dev", new
        {
            problem = CreateSchedulingProblem(),
        });
        var list = await client.GetAsync("/api/business-console/v1/scheduling/plans?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=20");
        var detail = await client.GetAsync("/api/business-console/v1/scheduling/plans/plan-001?organizationId=org-001&environmentId=env-dev");
        var gantt = await client.GetAsync("/api/business-console/v1/scheduling/plans/plan-001/gantt?organizationId=org-001&environmentId=env-dev");
        var release = await client.PostAsync("/api/business-console/v1/scheduling/plans/plan-001/release?organizationId=org-001&environmentId=env-dev", null);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gantt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal("internal-test-token", scheduling.LastInternalToken);
        Assert.Equal("problem-001", scheduling.LastProblem!.ProblemId);
        Assert.Equal(new BusinessConsoleSchedulingContextRequest("org-001", "env-dev", 1, 20), scheduling.LastListRequest);
        Assert.Equal("plan-001", scheduling.LastPlanId);
        Assert.Equal(new BusinessConsoleSchedulingPlanRequest("plan-001", "org-001", "env-dev"), scheduling.LastPlanRequest);

        using var listDocument = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var summary = listDocument.RootElement.GetProperty("data")[0];
        Assert.Equal("plan-001", summary.GetProperty("planId").GetString());
        Assert.Equal("generated", summary.GetProperty("status").GetString());
        Assert.Equal(1, summary.GetProperty("assignmentCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("conflictCount").GetInt32());

        using var ganttDocument = JsonDocument.Parse(await gantt.Content.ReadAsStringAsync());
        var item = ganttDocument.RootElement.GetProperty("data")[0];
        Assert.Equal("gantt-001", item.GetProperty("itemId").GetString());
        Assert.Equal("op-001", item.GetProperty("operationId").GetString());
        Assert.Equal("generated", item.GetProperty("status").GetString());
        Assert.Equal("dueDate", item.GetProperty("conflictReasonCode").GetString());

        using var releaseDocument = JsonDocument.Parse(await release.Content.ReadAsStringAsync());
        Assert.Equal("plan-001", releaseDocument.RootElement.GetProperty("data").GetProperty("planId").GetString());
        Assert.Equal("released", releaseDocument.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Scheduling_facade_accepts_generated_client_string_enum_payloads()
    {
        var scheduling = new RecordingSchedulingClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(scheduling);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        var requestJson = JsonSerializer.Serialize(
            new BusinessConsoleSchedulingProblemRequest(CreateSchedulingProblemWithOperation()),
            SchedulingJson.Options);
        using var content = new StringContent(
            requestJson,
            System.Text.Encoding.UTF8,
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));

        var response = await client.PostAsync(
            "/api/business-console/v1/scheduling/plans/preview?organizationId=org-001&environmentId=env-dev",
            content);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"splitPolicy\":\"nonSplittable\"", requestJson, StringComparison.Ordinal);
        Assert.Equal(ScheduleSplitPolicyContract.NonSplittable, scheduling.LastProblem!.Orders.Single().Operations.Single().SplitPolicy);
        Assert.Contains("\"status\":\"preview\"", responseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scheduling_facade_does_not_call_downstream_when_iam_denies_permission()
    {
        var scheduling = new RecordingSchedulingClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Forbidden(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(scheduling);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/scheduling/plans?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, scheduling.ListCallCount);
    }

    [Theory]
    [InlineData("GET", "/api/business-console/v1/scheduling/plans/plan-001?environmentId=env-dev")]
    [InlineData("GET", "/api/business-console/v1/scheduling/plans/plan-001?organizationId=org-001")]
    [InlineData("GET", "/api/business-console/v1/scheduling/plans/%20?organizationId=org-001&environmentId=env-dev")]
    [InlineData("GET", "/api/business-console/v1/scheduling/plans/plan-001/gantt?environmentId=env-dev")]
    [InlineData("POST", "/api/business-console/v1/scheduling/plans/plan-001/release?organizationId=org-001")]
    public async Task Scheduling_plan_endpoints_reject_missing_context_or_plan_id_before_downstream_forwarding(
        string method,
        string path)
    {
        var scheduling = new RecordingSchedulingClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(scheduling);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, scheduling.GetPlanCallCount);
        Assert.Equal(0, scheduling.GetPlanGanttCallCount);
        Assert.Equal(0, scheduling.ReleasePlanCallCount);
    }

    [Fact]
    public async Task Equipment_availability_facade_aggregates_iiot_and_maintenance_runtime_windows()
    {
        var industrialTelemetry = new RecordingIndustrialTelemetryClient();
        var maintenance = new RecordingMaintenanceClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/equipment/availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", industrialTelemetry.LastInternalToken);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        var expectedRequest = new BusinessConsoleEquipmentAvailabilityRequest(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
            "DEV-OIL-01",
            null);
        Assert.Equal(expectedRequest, industrialTelemetry.LastAvailabilityRequest);
        Assert.Equal(expectedRequest, maintenance.LastAvailabilityRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("sourceReferenceId").GetString() == "alarm-001");
        Assert.Contains(items, item => item.GetProperty("sourceReferenceId").GetString() == "maintenance-001");
    }

    [Fact]
    public async Task Equipment_overview_facade_merges_current_state_for_scoped_devices_without_active_blocks()
    {
        var industrialTelemetry = new RecordingIndustrialTelemetryClient
        {
            RuntimeAvailabilityResponse = CreateEmptyAvailabilityResponse(),
        };
        var maintenance = new RecordingMaintenanceClient
        {
            AvailabilityResponse = CreateEmptyAvailabilityResponse(),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/equipment/overview?organizationId=org-001&environmentId=env-dev&deviceAssetIds=DEV-IDLE-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, industrialTelemetry.CurrentStateCallCount);
        Assert.Equal("DEV-IDLE-01", industrialTelemetry.LastCurrentStateDeviceAssetId);
        Assert.Equal(new BusinessConsoleEquipmentContextRequest("org-001", "env-dev"), industrialTelemetry.LastCurrentStateRequest);
        Assert.Equal("DEV-IDLE-01", industrialTelemetry.LastAvailabilityRequest!.DeviceAssetIds);
        Assert.Equal("DEV-IDLE-01", maintenance.LastAvailabilityRequest!.DeviceAssetIds);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Empty(data.GetProperty("activeBlocks").EnumerateArray());
        var device = Assert.Single(data.GetProperty("devices").EnumerateArray());
        Assert.Equal("DEV-IDLE-01", device.GetProperty("deviceAssetId").GetString());
        Assert.Equal("RUNNING", device.GetProperty("currentState").GetString());
        Assert.True(device.GetProperty("isSourceFresh").GetBoolean());
        Assert.Equal(1, device.GetProperty("activeAlarmCount").GetInt32());
        Assert.Equal(0, device.GetProperty("activeBlockCount").GetInt32());
    }

    [Fact]
    public async Task Equipment_overview_normalizes_device_scope_and_rejects_empty_or_excessive_scope()
    {
        var industrialTelemetry = new RecordingIndustrialTelemetryClient
        {
            RuntimeAvailabilityResponse = CreateEmptyAvailabilityResponse(),
        };
        var maintenance = new RecordingMaintenanceClient
        {
            AvailabilityResponse = CreateEmptyAvailabilityResponse(),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var duplicateResponse = await client.GetAsync("/api/business-console/v1/equipment/overview?organizationId=org-001&environmentId=env-dev&deviceAssetIds=DEV-IDLE-01,%20DEV-IDLE-01,,DEV-RUN-02");
        var emptyResponse = await client.GetAsync("/api/business-console/v1/equipment/overview?organizationId=org-001&environmentId=env-dev&deviceAssetIds=,,,");
        var tooManyIds = string.Join(',', Enumerable.Range(1, 51).Select(index => $"DEV-{index:00}"));
        var tooManyResponse = await client.GetAsync($"/api/business-console/v1/equipment/overview?organizationId=org-001&environmentId=env-dev&deviceAssetIds={tooManyIds}");

        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Equal(2, industrialTelemetry.CurrentStateCallCount);
        Assert.Equal(new[] { "DEV-IDLE-01", "DEV-RUN-02" }, industrialTelemetry.CurrentStateDeviceAssetIds);
        Assert.Equal("DEV-IDLE-01,DEV-RUN-02", industrialTelemetry.LastAvailabilityRequest!.DeviceAssetIds);
        Assert.Equal("DEV-IDLE-01,DEV-RUN-02", maintenance.LastAvailabilityRequest!.DeviceAssetIds);
        using var document = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            new[] { "DEV-IDLE-01", "DEV-RUN-02" },
            document.RootElement.GetProperty("data").GetProperty("devices")
                .EnumerateArray()
                .Select(device => device.GetProperty("deviceAssetId").GetString()!)
                .ToArray());

        var callCountAfterDuplicateRequest = industrialTelemetry.CurrentStateCallCount;
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
        Assert.Equal(callCountAfterDuplicateRequest, industrialTelemetry.CurrentStateCallCount);
    }

    [Fact]
    public async Task Equipment_facade_deduplicates_and_sorts_merged_runtime_windows()
    {
        var duplicate = CreateWindow(
            "DEV-OIL-01",
            "dup-001",
            EquipmentRuntimeSourceType.Alarm,
            EquipmentRuntimeReasonCodes.ActiveAlarm,
            EquipmentRuntimeSeverity.Critical,
            "2026-06-01T10:00:00Z",
            "2026-06-01T11:00:00Z");
        var early = CreateWindow(
            "DEV-OIL-01",
            "maint-001",
            EquipmentRuntimeSourceType.MaintenanceWindow,
            EquipmentRuntimeReasonCodes.MaintenanceWindow,
            EquipmentRuntimeSeverity.Warning,
            "2026-06-01T09:00:00Z",
            "2026-06-01T10:00:00Z");
        var otherDevice = CreateWindow(
            "DEV-PACK-02",
            "down-001",
            EquipmentRuntimeSourceType.Downtime,
            EquipmentRuntimeReasonCodes.Downtime,
            EquipmentRuntimeSeverity.Blocked,
            "2026-06-01T08:00:00Z",
            "2026-06-01T09:00:00Z");
        var industrialTelemetry = new RecordingIndustrialTelemetryClient
        {
            RuntimeAvailabilityResponse = CreateAvailabilityResponse(duplicate, otherDevice),
            DeviceRuntimeAvailabilityResponse = CreateAvailabilityResponse(duplicate),
        };
        var maintenance = new RecordingMaintenanceClient
        {
            AvailabilityResponse = CreateAvailabilityResponse(duplicate, early),
            AssetAvailabilityResponse = CreateAvailabilityResponse(duplicate, early),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var overview = await client.GetAsync("/api/business-console/v1/equipment/overview?organizationId=org-001&environmentId=env-dev&deviceAssetIds=DEV-OIL-01");
        var availability = await client.GetAsync("/api/business-console/v1/equipment/availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01");
        var detail = await client.GetAsync("/api/business-console/v1/equipment/devices/DEV-OIL-01?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var overviewDocument = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        Assert.Equal(2, overviewDocument.RootElement.GetProperty("data").GetProperty("devices")[0].GetProperty("activeBlockCount").GetInt32());
        Assert.Equal(
            new[] { "maint-001", "dup-001", "down-001" },
            overviewDocument.RootElement.GetProperty("data").GetProperty("activeBlocks")
                .EnumerateArray()
                .Select(item => item.GetProperty("sourceReferenceId").GetString()!)
                .ToArray());
        using var availabilityDocument = JsonDocument.Parse(await availability.Content.ReadAsStringAsync());
        Assert.Equal(
            new[] { "maint-001", "dup-001", "down-001" },
            availabilityDocument.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("sourceReferenceId").GetString()!)
                .ToArray());
        using var detailDocument = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(
            new[] { "maint-001", "dup-001" },
            detailDocument.RootElement.GetProperty("data").GetProperty("availability").GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("sourceReferenceId").GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task Equipment_device_detail_facade_calls_current_state_and_device_runtime_availability()
    {
        var industrialTelemetry = new RecordingIndustrialTelemetryClient();
        var maintenance = new RecordingMaintenanceClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/equipment/devices/DEV-OIL-01?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DEV-OIL-01", industrialTelemetry.LastCurrentStateDeviceAssetId);
        Assert.Equal(new BusinessConsoleEquipmentContextRequest("org-001", "env-dev"), industrialTelemetry.LastCurrentStateRequest);
        Assert.Equal("DEV-OIL-01", industrialTelemetry.LastDeviceAvailabilityDeviceAssetId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("RUNNING", data.GetProperty("currentState").GetProperty("currentState").GetString());
        Assert.Contains(data.GetProperty("availability").GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("sourceReferenceId").GetString() == "alarm-001");
    }

    [Fact]
    public async Task Scheduling_http_client_sends_internal_token_and_downstream_routes()
    {
        var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, SchedulingResponseFor(request)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.local") };
        var client = new HttpBusinessSchedulingClient(httpClient);

        await client.PreviewPlanAsync("internal-token-001", CreateSchedulingProblem(), CancellationToken.None);
        await client.CreatePlanAsync("internal-token-001", CreateSchedulingProblem(), CancellationToken.None);
        await client.ListPlansAsync("internal-token-001", new BusinessConsoleSchedulingContextRequest("org-001", "env-dev", 2, 50), CancellationToken.None);
        var planRequest = new BusinessConsoleSchedulingPlanRequest("plan-001", "org-001", "env-dev");
        await client.GetPlanAsync("internal-token-001", planRequest, CancellationToken.None);
        await client.GetPlanGanttAsync("internal-token-001", planRequest, CancellationToken.None);
        await client.ReleasePlanAsync("internal-token-001", planRequest, CancellationToken.None);

        Assert.All(handler.Requests, request => Assert.Equal("Bearer", request.Headers.Authorization?.Scheme));
        Assert.All(handler.Requests, request => Assert.Equal("internal-token-001", request.Headers.Authorization?.Parameter));
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/business/v1/scheduling/plans/preview", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/api/business/v1/scheduling/plans", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[2].Method);
        Assert.Equal("/api/business/v1/scheduling/plans", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&pageIndex=2&pageSize=50", handler.Requests[2].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/scheduling/plans/plan-001", handler.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev", handler.Requests[3].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/scheduling/plans/plan-001/gantt", handler.Requests[4].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev", handler.Requests[4].RequestUri!.Query.TrimStart('?'));
        Assert.Equal(HttpMethod.Post, handler.Requests[5].Method);
        Assert.Equal("/api/business/v1/scheduling/plans/plan-001/release", handler.Requests[5].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev", handler.Requests[5].RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Equipment_http_clients_send_internal_token_and_downstream_routes()
    {
        var telemetryHandler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/api/business/v1/iiot/alarms"
            ? JsonResponse(HttpStatusCode.OK, new
            {
                data = new[]
                {
                    new
                    {
                        alarmEventId = "alarm-001",
                        organizationId = "org-001",
                        environmentId = "env-dev",
                        deviceAssetId = "DEV-OIL-01",
                        alarmCode = "TEMP_HIGH",
                        severity = "critical",
                        status = "raised",
                        raisedAtUtc = "2026-06-01T08:20:00Z",
                        externalAlarmId = "EXT-ALARM-001",
                    },
                },
            })
            : JsonResponse(HttpStatusCode.OK, new
        {
            data = CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm),
        }));
        var maintenanceHandler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = CreateAvailabilityResponse("maintenance-001", EquipmentRuntimeSourceType.MaintenanceWindow),
        }));
        using var telemetryHttpClient = new HttpClient(telemetryHandler) { BaseAddress = new Uri("http://industrial-telemetry.local") };
        using var maintenanceHttpClient = new HttpClient(maintenanceHandler) { BaseAddress = new Uri("http://maintenance.local") };
        var telemetry = new HttpBusinessIndustrialTelemetryClient(telemetryHttpClient);
        var maintenance = new HttpBusinessMaintenanceClient(maintenanceHttpClient);
        var request = new BusinessConsoleEquipmentAvailabilityRequest(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
            "DEV-OIL-01",
            null);

        await telemetry.GetRuntimeAvailabilityAsync("internal-token-001", request, CancellationToken.None);
        await telemetry.GetDeviceRuntimeAvailabilityAsync("internal-token-001", "DEV-OIL-01", request, CancellationToken.None);
        await telemetry.GetDeviceCurrentStateAsync("internal-token-001", "DEV-OIL-01", new BusinessConsoleEquipmentContextRequest("org-001", "env-dev"), CancellationToken.None);
        await telemetry.ListActiveAlarmsAsync("internal-token-001", new BusinessConsoleEquipmentContextRequest("org-001", "env-dev"), CancellationToken.None);
        await maintenance.GetAvailabilityWindowsAsync("internal-token-001", request, CancellationToken.None);
        await maintenance.GetAssetAvailabilityWindowsAsync("internal-token-001", "DEV-OIL-01", request, CancellationToken.None);

        Assert.All(telemetryHandler.Requests, sent => Assert.Equal("internal-token-001", sent.Headers.Authorization?.Parameter));
        Assert.Equal("/api/business/v1/iiot/runtime-availability", telemetryHandler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08%3A00%3A00.0000000%2B00%3A00&windowEndUtc=2026-06-01T16%3A00%3A00.0000000%2B00%3A00&deviceAssetIds=DEV-OIL-01", telemetryHandler.Requests[0].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/iiot/devices/DEV-OIL-01/runtime-availability", telemetryHandler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("/api/business/v1/iiot/devices/DEV-OIL-01/current-state", telemetryHandler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev", telemetryHandler.Requests[2].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/iiot/alarms", telemetryHandler.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&status=raised", telemetryHandler.Requests[3].RequestUri!.Query.TrimStart('?'));
        Assert.All(maintenanceHandler.Requests, sent => Assert.Equal("internal-token-001", sent.Headers.Authorization?.Parameter));
        Assert.Equal("/api/business/v1/maintenance/availability-windows", maintenanceHandler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(telemetryHandler.Requests[0].RequestUri!.Query, maintenanceHandler.Requests[0].RequestUri!.Query);
        Assert.Equal("/api/business/v1/maintenance/assets/DEV-OIL-01/availability-windows", maintenanceHandler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Maintenance_http_client_does_not_duplicate_source_alarm_as_related_alarm()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new[]
            {
                new
                {
                    workOrderId = "wo-maint-001",
                    deviceAssetId = "DEV-PRESS-01",
                    priority = "high",
                    status = "Open",
                    sourceAlarmId = "alarm-001",
                    openedAtUtc = "2026-06-01T08:10:00Z",
                },
            },
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var response = await client.ListWorkOrdersAsync(
            "internal-token-001",
            new BusinessConsoleMaintenanceContextRequest("org-001", "env-dev"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("alarm-001", item.SourceAlarmId);
        Assert.Null(item.RelatedAlarmId);
    }

    [Fact]
    public async Task Scheduling_http_client_uses_scheduling_contract_enum_json()
    {
        var handler = new RecordingHandler(request =>
        {
            var requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"splitPolicy\":\"nonSplittable\"", requestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("\"splitPolicy\":0", requestBody, StringComparison.Ordinal);
            return StringJsonResponse(HttpStatusCode.OK, SchedulingStringEnumPlanJson);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.local") };
        var client = new HttpBusinessSchedulingClient(httpClient);

        var plan = await client.PreviewPlanAsync("internal-token-001", CreateSchedulingProblemWithOperation(), CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Generated, plan.Status);
        Assert.Contains(plan.Conflicts, x => x.ReasonCode == ScheduleConflictReasonCodeContract.DueDate);
        Assert.Contains(plan.GanttItems, x =>
            x.Status == SchedulePlanStatusContract.Generated &&
            x.ConflictReasonCode == ScheduleConflictReasonCodeContract.DueDate);
    }

    [Theory]
    [InlineData("/api/business-console/v1/inventory/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1", "inventory")]
    [InlineData("/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev", "quality")]
    [InlineData("/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev", "mes")]
    [InlineData("/api/business-console/v1/engineering/production-versions?organizationId=org-001&environmentId=env-dev&status=active", "engineering")]
    [InlineData("/api/business-console/v1/planning/suggestions?organizationId=org-001&environmentId=env-dev", "planning")]
    [InlineData("/api/business-console/v1/erp/procurement/purchase-orders?organizationId=org-001&environmentId=env-dev", "erp")]
    [InlineData("/api/business-console/v1/scheduling/plans?organizationId=org-001&environmentId=env-dev", "scheduling")]
    public async Task New_domain_facade_endpoints_do_not_call_downstream_when_iam_denies_permission(
        string path,
        string domain)
    {
        var inventory = new RecordingInventoryClient();
        var quality = new RecordingQualityClient();
        var mes = new RecordingMesClient();
        var engineering = new RecordingProductEngineeringClient();
        var planning = new RecordingPlanningClient();
        var erp = new RecordingErpClient();
        var scheduling = new RecordingSchedulingClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Forbidden(), services =>
        {
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(engineering);
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(planning);
            services.RemoveAll<IBusinessErpClient>();
            services.AddSingleton<IBusinessErpClient>(erp);
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(scheduling);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(domain, new[] { "inventory", "quality", "mes", "engineering", "planning", "erp", "scheduling" });
        Assert.Equal(0, inventory.AvailabilityCallCount);
        Assert.Equal(0, quality.NcrListCallCount);
        Assert.Equal(0, mes.WorkOrderListCallCount);
        Assert.Equal(0, engineering.ProductionVersionListCallCount);
        Assert.Equal(0, planning.SuggestionListCallCount);
        Assert.Equal(0, erp.PurchaseOrderListCallCount);
        Assert.Equal(0, scheduling.ListCallCount);
    }

    [Fact]
    public async Task List_skus_maps_downstream_service_error_to_gateway_error_response()
    {
        var masterData = new RecordingMasterDataClient
        {
            Failure = BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "master-data-unavailable"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("master-data-unavailable", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task List_skus_does_not_leak_raw_downstream_error_body_to_gateway_response()
    {
        var masterData = new RecordingMasterDataClient
        {
            Failure = new BusinessServiceProxyException(HttpStatusCode.BadGateway, "<html>secret stack trace</html>"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("downstream-request-failed", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("secret stack trace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_sku_rejects_obviously_invalid_gateway_request()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = new string('S', 65),
            name = "Demo SKU",
            baseUomCode = "EA",
            category = "finished-good",
            materialType = "standard",
            batchTrackingPolicy = "none",
            serialTrackingPolicy = "none",
            shelfLifePolicyCode = "none",
            storageConditionCode = "ambient",
            defaultBarcodeRuleCode = "default",
            qualityRequired = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Count_adjustment_rejects_zero_counted_quantity()
    {
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/inventory/count-tasks/count-001/adjustments?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            countedQuantity = 0,
            idempotencyKey = "idem-001",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Master_data_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = new[]
                {
                    new { resourceType = "sku", code = "SKU-HTTP", displayName = "HTTP SKU", active = true, snapshotVersion = "v1" },
                },
                total = 1,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var response = await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", true, 12),
            CancellationToken.None);

        Assert.Equal("SKU-HTTP", response.Resources.Single().Code);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=sku&includeDisabled=true&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token-001", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Master_data_http_client_omits_default_false_include_disabled_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = Array.Empty<object>(),
                total = 0,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 12),
            CancellationToken.None);

        Assert.Equal("/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=sku&take=12", handler.Requests.Single().RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Master_data_http_client_only_sends_include_disabled_when_true()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = Array.Empty<object>(),
                total = 0,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", true, 12),
            CancellationToken.None);

        Assert.Contains("includeDisabled=true", handler.Requests.Single().RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notification_http_client_sends_scope_headers_and_only_supported_message_query_parameters()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        messageId = "message-http-001",
                        intentId = "intent-http-001",
                        recipientRef = "user-admin",
                        status = "unread",
                        severity = "warning",
                        title = "Message",
                        body = "Message body",
                        resource = (object?)null,
                        createdAtUtc = "2026-06-03T01:00:00Z",
                        readAtUtc = (string?)null,
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://notification.local") };
        var client = new HttpBusinessNotificationClient(httpClient);

        var response = await client.ListMessagesAsync(
            "internal-token-001",
            new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user-admin", "unread", 25),
            CancellationToken.None);

        Assert.Equal("message-http-001", response.Items.Single().MessageId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/notifications/v1/messages?recipientRef=user-admin&status=unread", request.RequestUri!.PathAndQuery);
        Assert.Equal("org-001", request.Headers.GetValues("X-Organization-Id").Single());
        Assert.Equal("env-dev", request.Headers.GetValues("X-Environment-Id").Single());
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Notification_http_client_sends_scope_headers_and_only_supported_task_query_parameters()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        taskId = "task-http-001",
                        messageId = "message-http-001",
                        recipientRef = "user-admin",
                        taskType = "approve",
                        status = "open",
                        actionRef = "APP-001",
                        createdAtUtc = "2026-06-03T02:00:00Z",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://notification.local") };
        var client = new HttpBusinessNotificationClient(httpClient);

        var response = await client.ListTasksAsync(
            "internal-token-001",
            new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user-admin", "open", 25),
            CancellationToken.None);

        Assert.Equal("task-http-001", response.Items.Single().TaskId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/notifications/v1/tasks?recipientRef=user-admin&status=open", request.RequestUri!.PathAndQuery);
        Assert.Equal("org-001", request.Headers.GetValues("X-Organization-Id").Single());
        Assert.Equal("env-dev", request.Headers.GetValues("X-Environment-Id").Single());
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Inventory_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                skuCode = "SKU-HTTP",
                uomCode = "EA",
                siteCode = "S1",
                locationCode = (string?)null,
                lotNo = (string?)null,
                serialNo = (string?)null,
                qualityStatus = "available",
                ownerType = "owned",
                ownerId = (string?)null,
                onHandQuantity = 10,
                reservedQuantity = 2,
                availableQuantity = 8,
                items = Array.Empty<object>(),
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") };
        var client = new HttpBusinessInventoryClient(httpClient);

        var response = await client.GetAvailabilityAsync(
            "internal-token-001",
            new BusinessConsoleInventoryAvailabilityRequest("org-001", "env-dev", "SKU-HTTP", "EA", "S1", null, null, null, "available", "owned", null),
            CancellationToken.None);

        Assert.Equal(8, response.AvailableQuantity);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/inventory/v1/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-HTTP&uomCode=EA&siteCode=S1&qualityStatus=available&ownerType=owned", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Product_engineering_http_client_sends_internal_bearer_token_and_builds_released_queries()
    {
        var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, ResponseForEngineeringRequest(request)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://engineering.local") };
        var client = new HttpBusinessProductEngineeringClient(httpClient);

        await client.ListProductionVersionsAsync(
            "internal-token-001",
            new BusinessConsoleListProductionVersionsRequest("org-001", "env-dev", "FG-FRONT-SHOCK", "active"),
            CancellationToken.None);
        await client.ResolveProductionVersionAsync(
            "internal-token-001",
            new BusinessConsoleResolveProductionVersionRequest("org-001", "env-dev", "FG-FRONT-SHOCK", DateOnly.Parse("2025-01-15"), 100),
            CancellationToken.None);

        Assert.Equal("/api/business/v1/engineering/production-versions?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&status=active", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", handler.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("/api/business/v1/engineering/production-versions/resolve?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&effectiveDate=2025-01-15&lotSize=100", handler.Requests[1].RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", handler.Requests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Product_engineering_http_client_formats_decimal_query_values_with_invariant_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, ResponseForEngineeringRequest(request)));
            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://engineering.local") };
            var client = new HttpBusinessProductEngineeringClient(httpClient);

            await client.ResolveProductionVersionAsync(
                "internal-token-001",
                new BusinessConsoleResolveProductionVersionRequest("org-001", "env-dev", "FG-FRONT-SHOCK", DateOnly.Parse("2025-01-15"), 100.5m),
                CancellationToken.None);

            Assert.Equal("/api/business/v1/engineering/production-versions/resolve?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&effectiveDate=2025-01-15&lotSize=100.5", handler.Requests.Single().RequestUri!.PathAndQuery);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Quality_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        ncrId = "ncr-001",
                        ncrCode = "NCR-001",
                        sourceType = "inspection",
                        sourceDocumentId = "IR-001",
                        skuCode = "SKU-001",
                        defectQuantity = 1,
                        defectReason = "Defect",
                        status = "open",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListNcrsAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 12),
            CancellationToken.None);

        Assert.Equal("ncr-001", response.Items.Single().Id);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_inspection_plan_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        inspectionPlanId = "plan-001",
                        planCode = "IP-001",
                        category = "incoming",
                        skuCode = "SKU-001",
                        status = "active",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListInspectionPlansAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "active", 12),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("plan-001", item.Id);
        Assert.Equal("IP-001", item.Code);
        Assert.Equal("active", item.Status);
        Assert.Equal("incoming", item.Category);
        Assert.Equal("SKU-001", item.SkuCode);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_ncr_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        ncrId = "ncr-001",
                        ncrCode = "NCR-001",
                        sourceType = "inspection",
                        sourceDocumentId = "IR-001",
                        skuCode = "SKU-001",
                        defectQuantity = 3,
                        defectReason = "dimension-out-of-spec",
                        status = "open",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListNcrsAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 12),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("ncr-001", item.Id);
        Assert.Equal("NCR-001", item.Code);
        Assert.Equal("open", item.Status);
        Assert.Equal("inspection", item.SourceType);
        Assert.Equal("IR-001", item.SourceDocumentId);
        Assert.Equal("SKU-001", item.SkuCode);
        Assert.Equal(3, item.DefectQuantity);
        Assert.Equal("dimension-out-of-spec", item.DefectReason);
    }

    [Fact]
    public async Task Quality_http_client_maps_inspection_record_to_real_downstream_request_shape()
    {
        string? requestBody = null;
        var handler = new RecordingHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, new
            {
                data = new
                {
                    inspectionRecordId = "inspection-001",
                },
                success = true,
                message = string.Empty,
                code = 0,
            });
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.CreateInspectionRecordAsync(
            "internal-token-001",
            new BusinessConsoleCreateInspectionRecordRequest(
                "org-001",
                "env-dev",
                "plan-001",
                "operation",
                "mes-operation",
                "OP-001",
                "SKU-001",
                10m,
                "LOT-001",
                null,
                [
                    new BusinessConsoleInspectionCharacteristicResult(
                        "dimension",
                        "10.2",
                        "mm",
                        "conditional-release",
                        "within-waiver-limit",
                        1m,
                        ["file-001"]),
                ],
                "waiver approved",
                ["disp-file-001"]),
            CancellationToken.None);

        Assert.Equal("inspection-001", response.InspectionRecordId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/business/v1/quality/inspection-records", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        Assert.Equal("operation", root.GetProperty("sourceType").GetString());
        Assert.Equal("mes-operation", root.GetProperty("sourceService").GetString());
        var line = root.GetProperty("resultLines")[0];
        Assert.Equal("dimension", line.GetProperty("characteristicCode").GetString());
        Assert.Equal("10.2", line.GetProperty("observedValue").GetString());
        Assert.Equal("mm", line.GetProperty("unitCode").GetString());
        Assert.Equal("conditional-release", line.GetProperty("result").GetString());
        Assert.Equal("within-waiver-limit", line.GetProperty("defectReason").GetString());
        Assert.Equal(1m, line.GetProperty("defectQuantity").GetDecimal());
        Assert.False(line.TryGetProperty("measuredValue", out _));
        Assert.False(line.TryGetProperty("dispositionReason", out _));
        Assert.False(line.TryGetProperty("defectCode", out _));
    }

    [Fact]
    public async Task Mes_http_client_sends_internal_bearer_token_and_builds_downstream_body()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            items = new[]
            {
                new
                {
                    workOrderId = "WO-HTTP",
                    skuId = "SKU-001",
                    productionVersionId = (string?)null,
                    quantity = 10,
                    priority = 0,
                    dueUtc = DateTimeOffset.Parse("2026-05-24T00:00:00Z"),
                    status = "released",
                    operationTasks = Array.Empty<object>(),
                },
            },
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesClient(httpClient);

        var response = await client.ListWorkOrdersAsync(
            "internal-token-001",
            new BusinessConsoleMesListRequest("org-001", "env-dev", "released", 12),
            CancellationToken.None);

        Assert.Equal("WO-HTTP", response.Items.Single().WorkOrderId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Master_data_http_client_forwards_accept_language_through_gateway_handler()
    {
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };
        contextAccessor.HttpContext.Request.Headers.AcceptLanguage = "zh-CN, en;q=0.8";
        var terminal = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = Array.Empty<object>(),
                total = 0,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(new AcceptLanguageForwardingHandler(contextAccessor)
        {
            InnerHandler = terminal,
        })
        {
            BaseAddress = new Uri("http://master-data.local"),
        };
        var client = new HttpBusinessMasterDataClient(httpClient);

        await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None);

        Assert.Equal(
            "zh-CN, en; q=0.8",
            string.Join(", ", terminal.Requests.Single().Headers.AcceptLanguage.Select(value => value.ToString())));
    }

    [Fact]
    public async Task Master_data_http_client_throws_proxy_exception_for_downstream_errors()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.BadRequest, new
        {
            success = false,
            message = "invalid-resource-type",
            code = 400,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("invalid-resource-type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_data_http_client_does_not_expose_plain_text_downstream_error_bodies()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>secret stack trace</html>"),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal("downstream-request-failed", ex.Message);
        Assert.DoesNotContain("secret stack trace", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html>", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload) => new(statusCode)
    {
        Content = JsonContent.Create(payload),
    };

    private static HttpResponseMessage StringJsonResponse(HttpStatusCode statusCode, string payload) => new(statusCode)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
    };

    internal static EquipmentRuntimeAvailabilityResponse CreateAvailabilityResponse(
        string sourceReferenceId,
        EquipmentRuntimeSourceType sourceType) =>
        CreateAvailabilityResponse(CreateWindow(
            "DEV-OIL-01",
            sourceReferenceId,
            sourceType,
            sourceType == EquipmentRuntimeSourceType.Alarm
                ? EquipmentRuntimeReasonCodes.ActiveAlarm
                : EquipmentRuntimeReasonCodes.MaintenanceWindow,
            sourceType == EquipmentRuntimeSourceType.Alarm
                ? EquipmentRuntimeSeverity.Critical
                : EquipmentRuntimeSeverity.Warning,
            "2026-06-01T09:00:00Z",
            "2026-06-01T10:00:00Z"));

    internal static EquipmentRuntimeAvailabilityResponse CreateAvailabilityResponse(
        params EquipmentRuntimeAvailabilityWindowContract[] windows) =>
        new(
            1,
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
            windows);

    internal static EquipmentRuntimeAvailabilityWindowContract CreateWindow(
        string deviceAssetId,
        string sourceReferenceId,
        EquipmentRuntimeSourceType sourceType,
        string reasonCode,
        EquipmentRuntimeSeverity severity,
        string startUtc,
        string endUtc) =>
        new(
            deviceAssetId,
            "WC-OIL",
            EquipmentRuntimeAvailabilityStatus.Unavailable,
            reasonCode,
            severity,
            DateTimeOffset.Parse(startUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(endUtc, CultureInfo.InvariantCulture),
            sourceType,
            sourceReferenceId,
            sourceReferenceId,
            []);

    internal static EquipmentRuntimeAvailabilityResponse CreateEmptyAvailabilityResponse() =>
        new(
            1,
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
            []);

    private static SchedulingProblemContract CreateSchedulingProblem() => new(
        ContractVersion: 1,
        ProblemId: "problem-001",
        OrganizationId: "org-001",
        EnvironmentId: "env-dev",
        HorizonStartUtc: DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
        HorizonEndUtc: DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
        Orders: [],
        Resources: [],
        Calendars: [],
        UnavailabilityWindows: [],
        MaterialReadiness: [],
        QualityBlocks: [],
        LockedAssignments: []);

    private static SchedulingProblemContract CreateSchedulingProblemWithOperation() => CreateSchedulingProblem() with
    {
        Orders =
        [
            new SchedulingOrderContract(
                "order-001",
                "SKU-001",
                1,
                DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
                1,
                false,
                [
                    new SchedulingOperationContract(
                        "op-001",
                        10,
                        [],
                        60,
                        "CAP-001",
                        ["res-001"],
                        "res-001",
                        DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
                        DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
                        1,
                        false,
                        ScheduleSplitPolicyContract.NonSplittable,
                        null,
                        null,
                        "source-001"),
                ]),
        ],
    };

    internal static SchedulePlanContract CreateSchedulePlan(SchedulePlanStatusContract status = SchedulePlanStatusContract.Generated) => new(
        ContractVersion: 1,
        PlanId: "plan-001",
        ProblemId: "problem-001",
        ProblemFingerprint: "fingerprint-001",
        AlgorithmVersion: "aps-lite-v1",
        Status: status,
        GeneratedAtUtc: DateTimeOffset.Parse("2026-06-01T08:05:00Z", CultureInfo.InvariantCulture),
        Assignments:
        [
            new ScheduleAssignmentContract(
                "assign-001",
                "order-001",
                "op-001",
                10,
                "res-001",
                "wc-001",
                DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture),
                false,
                "scheduled"),
        ],
        ResourceLoads: [],
        Conflicts: [],
        UnscheduledOperations: [],
        ChangeSummary: [],
        GanttItems:
        [
            new GanttScheduleItemContract(
                "gantt-001",
                "order-001",
                "op-001",
                10,
                "res-001",
                "wc-001",
                DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture),
                status,
                true,
                ScheduleConflictReasonCodeContract.DueDate),
        ]);

    private const string SchedulingStringEnumPlanJson = """
        {
          "data": {
            "contractVersion": 1,
            "planId": "plan-001",
            "problemId": "problem-001",
            "problemFingerprint": "fingerprint-001",
            "algorithmVersion": "aps-lite-v1",
            "status": "generated",
            "generatedAtUtc": "2026-06-01T08:05:00Z",
            "assignments": [
              {
                "assignmentId": "assign-001",
                "orderId": "order-001",
                "operationId": "op-001",
                "operationSequence": 10,
                "resourceId": "res-001",
                "workCenterId": "wc-001",
                "startUtc": "2026-06-01T08:00:00Z",
                "endUtc": "2026-06-01T09:00:00Z",
                "isLocked": false,
                "explanationCode": "scheduled"
              }
            ],
            "resourceLoads": [],
            "conflicts": [
              {
                "conflictId": "conflict-001",
                "reasonCode": "dueDate",
                "severity": "warning",
                "orderId": "order-001",
                "operationId": "op-001",
                "resourceId": "res-001",
                "message": "Due date conflict"
              }
            ],
            "unscheduledOperations": [
              {
                "orderId": "order-002",
                "operationId": "op-002",
                "reasonCode": "dueDate",
                "message": "Outside due date"
              }
            ],
            "changeSummary": [
              {
                "orderId": "order-001",
                "operationId": "op-001",
                "changeType": "delayed",
                "message": "Delayed by rush order"
              }
            ],
            "ganttItems": [
              {
                "itemId": "gantt-001",
                "orderId": "order-001",
                "operationId": "op-001",
                "operationSequence": 10,
                "resourceId": "res-001",
                "workCenterId": "wc-001",
                "startUtc": "2026-06-01T08:00:00Z",
                "endUtc": "2026-06-01T09:00:00Z",
                "status": "generated",
                "hasConflict": true,
                "conflictReasonCode": "dueDate"
              }
            ]
          },
          "success": true,
          "message": "",
          "code": 0
        }
        """;

    private static object SchedulingResponseFor(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/gantt", StringComparison.Ordinal))
        {
            return new
            {
                data = CreateSchedulePlan().GanttItems,
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        if (path.EndsWith("/release", StringComparison.Ordinal))
        {
            return new
            {
                data = new BusinessConsoleReleaseSchedulePlanResponse(
                    "plan-001",
                    SchedulePlanStatusContract.Released,
                    DateTimeOffset.Parse("2026-06-01T10:00:00Z", CultureInfo.InvariantCulture)),
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/plans", StringComparison.Ordinal))
        {
            return new
            {
                data = new[]
                {
                    new BusinessConsoleSchedulePlanSummaryResponse(
                        "plan-001",
                        "problem-001",
                        SchedulePlanStatusContract.Generated,
                        DateTimeOffset.Parse("2026-06-01T08:05:00Z", CultureInfo.InvariantCulture),
                        null,
                        1,
                        0,
                        0),
                },
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        return new
        {
            data = CreateSchedulePlan(path.EndsWith("/preview", StringComparison.Ordinal)
                ? SchedulePlanStatusContract.Preview
                : SchedulePlanStatusContract.Generated),
            success = true,
            message = string.Empty,
            code = 0,
        };
    }

    private static object ResponseForEngineeringRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/production-versions/resolve", StringComparison.Ordinal))
        {
            return new
            {
                data = new
                {
                    productionVersionId = "pv-front-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "FG-FRONT-SHOCK",
                    mbomVersionId = "mbom-front-r1",
                    routingVersionId = "routing-front-r1",
                    effectiveDate = "2025-01-15",
                    lotSize = 100,
                    status = "active",
                },
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        return new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        productionVersionId = "pv-front-001",
                        organizationId = "org-001",
                        environmentId = "env-dev",
                        skuCode = "FG-FRONT-SHOCK",
                        mbomVersionId = "mbom-front-r1",
                        routingVersionId = "routing-front-r1",
                        validFrom = "2026-05-01",
                        validTo = (string?)null,
                        lotSizeMin = 1,
                        lotSizeMax = 500,
                        priority = 10,
                        isDefault = true,
                        status = "active",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        };
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}

internal sealed class RecordingMasterDataClient : IBusinessMasterDataClient
{
    public int ListResourcesCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleListResourcesRequest? LastListResourcesRequest { get; private set; }

    public IReadOnlyCollection<BusinessConsoleResourceItem>? Resources { get; init; }

    public BusinessServiceProxyException? Failure { get; init; }

    public Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken)
    {
        ListResourcesCallCount++;
        LastInternalToken = internalBearerToken;
        LastListResourcesRequest = request;
        if (Failure is not null)
        {
            throw Failure;
        }

        var resources = Resources ??
            [
                new BusinessConsoleResourceItem("sku", "SKU-001", "Demo SKU", true, "v1"),
            ];
        resources = resources
            .Where(resource => string.Equals(resource.ResourceType, request.ResourceType, StringComparison.Ordinal))
            .Take(request.Take)
            .ToArray();
        return Task.FromResult(new BusinessConsoleResourceListResponse(resources, resources.Count));
    }

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleResourceItem("sku", request.Code ?? "SKU-GENERATED", request.Name, true, "v1"));
    }
}

internal sealed class RecordingInventoryClient : IBusinessInventoryClient
{
    public int AvailabilityCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleInventoryAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public Exception? AvailabilityFailure { get; init; }

    public Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        AvailabilityCallCount++;
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        if (AvailabilityFailure is not null)
        {
            throw AvailabilityFailure;
        }

        return Task.FromResult(new BusinessConsoleInventoryAvailabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            10,
            2,
            8,
            []));
    }

    public Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsolePostStockMovementResponse("move-001", 10, 8));

    public Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleCreateStockCountTaskResponse("count-001", 1));

    public Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleConfirmStockCountAdjustmentResponse("move-001", 1, 11));
}

internal sealed class RecordingQualityClient : IBusinessQualityClient
{
    public int NcrListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleQualityListRequest? LastNcrListRequest { get; private set; }

    public Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleQualityListResponse([]));

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleCreateInspectionRecordResponse("inspection-001"));

    public Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        NcrListCallCount++;
        LastInternalToken = internalBearerToken;
        LastNcrListRequest = request;
        return Task.FromResult(new BusinessConsoleQualityListResponse(
            [
                new BusinessConsoleQualityItem(
                    "ncr-001",
                    "NCR-001",
                    "open",
                    null,
                    "SKU-001",
                    null,
                    null,
                    null,
                    null,
                    "inspection",
                    "IR-001",
                    1,
                    "Defect",
                    null,
                    null),
            ]));
    }

    public Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleAcceptedResponse(true));

    public Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleAcceptedResponse(true));
}

internal sealed class RecordingProductEngineeringClient : IBusinessProductEngineeringClient
{
    public int ProductionVersionListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleResolveProductionVersionRequest? LastResolveRequest { get; private set; }

    public BusinessConsoleListProductionVersionsRequest? LastProductionVersionListRequest { get; private set; }

    public Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringBomListResponse([]));
    }

    public Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleRoutingListResponse([]));
    }

    public Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken)
    {
        ProductionVersionListCallCount++;
        LastInternalToken = internalBearerToken;
        LastProductionVersionListRequest = request;
        return Task.FromResult(new BusinessConsoleProductionVersionListResponse(
            [
                new BusinessConsoleProductionVersionItem(
                    "pv-front-001",
                    request.OrganizationId,
                    request.EnvironmentId,
                    "FG-FRONT-SHOCK",
                    "mbom-front-r1",
                    "routing-front-r1",
                    DateOnly.Parse("2026-05-01"),
                    null,
                    1,
                    500,
                    10,
                    true,
                    "active"),
            ]));
    }

    public Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastResolveRequest = request;
        return Task.FromResult(new BusinessConsoleResolveProductionVersionResponse(
            "pv-front-001",
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            "mbom-front-r1",
            "routing-front-r1",
            request.EffectiveDate,
            request.LotSize,
            "active"));
    }
}

internal sealed class RecordingPlanningClient : IBusinessPlanningClient
{
    public int SuggestionListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleRunMrpRequest? LastRunMrpRequest { get; private set; }

    public Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleDemandSourceListResponse([]));
    }

    public Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleDemandSourceResponse(
            "demand-001",
            request.SourceReference ?? "DEMAND-001",
            request.DemandType,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.DueDate));
    }

    public Task<BusinessConsoleRunMrpResponse> RunMrpAsync(
        string internalBearerToken,
        BusinessConsoleRunMrpRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRunMrpRequest = request;
        return Task.FromResult(new BusinessConsoleRunMrpResponse("mrp-run-001", 2));
    }

    public Task<BusinessConsoleMrpRunListResponse> ListMrpRunsAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMrpRunListResponse([]));
    }

    public Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMrpPeggingListResponse([]));
    }

    public Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken)
    {
        SuggestionListCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsolePlanningSuggestionListResponse([]));
    }

    public Task<BusinessConsoleAcceptedResponse> AcceptSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
    }
}

internal sealed class RecordingErpClient : IBusinessErpClient
{
    public int PurchaseOrderListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleErpContextRequest? LastPurchaseOrderListRequest { get; private set; }

    public BusinessConsoleErpContextRequest? LastSalesOrderListRequest { get; private set; }

    public BusinessConsoleErpSourceDocumentRequest? LastFinanceSourceDocumentRequest { get; private set; }

    public Task<BusinessConsoleCreateErpPurchaseRequisitionResponse> CreatePurchaseRequisitionFromSuggestionAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseRequisitionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpPurchaseRequisitionResponse("pr-001"));
    }

    public Task<BusinessConsoleCreateErpRequestForQuotationResponse> CreateRequestForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpRequestForQuotationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpRequestForQuotationResponse("rfq-001"));
    }

    public Task<BusinessConsoleReceiveErpSupplierQuotationResponse> ReceiveSupplierQuotationAsync(
        string internalBearerToken,
        BusinessConsoleReceiveErpSupplierQuotationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleReceiveErpSupplierQuotationResponse("sq-001"));
    }

    public Task<BusinessConsoleErpPurchaseOrderListResponse> ListPurchaseOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpContextRequest request,
        CancellationToken cancellationToken)
    {
        PurchaseOrderListCallCount++;
        LastInternalToken = internalBearerToken;
        LastPurchaseOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleErpPurchaseOrderListResponse(
            [
                new BusinessConsoleErpPurchaseOrderItem(
                    "PO-001",
                    "SUP-001",
                    "SITE-01",
                    "PartiallyReceived",
                    "partially-received",
                    2400m,
                    [
                        new BusinessConsoleErpPurchaseOrderLineItem(
                            "10",
                            "RM-SEAL-KIT",
                            "EA",
                            120m,
                            40m,
                            20m,
                            DateOnly.Parse("2026-06-06")),
                    ]),
            ]));
    }

    public Task<BusinessConsoleCreateErpPurchaseOrderResponse> CreatePurchaseOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpPurchaseOrderResponse("po-id-001"));
    }

    public Task<BusinessConsoleRecordErpPurchaseReceiptResponse> RecordPurchaseReceiptAsync(
        string internalBearerToken,
        BusinessConsoleRecordErpPurchaseReceiptRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleRecordErpPurchaseReceiptResponse("receipt-001"));
    }

    public Task<BusinessConsoleErpSalesOrderListResponse> ListSalesOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastSalesOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleErpSalesOrderListResponse(
        [
            new BusinessConsoleErpSalesOrderItem("SO-001", "CUST-001", "Released", 1200m),
        ]));
    }

    public Task<BusinessConsoleOpenErpOpportunityResponse> OpenOpportunityAsync(
        string internalBearerToken,
        BusinessConsoleOpenErpOpportunityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleOpenErpOpportunityResponse("opp-001"));
    }

    public Task<BusinessConsoleCreateErpQuotationResponse> CreateQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpQuotationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpQuotationResponse("quo-001"));
    }

    public Task<string> ApproveQuotationAsync(
        string internalBearerToken,
        BusinessConsoleApproveErpQuotationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult("approved");
    }

    public Task<BusinessConsoleCreateErpSalesOrderResponse> CreateSalesOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpSalesOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpSalesOrderResponse("so-id-001"));
    }

    public Task<BusinessConsoleReleaseErpDeliveryOrderResponse> ReleaseDeliveryOrderAsync(
        string internalBearerToken,
        BusinessConsoleReleaseErpDeliveryOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleReleaseErpDeliveryOrderResponse("do-id-001"));
    }

    public Task<BusinessConsoleCreateErpAccountPayableResponse> CreateAccountPayableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountPayableRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpAccountPayableResponse("ap-001"));
    }

    public Task<BusinessConsoleCreateErpAccountReceivableResponse> CreateAccountReceivableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountReceivableRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpAccountReceivableResponse("ar-001"));
    }

    public Task<BusinessConsoleCreateErpCostCandidateResponse> CreateCostCandidateAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpCostCandidateRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateErpCostCandidateResponse("cost-001"));
    }

    public Task<BusinessConsolePostErpJournalVoucherResponse> PostJournalVoucherAsync(
        string internalBearerToken,
        BusinessConsolePostErpJournalVoucherRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsolePostErpJournalVoucherResponse("jv-001"));
    }

    public Task<BusinessConsoleErpFinanceSummaryResponse> GetFinanceSummaryAsync(
        string internalBearerToken,
        BusinessConsoleErpContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleErpFinanceSummaryResponse(100m, 200m, 50m, 3));
    }

    public Task<BusinessConsoleErpPayableSourceDocumentResponse> GetPayableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastFinanceSourceDocumentRequest = request;
        return Task.FromResult(new BusinessConsoleErpPayableSourceDocumentResponse("AP-001", request.SourceDocumentNo, "SUP-001", 100m, 80m, "CNY", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)));
    }

    public Task<BusinessConsoleErpReceivableSourceDocumentResponse> GetReceivableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastFinanceSourceDocumentRequest = request;
        return Task.FromResult(new BusinessConsoleErpReceivableSourceDocumentResponse("AR-001", request.SourceDocumentNo, "CUST-001", 100m, 80m, "CNY", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)));
    }

    public Task<BusinessConsoleErpCostCandidateSourceDocumentResponse> GetCostCandidateBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastFinanceSourceDocumentRequest = request;
        return Task.FromResult(new BusinessConsoleErpCostCandidateSourceDocumentResponse("COST-001", request.SourceType ?? "production", request.SourceDocumentNo, 100m, "CNY", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)));
    }
}

internal sealed class RecordingBarcodeLabelClient : IBusinessBarcodeLabelClient
{
    public string? LastInternalToken { get; private set; }

    public BusinessConsoleCreateBarcodePrintBatchRequest? LastPrintBatchRequest { get; private set; }

    public BusinessConsoleRecordBarcodeScanRequest? LastScanRequest { get; private set; }

    public Task<BusinessConsoleCreateOrUpdateBarcodeRuleResponse> CreateOrUpdateRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeRuleRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateBarcodeRuleResponse("rule-001"));
    }

    public Task<BusinessConsoleBarcodeTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeTemplateListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleBarcodeTemplateListResponse(
        [
            new BusinessConsoleBarcodeTemplateItem("template-001", "box-label", "Box Label", "file-001", "{}", "active"),
        ]));
    }

    public Task<BusinessConsoleCreateOrUpdateBarcodeTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateBarcodeTemplateResponse("template-001"));
    }

    public Task<BusinessConsoleCreateBarcodePrintBatchResponse> CreatePrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleCreateBarcodePrintBatchRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastPrintBatchRequest = request;
        return Task.FromResult(new BusinessConsoleCreateBarcodePrintBatchResponse("print-batch-001"));
    }

    public Task<BusinessConsoleBarcodePrintBatchResponse> GetPrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleBarcodePrintBatchResponse(
            new BusinessConsoleBarcodePrintBatchDetail(
                request.PrintBatchId,
                "template-001",
                "work-order",
                "WO-001",
                "print-001",
                1,
                "created",
                [])));
    }

    public Task<BusinessConsoleRecordBarcodeScanResponse> RecordScanAsync(
        string internalBearerToken,
        BusinessConsoleRecordBarcodeScanRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastScanRequest = request;
        return Task.FromResult(new BusinessConsoleRecordBarcodeScanResponse("scan-001"));
    }

    public Task<BusinessConsoleBarcodeScanListResponse> ListScansAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeScanListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleBarcodeScanListResponse(
        [
            new BusinessConsoleBarcodeScanRecordItem(
                "scan-001",
                request.DeviceCode ?? "PDA-01",
                request.ScannedValue ?? "BC-001",
                request.SourceWorkflow ?? "wms-receipt",
                request.SourceDocumentId ?? "WO-001",
                "accepted",
                null,
                DateTimeOffset.Parse("2026-06-03T01:00:00Z", CultureInfo.InvariantCulture)),
        ]));
    }
}

internal sealed class RecordingSchedulingClient : IBusinessSchedulingClient
{
    public int ListCallCount { get; private set; }

    public int GetPlanCallCount { get; private set; }

    public int GetPlanGanttCallCount { get; private set; }

    public int ReleasePlanCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public SchedulingProblemContract? LastProblem { get; private set; }

    public BusinessConsoleSchedulingContextRequest? LastListRequest { get; private set; }

    public string? LastPlanId { get; private set; }

    public BusinessConsoleSchedulingPlanRequest? LastPlanRequest { get; private set; }

    public Task<SchedulePlanContract> PreviewPlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastProblem = problem;
        return Task.FromResult(BusinessGatewayProxyTests.CreateSchedulePlan(SchedulePlanStatusContract.Preview));
    }

    public Task<SchedulePlanContract> CreatePlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastProblem = problem;
        return Task.FromResult(BusinessGatewayProxyTests.CreateSchedulePlan(SchedulePlanStatusContract.Generated));
    }

    public Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingContextRequest request,
        CancellationToken cancellationToken)
    {
        ListCallCount++;
        LastInternalToken = internalBearerToken;
        LastListRequest = request;
        return Task.FromResult<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>>(
        [
            new(
                "plan-001",
                "problem-001",
                SchedulePlanStatusContract.Generated,
                DateTimeOffset.Parse("2026-06-01T08:05:00Z", CultureInfo.InvariantCulture),
                null,
                1,
                0,
                0),
        ]);
    }

    public Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken)
    {
        GetPlanCallCount++;
        LastInternalToken = internalBearerToken;
        LastPlanId = request.PlanId;
        LastPlanRequest = request;
        return Task.FromResult(BusinessGatewayProxyTests.CreateSchedulePlan());
    }

    public Task<IReadOnlyCollection<GanttScheduleItemContract>> GetPlanGanttAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken)
    {
        GetPlanGanttCallCount++;
        LastInternalToken = internalBearerToken;
        LastPlanId = request.PlanId;
        LastPlanRequest = request;
        return Task.FromResult<IReadOnlyCollection<GanttScheduleItemContract>>(
            BusinessGatewayProxyTests.CreateSchedulePlan().GanttItems);
    }

    public Task<BusinessConsoleReleaseSchedulePlanResponse> ReleasePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken)
    {
        ReleasePlanCallCount++;
        LastInternalToken = internalBearerToken;
        LastPlanId = request.PlanId;
        LastPlanRequest = request;
        return Task.FromResult(new BusinessConsoleReleaseSchedulePlanResponse(
            request.PlanId,
            SchedulePlanStatusContract.Released,
            DateTimeOffset.Parse("2026-06-01T10:00:00Z", CultureInfo.InvariantCulture)));
    }
}

internal sealed class RecordingIndustrialTelemetryClient : IBusinessIndustrialTelemetryClient
{
    public int AvailabilityCallCount { get; private set; }

    public int CurrentStateCallCount { get; private set; }

    public List<string> CurrentStateDeviceAssetIds { get; } = [];

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleEquipmentAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public string? LastDeviceAvailabilityDeviceAssetId { get; private set; }

    public string? LastCurrentStateDeviceAssetId { get; private set; }

    public BusinessConsoleEquipmentContextRequest? LastCurrentStateRequest { get; private set; }

    public EquipmentRuntimeAvailabilityResponse? RuntimeAvailabilityResponse { get; init; }

    public EquipmentRuntimeAvailabilityResponse? DeviceRuntimeAvailabilityResponse { get; init; }

    public Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryTagListResponse([]));
    }

    public Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryAlarmEventListResponse([]));
    }

    public Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryHistoryResponse([]));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        AvailabilityCallCount++;
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        return Task.FromResult(RuntimeAvailabilityResponse
            ?? BusinessGatewayProxyTests.CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDeviceAvailabilityDeviceAssetId = deviceAssetId;
        LastAvailabilityRequest = request;
        return Task.FromResult(DeviceRuntimeAvailabilityResponse
            ?? BusinessGatewayProxyTests.CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm));
    }

    public Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken)
    {
        CurrentStateCallCount++;
        CurrentStateDeviceAssetIds.Add(deviceAssetId);
        LastInternalToken = internalBearerToken;
        LastCurrentStateDeviceAssetId = deviceAssetId;
        LastCurrentStateRequest = request;
        return Task.FromResult(new EquipmentRuntimeCurrentStateResponse(
            1,
            request.OrganizationId,
            request.EnvironmentId,
            deviceAssetId,
            "RUNNING",
            DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture),
            true,
            [
                new EquipmentRuntimeAlarmSummary(
                    "alarm-001",
                    deviceAssetId,
                    "TEMP_HIGH",
                    "critical",
                    DateTimeOffset.Parse("2026-06-01T08:20:00Z", CultureInfo.InvariantCulture),
                "EXT-ALARM-001"),
            ]));
    }

    public Task<BusinessConsoleEquipmentAlarmListResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEquipmentAlarmListResponse(
        [
            new EquipmentRuntimeAlarmSummary(
                "alarm-001",
                "DEV-OIL-01",
                "TEMP_HIGH",
                "critical",
                DateTimeOffset.Parse("2026-06-01T08:20:00Z", CultureInfo.InvariantCulture),
                "EXT-ALARM-001"),
        ]));
    }
}

internal sealed class RecordingMaintenanceClient : IBusinessMaintenanceClient
{
    public int AvailabilityCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleEquipmentAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public string? LastDeviceAssetId { get; private set; }

    public EquipmentRuntimeAvailabilityResponse? AvailabilityResponse { get; init; }

    public EquipmentRuntimeAvailabilityResponse? AssetAvailabilityResponse { get; init; }

    public Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderListResponse([]));
    }

    public Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderItem(
            workOrderId,
            "DEV-OIL-01",
            "normal",
            "Open",
            null,
            null,
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture)));
    }

    public Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenancePlanListResponse([]));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        AvailabilityCallCount++;
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        return Task.FromResult(AvailabilityResponse
            ?? BusinessGatewayProxyTests.CreateAvailabilityResponse("maintenance-001", EquipmentRuntimeSourceType.MaintenanceWindow));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDeviceAssetId = deviceAssetId;
        LastAvailabilityRequest = request;
        return Task.FromResult(AssetAvailabilityResponse
            ?? BusinessGatewayProxyTests.CreateAvailabilityResponse("maintenance-001", EquipmentRuntimeSourceType.MaintenanceWindow));
    }
}

internal sealed class RecordingMesClient : IBusinessMesClient
{
    public int WorkOrderListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleMesListRequest? LastWorkOrderListRequest { get; private set; }

    public Exception? FoundationReadinessFailure { get; init; }

    public Exception? ProductionPlanReadinessFailure { get; init; }

    public Exception? ReleaseFailure { get; init; }

    public Exception? StartOperationFailure { get; init; }

    public IReadOnlyCollection<BusinessConsoleMesWorkOrderItem>? WorkOrders { get; init; }

    public IReadOnlyCollection<BusinessConsoleMesProductionPlanRow>? ProductionPlans { get; init; }

    public BusinessConsoleMesListRequest? LastProductionPlanListRequest { get; private set; }

    public BusinessConsoleMesConvertPlanToWorkOrderRequest? LastConvertPlanToWorkOrderRequest { get; private set; }

    public Task<BusinessConsoleMesReadinessArea> GetFoundationReadinessAreaAsync(
        string internalBearerToken,
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        if (FoundationReadinessFailure is not null)
        {
            throw FoundationReadinessFailure;
        }

        return Task.FromResult(new BusinessConsoleMesReadinessArea(areaCode, "Ready", []));
    }

    public Task<BusinessConsoleMesOverviewResponse> GetOverviewAsync(
        string internalBearerToken,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesOverviewResponse([], [], []));
    }

    public Task<BusinessConsoleMesProductionPlanListResponse> ListProductionPlansAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastProductionPlanListRequest = request;
        return Task.FromResult(new BusinessConsoleMesProductionPlanListResponse(ProductionPlans ?? []));
    }

    public Task<BusinessConsoleMesFoundationReadinessResponse> GetProductionPlanReadinessAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        if (ProductionPlanReadinessFailure is not null)
        {
            throw ProductionPlanReadinessFailure;
        }

        return Task.FromResult(new BusinessConsoleMesFoundationReadinessResponse("Ready", [], [], []));
    }

    public Task<BusinessConsoleAcceptedResponse> ConvertPlanToWorkOrderAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastConvertPlanToWorkOrderRequest = request;
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
    }

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        WorkOrderListCallCount++;
        LastInternalToken = internalBearerToken;
        LastWorkOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleMesWorkOrderListResponse(
            WorkOrders ??
            [
                new BusinessConsoleMesWorkOrderItem(
                    "wo-001",
                    "SKU-001",
                    null,
                    10,
                    0,
                    DateTimeOffset.Parse("2026-05-24T00:00:00Z"),
                    "released",
                    []),
            ]));
    }

    public Task<BusinessConsoleMesWorkOrderDetailResponse> GetWorkOrderDetailAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesWorkOrderDetailResponse(
            workOrderId,
            "SKU-001",
            null,
            10,
            "released",
            "Ready",
            [],
            []));
    }

    public Task<BusinessConsoleAcceptedResponse> ReleaseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesReleaseWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        if (ReleaseFailure is not null)
        {
            throw ReleaseFailure;
        }

        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
    }

    public Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesMaterialReadinessResponse(workOrderId, "Ready", [], []));
    }

    public Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesOperationTaskListResponse([]));
    }

    public Task<BusinessConsoleMesOperationTaskActionResponse> StartOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        if (StartOperationFailure is not null)
        {
            throw StartOperationFailure;
        }

        return Task.FromResult(new BusinessConsoleMesOperationTaskActionResponse(operationTaskId, "in-progress", DateTimeOffset.Parse("2026-05-30T00:00:00Z")));
    }

    public Task<BusinessConsoleMesOperationTaskActionResponse> PauseOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesOperationTaskActionResponse> ResumeOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesOperationTaskActionResponse> CompleteOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesWipSummaryResponse> GetWipSummaryAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesWipSummaryResponse([]));
    }

    public Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesProductionReportListResponse([]));
    }

    public Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDefectRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesReceiptRequestListResponse([]));
    }

    public Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesCreateReceiptResponse("receipt-1", "FGR-1"));
    }

    public Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesShiftHandoverListResponse> ListShiftHandoversAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> CreateShiftHandoverAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesTraceabilityResponse> GetBatchTraceabilityAsync(
        string internalBearerToken,
        string batchOrSerial,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesTraceabilityResponse> GetMaterialLotTraceabilityAsync(
        string internalBearerToken,
        string materialLotId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesCapacityImpactListResponse([]));
    }
}
