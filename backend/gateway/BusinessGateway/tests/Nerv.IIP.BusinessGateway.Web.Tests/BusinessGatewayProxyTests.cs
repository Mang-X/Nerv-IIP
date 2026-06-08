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
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new BusinessConsoleResourceItem("sku", "SKU-001", "Demo SKU 1", true, "v1"),
                new BusinessConsoleResourceItem("sku", "SKU-002", "Demo SKU 2", true, "v2"),
            ],
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

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev&skip=1&take=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 1, 25), masterData.LastListResourcesRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SKU-002", document.RootElement.GetProperty("data").GetProperty("resources")[0].GetProperty("code").GetString());
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
    public async Task Master_data_resource_lifecycle_facade_uses_internal_service_token()
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

        var detail = await client.GetAsync("/api/business-console/v1/master-data/resources/reference-data/powder?organizationId=org-001&environmentId=env-dev&codeSet=material-type");
        var update = await client.PatchAsJsonAsync("/api/business-console/v1/master-data/resources/sku/SKU-001", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            resourceType = "sku",
            code = "SKU-001",
            name = "Updated SKU",
            category = "raw-material",
            materialType = "powder",
        });
        var disable = await client.PostAsJsonAsync("/api/business-console/v1/master-data/resources/sku/SKU-001/disable", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            resourceType = "sku",
            code = "SKU-001",
            reason = "duplicate",
        });
        var enable = await client.PostAsJsonAsync("/api/business-console/v1/master-data/resources/sku/SKU-001/enable", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            resourceType = "sku",
            code = "SKU-001",
            reason = "reactivated",
        });

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleMasterDataResourceRequest("org-001", "env-dev", "reference-data", "powder", "material-type"), masterData.LastDetailRequest);
        Assert.Equal(new BusinessConsoleUpdateMasterDataResourceRequest("org-001", "env-dev", "sku", "SKU-001", Name: "Updated SKU", Category: "raw-material", MaterialType: "powder"), masterData.LastUpdateRequest);
        Assert.Contains(false, masterData.SetResourceEnabledCalls);
        Assert.Contains(masterData.SetResourceEnabledRequests, request => request.Reason == "duplicate");
        Assert.Contains(true, masterData.SetResourceEnabledCalls);
    }

    [Fact]
    public async Task Master_data_workshop_and_team_member_facades_use_internal_service_token()
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

        var listWorkshops = await client.GetAsync("/api/business-console/v1/master-data/workshops?organizationId=org-001&environmentId=env-dev&includeDisabled=true&skip=2&take=20");
        var createWorkshop = await client.PostAsJsonAsync("/api/business-console/v1/master-data/workshops", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "WS-001",
            name = "Workshop 1",
            siteCode = "SITE-001",
            managerUserId = "user-manager",
            description = "Process area",
        });
        var addMember = await client.PostAsJsonAsync("/api/business-console/v1/master-data/teams/T-001/members", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            teamCode = "T-001",
            userId = "user-001",
            isLeader = true,
            effectiveFrom = "2026-01-01",
        });
        var listMembers = await client.GetAsync("/api/business-console/v1/master-data/teams/T-001/members?organizationId=org-001&environmentId=env-dev&includeDisabled=true");
        var removeMember = await client.DeleteAsync("/api/business-console/v1/master-data/teams/T-001/members/user-001?organizationId=org-001&environmentId=env-dev&reason=transferred");

        Assert.Equal(HttpStatusCode.OK, listWorkshops.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createWorkshop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addMember.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listMembers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeMember.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleListResourcesRequest("org-001", "env-dev", "workshop", true, 2, 20), masterData.LastListResourcesRequest);
        Assert.Equal(new BusinessConsoleCreateWorkshopRequest("org-001", "env-dev", "WS-001", "Workshop 1", "SITE-001", "user-manager", "Process area"), masterData.LastCreateWorkshopRequest);
        Assert.Equal(new BusinessConsoleAddTeamMemberRequest("org-001", "env-dev", "T-001", "user-001", true, new DateOnly(2026, 1, 1), null), masterData.LastAddTeamMemberRequest);
        Assert.Equal(new BusinessConsoleListTeamMembersRequest("org-001", "env-dev", "T-001", true), masterData.LastListTeamMembersRequest);
        Assert.Equal(new BusinessConsoleRemoveTeamMemberRequest("org-001", "env-dev", "T-001", "user-001", "transferred"), masterData.LastRemoveTeamMemberRequest);
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

        var response = await client.GetAsync("/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&skip=5&take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", quality.LastInternalToken);
        Assert.Equal(new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", Skip: 5, Take: 20), quality.LastNcrListRequest);
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

        var response = await client.GetAsync("/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=filter&workCenterId=WC-FILTER&shiftId=SHIFT-FILTER&deviceAssetId=DEV-FILTER&skip=5&take=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.Equal(new BusinessConsoleMesListRequest(
            "org-001",
            "env-dev",
            "released",
            "filter",
            "WC-FILTER",
            "SHIFT-FILTER",
            "DEV-FILTER",
            Skip: 5,
            Take: 15), mes.LastWorkOrderListRequest);
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

        var response = await client.GetAsync("/api/business-console/v1/mes/production-plans?organizationId=org-001&environmentId=env-dev&status=Converted&keyword=SUG-001&source=DemandPlanning&readinessStatus=Ready&skip=10&take=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.Equal(new BusinessConsoleMesProductionPlanListRequest(
            "org-001",
            "env-dev",
            "Converted",
            Keyword: "SUG-001",
            Source: "DemandPlanning",
            ReadinessStatus: "Ready",
            Skip: 10,
            Take: 15), mes.LastProductionPlanListRequest);
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
    public async Task Engineering_write_facades_use_internal_service_token_for_downstream_business_service()
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

        var response = await client.PostAsJsonAsync("/api/business-console/v1/engineering/manufacturing-boms/release", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            bomCode = "MBOM-001",
            revision = "A",
            skuCode = "SKU-001",
            engineeringBomCode = "EBOM-001",
            engineeringBomRevision = "A",
            effectiveDate = "2026-06-01",
            materialLines = new[] { new { skuCode = "RM-001", quantity = 2.5m, unitOfMeasureCode = "KG", scrapRate = 0.01m } },
            recipeLines = Array.Empty<object>(),
            idempotencyKey = "mbom-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", engineering.LastInternalToken);
        Assert.Equal("MBOM-001", engineering.LastReleaseManufacturingBomRequest!.BomCode);
        Assert.Equal("SKU-001", engineering.LastReleaseManufacturingBomRequest.SkuCode);
        Assert.Equal("RM-001", engineering.LastReleaseManufacturingBomRequest.MaterialLines.Single().SkuCode);
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

        var response = await client.GetAsync("/api/business-console/v1/erp/procurement/purchase-orders?organizationId=org-001&environmentId=env-dev&status=Released&keyword=SUP-001&skip=5&take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "Released", "SUP-001", 5, 20), erp.LastPurchaseOrderListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PO-001", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("purchaseOrderNo").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
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

        var sales = await client.GetAsync("/api/business-console/v1/erp/sales/sales-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=CUST-001&skip=10&take=20");
        var payable = await client.GetAsync("/api/business-console/v1/erp/finance/payables/by-source?organizationId=org-001&environmentId=env-dev&sourceDocumentNo=PR-001");

        Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
        Assert.Equal(HttpStatusCode.OK, payable.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "released", "CUST-001", 10, 20), erp.LastSalesOrderListRequest);
        Assert.Equal(new BusinessConsoleErpSourceDocumentRequest("org-001", "env-dev", "PR-001"), erp.LastFinanceSourceDocumentRequest);
    }

    [Fact]
    public async Task Erp_finance_lists_use_internal_service_token_and_pass_server_paging_filters()
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

        var payables = await client.GetAsync("/api/business-console/v1/erp/finance/payables?organizationId=org-001&environmentId=env-dev&status=open&keyword=SUP-001&skip=2&take=15");
        var receivables = await client.GetAsync("/api/business-console/v1/erp/finance/receivables?organizationId=org-001&environmentId=env-dev&status=open&keyword=CUST-001&skip=3&take=16");
        var costs = await client.GetAsync("/api/business-console/v1/erp/finance/cost-candidates?organizationId=org-001&environmentId=env-dev&status=pending&keyword=production&skip=4&take=17");

        Assert.Equal(HttpStatusCode.OK, payables.StatusCode);
        Assert.Equal(HttpStatusCode.OK, receivables.StatusCode);
        Assert.Equal(HttpStatusCode.OK, costs.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "open", "SUP-001", 2, 15), erp.LastPayableListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "open", "CUST-001", 3, 16), erp.LastReceivableListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "pending", "production", 4, 17), erp.LastCostCandidateListRequest);
    }

    [Fact]
    public async Task Erp_create_only_documents_now_have_list_facades_with_server_paging_filters()
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

        var rfqs = await client.GetAsync("/api/business-console/v1/erp/procurement/rfqs?organizationId=org-001&environmentId=env-dev&status=Open&keyword=SUP-002&skip=1&take=11");
        var opportunities = await client.GetAsync("/api/business-console/v1/erp/sales/opportunities?organizationId=org-001&environmentId=env-dev&status=open&keyword=CUST-002&skip=2&take=12");
        var quotations = await client.GetAsync("/api/business-console/v1/erp/sales/quotations?organizationId=org-001&environmentId=env-dev&status=Draft&keyword=SKU-FG&skip=3&take=13");
        var deliveries = await client.GetAsync("/api/business-console/v1/erp/sales/delivery-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=DO-001&skip=4&take=14");
        var vouchers = await client.GetAsync("/api/business-console/v1/erp/finance/vouchers?organizationId=org-001&environmentId=env-dev&status=posted&keyword=6001&skip=5&take=15");

        Assert.Equal(HttpStatusCode.OK, rfqs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, opportunities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quotations.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deliveries.StatusCode);
        Assert.Equal(HttpStatusCode.OK, vouchers.StatusCode);
        Assert.Equal("internal-test-token", erp.LastInternalToken);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "Open", "SUP-002", 1, 11), erp.LastRequestForQuotationListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "open", "CUST-002", 2, 12), erp.LastOpportunityListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "Draft", "SKU-FG", 3, 13), erp.LastQuotationListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "released", "DO-001", 4, 14), erp.LastDeliveryOrderListRequest);
        Assert.Equal(new BusinessConsoleErpListRequest("org-001", "env-dev", "posted", "6001", 5, 15), erp.LastJournalVoucherListRequest);
        using var document = JsonDocument.Parse(await quotations.Content.ReadAsStringAsync());
        Assert.Equal("QUO-001", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("quotationNo").GetString());
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

        var response = await client.GetAsync("/api/business-console/v1/approval/templates?organizationId=org-001&environmentId=env-dev&documentType=purchase-order&skip=2&take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", approval.LastInternalToken);
        Assert.Equal(new BusinessConsoleApprovalTemplateListRequest("org-001", "env-dev", "purchase-order", null, 2, 20), approval.LastTemplateListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("purchase-order-default", document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("templateCode").GetString());
    }

    [Fact]
    public async Task Approval_center_list_and_delegation_facades_forward_filters_and_internal_token()
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

        var chains = await client.GetAsync("/api/business-console/v1/approval/chains?organizationId=org-001&environmentId=env-dev&status=pending&startedBy=u-requester&documentType=purchase-order&documentId=PO-001&skip=1&take=10");
        var decisions = await client.GetAsync("/api/business-console/v1/approval/decisions?organizationId=org-001&environmentId=env-dev&chainId=chain-001&actorType=user&actorRef=u-manager&decision=approve&skip=2&take=11");
        var tasks = await client.GetAsync("/api/business-console/v1/approval/tasks?organizationId=org-001&environmentId=env-dev&actorType=user&actorRef=u-manager&skip=3&take=12");
        var delegations = await client.GetAsync("/api/business-console/v1/approval/delegations?organizationId=org-001&environmentId=env-dev&status=active&delegateActorRef=u-backup&skip=4&take=13");
        var createDelegation = await client.PostAsJsonAsync("/api/business-console/v1/approval/delegations", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            delegatorActorType = "user",
            delegatorActorRef = "u-manager",
            delegateActorType = "user",
            delegateActorRef = "u-backup",
            documentType = "purchase-order",
            effectiveFromUtc = "2026-06-01T00:00:00Z",
            effectiveToUtc = "2026-06-30T00:00:00Z",
            reason = "travel",
            createdBy = "u-manager",
        });
        var revokeDelegation = await client.PostAsJsonAsync(
            "/api/business-console/v1/approval/delegations/delegation-001/revoke?organizationId=org-001&environmentId=env-dev",
            new { revokedBy = "u-manager" });

        Assert.Equal(HttpStatusCode.OK, chains.StatusCode);
        Assert.Equal(HttpStatusCode.OK, decisions.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tasks.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delegations.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createDelegation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, revokeDelegation.StatusCode);
        Assert.Equal("internal-test-token", approval.LastInternalToken);
        Assert.Equal(new BusinessConsoleApprovalChainListRequest("org-001", "env-dev", "pending", "u-requester", null, "purchase-order", "PO-001", 1, 10), approval.LastChainListRequest);
        Assert.Equal(new BusinessConsoleApprovalDecisionListRequest("org-001", "env-dev", "chain-001", "user", "u-manager", "approve", null, null, 2, 11), approval.LastDecisionListRequest);
        Assert.Equal(new BusinessConsoleApprovalTaskListRequest("org-001", "env-dev", "user", "u-manager", 3, 12), approval.LastRequest);
        Assert.Equal(new BusinessConsoleApprovalDelegationListRequest("org-001", "env-dev", "active", null, "u-backup", null, 4, 13), approval.LastDelegationListRequest);
        Assert.Equal("u-backup", approval.LastCreateDelegationRequest?.DelegateActorRef);
        Assert.Equal("delegation-001", approval.LastRevokeDelegationRequest?.DelegationId);
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
    public async Task Barcode_facade_forwards_rule_print_batch_template_and_scan_list_paging()
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

        var rules = await client.GetAsync("/api/business-console/v1/barcode/rules?organizationId=org-001&environmentId=env-dev&status=active&keyword=FG&skip=1&take=10");
        var templates = await client.GetAsync("/api/business-console/v1/barcode/templates?organizationId=org-001&environmentId=env-dev&status=active&skip=2&take=20");
        var batches = await client.GetAsync("/api/business-console/v1/barcode/print-batches?organizationId=org-001&environmentId=env-dev&sourceDocumentType=work-order&sourceDocumentId=WO-001&status=completed&skip=3&take=30");
        var scans = await client.GetAsync("/api/business-console/v1/barcode/scans?organizationId=org-001&environmentId=env-dev&deviceCode=PDA-01&sourceWorkflow=wms.receiving&skip=4&take=40");

        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);
        Assert.Equal(HttpStatusCode.OK, templates.StatusCode);
        Assert.Equal(HttpStatusCode.OK, batches.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scans.StatusCode);
        Assert.Equal("internal-test-token", barcode.LastInternalToken);
        Assert.Equal(new BusinessConsoleBarcodeRuleListRequest("org-001", "env-dev", "active", "FG", 1, 10), barcode.LastRuleListRequest);
        Assert.Equal(new BusinessConsoleBarcodeTemplateListRequest("org-001", "env-dev", "active", 2, 20), barcode.LastTemplateListRequest);
        Assert.Equal(new BusinessConsoleBarcodePrintBatchListRequest("org-001", "env-dev", "work-order", "WO-001", "completed", 3, 30), barcode.LastPrintBatchListRequest);
        Assert.Equal(new BusinessConsoleBarcodeScanListRequest("org-001", "env-dev", "PDA-01", null, "wms.receiving", null, 4, 40), barcode.LastScanListRequest);
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
    public async Task Telemetry_rule_and_oee_facades_forward_internal_token_and_scope()
    {
        var industrialTelemetry = new RecordingIndustrialTelemetryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(industrialTelemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        using var createResponse = await client.PostAsJsonAsync("/api/business-console/v1/telemetry/alarm-rules", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-OIL-01",
            ruleCode = "OIL_TEMP_RULE",
            alarmCode = "OIL_TEMP_HIGH",
            severity = "warning",
            tagKey = "temperature",
            comparisonOperator = ">=",
            thresholdValue = 95m,
            unitCode = "celsius",
            isEnabled = true,
        });
        using var listResponse = await client.GetAsync("/api/business-console/v1/telemetry/alarm-rules?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OIL-01");
        using var oeeResponse = await client.GetAsync("/api/business-console/v1/telemetry/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OIL-01&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oeeResponse.StatusCode);
        Assert.Equal("internal-test-token", industrialTelemetry.LastInternalToken);
        Assert.Equal(new BusinessConsoleTelemetryAlarmRuleListRequest("org-001", "env-dev", "DEV-OIL-01", null), industrialTelemetry.LastAlarmRuleListRequest);
        Assert.Equal("OIL_TEMP_RULE", industrialTelemetry.LastAlarmRuleUpsertRequest?.RuleCode);
        Assert.Equal(new BusinessConsoleTelemetryOeeRequest(
            "org-001",
            "env-dev",
            "DEV-OIL-01",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture)),
            industrialTelemetry.LastOeeRequest);
        using var oeeDocument = JsonDocument.Parse(await oeeResponse.Content.ReadAsStringAsync());
        var oee = oeeDocument.RootElement.GetProperty("data");
        Assert.True(oee.GetProperty("performanceRateEstimated").GetBoolean());
        Assert.True(oee.GetProperty("qualityRateEstimated").GetBoolean());
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
        var telemetryHandler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, TelemetryResponseFor(request)));
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
        await telemetry.ListActiveAlarmsAsync("internal-token-001", new BusinessConsoleEquipmentAlarmListRequest("org-001", "env-dev", null, "raised"), CancellationToken.None);
        await telemetry.ListAlarmRulesAsync("internal-token-001", new BusinessConsoleTelemetryAlarmRuleListRequest("org-001", "env-dev", "DEV-OIL-01", true), CancellationToken.None);
        await telemetry.CreateOrUpdateAlarmRuleAsync("internal-token-001", new BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest("org-001", "env-dev", "DEV-OIL-01", "RULE-001", "TEMP_HIGH", "warning", "temperature", ">=", 95m, "celsius", true), CancellationToken.None);
        await telemetry.QueryOeeAsync("internal-token-001", new BusinessConsoleTelemetryOeeRequest("org-001", "env-dev", "DEV-OIL-01", request.WindowStartUtc, request.WindowEndUtc), CancellationToken.None);
        await maintenance.GetAvailabilityWindowsAsync("internal-token-001", request, CancellationToken.None);
        await maintenance.GetAssetAvailabilityWindowsAsync("internal-token-001", "DEV-OIL-01", request, CancellationToken.None);

        Assert.All(telemetryHandler.Requests, sent => Assert.Equal("internal-token-001", sent.Headers.Authorization?.Parameter));
        Assert.Equal("/api/business/v1/iiot/runtime-availability", telemetryHandler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08%3A00%3A00.0000000%2B00%3A00&windowEndUtc=2026-06-01T16%3A00%3A00.0000000%2B00%3A00&deviceAssetIds=DEV-OIL-01", telemetryHandler.Requests[0].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/iiot/devices/DEV-OIL-01/runtime-availability", telemetryHandler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("/api/business/v1/iiot/devices/DEV-OIL-01/current-state", telemetryHandler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev", telemetryHandler.Requests[2].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/iiot/alarms", telemetryHandler.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&status=raised&skip=0&take=100", telemetryHandler.Requests[3].RequestUri!.Query.TrimStart('?'));
        Assert.Equal("/api/business/v1/iiot/alarm-rules", telemetryHandler.Requests[4].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OIL-01&isEnabled=true&skip=0&take=100", telemetryHandler.Requests[4].RequestUri!.Query.TrimStart('?'));
        Assert.Equal(HttpMethod.Post, telemetryHandler.Requests[5].Method);
        Assert.Equal("/api/business/v1/iiot/alarm-rules", telemetryHandler.Requests[5].RequestUri!.AbsolutePath);
        Assert.Equal("/api/business/v1/iiot/oee", telemetryHandler.Requests[6].RequestUri!.AbsolutePath);
        Assert.Equal("organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OIL-01&windowStartUtc=2026-06-01T08%3A00%3A00.0000000%2B00%3A00&windowEndUtc=2026-06-01T16%3A00%3A00.0000000%2B00%3A00", telemetryHandler.Requests[6].RequestUri!.Query.TrimStart('?'));
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
            data = new
            {
                items = new[]
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
                skip = 0,
                take = 100,
                total = 1,
            },
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var response = await client.ListWorkOrdersAsync(
            "internal-token-001",
            new BusinessConsoleMaintenanceListRequest("org-001", "env-dev"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("alarm-001", item.SourceAlarmId);
        Assert.Null(item.RelatedAlarmId);
    }

    [Fact]
    public async Task Maintenance_http_client_forwards_write_operations_to_backend_maintenance_paths()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/business/v1/maintenance/work-orders" => JsonResponse(HttpStatusCode.OK, new { data = new { workOrderId = "wo-maint-001" } }),
            "/api/business/v1/maintenance/work-orders/wo-maint-001/complete" => JsonResponse(HttpStatusCode.OK, new { data = new { accepted = true } }),
            "/api/business/v1/maintenance/plans" => JsonResponse(HttpStatusCode.OK, new { data = new { planId = "plan-001" } }),
            "/api/business/v1/maintenance/inspections" => JsonResponse(HttpStatusCode.OK, new { data = new { inspectionId = "inspection-001" } }),
            "/api/business/v1/maintenance/spare-parts" => JsonResponse(HttpStatusCode.OK, new { data = new { sparePartLineId = "spare-line-001" } }),
            _ => JsonResponse(HttpStatusCode.NotFound, new { message = "unexpected path" }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        await client.CreateWorkOrderAsync(
            "internal-token-001",
            new BusinessConsoleCreateMaintenanceWorkOrderRequest("org-001", "env-dev", "DEV-PRESS-01", "high", "alarm-001", "operator-001", null),
            CancellationToken.None);
        await client.CompleteWorkOrderAsync(
            "internal-token-001",
            "wo-maint-001",
            new BusinessConsoleCompleteMaintenanceWorkOrderRequest(
                "org-001",
                "env-dev",
                "fixed",
                "planned-maintenance",
                30,
                [new BusinessConsoleMaintenanceSparePartInput("SPARE-001", 1, "EA")]),
            CancellationToken.None);
        await client.CreatePlanAsync(
            "internal-token-001",
            new BusinessConsoleCreateMaintenancePlanRequest(
                "org-001",
                "env-dev",
                "DEV-PRESS-01",
                "PLAN-001",
                "monthly",
                DateOnly.Parse("2026-06-01", CultureInfo.InvariantCulture),
                "maintenance-team",
                DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse("2026-06-01T10:00:00Z", CultureInfo.InvariantCulture)),
            CancellationToken.None);
        await client.RecordInspectionAsync(
            "internal-token-001",
            new BusinessConsoleRecordMaintenanceInspectionRequest(
                "org-001",
                "env-dev",
                "plan-001",
                "wo-maint-001",
                "inspector-001",
                "pass",
                DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture)),
            CancellationToken.None);
        await client.CreateSparePartAsync(
            "internal-token-001",
            new BusinessConsoleCreateMaintenanceSparePartRequest("org-001", "env-dev", "wo-maint-001", "SPARE-001", 1, "EA"),
            CancellationToken.None);

        Assert.All(handler.Requests, sent => Assert.Equal("internal-token-001", sent.Headers.Authorization?.Parameter));
        AssertRequest(handler.Requests[0], HttpMethod.Post, "/api/business/v1/maintenance/work-orders");
        AssertRequest(handler.Requests[1], HttpMethod.Post, "/api/business/v1/maintenance/work-orders/wo-maint-001/complete");
        AssertRequest(handler.Requests[2], HttpMethod.Post, "/api/business/v1/maintenance/plans");
        AssertRequest(handler.Requests[3], HttpMethod.Post, "/api/business/v1/maintenance/inspections");
        AssertRequest(handler.Requests[4], HttpMethod.Post, "/api/business/v1/maintenance/spare-parts");
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

    [Theory]
    [InlineData("/api/business-console/v1/master-data/business-partners", "name")]
    [InlineData("/api/business-console/v1/master-data/units-of-measure", "name")]
    [InlineData("/api/business-console/v1/master-data/uom-conversions", "fromUomCode")]
    [InlineData("/api/business-console/v1/master-data/sites", "timezone")]
    [InlineData("/api/business-console/v1/master-data/production-lines", "siteCode")]
    [InlineData("/api/business-console/v1/master-data/work-centers", "capacityUnit")]
    [InlineData("/api/business-console/v1/master-data/device-assets", "serialNo")]
    [InlineData("/api/business-console/v1/master-data/shifts", "name")]
    [InlineData("/api/business-console/v1/master-data/work-calendars", "name")]
    [InlineData("/api/business-console/v1/master-data/teams", "departmentCode")]
    [InlineData("/api/business-console/v1/master-data/departments", "name")]
    [InlineData("/api/business-console/v1/master-data/personnel-skills", "skillCode")]
    [InlineData("/api/business-console/v1/master-data/reference-data", "codeSet")]
    public async Task Master_data_create_facades_reject_invalid_required_fields(
        string gatewayPath,
        string fieldName)
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        var body = BusinessConsoleTestRequestBodies.ValidMasterDataCreateBody(gatewayPath);
        body[fieldName] = string.Empty;

        var response = await client.PostAsJsonAsync(
            $"{gatewayPath}?organizationId=org-001&environmentId=env-dev",
            body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, masterData.CreateResourceCallCount);
    }

    [Theory]
    [InlineData("/api/business-console/v1/engineering/documents", "fileId")]
    [InlineData("/api/business-console/v1/engineering/items", "name")]
    [InlineData("/api/business-console/v1/engineering/engineering-boms/release", "parentItemCode")]
    [InlineData("/api/business-console/v1/engineering/manufacturing-boms/release", "skuCode")]
    [InlineData("/api/business-console/v1/engineering/routings/release", "skuCode")]
    [InlineData("/api/business-console/v1/engineering/engineering-changes/release", "reason")]
    [InlineData("/api/business-console/v1/engineering/production-versions", "skuCode")]
    [InlineData("/api/business-console/v1/engineering/production-versions/pv-001", "mbomVersionId")]
    [InlineData("/api/business-console/v1/engineering/production-versions/pv-001/archive", "reason")]
    public async Task Product_engineering_write_facades_reject_invalid_required_fields(
        string gatewayPath,
        string fieldName)
    {
        var engineering = new RecordingProductEngineeringClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(engineering);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        var body = BusinessConsoleTestRequestBodies.ValidEngineeringWriteBody(gatewayPath);
        body[fieldName] = string.Empty;
        var method = gatewayPath == "/api/business-console/v1/engineering/production-versions/pv-001"
            ? HttpMethod.Put
            : HttpMethod.Post;
        using var request = new HttpRequestMessage(
            method,
            $"{gatewayPath}?organizationId=org-001&environmentId=env-dev")
        {
            Content = JsonContent.Create(body),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, engineering.WriteCallCount);
    }

    [Theory]
    [InlineData("/api/business-console/v1/master-data/business-partners", "/api/business/v1/master-data/partners")]
    [InlineData("/api/business-console/v1/master-data/units-of-measure", "/api/business/v1/master-data/units-of-measure")]
    [InlineData("/api/business-console/v1/master-data/uom-conversions", "/api/business/v1/master-data/uom-conversions")]
    [InlineData("/api/business-console/v1/master-data/sites", "/api/business/v1/master-data/sites")]
    [InlineData("/api/business-console/v1/master-data/production-lines", "/api/business/v1/master-data/production-lines")]
    [InlineData("/api/business-console/v1/master-data/work-centers", "/api/business/v1/master-data/work-centers")]
    [InlineData("/api/business-console/v1/master-data/device-assets", "/api/business/v1/master-data/device-assets")]
    [InlineData("/api/business-console/v1/master-data/shifts", "/api/business/v1/master-data/shifts")]
    [InlineData("/api/business-console/v1/master-data/work-calendars", "/api/business/v1/master-data/work-calendars")]
    [InlineData("/api/business-console/v1/master-data/teams", "/api/business/v1/master-data/teams")]
    [InlineData("/api/business-console/v1/master-data/departments", "/api/business/v1/master-data/departments")]
    [InlineData("/api/business-console/v1/master-data/personnel-skills", "/api/business/v1/master-data/personnel-skills")]
    [InlineData("/api/business-console/v1/master-data/reference-data", "/api/business/v1/master-data/reference-data")]
    public async Task Master_data_create_facades_forward_internal_token_to_downstream_create_endpoint(
        string gatewayPath,
        string downstreamPath)
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

        var response = await client.PostAsJsonAsync(
            $"{gatewayPath}?organizationId=org-001&environmentId=env-dev",
            ValidMasterDataCreateBody(gatewayPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, masterData.CreateResourceCallCount);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(downstreamPath, masterData.LastCreateResourcePath);
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", true, Take: 12),
            CancellationToken.None);

        Assert.Equal("SKU-HTTP", response.Resources.Single().Code);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=sku&includeDisabled=true&skip=0&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token-001", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Master_data_http_client_forwards_reference_code_set_and_lifecycle_paths()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resourceType = "reference-data",
                code = "powder",
                displayName = "Powder",
                active = true,
                snapshotVersion = "v1",
                organizationId = "org-001",
                environmentId = "env-dev",
                codeSet = "material-type",
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        await client.GetResourceDetailAsync(
            "internal-token-001",
            new BusinessConsoleMasterDataResourceRequest("org-001", "env-dev", "reference-data", "powder", "material-type"),
            CancellationToken.None);
        await client.UpdateResourceAsync(
            "internal-token-001",
            new BusinessConsoleUpdateMasterDataResourceRequest("org-001", "env-dev", "sku", "SKU-001", Name: "Updated SKU"),
            CancellationToken.None);
        await client.SetResourceEnabledAsync(
            "internal-token-001",
            new BusinessConsoleSetMasterDataResourceEnabledRequest("org-001", "env-dev", "sku", "SKU-001", Reason: "duplicate"),
            false,
            CancellationToken.None);

        AssertRequest(handler.Requests[0], HttpMethod.Get, "/api/business/v1/master-data/resources/reference-data/powder?organizationId=org-001&environmentId=env-dev&codeSet=material-type");
        AssertRequest(handler.Requests[1], HttpMethod.Patch, "/api/business/v1/master-data/resources/sku/SKU-001");
        AssertRequest(handler.Requests[2], HttpMethod.Post, "/api/business/v1/master-data/resources/sku/SKU-001/disable");
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, Take: 12),
            CancellationToken.None);

        Assert.Equal("/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=sku&skip=0&take=12", handler.Requests.Single().RequestUri!.PathAndQuery);
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", true, Take: 12),
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
                total = 1,
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
            new BusinessConsoleListProductionVersionsRequest("org-001", "env-dev", "FG-FRONT-SHOCK", "active", Skip: 5, Take: 15),
            CancellationToken.None);
        await client.ResolveProductionVersionAsync(
            "internal-token-001",
            new BusinessConsoleResolveProductionVersionRequest("org-001", "env-dev", "FG-FRONT-SHOCK", DateOnly.Parse("2025-01-15"), 100),
            CancellationToken.None);

        Assert.Equal("/api/business/v1/engineering/production-versions?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&status=active&skip=5&take=15", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", handler.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("/api/business/v1/engineering/production-versions/resolve?organizationId=org-001&environmentId=env-dev&skuCode=FG-FRONT-SHOCK&effectiveDate=2025-01-15&lotSize=100", handler.Requests[1].RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", handler.Requests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Product_engineering_http_client_forwards_write_facades_to_product_engineering_routes()
    {
        var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, ResponseForEngineeringWriteRequest(request)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://engineering.local") };
        var client = new HttpBusinessProductEngineeringClient(httpClient);

        await client.RegisterEngineeringDocumentAsync(
            "internal-token-001",
            new BusinessConsoleRegisterEngineeringDocumentRequest("org-001", "env-dev", "DOC-001", "A", "file-001", "design.dwg", "application/dwg", "cad"),
            CancellationToken.None);
        await client.CreateEngineeringItemRevisionAsync(
            "internal-token-001",
            new BusinessConsoleCreateEngineeringItemRevisionRequest("org-001", "env-dev", "ITEM-001", "A", "Demo", true),
            CancellationToken.None);
        await client.ReleaseEngineeringBomAsync(
            "internal-token-001",
            new BusinessConsoleReleaseEngineeringBomRequest("org-001", "env-dev", "EBOM-001", "A", "ITEM-001", new DateOnly(2026, 6, 1), [new BusinessConsoleBomLineRequest("ITEM-002", 1, "EA")]),
            CancellationToken.None);
        await client.ListManufacturingBomsAsync(
            "internal-token-001",
            new BusinessConsoleListManufacturingBomsRequest("org-001", "env-dev", "SKU-001", "Published", Skip: 3, Take: 25),
            CancellationToken.None);
        await client.ReleaseManufacturingBomAsync(
            "internal-token-001",
            new BusinessConsoleReleaseManufacturingBomRequest("org-001", "env-dev", "MBOM-001", "A", "SKU-001", "EBOM-001", "A", new DateOnly(2026, 6, 1), [new BusinessConsoleManufacturingBomMaterialLineRequest("RM-001", 1, "EA", 0)], []),
            CancellationToken.None);
        await client.ReleaseRoutingAsync(
            "internal-token-001",
            new BusinessConsoleReleaseRoutingRequest("org-001", "env-dev", "RTG-001", "A", "SKU-001", new DateOnly(2026, 6, 1), [new BusinessConsoleRoutingOperationRequest(10, "WC-001", "Assemble", 15)]),
            CancellationToken.None);
        await client.ReleaseEngineeringChangeAsync(
            "internal-token-001",
            new BusinessConsoleReleaseEngineeringChangeRequest("org-001", "env-dev", "ECO-001", "Initial", "approval-001", new DateOnly(2026, 6, 1), [new BusinessConsoleAffectedVersionRequest("mbom", "MBOM-001:A")]),
            CancellationToken.None);
        await client.CreateProductionVersionAsync(
            "internal-token-001",
            new BusinessConsoleCreateProductionVersionRequest("org-001", "env-dev", "SKU-001", "MBOM-001:A", "RTG-001:A", new DateOnly(2026, 6, 1), null, 1, 100, 10, true),
            CancellationToken.None);
        await client.UpdateProductionVersionAsync(
            "internal-token-001",
            "pv-001",
            new BusinessConsoleUpdateProductionVersionRequest("pv-001", "org-001", "env-dev", "MBOM-001:B", "RTG-001:B", new DateOnly(2026, 7, 1), null, 1, 100, 20, true),
            CancellationToken.None);
        await client.ArchiveProductionVersionAsync(
            "internal-token-001",
            "pv-001",
            new BusinessConsoleArchiveProductionVersionRequest("pv-001", "org-001", "env-dev", "Superseded"),
            CancellationToken.None);

        Assert.Collection(
            handler.Requests,
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/documents"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/items"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/engineering-boms/release"),
            request => AssertRequest(request, HttpMethod.Get, "/api/business/v1/engineering/manufacturing-boms?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&status=Published&skip=3&take=25"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/manufacturing-boms/release"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/routings/release"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/engineering-changes/release"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/production-versions"),
            request => AssertRequest(request, HttpMethod.Put, "/api/business/v1/engineering/production-versions/pv-001"),
            request => AssertRequest(request, HttpMethod.Post, "/api/business/v1/engineering/production-versions/pv-001/archive"));
        Assert.All(handler.Requests, request => Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter));
    }

    [Fact]
    public void Product_engineering_client_contract_exposes_business_console_write_facades()
    {
        var methodNames = typeof(IBusinessProductEngineeringClient)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RegisterEngineeringDocumentAsync", methodNames);
        Assert.Contains("CreateEngineeringItemRevisionAsync", methodNames);
        Assert.Contains("ReleaseEngineeringBomAsync", methodNames);
        Assert.Contains("ListManufacturingBomsAsync", methodNames);
        Assert.Contains("ReleaseManufacturingBomAsync", methodNames);
        Assert.Contains("ReleaseRoutingAsync", methodNames);
        Assert.Contains("ReleaseEngineeringChangeAsync", methodNames);
        Assert.Contains("CreateProductionVersionAsync", methodNames);
        Assert.Contains("UpdateProductionVersionAsync", methodNames);
        Assert.Contains("ArchiveProductionVersionAsync", methodNames);
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
                total = 1,
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
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", Keyword: "NCR-001", Skip: 4, Take: 12),
            CancellationToken.None);

        Assert.Equal("ncr-001", response.Items.Single().Id);
        Assert.Equal(1, response.Total);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&keyword=NCR-001&skip=4&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_inspection_plan_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                total = 1,
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
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "active", Keyword: "IP-001", Take: 12),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("plan-001", item.Id);
        Assert.Equal("IP-001", item.Code);
        Assert.Equal("active", item.Status);
        Assert.Equal("incoming", item.Category);
        Assert.Equal("SKU-001", item.SkuCode);
        var request = handler.Requests.Single();
        Assert.Equal("/api/business/v1/quality/inspection-plans?organizationId=org-001&environmentId=env-dev&status=active&keyword=IP-001&skip=0&take=12", request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_ncr_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                total = 1,
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
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", Take: 12),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
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
            new BusinessConsoleMesListRequest(
                "org-001",
                "env-dev",
                "released",
                "filter",
                "WC-FILTER",
                "SHIFT-FILTER",
                "DEV-FILTER",
                Skip: 4,
                Take: 12),
            CancellationToken.None);

        Assert.Equal("WO-HTTP", response.Items.Single().WorkOrderId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=filter&workCenterId=WC-FILTER&shiftId=SHIFT-FILTER&deviceAssetId=DEV-FILTER&skip=4&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Mes_secondary_http_clients_forward_skip_take_and_map_total()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            items = Array.Empty<object>(),
            total = 37,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesClient(httpClient);
        var request = new BusinessConsoleMesListRequest("org-001", "env-dev", Keyword: "filter", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER", Skip: 4, Take: 12);
        var expectedQuery = "?organizationId=org-001&environmentId=env-dev&keyword=filter&workCenterId=WC-FILTER&shiftId=SHIFT-FILTER&deviceAssetId=DEV-FILTER&skip=4&take=12";

        var cases = new (string Path, Func<Task<int>> Invoke)[]
        {
            ("/api/business/v1/mes/wip" + expectedQuery, async () => (await client.GetWipSummaryAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/capacity-impacts" + expectedQuery, async () => (await client.ListCapacityImpactsAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/dispatch-tasks" + expectedQuery, async () => (await client.ListDispatchTasksAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/finished-goods-receipt-requests" + expectedQuery, async () => (await client.ListFinishedGoodsReceiptRequestsAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/material-issue-requests" + expectedQuery, async () => (await client.ListMaterialIssueRequestsAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/downtime-events" + expectedQuery, async () => (await client.ListDowntimeEventsAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/shift-handovers" + expectedQuery, async () => (await client.ListShiftHandoversAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/production-reports" + expectedQuery, async () => (await client.ListProductionReportsAsync("internal-token-001", request, CancellationToken.None)).Total),
            ("/api/business/v1/mes/related-quality-items" + expectedQuery, async () => (await client.ListRelatedQualityItemsAsync("internal-token-001", request, CancellationToken.None)).Total),
        };

        foreach (var testCase in cases)
        {
            Assert.Equal(37, await testCase.Invoke());
        }

        Assert.Equal(cases.Select(x => x.Path), handler.Requests.Select(x => x.RequestUri!.PathAndQuery));
        Assert.All(handler.Requests, sent => Assert.Equal("internal-token-001", sent.Headers.Authorization!.Parameter));
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, Take: 100),
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, Take: 100),
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
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, Take: 100),
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

    private static object ValidMasterDataCreateBody(string path) =>
        BusinessConsoleTestRequestBodies.ValidMasterDataCreateBody(path);

    private static object TelemetryResponseFor(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath switch
        {
            "/api/business/v1/iiot/alarms" => new
            {
                data = new
                {
                    items = new[]
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
                    total = 1,
                },
            },
            "/api/business/v1/iiot/alarm-rules" when request.Method == HttpMethod.Get => new
            {
                data = new
                {
                    items = new[]
                    {
                        new
                        {
                            alarmRuleId = "rule-001",
                            organizationId = "org-001",
                            environmentId = "env-dev",
                            deviceAssetId = "DEV-OIL-01",
                            ruleCode = "RULE-001",
                            alarmCode = "TEMP_HIGH",
                            severity = "warning",
                            tagKey = "temperature",
                            comparisonOperator = ">=",
                            thresholdValue = 95m,
                            unitCode = "celsius",
                            isEnabled = true,
                            updatedAtUtc = "2026-06-01T08:20:00Z",
                        },
                    },
                    total = 1,
                },
            },
            "/api/business/v1/iiot/alarm-rules" => new { data = new { alarmRuleId = "rule-001" } },
            "/api/business/v1/iiot/oee" => new
            {
                data = new
                {
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    deviceAssetId = "DEV-OIL-01",
                    windowStartUtc = "2026-06-01T08:00:00Z",
                    windowEndUtc = "2026-06-01T16:00:00Z",
                    stateSampleCount = 2,
                    availabilityRate = 0.5m,
                    performanceRate = 1m,
                    qualityRate = 1m,
                    oeeRate = 0.5m,
                },
            },
            _ => new
            {
                data = CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm),
            },
        };
    }

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

    private static object ResponseForEngineeringWriteRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get)
        {
            return new
            {
                data = new
                {
                    total = 0,
                    items = Array.Empty<object>(),
                },
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        if (path.Contains("/production-versions", StringComparison.Ordinal) &&
            !path.EndsWith("/archive", StringComparison.Ordinal))
        {
            return new
            {
                data = new
                {
                    productionVersionId = "pv-001",
                },
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        if (path.EndsWith("/archive", StringComparison.Ordinal))
        {
            return new
            {
                data = new { },
                success = true,
                message = string.Empty,
                code = 0,
            };
        }

        return new
        {
            data = new
            {
                id = "entity-001",
            },
            success = true,
            message = string.Empty,
            code = 0,
        };
    }

    private static void AssertRequest(HttpRequestMessage request, HttpMethod method, string pathAndQuery)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(pathAndQuery, request.RequestUri!.PathAndQuery);
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

    public BusinessConsoleMasterDataResourceRequest? LastDetailRequest { get; private set; }

    public BusinessConsoleUpdateMasterDataResourceRequest? LastUpdateRequest { get; private set; }

    public BusinessConsoleSetMasterDataResourceEnabledRequest? LastSetEnabledRequest { get; private set; }

    public bool LastSetEnabled { get; private set; }

    public List<bool> SetResourceEnabledCalls { get; } = [];

    public List<BusinessConsoleSetMasterDataResourceEnabledRequest> SetResourceEnabledRequests { get; } = [];

    public int CreateResourceCallCount { get; private set; }

    public string? LastCreateResourcePath { get; private set; }

    public BusinessConsoleCreateWorkshopRequest? LastCreateWorkshopRequest { get; private set; }

    public BusinessConsoleAddTeamMemberRequest? LastAddTeamMemberRequest { get; private set; }

    public BusinessConsoleListTeamMembersRequest? LastListTeamMembersRequest { get; private set; }

    public BusinessConsoleRemoveTeamMemberRequest? LastRemoveTeamMemberRequest { get; private set; }

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
        var filtered = resources
            .Where(resource => string.Equals(resource.ResourceType, request.ResourceType, StringComparison.Ordinal))
            .ToArray();
        resources = filtered
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArray();
        return Task.FromResult(new BusinessConsoleResourceListResponse(resources, filtered.Length));
    }

    public Task<BusinessConsoleMasterDataResourceDetail> GetResourceDetailAsync(
        string internalBearerToken,
        BusinessConsoleMasterDataResourceRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDetailRequest = request;
        return Task.FromResult(ResourceDetail(request.ResourceType, request.Code, request.CodeSet, true));
    }

    public Task<BusinessConsoleMasterDataResourceDetail> UpdateResourceAsync(
        string internalBearerToken,
        BusinessConsoleUpdateMasterDataResourceRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastUpdateRequest = request;
        return Task.FromResult(ResourceDetail(request.ResourceType, request.Code, request.CodeSet, true, request.Name));
    }

    public Task<BusinessConsoleMasterDataResourceDetail> SetResourceEnabledAsync(
        string internalBearerToken,
        BusinessConsoleSetMasterDataResourceEnabledRequest request,
        bool enabled,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastSetEnabledRequest = request;
        LastSetEnabled = enabled;
        SetResourceEnabledCalls.Add(enabled);
        SetResourceEnabledRequests.Add(request);
        return Task.FromResult(ResourceDetail(request.ResourceType, request.Code, request.CodeSet, enabled));
    }

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleResourceItem("sku", request.Code ?? "SKU-GENERATED", request.Name, true, "v1"));
    }

    public Task<BusinessConsoleResourceItem> CreateBusinessPartnerAsync(
        string internalBearerToken,
        BusinessConsoleCreateBusinessPartnerRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/partners", "business-partner", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateUnitOfMeasureAsync(
        string internalBearerToken,
        BusinessConsoleCreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/units-of-measure", "unit-of-measure", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateUomConversionAsync(
        string internalBearerToken,
        BusinessConsoleCreateUomConversionRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/uom-conversions", "uom-conversion", $"{request.FromUomCode}->{request.ToUomCode}", $"{request.FromUomCode} to {request.ToUomCode}");

    public Task<BusinessConsoleResourceItem> CreateWorkshopAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkshopRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateWorkshopRequest = request;
        return CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/workshops", "workshop", request.Code, request.Name);
    }

    public Task<BusinessConsoleResourceItem> CreateSiteAsync(
        string internalBearerToken,
        BusinessConsoleCreateSiteRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/sites", "site", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateProductionLineAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionLineRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/production-lines", "production-line", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateWorkCenterAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCenterRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/work-centers", "work-center", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> RegisterDeviceAssetAsync(
        string internalBearerToken,
        BusinessConsoleRegisterDeviceAssetRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/device-assets", "device-asset", request.Code, request.Model);

    public Task<BusinessConsoleResourceItem> CreateShiftAsync(
        string internalBearerToken,
        BusinessConsoleCreateShiftRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/shifts", "shift", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateWorkCalendarAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCalendarRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/work-calendars", "work-calendar", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> CreateTeamAsync(
        string internalBearerToken,
        BusinessConsoleCreateTeamRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/teams", "team", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> AddTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleAddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        LastAddTeamMemberRequest = request;
        return CreateResourceAsync(internalBearerToken, $"/api/business/v1/master-data/teams/{request.TeamCode}/members", "team-member", $"{request.TeamCode}:{request.UserId}", request.UserId);
    }

    public Task<BusinessConsoleTeamMemberListResponse> ListTeamMembersAsync(
        string internalBearerToken,
        BusinessConsoleListTeamMembersRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastListTeamMembersRequest = request;
        return Task.FromResult(new BusinessConsoleTeamMemberListResponse(
            [new BusinessConsoleTeamMemberItem(request.TeamCode, "user-001", true, new DateOnly(2026, 1, 1), null, true, "v1")],
            1));
    }

    public Task<BusinessConsoleResourceItem> RemoveTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleRemoveTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        LastRemoveTeamMemberRequest = request;
        return CreateResourceAsync(internalBearerToken, $"/api/business/v1/master-data/teams/{request.TeamCode}/members/{request.UserId}", "team-member", $"{request.TeamCode}:{request.UserId}", request.UserId);
    }

    public Task<BusinessConsoleResourceItem> CreateDepartmentAsync(
        string internalBearerToken,
        BusinessConsoleCreateDepartmentRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/departments", "department", request.Code, request.Name);

    public Task<BusinessConsoleResourceItem> AssignPersonnelSkillAsync(
        string internalBearerToken,
        BusinessConsoleAssignPersonnelSkillRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/personnel-skills", "personnel-skill", $"{request.UserId}:{request.SkillCode}", request.Level);

    public Task<BusinessConsoleResourceItem> CreateReferenceDataCodeAsync(
        string internalBearerToken,
        BusinessConsoleCreateReferenceDataCodeRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/reference-data", "reference-data-code", request.Code, request.Name);

    private Task<BusinessConsoleResourceItem> CreateResourceAsync(
        string internalBearerToken,
        string downstreamPath,
        string resourceType,
        string code,
        string displayName)
    {
        LastInternalToken = internalBearerToken;
        CreateResourceCallCount++;
        LastCreateResourcePath = downstreamPath;
        return Task.FromResult(new BusinessConsoleResourceItem(resourceType, code, displayName, true, "v1"));
    }

    private static BusinessConsoleMasterDataResourceDetail ResourceDetail(
        string resourceType,
        string code,
        string? codeSet,
        bool active,
        string? displayName = null) =>
        new(
            resourceType,
            code,
            displayName ?? code,
            active,
            "v1",
            "org-001",
            "env-dev",
            displayName ?? code,
            CodeSet: codeSet,
            Status: active ? "active" : "disabled");
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

    public int? NcrTotal { get; init; }

    public Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleQualityListResponse([], 0));

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
            ],
            NcrTotal ?? 1));
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

    public int WriteCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleResolveProductionVersionRequest? LastResolveRequest { get; private set; }

    public BusinessConsoleListProductionVersionsRequest? LastProductionVersionListRequest { get; private set; }

    public BusinessConsoleReleaseManufacturingBomRequest? LastReleaseManufacturingBomRequest { get; private set; }

    public Task<BusinessConsoleEngineeringEntityResponse> RegisterEngineeringDocumentAsync(
        string internalBearerToken,
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.DocumentNumber ?? "DOC-001"));
    }

    public Task<BusinessConsoleEngineeringEntityResponse> CreateEngineeringItemRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.ItemCode ?? "ITEM-001"));
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringBomRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.BomCode ?? "EBOM-001"));
    }

    public Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringBomListResponse([], 0));
    }

    public Task<BusinessConsoleManufacturingBomListResponse> ListManufacturingBomsAsync(
        string internalBearerToken,
        BusinessConsoleListManufacturingBomsRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleManufacturingBomListResponse([], 0));
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseManufacturingBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseManufacturingBomRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        LastReleaseManufacturingBomRequest = request;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.BomCode ?? "MBOM-001"));
    }

    public Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleRoutingListResponse([], 0));
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseRoutingAsync(
        string internalBearerToken,
        BusinessConsoleReleaseRoutingRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.RoutingCode ?? "RTG-001"));
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringChangeRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEngineeringEntityResponse(request.ChangeNumber ?? "ECO-001"));
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
            ],
            1));
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

    public Task<BusinessConsoleCreateProductionVersionResponse> CreateProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateProductionVersionResponse("pv-front-001"));
    }

    public Task<BusinessConsoleCreateProductionVersionResponse> UpdateProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleUpdateProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateProductionVersionResponse(productionVersionId));
    }

    public Task<BusinessConsoleAcceptedResponse> ArchiveProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleArchiveProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        WriteCallCount++;
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
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

    public BusinessConsoleErpListRequest? LastPurchaseOrderListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastRequestForQuotationListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastSalesOrderListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastOpportunityListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastQuotationListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastDeliveryOrderListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastPayableListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastReceivableListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastCostCandidateListRequest { get; private set; }

    public BusinessConsoleErpListRequest? LastJournalVoucherListRequest { get; private set; }

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

    public Task<BusinessConsoleErpRequestForQuotationListResponse> ListRequestsForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRequestForQuotationListRequest = request;
        return Task.FromResult(new BusinessConsoleErpRequestForQuotationListResponse(
            [
                new BusinessConsoleErpRequestForQuotationItem(
                    "RFQ-001",
                    "Open",
                    ["SUP-001", "SUP-002"],
                    [
                        new BusinessConsoleErpRequestForQuotationLineItem("10", "SKU-RM-001", "EA", 5m, "SITE-01", DateOnly.Parse("2026-06-10")),
                    ],
                    DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)),
            ],
            1));
    }

    public Task<BusinessConsoleErpPurchaseOrderListResponse> ListPurchaseOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
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
            ],
            1));
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
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastSalesOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleErpSalesOrderListResponse(
        [
            new BusinessConsoleErpSalesOrderItem("SO-001", "CUST-001", "Released", 1200m),
        ],
        1));
    }

    public Task<BusinessConsoleErpOpportunityListResponse> ListOpportunitiesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastOpportunityListRequest = request;
        return Task.FromResult(new BusinessConsoleErpOpportunityListResponse(
            [new BusinessConsoleErpOpportunityItem("OPP-001", "CUST-001", "Line expansion", "open", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture))],
            1));
    }

    public Task<BusinessConsoleErpQuotationListResponse> ListQuotationsAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastQuotationListRequest = request;
        return Task.FromResult(new BusinessConsoleErpQuotationListResponse(
            [
                new BusinessConsoleErpQuotationItem(
                    "QUO-001",
                    "CUST-001",
                    DateOnly.Parse("2026-12-31"),
                    "Draft",
                    200m,
                    [new BusinessConsoleErpQuotationLineItem("10", "SKU-FG", "EA", 2m, 100m, DateOnly.Parse("2026-07-01"))],
                    DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)),
            ],
            1));
    }

    public Task<BusinessConsoleErpDeliveryOrderListResponse> ListDeliveryOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDeliveryOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleErpDeliveryOrderListResponse(
            [
                new BusinessConsoleErpDeliveryOrderItem(
                    "DO-001",
                    "SO-001",
                    "CUST-001",
                    "released",
                    [new BusinessConsoleErpDeliveryOrderLineItem("10", 1m)],
                    DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)),
            ],
            1));
    }

    public Task<BusinessConsoleErpPayableListResponse> ListPayablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastPayableListRequest = request;
        return Task.FromResult(new BusinessConsoleErpPayableListResponse(
            [new BusinessConsoleErpPayableItem("AP-001", "RCV-001", "SUP-001", 100m, 80m, "CNY", "open", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture))],
            1));
    }

    public Task<BusinessConsoleErpReceivableListResponse> ListReceivablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastReceivableListRequest = request;
        return Task.FromResult(new BusinessConsoleErpReceivableListResponse(
            [new BusinessConsoleErpReceivableItem("AR-001", "DO-001", "CUST-001", 100m, 80m, "CNY", "open", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture))],
            1));
    }

    public Task<BusinessConsoleErpCostCandidateListResponse> ListCostCandidatesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCostCandidateListRequest = request;
        return Task.FromResult(new BusinessConsoleErpCostCandidateListResponse(
            [new BusinessConsoleErpCostCandidateItem("COST-001", "production-report", "RPT-001", 100m, "CNY", "pending", DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture))],
            1));
    }

    public Task<BusinessConsoleErpJournalVoucherListResponse> ListJournalVouchersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastJournalVoucherListRequest = request;
        return Task.FromResult(new BusinessConsoleErpJournalVoucherListResponse(
            [
                new BusinessConsoleErpJournalVoucherItem(
                    "JV-001",
                    DateOnly.Parse("2026-06-01"),
                    "posted",
                    100m,
                    100m,
                    [
                        new BusinessConsoleErpJournalVoucherLineItem("1401", 100m, 0m, "inventory"),
                        new BusinessConsoleErpJournalVoucherLineItem("2202", 0m, 100m, "payable"),
                    ],
                    DateTime.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture)),
            ],
            1));
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

    public BusinessConsoleBarcodeRuleListRequest? LastRuleListRequest { get; private set; }

    public BusinessConsoleBarcodeTemplateListRequest? LastTemplateListRequest { get; private set; }

    public BusinessConsoleCreateBarcodePrintBatchRequest? LastPrintBatchRequest { get; private set; }

    public BusinessConsoleBarcodePrintBatchListRequest? LastPrintBatchListRequest { get; private set; }

    public BusinessConsoleRecordBarcodeScanRequest? LastScanRequest { get; private set; }

    public BusinessConsoleBarcodeScanListRequest? LastScanListRequest { get; private set; }

    public Task<BusinessConsoleBarcodeRuleListResponse> ListRulesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeRuleListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRuleListRequest = request;
        return Task.FromResult(new BusinessConsoleBarcodeRuleListResponse(
        [
            new BusinessConsoleBarcodeRuleItem("rule-001", "FG", "code128", "FG", 40, "none", ["work-order"], "active"),
        ], 1));
    }

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
        LastTemplateListRequest = request;
        return Task.FromResult(new BusinessConsoleBarcodeTemplateListResponse(
        [
            new BusinessConsoleBarcodeTemplateItem("template-001", "box-label", "Box Label", "file-001", "{}", "active"),
        ], 1));
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

    public Task<BusinessConsoleBarcodePrintBatchListResponse> ListPrintBatchesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastPrintBatchListRequest = request;
        return Task.FromResult(new BusinessConsoleBarcodePrintBatchListResponse(
        [
            new BusinessConsoleBarcodePrintBatchItem("print-batch-001", "template-001", "work-order", request.SourceDocumentId ?? "WO-001", "print-001", 1, "completed", DateTimeOffset.Parse("2026-06-03T01:00:00Z", CultureInfo.InvariantCulture)),
        ], 1));
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
        LastScanListRequest = request;
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
        ], 1));
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

    public BusinessConsoleTelemetryAlarmRuleListRequest? LastAlarmRuleListRequest { get; private set; }

    public BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest? LastAlarmRuleUpsertRequest { get; private set; }

    public BusinessConsoleTelemetryOeeRequest? LastOeeRequest { get; private set; }

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

    public Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmRuleListRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryAlarmRuleListResponse([]));
    }

    public Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmRuleUpsertRequest = request;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse("rule-001"));
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

    public Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastOeeRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryOeeResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            2,
            0.5m,
            1m,
            1m,
            0.5m,
            true,
            true));
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

    public Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleEquipmentAlarmListPageResponse(
        [
            new EquipmentRuntimeAlarmSummary(
                "alarm-001",
                "DEV-OIL-01",
                "TEMP_HIGH",
                "critical",
                DateTimeOffset.Parse("2026-06-01T08:20:00Z", CultureInfo.InvariantCulture),
                "EXT-ALARM-001"),
        ], 1));
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

    public Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateMaintenanceWorkOrderResponse("wo-maint-created"));
    }

    public Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCompleteMaintenanceWorkOrderResponse(true));
    }

    public Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderListResponse([], request.Skip, request.Take, 0));
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
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenancePlanListResponse([], request.Skip, request.Take, 0));
    }

    public Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateMaintenancePlanResponse("plan-created"));
    }

    public Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleRecordMaintenanceInspectionResponse("inspection-created"));
    }

    public Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenanceInspectionListResponse([], request.Skip, request.Take, 0));
    }

    public Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMaintenanceSparePartListResponse([], request.Skip, request.Take, 0));
    }

    public Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateMaintenanceSparePartResponse("spare-line-created"));
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

    public int? WorkOrdersTotal { get; init; }

    public IReadOnlyCollection<BusinessConsoleMesProductionPlanRow>? ProductionPlans { get; init; }

    public BusinessConsoleMesProductionPlanListRequest? LastProductionPlanListRequest { get; private set; }

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
        BusinessConsoleMesProductionPlanListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastProductionPlanListRequest = request;
        var plans = ProductionPlans ?? [];
        return Task.FromResult(new BusinessConsoleMesProductionPlanListResponse(plans, plans.Count));
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
        var workOrders = WorkOrders ??
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
            ];
        return Task.FromResult(new BusinessConsoleMesWorkOrderListResponse(workOrders, WorkOrdersTotal ?? workOrders.Count));
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
        return Task.FromResult(new BusinessConsoleMesOperationTaskListResponse([], 0));
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
        return Task.FromResult(new BusinessConsoleMesWipSummaryResponse([], 0));
    }

    public Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleMesProductionReportListResponse([], 0));
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
        return Task.FromResult(new BusinessConsoleMesReceiptRequestListResponse([], 0));
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
        return Task.FromResult(new BusinessConsoleMesCapacityImpactListResponse([], 0));
    }
}
