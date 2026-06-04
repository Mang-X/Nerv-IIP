using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayAuthorizationTests
{
    [Fact]
    public async Task Business_console_endpoint_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_gateway_authentication_requires_configured_jwt_settings()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            BusinessGatewayTestServiceBaseUrls.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Iam:Jwt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_forbidden_when_iam_denies_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_resource_list_when_permission_is_allowed()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("SKU-001", data.GetProperty("resources")[0].GetProperty("code").GetString());
        Assert.Equal(1, auth.CallCount);
    }

    [Fact]
    public async Task Product_engineering_write_facade_returns_forbidden_with_manage_permission_when_iam_denies()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        var engineering = new RecordingProductEngineeringClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(engineering);
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
            materialLines = new[] { new { skuCode = "RM-001", quantity = 1, unitOfMeasureCode = "EA", scrapRate = 0 } },
            recipeLines = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.EngineeringBomsManage, auth.LastRequirement!.PermissionCode);
        Assert.Null(engineering.LastReleaseManufacturingBomRequest);
    }

    [Fact]
    public async Task Business_console_endpoint_rejects_context_mismatch_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken(environmentId: "env-prod"));

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_scheduling_endpoint_rejects_missing_problem_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/scheduling/plans/preview", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Theory]
    [MemberData(nameof(BusinessConsoleRoutes))]
    public async Task Business_console_routes_return_forbidden_when_iam_denies_permission(
        HttpMethod method,
        string path,
        string expectedPermission)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(method, $"{path}{(path.Contains('?') ? '&' : '?')}organizationId=org-001&environmentId=env-dev")
        {
            Content = method != HttpMethod.Get
                ? JsonContent.Create(ValidPostBody(path))
                : null
        };

        var response = await client.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected Forbidden for {method} {path}, got {(int)response.StatusCode}: {responseBody}");
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(expectedPermission, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    private static object ValidPostBody(string path) => path switch
    {
        "/api/business-console/v1/master-data/skus" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "SKU-001",
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
            complianceTags = Array.Empty<string>(),
        },
        "/api/business-console/v1/master-data/business-partners" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "SUP-001",
            partnerType = "supplier",
            name = "Demo Supplier",
        },
        "/api/business-console/v1/master-data/units-of-measure" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "EA",
            name = "Each",
            dimensionType = "count",
            precision = 0,
            roundingMode = "half-up",
        },
        "/api/business-console/v1/master-data/uom-conversions" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            fromUomCode = "BOX",
            toUomCode = "EA",
            factor = 12,
            offset = 0,
            precision = 6,
            roundingMode = "half-up",
            effectiveFrom = "2026-01-01",
        },
        "/api/business-console/v1/master-data/sites" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "SITE-01",
            name = "Main Site",
            timezone = "Asia/Shanghai",
        },
        "/api/business-console/v1/master-data/production-lines" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "LINE-01",
            name = "Line 1",
            siteCode = "SITE-01",
        },
        "/api/business-console/v1/master-data/work-centers" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "WC-01",
            name = "Work Center 1",
            capacityMinutesPerDay = 480,
            resourceType = "line",
            plantCode = "SITE-01",
            lineCode = "LINE-01",
            defaultCalendarCode = "CAL-01",
            capacityUnit = "minute",
            finiteCapacity = true,
        },
        "/api/business-console/v1/master-data/device-assets" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "DEV-01",
            model = "Robot",
            lineCode = "LINE-01",
            workCenterCode = "WC-01",
            assetClassCode = "robot",
            manufacturer = "Demo Maker",
            serialNo = "SN-001",
            minimumCapacity = 1,
            maximumCapacity = 10,
            capacityUomCode = "EA",
            criticality = "high",
            maintainable = true,
            telemetryEnabled = true,
            externalReferences = new Dictionary<string, string> { ["iiot"] = "DEV-01" },
        },
        "/api/business-console/v1/master-data/shifts" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "SHIFT-A",
            name = "Shift A",
            startsAt = "08:00:00",
            endsAt = "16:00:00",
            paidMinutes = 480,
        },
        "/api/business-console/v1/master-data/work-calendars" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "CAL-01",
            name = "Standard Calendar",
        },
        "/api/business-console/v1/master-data/teams" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "TEAM-A",
            name = "Team A",
            departmentCode = "DEP-01",
            shiftCode = "SHIFT-A",
        },
        "/api/business-console/v1/master-data/departments" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "DEP-01",
            name = "Production Department",
            parentDepartmentCode = (string?)null,
        },
        "/api/business-console/v1/master-data/personnel-skills" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            userId = "user-001",
            skillCode = "WELD",
            level = "L2",
            effectiveFrom = "2026-01-01",
            effectiveTo = "2026-12-31",
        },
        "/api/business-console/v1/master-data/reference-data" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            codeSet = "asset-class",
            code = "robot",
            name = "Robot",
        },
        "/api/business-console/v1/inventory/count-tasks/count-001/adjustments" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            countedQuantity = 1,
            idempotencyKey = "idem-001",
        },
        "/api/business-console/v1/planning/demands" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            demandType = "sales-order",
            sourceReference = "SO-001",
            skuCode = "SKU-FG-1000",
            uomCode = "pcs",
            siteCode = "SITE-01",
            quantity = 10,
            dueDate = "2026-06-01",
        },
        "/api/business-console/v1/planning/mrp-runs" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            horizonStart = "2026-05-25",
            horizonEnd = "2026-06-30",
        },
        "/api/business-console/v1/engineering/documents" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            documentNumber = "DOC-001",
            revision = "A",
            fileId = "file-001",
            fileName = "design.dwg",
            contentType = "application/dwg",
            documentType = "cad",
            idempotencyKey = "doc-001",
        },
        "/api/business-console/v1/engineering/items" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            itemCode = "ITEM-001",
            revision = "A",
            name = "Demo item",
            release = true,
            idempotencyKey = "item-001",
        },
        "/api/business-console/v1/engineering/engineering-boms/release" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            bomCode = "EBOM-001",
            revision = "A",
            parentItemCode = "ITEM-001",
            effectiveDate = "2026-06-01",
            lines = new[] { new { componentCode = "ITEM-002", quantity = 1, unitOfMeasureCode = "EA" } },
            idempotencyKey = "ebom-001",
        },
        "/api/business-console/v1/engineering/manufacturing-boms/release" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            bomCode = "MBOM-001",
            revision = "A",
            skuCode = "SKU-001",
            engineeringBomCode = "EBOM-001",
            engineeringBomRevision = "A",
            effectiveDate = "2026-06-01",
            materialLines = new[] { new { skuCode = "RM-001", quantity = 1, unitOfMeasureCode = "EA", scrapRate = 0 } },
            recipeLines = Array.Empty<object>(),
            idempotencyKey = "mbom-001",
        },
        "/api/business-console/v1/engineering/routings/release" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            routingCode = "RTG-001",
            revision = "A",
            skuCode = "SKU-001",
            effectiveDate = "2026-06-01",
            operations = new[] { new { sequence = 10, workCenterCode = "WC-001", operationName = "Assemble", standardMinutes = 15 } },
            idempotencyKey = "routing-001",
        },
        "/api/business-console/v1/engineering/engineering-changes/release" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            changeNumber = "ECO-001",
            reason = "Initial release",
            approvalReferenceId = "approval-001",
            effectiveDate = "2026-06-01",
            affectedVersions = new[] { new { versionKind = "mbom", versionId = "MBOM-001:A" } },
            idempotencyKey = "eco-001",
        },
        "/api/business-console/v1/engineering/production-versions" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuCode = "SKU-001",
            mbomVersionId = "MBOM-001:A",
            routingVersionId = "RTG-001:A",
            validFrom = "2026-06-01",
            validTo = (string?)null,
            lotSizeMin = 1,
            lotSizeMax = 100,
            priority = 10,
            isDefault = true,
        },
        "/api/business-console/v1/engineering/production-versions/pv-001" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            productionVersionId = "pv-001",
            mbomVersionId = "MBOM-001:B",
            routingVersionId = "RTG-001:B",
            validFrom = "2026-07-01",
            validTo = (string?)null,
            lotSizeMin = 1,
            lotSizeMax = 100,
            priority = 20,
            isDefault = true,
        },
        "/api/business-console/v1/engineering/production-versions/pv-001/archive" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            productionVersionId = "pv-001",
            reason = "Superseded",
        },
        "/api/business-console/v1/scheduling/plans/preview" or "/api/business-console/v1/scheduling/plans" => new
        {
            problem = SchedulingProblemBody(),
        },
        "/api/business-console/v1/planning/suggestions/suggestion-001/accept" => new
        {
            suggestionId = "suggestion-001",
            organizationId = "org-001",
            environmentId = "env-dev",
            downstreamService = "mes",
            downstreamDocumentType = "work-order",
            downstreamDocumentId = "WO-001",
        },
        "/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            suggestionId = "suggestion-001",
            skuCode = "SKU-001",
            uomCode = "EA",
            siteCode = "SITE-01",
            quantity = 10,
            requiredDate = "2026-06-10",
        },
        "/api/business-console/v1/wms/inbound-orders" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            inboundOrderNo = "IN-001",
            sourceDocumentType = "purchase-receipt",
            sourceDocumentId = "PR-001",
            siteCode = "S1",
            lines = new[]
            {
                new
                {
                    lineNo = "10",
                    skuCode = "SKU-001",
                    uomCode = "EA",
                    receivedQuantity = 1,
                    stagingLocationCode = "STAGE-01",
                    qualityStatus = "qualified",
                    ownerType = "company",
                },
            },
        },
        "/api/business-console/v1/wms/inbound-orders/inbound-order-001/putaway-tasks" => new
        {
            taskNo = "PUT-001",
            lineNo = "10",
            fromLocationCode = "STAGE-01",
            toLocationCode = "BIN-01",
            quantity = 1,
        },
        "/api/business-console/v1/wms/inbound-orders/inbound-order-001/complete" => new
        {
            idempotencyKey = "complete-in-001",
        },
        "/api/business-console/v1/wms/outbound-orders" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            outboundOrderNo = "OUT-001",
            sourceDocumentType = "sales-shipment",
            sourceDocumentId = "SO-001",
            siteCode = "S1",
            lines = new[]
            {
                new
                {
                    lineNo = "10",
                    skuCode = "SKU-001",
                    uomCode = "EA",
                    requestedQuantity = 1,
                    pickLocationCode = "BIN-01",
                    qualityStatus = "qualified",
                    ownerType = "company",
                },
            },
        },
        "/api/business-console/v1/wms/outbound-orders/outbound-order-001/picking-tasks" => new
        {
            taskNo = "PICK-001",
            lineNo = "10",
            fromLocationCode = "BIN-01",
            toLocationCode = "SHIP-01",
            quantity = 1,
        },
        "/api/business-console/v1/wms/outbound-orders/outbound-order-001/complete" => new
        {
            packReviewNo = "PACK-001",
            passed = true,
            idempotencyKey = "complete-out-001",
        },
        "/api/business-console/v1/wms/count-executions" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            countNo = "COUNT-001",
            skuCode = "SKU-001",
            uomCode = "EA",
            siteCode = "S1",
            locationCode = "BIN-01",
            expectedQuantity = 1,
        },
        "/api/business-console/v1/wms/count-executions/count-execution-001/complete" => new
        {
            countedQuantity = 1,
            idempotencyKey = "complete-count-001",
        },
        "/api/business-console/v1/wms/wcs-tasks/warehouse-task-001/dispatch" => new
        {
            adapterType = "agv",
            externalTaskId = "EXT-001",
            payloadJson = "{}",
        },
        "/api/business-console/v1/wms/wcs-tasks/EXT-001/fail" => new
        {
            failureCode = "PLC_TIMEOUT",
            failureMessage = "PLC did not acknowledge.",
        },
        "/api/business-console/v1/wms/wcs-tasks/EXT-001/complete" => new
        {
            completionPayloadJson = "{}",
        },
        _ => new { organizationId = "org-001", environmentId = "env-dev" },
    };

    public static TheoryData<HttpMethod, string, string> BusinessConsoleRoutes()
    {
        var routes = new TheoryData<HttpMethod, string, string>();
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/resources", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/business-partners", BusinessGatewayPermissions.MasterDataPartnersManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/units-of-measure", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/uom-conversions", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/sites", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/production-lines", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/work-centers", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/device-assets", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/shifts", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/work-calendars", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/teams", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/departments", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/personnel-skills", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/reference-data", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/inventory/availability", BusinessGatewayPermissions.InventoryLedgerRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/movements", BusinessGatewayPermissions.InventoryMovementsCreate);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks/count-001/adjustments", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/inspection-plans", BusinessGatewayPermissions.QualityInspectionRecordsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/inspection-records", BusinessGatewayPermissions.QualityInspectionRecordsCreate);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/ncrs", BusinessGatewayPermissions.QualityNcrRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/disposition", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/close", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/documents", BusinessGatewayPermissions.EngineeringDocumentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/items", BusinessGatewayPermissions.EngineeringItemsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-boms", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-boms/release", BusinessGatewayPermissions.EngineeringBomsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/manufacturing-boms", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/manufacturing-boms/release", BusinessGatewayPermissions.EngineeringBomsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/routings", BusinessGatewayPermissions.EngineeringRoutingsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/routings/release", BusinessGatewayPermissions.EngineeringRoutingsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-changes/release", BusinessGatewayPermissions.EngineeringChangesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/production-versions", BusinessGatewayPermissions.EngineeringProductionVersionsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/production-versions", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/engineering/production-versions/pv-001", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/production-versions/pv-001/archive", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/production-versions/resolve", BusinessGatewayPermissions.EngineeringProductionVersionsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/demands", BusinessGatewayPermissions.PlanningDemandsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/demands", BusinessGatewayPermissions.PlanningDemandsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/mrp-runs", BusinessGatewayPermissions.PlanningMrpRun);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/mrp-runs", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/mrp-runs/mrp-run-001/pegging", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/suggestions", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/suggestions/suggestion-001/accept", BusinessGatewayPermissions.PlanningSuggestionsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/scheduling/plans/preview", BusinessGatewayPermissions.SchedulingPlansManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/scheduling/plans", BusinessGatewayPermissions.SchedulingPlansManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/scheduling/plans", BusinessGatewayPermissions.SchedulingPlansRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/scheduling/plans/plan-001", BusinessGatewayPermissions.SchedulingPlansRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/scheduling/plans/plan-001/gantt", BusinessGatewayPermissions.SchedulingPlansRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/scheduling/plans/plan-001/release", BusinessGatewayPermissions.SchedulingPlansRelease);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/overview?deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/devices/DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/availability?windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/alarms", BusinessGatewayPermissions.IiotAlarmsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/tags?deviceAssetId=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/alarms?deviceAssetId=DEV-OIL-01&status=raised", BusinessGatewayPermissions.IiotAlarmsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/devices/DEV-OIL-01/history?fromUtc=2026-06-01T08:00:00Z&toUtc=2026-06-01T16:00:00Z", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/runtime-availability?windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/work-orders", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/work-orders/wo-maint-001", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/plans", BusinessGatewayPermissions.MaintenancePlansRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/availability-windows?windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/procurement/purchase-orders", BusinessGatewayPermissions.ErpProcurementRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/rfqs", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/supplier-quotations", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-orders", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-receipts", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/sales/sales-orders", BusinessGatewayPermissions.ErpSalesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/opportunities", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/quotations", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/quotations/QUO-001/approve", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/sales-orders", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/delivery-orders", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/payables", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/receivables", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/cost-candidates", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/vouchers", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/summary", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/payables/by-source?sourceDocumentNo=PR-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/receivables/by-source?sourceDocumentNo=DO-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/cost-candidates/by-source?sourceType=production&sourceDocumentNo=WO-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/templates", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/templates", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/chains", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/chains/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/tasks?actorType=user&actorRef=user-admin", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/chains/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9/steps/1/resolve", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/rules", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/templates", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/templates", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/print-batches", BusinessGatewayPermissions.BarcodePrint);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/print-batches/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", BusinessGatewayPermissions.BarcodePrint);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/scans", BusinessGatewayPermissions.BarcodeScansWrite);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/scans", BusinessGatewayPermissions.BarcodeScansWrite);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/inbound-orders", BusinessGatewayPermissions.WmsReceiptsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders/inbound-order-001/putaway-tasks", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders/inbound-order-001/complete", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/outbound-orders", BusinessGatewayPermissions.WmsShipmentsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders/outbound-order-001/picking-tasks", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders/outbound-order-001/complete", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/count-executions", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/count-executions/count-execution-001/complete", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/wcs-tasks", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/warehouse-task-001/dispatch", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/EXT-001/fail", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/EXT-001/complete", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/mes/work-orders", BusinessGatewayPermissions.MesWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/work-orders/rush", BusinessGatewayPermissions.MesWorkOrdersManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/schedules/run", BusinessGatewayPermissions.MesSchedulesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/production-reports", BusinessGatewayPermissions.MesReportingWrite);
        return routes;
    }

    private static object SchedulingProblemBody() => new
    {
        contractVersion = 1,
        problemId = "problem-001",
        organizationId = "org-001",
        environmentId = "env-dev",
        horizonStartUtc = "2026-06-01T08:00:00Z",
        horizonEndUtc = "2026-06-01T16:00:00Z",
        orders = Array.Empty<object>(),
        resources = Array.Empty<object>(),
        calendars = Array.Empty<object>(),
        unavailabilityWindows = Array.Empty<object>(),
        materialReadiness = Array.Empty<object>(),
        qualityBlocks = Array.Empty<object>(),
        lockedAssignments = Array.Empty<object>(),
    };

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            BusinessGatewayTestServiceBaseUrls.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}

internal sealed class FakeBusinessGatewayAuthorizationClient(Func<BusinessGatewayPermissionRequirement, bool> isAllowed)
    : IBusinessGatewayAuthorizationClient
{
    public int CallCount { get; private set; }

    public BusinessGatewayPermissionRequirement? LastRequirement { get; private set; }

    public List<BusinessGatewayPermissionRequirement> Requirements { get; } = [];

    public static FakeBusinessGatewayAuthorizationClient Allowed() => new(_ => true);

    public static FakeBusinessGatewayAuthorizationClient Forbidden() => new(_ => false);

    public static FakeBusinessGatewayAuthorizationClient AllowOnly(params string[] permissionCodes)
    {
        var allowedPermissions = permissionCodes.ToHashSet(StringComparer.Ordinal);
        return new(requirement => allowedPermissions.Contains(requirement.PermissionCode));
    }

    public Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequirement = requirement;
        Requirements.Add(requirement);
        return Task.FromResult(isAllowed(requirement)
            ? BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
