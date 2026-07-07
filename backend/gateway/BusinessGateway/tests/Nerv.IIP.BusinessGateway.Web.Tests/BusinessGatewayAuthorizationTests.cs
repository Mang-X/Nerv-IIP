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
    public async Task Business_console_endpoint_accepts_rs256_token_when_jwks_omits_optional_algorithm()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(
            auth,
            services =>
            {
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(masterData);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
            },
            builder => builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJsonWithoutAlgorithm()));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Business_console_endpoint_rejects_hs256_token_with_rsa_key_id()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.Hs256AccessTokenWithRsaKid());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
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
    public async Task Product_engineering_standard_operation_create_uses_collection_level_authorization()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var engineering = new RecordingProductEngineeringClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessProductEngineeringClient>();
            services.AddSingleton<IBusinessProductEngineeringClient>(engineering);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/engineering/standard-operations",
            BusinessConsoleTestRequestBodies.ValidEngineeringWriteBody("/api/business-console/v1/engineering/standard-operations"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.EngineeringStandardOperationsManage, auth.LastRequirement!.PermissionCode);
        Assert.Null(auth.LastRequirement.ResourceType);
        Assert.Null(auth.LastRequirement.ResourceId);
    }

    [Theory]
    [InlineData("GET", "/api/business-console/v1/planning/forecasts?skuCode=SKU-FG-1000&siteCode=SITE-01", BusinessGatewayPermissions.PlanningDemandsRead)]
    [InlineData("POST", "/api/business-console/v1/planning/forecasts", BusinessGatewayPermissions.PlanningDemandsManage)]
    public async Task Planning_forecast_facade_returns_forbidden_when_iam_denies(
        string method,
        string path,
        string expectedPermission)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{path}{(path.Contains('?') ? '&' : '?')}organizationId=org-001&environmentId=env-dev")
        {
            Content = method != "GET"
                ? JsonContent.Create(ValidPostBody(path))
                : null
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(expectedPermission, auth.LastRequirement!.PermissionCode);
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

    [Fact]
    public async Task Business_console_routing_release_rejects_blank_operation_code_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/engineering/routings/release", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            routingCode = "ROUTE-1000",
            revision = "A",
            skuCode = "SKU-FG-1000",
            effectiveDate = "2026-06-01",
            operations = new[]
            {
                new
                {
                    sequence = 10,
                    workCenterCode = "WC-MIX-01",
                    operationCode = "   ",
                    operationName = "混合",
                    standardMinutes = 30,
                },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_alarm_rule_endpoint_rejects_invalid_comparison_operator_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync("/api/business-console/v1/telemetry/alarm-rules", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-OIL-01",
            ruleCode = "OIL_TEMP_RULE",
            alarmCode = "OIL_TEMP_HIGH",
            severity = "warning",
            tagKey = "temperature",
            comparisonOperator = "contains",
            thresholdValue = 95m,
            unitCode = "celsius",
            isEnabled = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Theory]
    [InlineData("POST", "/api/business-console/v1/quality/reason-codes", "low", "rework")]
    [InlineData("PUT", "/api/business-console/v1/quality/reason-codes/QR-SCRATCH", "major", "use-as-is")]
    public async Task Business_console_quality_reason_endpoint_rejects_invalid_catalog_values_before_permission_check(
        string method,
        string path,
        string severity,
        string defaultDisposition)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                reasonCode = "QR-SCRATCH",
                reasonName = "Scratch",
                groupName = "Appearance",
                severity,
                defaultDisposition,
            }),
        };

        var response = await client.SendAsync(request);

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

    [Fact]
    public async Task Business_console_purchase_requisition_convert_route_requires_erp_procurement_manage_permission()
    {
        const string path = "/api/business-console/v1/erp/procurement/purchase-requisitions/convert-to-purchase-order";
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{path}?organizationId=org-001&environmentId=env-dev")
        {
            Content = JsonContent.Create(ValidPostBody(path))
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.ErpProcurementManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    private static object ValidPostBody(string path)
    {
        if (BusinessConsoleTestRequestBodies.IsMasterDataCreatePath(path))
        {
            return BusinessConsoleTestRequestBodies.ValidMasterDataCreateBody(path);
        }

        if (BusinessConsoleTestRequestBodies.IsEngineeringWritePath(path))
        {
            return BusinessConsoleTestRequestBodies.ValidEngineeringWriteBody(path);
        }

        return path switch
        {
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
        "/api/business-console/v1/planning/forecasts" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            forecastReference = "FCST-001",
            skuCode = "SKU-FG-1000",
            uomCode = "pcs",
            siteCode = "SITE-01",
            periodStartDate = "2026-06-01",
            periodEndDate = "2026-06-30",
            quantity = 120m,
            backwardConsumptionDays = 7,
            forwardConsumptionDays = 7,
        },
        "/api/business-console/v1/planning/mps" or "/api/business-console/v1/planning/mps/mps-001" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuCode = "SKU-FG-1000",
            uomCode = "pcs",
            siteCode = "SITE-01",
            bucketDate = "2026-06-15",
            quantity = 120m,
        },
        "/api/business-console/v1/planning/mps/mps-001/review" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            reviewedBy = "planner.li",
        },
        "/api/business-console/v1/planning/mps/mps-001/release" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            releasedBy = "planning.manager",
        },
        "/api/business-console/v1/planning/mrp-runs" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            horizonStart = "2026-05-25",
            horizonEnd = "2026-06-30",
        },
        "/api/business-console/v1/files/file-sop-v2/download-grants" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
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
        "/api/business-console/v1/planning/demands/demand-001/cancel" => new
        {
            demandSourceId = "demand-001",
            organizationId = "org-001",
            environmentId = "env-dev",
        },
        "/api/business-console/v1/quality/inspection-records/inspection-001/failures/ncr" => new
        {
            inspectionRecordId = "inspection-001",
            organizationId = "org-001",
            environmentId = "env-dev",
            defectReason = "Supplier certificate mismatch",
        },
        "/api/business-console/v1/telemetry/samples" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-OIL-01",
            tagKey = "temperature",
            bucketStartUtc = "2026-06-01T08:00:00Z",
            bucketEndUtc = "2026-06-01T08:01:00Z",
            sampleCount = 1,
            minValue = 95m,
            maxValue = 95m,
            averageValue = 95m,
            sourceSequence = "seq-001",
            sourceSystem = "manual-seed",
            sourceConnector = "business-console",
        },
        "/api/business-console/v1/telemetry/alarms" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-OIL-01",
            alarmCode = "OIL_TEMP_HIGH",
            severity = "warning",
            raisedAtUtc = "2026-06-01T08:00:00Z",
            externalAlarmId = "alarm-001",
        },
        "/api/business-console/v1/telemetry/alarm-rules" => new
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
        },
        "/api/business-console/v1/approval/delegations" => new
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
        },
        "/api/business-console/v1/approval/delegations/delegation-001/revoke" => new
        {
            revokedBy = "u-manager",
        },
        "/api/business-console/v1/master-data/code-rules/master-data.sku/versions" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            ruleKey = "master-data.sku",
            displayName = "SKU code v2",
            appliesTo = "sku",
            scope = 3,
            segments = new object[]
            {
                new { type = 0, value = "SKU-" },
                new { type = 2, width = 4, start = 1 },
            },
            isActive = true,
            effectiveFromUtc = "2026-06-01T00:00:00Z",
            createdBy = "admin-001",
            changeReason = "align plant convention",
        },
        "/api/business-console/v1/master-data/code-rules/master-data.sku/preview" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            ruleKey = "master-data.sku",
            segments = new object[]
            {
                new { type = 0, value = "SKU-" },
                new { type = 2, width = 4, start = 42 },
            },
            siteCode = "SITE-01",
        },
        "/api/business-console/v1/master-data/product-categories/CAT-001" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            categoryCode = "CAT-001",
            categoryName = "Finished Goods",
            parentCode = (string?)null,
            description = "Finished goods category",
        },
        "/api/business-console/v1/master-data/product-categories/CAT-001/archive" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            categoryCode = "CAT-001",
            reason = "obsolete",
        },
        "/api/business-console/v1/master-data/skills/SK-WELD" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skillCode = "SK-WELD",
            skillName = "Welding",
            groupName = "Manufacturing",
            requiresCertification = true,
            validityMonths = 24,
            description = "Welding qualification",
        },
        "/api/business-console/v1/master-data/skills/SK-WELD/archive" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skillCode = "SK-WELD",
            reason = "obsolete",
        },
        "/api/business-console/v1/quality/reason-codes" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            reasonCode = "QR-SCRATCH",
            reasonName = "Scratch",
            groupName = "Appearance",
            severity = "minor",
            defaultDisposition = "rework",
        },
        "/api/business-console/v1/quality/reason-codes/QR-SCRATCH" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            reasonCode = "QR-SCRATCH",
            reasonName = "Deep scratch",
            groupName = "Appearance",
            severity = "major",
            defaultDisposition = "scrap",
        },
        "/api/business-console/v1/quality/reason-codes/QR-SCRATCH/archive" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            reasonCode = "QR-SCRATCH",
        },
        "/api/business-console/v1/master-data/teams/T-001/members" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            teamCode = "T-001",
            userId = "user-001",
            isLeader = true,
            effectiveFrom = "2026-01-01",
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
        "/api/business-console/v1/erp/procurement/purchase-requisitions/convert-to-purchase-order" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            purchaseRequisitionNos = new[] { "PR-001", "PR-002" },
            purchaseOrderNo = "PO-REQ-001",
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
        "/api/business-console/v1/maintenance/work-orders" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            priority = "high",
            sourceAlarmId = "alarm-001",
            openedBy = "operator-001",
            assetUnavailableReason = "bearing temperature high",
        },
        "/api/business-console/v1/maintenance/work-orders/wo-maint-001/complete" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            result = "fixed",
            downtimeReasonCode = "planned-maintenance",
            downtimeMinutes = 30,
            spareParts = new[] { new { skuCode = "SPARE-001", quantity = 1, uomCode = "EA" } },
        },
        "/api/business-console/v1/maintenance/plans" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            planCode = "PLAN-001",
            interval = "monthly",
            startsOn = "2026-06-01",
            owner = "maintenance-team",
            windowStartUtc = "2026-06-01T08:00:00Z",
            windowEndUtc = "2026-06-01T10:00:00Z",
        },
        "/api/business-console/v1/maintenance/inspections" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            planId = "plan-001",
            workOrderId = "wo-maint-001",
            inspector = "inspector-001",
            result = "pass",
            inspectedAtUtc = "2026-06-01T09:00:00Z",
        },
        "/api/business-console/v1/maintenance/spare-parts" => new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "wo-maint-001",
            skuCode = "SPARE-001",
            quantity = 1,
            uomCode = "EA",
        },
        _ => new { organizationId = "org-001", environmentId = "env-dev" },
        };
    }

    public static TheoryData<HttpMethod, string, string> BusinessConsoleRoutes()
    {
        var routes = new TheoryData<HttpMethod, string, string>();
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/resources", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/resources/sku/SKU-001", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Patch, "/api/business-console/v1/master-data/resources/sku/SKU-001", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/resources/sku/SKU-001/disable", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/resources/sku/SKU-001/enable", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/workshops", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/device-assets?lineCode=LINE-001&workCenterCode=WC-001", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/workshops", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/workers", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/teams/T-001/members", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/personnel-skills/matrix", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/teams/T-001/members", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Delete, "/api/business-console/v1/master-data/teams/T-001/members/user-001", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/skus", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/product-categories", BusinessGatewayPermissions.MasterDataProductsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/product-categories/CAT-001", BusinessGatewayPermissions.MasterDataProductsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/product-categories", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/master-data/product-categories/CAT-001", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/product-categories/CAT-001/archive", BusinessGatewayPermissions.MasterDataProductsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/skills", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/skills/SK-WELD", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/skills", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/master-data/skills/SK-WELD", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/skills/SK-WELD/archive", BusinessGatewayPermissions.MasterDataResourcesManage);
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
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/code-rules", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/master-data/code-rules/master-data.sku", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/code-rules/master-data.sku/versions", BusinessGatewayPermissions.MasterDataResourcesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/master-data/code-rules/master-data.sku/preview", BusinessGatewayPermissions.MasterDataResourcesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/inventory/availability", BusinessGatewayPermissions.InventoryLedgerRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/movements", BusinessGatewayPermissions.InventoryMovementsCreate);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/inventory/count-tasks/count-001/adjustments", BusinessGatewayPermissions.InventoryCountsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/inspection-plans", BusinessGatewayPermissions.QualityInspectionRecordsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/inspection-records", BusinessGatewayPermissions.QualityInspectionRecordsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/inspection-records", BusinessGatewayPermissions.QualityInspectionRecordsCreate);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/inspection-records/inspection-001/failures/ncr", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/ncrs", BusinessGatewayPermissions.QualityNcrRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/reason-codes", BusinessGatewayPermissions.QualityNcrRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/quality/reason-codes/QR-SCRATCH", BusinessGatewayPermissions.QualityNcrRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/reason-codes", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/quality/reason-codes/QR-SCRATCH", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/reason-codes/QR-SCRATCH/archive", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/disposition", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/quality/ncrs/ncr-001/close", BusinessGatewayPermissions.QualityNcrManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/documents", BusinessGatewayPermissions.EngineeringDocumentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/sops/publish", BusinessGatewayPermissions.EngineeringDocumentsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/sops/current?operationCode=STD-MIX", BusinessGatewayPermissions.EngineeringDocumentsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/documents", BusinessGatewayPermissions.EngineeringDocumentsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/documents/DOC-001/A", BusinessGatewayPermissions.EngineeringDocumentsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/items", BusinessGatewayPermissions.EngineeringItemsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/items", BusinessGatewayPermissions.EngineeringItemsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/items/ITEM-001/A", BusinessGatewayPermissions.EngineeringItemsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-boms", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-boms/EBOM-001/A", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-boms/explosion?itemCode=FG-001&effectiveDate=2026-03-01", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-boms/where-used?componentCode=RM-001&effectiveDate=2026-03-01", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-boms/release", BusinessGatewayPermissions.EngineeringBomsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/manufacturing-boms", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/manufacturing-boms/MBOM-001/A", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/manufacturing-boms/explosion?skuCode=FG-001&effectiveDate=2026-03-01", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/manufacturing-boms/where-used?componentCode=RM-001&effectiveDate=2026-03-01", BusinessGatewayPermissions.EngineeringBomsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/manufacturing-boms/release", BusinessGatewayPermissions.EngineeringBomsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/routings", BusinessGatewayPermissions.EngineeringRoutingsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/routings/RTG-001/A", BusinessGatewayPermissions.EngineeringRoutingsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/routings/release", BusinessGatewayPermissions.EngineeringRoutingsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/standard-operations", "business.engineering.standard-operations.read");
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/standard-operations/OP-001", "business.engineering.standard-operations.read");
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/standard-operations", "business.engineering.standard-operations.manage");
        routes.Add(HttpMethod.Put, "/api/business-console/v1/engineering/standard-operations/OP-001", "business.engineering.standard-operations.manage");
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/standard-operations/OP-001/archive", "business.engineering.standard-operations.manage");
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-changes", BusinessGatewayPermissions.EngineeringChangesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/engineering-changes/ECO-001", BusinessGatewayPermissions.EngineeringChangesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-changes/release", BusinessGatewayPermissions.EngineeringChangesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-changes/cancel-scheduled", BusinessGatewayPermissions.EngineeringChangesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/engineering-changes/reschedule", BusinessGatewayPermissions.EngineeringChangesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/production-versions", BusinessGatewayPermissions.EngineeringProductionVersionsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/production-versions", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/engineering/production-versions/pv-001", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/engineering/production-versions/pv-001/archive", BusinessGatewayPermissions.EngineeringProductionVersionsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/engineering/production-versions/resolve", BusinessGatewayPermissions.EngineeringProductionVersionsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/files/file-sop-v2/download-grants", BusinessGatewayPermissions.EngineeringDocumentsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/files/download-grants/grant-sop-v2/content", BusinessGatewayPermissions.EngineeringDocumentsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/demands", BusinessGatewayPermissions.PlanningDemandsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/demands", BusinessGatewayPermissions.PlanningDemandsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/forecasts", BusinessGatewayPermissions.PlanningDemandsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/forecasts", BusinessGatewayPermissions.PlanningDemandsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/mps", BusinessGatewayPermissions.PlanningMpsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/mps", BusinessGatewayPermissions.PlanningMpsManage);
        routes.Add(HttpMethod.Put, "/api/business-console/v1/planning/mps/mps-001", BusinessGatewayPermissions.PlanningMpsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/mps/mps-001/review", BusinessGatewayPermissions.PlanningMpsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/mps/mps-001/release", BusinessGatewayPermissions.PlanningMpsRelease);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/mrp-runs", BusinessGatewayPermissions.PlanningMrpRun);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/mrp-runs", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/mrp-runs/mrp-run-001/pegging", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/planning/suggestions", BusinessGatewayPermissions.PlanningMrpRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/suggestions/suggestion-001/accept", BusinessGatewayPermissions.PlanningSuggestionsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/planning/demands/demand-001/cancel", BusinessGatewayPermissions.PlanningDemandsManage);
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
        routes.Add(HttpMethod.Post, "/api/business-console/v1/equipment/alarms/alarm-001/acknowledge", BusinessGatewayPermissions.IiotAlarmsWrite);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/equipment/alarms/alarm-001/shelve", BusinessGatewayPermissions.IiotAlarmsWrite);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/equipment/alarms/alarm-001/unshelve", BusinessGatewayPermissions.IiotAlarmsWrite);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/tags?deviceAssetId=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/alarm-rules?deviceAssetId=DEV-OIL-01", BusinessGatewayPermissions.IiotAlarmsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/telemetry/samples", BusinessGatewayPermissions.IiotTelemetryWrite);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/telemetry/alarms", BusinessGatewayPermissions.IiotAlarmsWrite);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/telemetry/alarm-rules", "business.iiot.alarm-rules.manage");
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/alarms?deviceAssetId=DEV-OIL-01&status=raised", BusinessGatewayPermissions.IiotAlarmsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/devices/DEV-OIL-01/history?fromUtc=2026-06-01T08:00:00Z&toUtc=2026-06-01T16:00:00Z", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/oee?deviceAssetId=DEV-OIL-01&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/telemetry/runtime-availability?windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/work-orders", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/maintenance/work-orders", BusinessGatewayPermissions.MaintenanceWorkOrdersManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/work-orders/wo-maint-001", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/maintenance/work-orders/wo-maint-001/complete", BusinessGatewayPermissions.MaintenanceWorkOrdersManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/plans", BusinessGatewayPermissions.MaintenancePlansRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/maintenance/plans", BusinessGatewayPermissions.MaintenancePlansManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/maintenance/inspections", BusinessGatewayPermissions.MaintenancePlansManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/inspections", BusinessGatewayPermissions.MaintenancePlansRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/inspection-measurements/trends?deviceAssetId=DEV-OIL-01&characteristicCode=bearing-temperature&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z", BusinessGatewayPermissions.MaintenancePlansRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/reliability/summary?deviceAssetId=DEV-OIL-01&technicianUserId=worker-001&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/spare-parts", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/maintenance/spare-parts", BusinessGatewayPermissions.MaintenanceWorkOrdersManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/maintenance/availability-windows?windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01", BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/procurement/purchase-orders", BusinessGatewayPermissions.ErpProcurementRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/procurement/rfqs", BusinessGatewayPermissions.ErpProcurementRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-requisitions/from-suggestion", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-requisitions/convert-to-purchase-order", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/rfqs", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/supplier-quotations", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-orders", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/procurement/purchase-receipts", BusinessGatewayPermissions.ErpProcurementManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/sales/sales-orders", BusinessGatewayPermissions.ErpSalesRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/sales/opportunities", BusinessGatewayPermissions.ErpSalesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/opportunities", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/sales/quotations", BusinessGatewayPermissions.ErpSalesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/quotations", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/quotations/QUO-001/approve", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/sales-orders", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/sales/delivery-orders", BusinessGatewayPermissions.ErpSalesRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/sales/delivery-orders", BusinessGatewayPermissions.ErpSalesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/payables", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/receivables", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/cost-candidates", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/erp/finance/vouchers", BusinessGatewayPermissions.ErpFinanceManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/vouchers", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/summary", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/payables/by-source?sourceDocumentNo=PR-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/receivables/by-source?sourceDocumentNo=DO-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/erp/finance/cost-candidates/by-source?sourceType=production&sourceDocumentNo=WO-001", BusinessGatewayPermissions.ErpFinanceRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/templates", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/templates", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/chains", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/chains", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/chains/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/tasks?actorType=user&actorRef=user-admin", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/decisions", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/chains/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9/steps/1/resolve", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/approval/delegations", BusinessGatewayPermissions.ApprovalsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/delegations", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/approval/delegations/delegation-001/revoke", BusinessGatewayPermissions.ApprovalsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/rules", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/rules", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/templates", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/templates", BusinessGatewayPermissions.BarcodeTemplatesManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/print-batches", BusinessGatewayPermissions.BarcodePrint);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/print-batches", BusinessGatewayPermissions.BarcodePrint);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/print-batches/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", BusinessGatewayPermissions.BarcodePrint);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/barcode/scans", BusinessGatewayPermissions.BarcodeScansWrite);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/barcode/scans", BusinessGatewayPermissions.BarcodeScansWrite);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/inbound-orders", BusinessGatewayPermissions.WmsReceiptsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders/inbound-order-001/putaway-tasks", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/putaway-tasks", BusinessGatewayPermissions.WmsReceiptsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/inbound-orders/inbound-order-001/complete", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/outbound-orders", BusinessGatewayPermissions.WmsShipmentsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders/outbound-order-001/picking-tasks", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/picking-tasks", BusinessGatewayPermissions.WmsShipmentsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/outbound-orders/outbound-order-001/complete", BusinessGatewayPermissions.WmsShipmentsManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/count-executions", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/count-executions", BusinessGatewayPermissions.WmsReceiptsRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/count-executions/count-execution-001/complete", BusinessGatewayPermissions.WmsReceiptsManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/wms/wcs-tasks", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/warehouse-task-001/dispatch", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/EXT-001/fail", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/wms/wcs-tasks/EXT-001/complete", BusinessGatewayPermissions.WmsAutomationManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/mes/work-orders", BusinessGatewayPermissions.MesWorkOrdersRead);
        routes.Add(HttpMethod.Post, "/api/business-console/v1/mes/work-orders/rush", BusinessGatewayPermissions.MesWorkOrdersManage);
        routes.Add(HttpMethod.Get, "/api/business-console/v1/mes/operation-sops/current?operationCode=STD-MIX", BusinessGatewayPermissions.MesOperationsRead);
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
        Action<IServiceCollection>? configureServices = null,
        Action<IWebHostBuilder>? configureBuilder = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            configureBuilder?.Invoke(builder);
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
