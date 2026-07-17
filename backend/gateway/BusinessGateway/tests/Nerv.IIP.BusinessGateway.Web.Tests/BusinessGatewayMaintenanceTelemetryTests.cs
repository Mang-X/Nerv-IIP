using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Maintenance;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayMaintenanceTelemetryTests
{
    [Fact]
    public async Task Connector_collection_health_authorizes_connector_scope_and_preserves_field_connection_loss()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var appHub = new RecordingAppHubClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessAppHubClient>();
            services.AddSingleton<IBusinessAppHubClient>(appHub);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/telemetry/connectors/opc-main/collection-health?organizationId=org-001&environmentId=env-dev");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("connector", auth.LastRequirement.ResourceType);
        Assert.Equal("opc-main", auth.LastRequirement.ResourceId);
        Assert.Equal("org-001", appHub.LastRequest!.OrganizationId);
        Assert.Equal("env-dev", appHub.LastRequest.EnvironmentId);
        Assert.Equal("internal-test-token", appHub.LastToken);
        Assert.Equal("stale", data.GetProperty("status").GetString());
        Assert.Equal("offline", data.GetProperty("staleReason").GetString());
        Assert.Equal("field-connection", data.GetProperty("offlineReason").GetString());
        var connection = data.GetProperty("connection");
        Assert.Equal("lost", connection.GetProperty("status").GetString());
        Assert.Equal("2026-07-13T01:00:00+00:00", connection.GetProperty("disconnectedSinceUtc").GetString());
        Assert.Equal("2026-07-13T01:05:06+00:00", data.GetProperty("hostLivenessDeadlineUtc").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("receivedCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("droppedCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("errorCount").ValueKind);
    }

    [Fact]
    public async Task Connector_collection_health_list_authorizes_telemetry_read_and_forwards_scope()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var appHub = new RecordingAppHubClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessAppHubClient>();
            services.AddSingleton<IBusinessAppHubClient>(appHub);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/telemetry/connectors/collection-health?organizationId=org-001&environmentId=env-dev");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        var items = data.GetProperty("items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", appHub.LastListRequest!.OrganizationId);
        Assert.Equal("env-dev", appHub.LastListRequest.EnvironmentId);
        Assert.Equal("internal-test-token", appHub.LastToken);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal("modbus-main", items[0].GetProperty("connectorId").GetString());
        Assert.Equal("stale", items[0].GetProperty("status").GetString());
        Assert.Equal("offline", items[0].GetProperty("staleReason").GetString());
        Assert.Equal("host-liveness", items[0].GetProperty("offlineReason").GetString());
        Assert.Equal("alive", items[0].GetProperty("connection").GetProperty("status").GetString());
        Assert.Equal("2026-07-13T01:00:06+00:00", items[0].GetProperty("hostLivenessDeadlineUtc").GetString());
        Assert.Equal("modbus", items[0].GetProperty("sourceSystem").GetString());
        Assert.Equal("mqtt-main", items[1].GetProperty("connectorId").GetString());
        Assert.Equal("fault", items[1].GetProperty("staleReason").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("offlineReason").ValueKind);
        Assert.Equal("2026-07-13T01:05:06+00:00", items[1].GetProperty("hostLivenessDeadlineUtc").GetString());
        Assert.Equal("legacy-main", items[2].GetProperty("connectorId").GetString());
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("connection").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("staleReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("offlineReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("hostLivenessDeadlineUtc").ValueKind);
    }

    [Fact]
    public void Complete_work_order_validator_limits_actual_technician_reference_length()
    {
        var result = new BusinessConsoleCompleteMaintenanceWorkOrderRequestValidator().Validate(
            new BusinessConsoleCompleteMaintenanceWorkOrderRequest(
                "org-001",
                "env-dev",
                "fixed",
                "equipment-failure",
                10,
                [],
                ActualTechnicianUserId: new string('x', 151)));

        Assert.Contains(result.Errors, x =>
            string.Equals(
                x.PropertyName.Replace(" ", string.Empty, StringComparison.Ordinal),
                nameof(BusinessConsoleCompleteMaintenanceWorkOrderRequest.ActualTechnicianUserId),
                StringComparison.OrdinalIgnoreCase)
            && x.ErrorMessage.Contains("150", StringComparison.Ordinal));
    }

    [Fact]
    public void Maintenance_plan_gateway_validators_match_service_interval_length_limit()
    {
        var acceptedInterval = new string('D', 50);
        var rejectedInterval = new string('D', 51);
        var startsOn = new DateOnly(2026, 7, 17);

        var createValidator = new BusinessConsoleCreateMaintenancePlanRequestValidator();
        Assert.True(createValidator.Validate(new BusinessConsoleCreateMaintenancePlanRequest(
            "org-001",
            "env-dev",
            "DEV-PRESS-01",
            "PM-PRESS-01",
            acceptedInterval,
            startsOn,
            "设备保全班",
            null,
            null)).IsValid);
        Assert.False(createValidator.Validate(new BusinessConsoleCreateMaintenancePlanRequest(
            "org-001",
            "env-dev",
            "DEV-PRESS-01",
            "PM-PRESS-01",
            rejectedInterval,
            startsOn,
            "设备保全班",
            null,
            null)).IsValid);

        var updateValidator = new BusinessConsoleUpdateMaintenancePlanRequestValidator();
        Assert.True(updateValidator.Validate(new BusinessConsoleUpdateMaintenancePlanRequest(
            "org-001",
            "env-dev",
            acceptedInterval,
            null)).IsValid);
        Assert.False(updateValidator.Validate(new BusinessConsoleUpdateMaintenancePlanRequest(
            "org-001",
            "env-dev",
            rejectedInterval,
            null)).IsValid);
    }

    [Fact]
    public async Task Workshop_data_scope_is_pushed_down_to_maintenance_telemetry_and_equipment_alarm_lists()
    {
        var dataScope = new AuthorizationDataScope([], ["WS-A"], []);
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(dataScope);
        var maintenance = new RecordingMaintenanceFacadeClient();
        var telemetry = new RecordingTelemetryFacadeClient();
        var masterData = new RecordingMasterDataClient
        {
            Resources =
            [
                new BusinessConsoleResourceItem("production-line", "LINE-A", "Line A", true, "v1", WorkshopCode: "WS-A"),
                new BusinessConsoleResourceItem("production-line", "LINE-B", "Line B", true, "v1", WorkshopCode: "WS-B"),
                new BusinessConsoleResourceItem("work-center", "WC-A", "Work center A", true, "v1", LineCode: "LINE-A", WorkshopCode: "WS-A"),
                new BusinessConsoleResourceItem("work-center", "WC-B", "Work center B", true, "v1", LineCode: "LINE-B", WorkshopCode: "WS-B"),
                new BusinessConsoleResourceItem("device-asset", "DEV-A-CODE", "Device A", true, "v1", LineCode: "LINE-A", WorkCenterCode: "WC-A", DeviceAssetId: "DEV-A"),
                new BusinessConsoleResourceItem("device-asset", "DEV-B-CODE", "Device B", true, "v1", LineCode: "LINE-B", WorkCenterCode: "WC-B", DeviceAssetId: "DEV-B"),
            ],
        };
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var maintenanceResponse = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev");
        var telemetryResponse = await client.GetAsync("/api/business-console/v1/telemetry/alarms?organizationId=org-001&environmentId=env-dev&status=active");
        var equipmentResponse = await client.GetAsync("/api/business-console/v1/equipment/alarms?organizationId=org-001&environmentId=env-dev&status=active");

        Assert.Equal(HttpStatusCode.OK, maintenanceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, telemetryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, equipmentResponse.StatusCode);
        Assert.Equal("DEV-A", maintenance.LastWorkOrderListRequest!.DeviceAssetIds);
        Assert.Equal("DEV-A", telemetry.LastAlarmListRequest!.DeviceAssetIds);
        Assert.Equal("DEV-A", telemetry.LastEquipmentAlarmListRequest!.DeviceAssetIds);
        Assert.DoesNotContain("DEV-B", maintenance.LastWorkOrderListRequest.DeviceAssetIds);
    }

    [Fact]
    public async Task Maintenance_work_order_list_uses_maintenance_permission_and_preserves_device_alarm_context()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=5&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MaintenanceWorkOrdersRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal(new BusinessConsoleMaintenanceWorkOrderListRequest("org-001", "env-dev", 5, 10), maintenance.LastWorkOrderListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("wo-maint-001", item.GetProperty("workOrderId").GetString());
        Assert.Equal("DEV-PRESS-01", item.GetProperty("deviceAssetId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("sourceAlarmId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("relatedAlarmId").GetString());
        Assert.Equal("in-warranty", item.GetProperty("warrantyStatus").GetString());
        Assert.Equal("2027-01-14", item.GetProperty("warrantyExpiresOn").GetString());
        Assert.Equal("SUP-ACME", item.GetProperty("supplierPartnerCode").GetString());
        Assert.Equal(new[] { "DEV-PRESS-01" }, masterData.DetailRequests.Select(x => x.Code).ToArray());
        Assert.Equal(5, document.RootElement.GetProperty("data").GetProperty("skip").GetInt32());
        Assert.Equal(10, document.RootElement.GetProperty("data").GetProperty("take").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Maintenance_work_order_list_enriches_distinct_device_assets_once()
    {
        var maintenance = new RecordingMaintenanceFacadeClient
        {
            WorkOrderItems =
            [
                new BusinessConsoleMaintenanceWorkOrderItem("wo-maint-001", "018ff8f1-2b8a-7000-8000-000000000001", "high", "Open", null, null, DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture)),
                new BusinessConsoleMaintenanceWorkOrderItem("wo-maint-002", "018ff8f1-2b8a-7000-8000-000000000001", "medium", "Open", null, null, DateTimeOffset.Parse("2026-06-01T08:20:00Z", CultureInfo.InvariantCulture)),
            ],
        };
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=0&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "018ff8f1-2b8a-7000-8000-000000000001" }, masterData.DetailRequests.Select(x => x.Code).ToArray());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), item => Assert.Equal("in-warranty", item.GetProperty("warrantyStatus").GetString()));
    }

    [Fact]
    public async Task Maintenance_work_order_warranty_enrichment_degrades_master_data_outages_to_unknown()
    {
        var masterData = new RecordingMasterDataClient
        {
            DetailFailure = new BusinessServiceProxyException(HttpStatusCode.BadGateway, "master-data-unavailable"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(new RecordingMaintenanceFacadeClient());
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=0&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "unknown",
            document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("warrantyStatus").GetString());
    }

    public static TheoryData<Exception> UnavailableMasterDataTransportFailures =>
        new()
        {
            new HttpRequestException("connection refused"),
            new TaskCanceledException("client timeout"),
        };

    [Theory]
    [MemberData(nameof(UnavailableMasterDataTransportFailures))]
    public async Task Maintenance_work_order_warranty_enrichment_degrades_transport_failures_to_unknown(Exception transportFailure)
    {
        var masterData = new RecordingMasterDataClient
        {
            DetailFailure = transportFailure,
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(new RecordingMaintenanceFacadeClient());
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=0&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "unknown",
            document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("warrantyStatus").GetString());
    }

    [Fact]
    public void Maintenance_work_order_list_validator_bounds_downstream_fan_out()
    {
        var validator = new BusinessConsoleMaintenanceWorkOrderListRequestValidator();

        Assert.True(validator.Validate(new BusinessConsoleMaintenanceWorkOrderListRequest("org-001", "env-dev", 0, 200)).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenanceWorkOrderListRequest("org-001", "env-dev", -1, 10)).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenanceWorkOrderListRequest("org-001", "env-dev", 0, 201)).IsValid);
    }

    [Fact]
    public void Maintenance_plan_list_validator_enforces_context_paging_and_device_bounds()
    {
        // The plan-list endpoint binds a dedicated request type; it needs its own validator, else org/env,
        // skip/take bounds and the optional DeviceAssetId length go unchecked.
        var validator = new BusinessConsoleMaintenancePlanListRequestValidator();

        Assert.True(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "env-dev", 0, 200, "DEV-CNC-01")).IsValid);
        Assert.True(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "env-dev")).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("", "env-dev")).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "")).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "env-dev", -1, 10)).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "env-dev", 0, 201)).IsValid);
        Assert.False(validator.Validate(new BusinessConsoleMaintenancePlanListRequest("org-001", "env-dev", 0, 100, new string('D', 151))).IsValid);
    }

    [Fact]
    public async Task Maintenance_work_order_detail_reads_existing_work_order_surface_by_id()
    {
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(new RecordingMasterDataClient());
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders/wo-maint-001?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("wo-maint-001", maintenance.LastWorkOrderDetailId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("DEV-PRESS-01", data.GetProperty("deviceAssetId").GetString());
        Assert.Equal("alarm-001", data.GetProperty("relatedAlarmId").GetString());
    }

    [Fact]
    public async Task Maintenance_plans_and_windows_use_maintenance_specific_permissions()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var plansResponse = await client.GetAsync("/api/business-console/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01");
        var windowsResponse = await client.GetAsync("/api/business-console/v1/maintenance/availability-windows?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-PRESS-01");

        Assert.Equal(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, windowsResponse.StatusCode);
        // Device filter flows through to the downstream plan list request.
        Assert.Equal("DEV-PRESS-01", maintenance.LastPlanListRequest?.DeviceAssetId);
        using var plansDocument = JsonDocument.Parse(await plansResponse.Content.ReadAsStringAsync());
        var planItem = plansDocument.RootElement.GetProperty("data").GetProperty("items")[0];
        // Runtime-only plan: no calendar interval / next-due; runtime threshold fields surfaced.
        Assert.Equal(JsonValueKind.Null, planItem.GetProperty("interval").ValueKind);
        Assert.Equal(JsonValueKind.Null, planItem.GetProperty("nextDueOn").ValueKind);
        Assert.Equal(1000m, planItem.GetProperty("runtimeHourInterval").GetDecimal());
        Assert.Equal(1000m, planItem.GetProperty("nextDueRuntimeHours").GetDecimal());
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenancePlansRead);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        Assert.DoesNotContain(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.IiotTelemetryRead);
        Assert.Equal(new BusinessConsoleEquipmentAvailabilityRequest(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture),
            "DEV-PRESS-01",
            null), maintenance.LastAvailabilityRequest);
    }

    [Fact]
    public async Task Maintenance_work_order_write_facade_uses_manage_permission_and_forwards_payloads()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var createResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            priority = "high",
            sourceAlarmId = "alarm-001",
            openedBy = "operator-001",
            assetUnavailableReason = "alarm-raised",
        });
        var completeResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/work-orders/wo-maint-001/complete", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            result = "restored",
            downtimeReasonCode = "mechanical",
            downtimeMinutes = 35,
            spareParts = new[]
            {
                new { skuCode = "SPARE-001", quantity = 2m, uomCode = "EA" },
            },
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersManage);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal("DEV-PRESS-01", maintenance.LastCreateWorkOrderRequest.GetProperty("deviceAssetId").GetString());
        Assert.Equal("wo-maint-001", maintenance.LastCompleteWorkOrderId);
        Assert.Equal("restored", maintenance.LastCompleteWorkOrderRequest.GetProperty("result").GetString());
        Assert.Equal("SPARE-001", maintenance.LastCompleteWorkOrderRequest.GetProperty("spareParts")[0].GetProperty("skuCode").GetString());
    }

    [Fact]
    public async Task Maintenance_plan_and_inspection_write_facades_use_plan_manage_permission_and_forward_payloads()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var planResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/plans", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            interval = "P7D",
            startsOn = "2026-06-01",
            owner = "maintenance",
            windowStartUtc = "2026-06-01T08:00:00Z",
            windowEndUtc = "2026-06-01T16:00:00Z",
            idempotencyKey = "maintenance-plan-create-001",
            runtimeHourInterval = 1000m,
        });
        var inspectionResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/inspections", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            planId = "plan-001",
            workOrderId = "wo-maint-001",
            inspector = "inspector-001",
            result = "passed",
            inspectedAtUtc = "2026-06-01T09:00:00Z",
        });

        Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inspectionResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenancePlansManage);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.True(maintenance.LastCreatePlanRequest.TryGetProperty("planCode", out var planCode));
        Assert.Equal(JsonValueKind.Null, planCode.ValueKind);
        Assert.Equal("maintenance-plan-create-001", maintenance.LastCreatePlanRequest.GetProperty("idempotencyKey").GetString());
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            maintenance.LastCreatePlanRequest.GetProperty("windowStartUtc").GetDateTimeOffset());
        Assert.Equal(1000m, maintenance.LastCreatePlanRequest.GetProperty("runtimeHourInterval").GetDecimal());
        Assert.Equal("plan-001", maintenance.LastRecordInspectionRequest.GetProperty("planId").GetString());
        Assert.Equal("wo-maint-001", maintenance.LastRecordInspectionRequest.GetProperty("workOrderId").GetString());
    }

    [Fact]
    public async Task Maintenance_plan_update_facade_preserves_explicit_null_triggers_in_downstream_json()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var handler = new RecordingMaintenanceUpdateHandler();
        using var downstreamHttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var maintenance = new HttpBusinessMaintenanceClient(downstreamHttpClient);
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var runtimeOnlyResponse = await client.PutAsJsonAsync("/api/business-console/v1/maintenance/plans/plan-runtime", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            interval = (string?)null,
            runtimeHourInterval = 500m,
        });
        var calendarOnlyResponse = await client.PutAsJsonAsync("/api/business-console/v1/maintenance/plans/plan-calendar", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            interval = "P30D",
            runtimeHourInterval = (decimal?)null,
        });

        Assert.Equal(HttpStatusCode.OK, runtimeOnlyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, calendarOnlyResponse.StatusCode);
        Assert.All(auth.Requirements, x => Assert.Equal(BusinessGatewayPermissions.MaintenancePlansManage, x.PermissionCode));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("internal-test-token", request.BearerToken);
        });

        Assert.Equal("/api/business/v1/maintenance/plans/plan-runtime", handler.Requests[0].Path);
        using var runtimeOnlyBody = JsonDocument.Parse(handler.Requests[0].Body);
        var runtimeOnlyRoot = runtimeOnlyBody.RootElement;
        Assert.Equal(JsonValueKind.Null, runtimeOnlyRoot.GetProperty("interval").ValueKind);
        Assert.Equal(500m, runtimeOnlyRoot.GetProperty("runtimeHourInterval").GetDecimal());

        Assert.Equal("/api/business/v1/maintenance/plans/plan-calendar", handler.Requests[1].Path);
        using var calendarOnlyBody = JsonDocument.Parse(handler.Requests[1].Body);
        var calendarOnlyRoot = calendarOnlyBody.RootElement;
        Assert.Equal("P30D", calendarOnlyRoot.GetProperty("interval").GetString());
        Assert.Equal(JsonValueKind.Null, calendarOnlyRoot.GetProperty("runtimeHourInterval").ValueKind);
    }

    [Fact]
    public async Task Maintenance_plan_update_facade_rejects_a_request_without_any_trigger()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PutAsJsonAsync("/api/business-console/v1/maintenance/plans/plan-001", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            interval = (string?)null,
            runtimeHourInterval = (decimal?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_generate_due_and_reliability_facades_use_permissions_and_forward_scope()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var generateResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/plans/generate-due", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            businessDate = "2026-06-17",
            requestedBy = "planner-001",
        });
        var reliabilityResponse = await client.GetAsync("/api/business-console/v1/maintenance/assets/DEV-PRESS-01/reliability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-30T16:00:00Z");

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reliabilityResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenancePlansManage);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal(new BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest("org-001", "env-dev", new DateOnly(2026, 6, 17), "planner-001"), maintenance.LastGenerateDueRequest);
        Assert.Equal("DEV-PRESS-01", maintenance.LastReliabilityDeviceAssetId);
        Assert.Equal(new BusinessConsoleQueryMaintenanceAssetReliabilityRequest(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-30T16:00:00Z", CultureInfo.InvariantCulture)), maintenance.LastReliabilityRequest);

        using var document = JsonDocument.Parse(await reliabilityResponse.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("DEV-PRESS-01", data.GetProperty("deviceAssetId").GetString());
        Assert.Equal(2, data.GetProperty("failureCount").GetInt32());
        Assert.Equal(24.5m, data.GetProperty("mtbfHours").GetDecimal());
        Assert.Equal(35m, data.GetProperty("mttrMinutes").GetDecimal());
        Assert.Equal("oee", data.GetProperty("mtbfRuntimeSource").GetString());
        Assert.True(data.GetProperty("mtbfRuntimeHasSamples").GetBoolean());
    }

    [Fact]
    public async Task Maintenance_inspection_and_spare_part_facades_use_maintenance_permissions_and_forward_paging()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var inspectionsResponse = await client.GetAsync("/api/business-console/v1/maintenance/inspections?organizationId=org-001&environmentId=env-dev&skip=2&take=3");
        var sparePartsResponse = await client.GetAsync("/api/business-console/v1/maintenance/spare-parts?organizationId=org-001&environmentId=env-dev&skip=4&take=5");
        var createSparePartResponse = await client.PostAsJsonAsync("/api/business-console/v1/maintenance/spare-parts", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "wo-maint-001",
            skuCode = "SPARE-001",
            quantity = 2m,
            uomCode = "EA",
        });

        Assert.Equal(HttpStatusCode.OK, inspectionsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sparePartsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createSparePartResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenancePlansRead);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersManage);
        Assert.Equal(new BusinessConsoleMaintenanceListRequest("org-001", "env-dev", 2, 3), maintenance.LastInspectionListRequest);
        Assert.Equal(new BusinessConsoleMaintenanceListRequest("org-001", "env-dev", 4, 5), maintenance.LastSparePartListRequest);
        Assert.Equal("wo-maint-001", maintenance.LastCreateSparePartRequest.GetProperty("workOrderId").GetString());
        Assert.Equal("SPARE-001", maintenance.LastCreateSparePartRequest.GetProperty("skuCode").GetString());
    }

    [Fact]
    public async Task Maintenance_measurement_trend_and_reliability_summary_facades_forward_queries()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var trendResponse = await client.GetAsync("/api/business-console/v1/maintenance/inspection-measurements/trends?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&characteristicCode=bearing-temperature&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-30T16:00:00Z");
        var summaryResponse = await client.GetAsync("/api/business-console/v1/maintenance/reliability/summary?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&technicianUserId=worker-001&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-30T16:00:00Z");

        Assert.Equal(HttpStatusCode.OK, trendResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenancePlansRead);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersRead);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal(new BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest(
            "org-001",
            "env-dev",
            "DEV-PRESS-01",
            "bearing-temperature",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-30T16:00:00Z", CultureInfo.InvariantCulture)), maintenance.LastInspectionMeasurementTrendRequest);
        Assert.Equal(new BusinessConsoleQueryMaintenanceReliabilitySummaryRequest(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-30T16:00:00Z", CultureInfo.InvariantCulture),
            "DEV-PRESS-01",
            "worker-001"), maintenance.LastReliabilitySummaryRequest);

        using var trendDocument = JsonDocument.Parse(await trendResponse.Content.ReadAsStringAsync());
        Assert.Equal(65m, trendDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("measuredValue").GetDecimal());
        using var summaryDocument = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        Assert.Equal(165m, summaryDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("totalCostAmount").GetDecimal());
    }

    [Fact]
    public async Task Telemetry_history_uses_iiot_permission_and_forwards_device_time_range()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var telemetry = new RecordingTelemetryFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/telemetry/devices/DEV-PRESS-01/history?organizationId=org-001&environmentId=env-dev&fromUtc=2026-06-01T08:00:00Z&toUtc=2026-06-01T12:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("internal-test-token", telemetry.LastInternalToken);
        Assert.Equal("DEV-PRESS-01", telemetry.LastHistoryDeviceAssetId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture), telemetry.LastHistoryRequest!.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T12:00:00Z", CultureInfo.InvariantCulture), telemetry.LastHistoryRequest.ToUtc);
    }

    [Fact]
    public async Task Telemetry_runtime_hours_enforces_read_permission_and_preserves_cumulative_facts()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var telemetry = new RecordingTelemetryFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/telemetry/runtime-hours?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T12:00:00Z");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("internal-test-token", telemetry.LastInternalToken);
        Assert.Equal("org-001", telemetry.LastRuntimeHoursRequest!.OrganizationId);
        Assert.Equal(2.5m, body.GetProperty("totalRuntimeHours").GetDecimal());
        Assert.Equal(3m, body.GetProperty("totalLoadingHours").GetDecimal());
        Assert.True(body.GetProperty("hasRuntimeSamples").GetBoolean());
        Assert.Single(body.GetProperty("daily").EnumerateArray());
    }

    [Fact]
    public async Task Telemetry_tags_and_alarms_use_their_industrial_telemetry_permissions()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var telemetry = new RecordingTelemetryFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var tagsResponse = await client.GetAsync("/api/business-console/v1/telemetry/tags?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01");
        var alarmsResponse = await client.GetAsync("/api/business-console/v1/telemetry/alarms?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&status=raised");

        Assert.Equal(HttpStatusCode.OK, tagsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alarmsResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.IiotTelemetryRead);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.IiotAlarmsRead);
        Assert.Equal(new BusinessConsoleTelemetryTagListRequest("org-001", "env-dev", "DEV-PRESS-01"), telemetry.LastTagListRequest);
        Assert.Equal(new BusinessConsoleTelemetryAlarmListRequest("org-001", "env-dev", "DEV-PRESS-01", "raised"), telemetry.LastAlarmListRequest);
    }

    [Fact]
    public async Task Equipment_alarm_lifecycle_actions_use_alarm_write_permission_and_forward_payloads()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var telemetry = new RecordingTelemetryFacadeClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var ackResponse = await client.PostAsJsonAsync("/api/business-console/v1/equipment/alarms/alarm-001/acknowledge", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            acknowledgedAtUtc = "2026-07-06T08:05:00Z",
            acknowledgedBy = "operator-001",
        });
        var shelveResponse = await client.PostAsJsonAsync("/api/business-console/v1/equipment/alarms/alarm-001/shelve", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            shelvedAtUtc = "2026-07-06T08:06:00Z",
            durationMinutes = 30,
            shelvedBy = "operator-001",
            reason = "maintenance check",
        });
        var unshelveResponse = await client.PostAsJsonAsync("/api/business-console/v1/equipment/alarms/alarm-001/unshelve", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            unshelvedAtUtc = "2026-07-06T08:40:00Z",
        });

        Assert.Equal(HttpStatusCode.OK, ackResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, shelveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unshelveResponse.StatusCode);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == BusinessGatewayPermissions.IiotAlarmsWrite);
        Assert.Equal("internal-test-token", telemetry.LastInternalToken);
        Assert.Equal("alarm-001", telemetry.LastAlarmLifecycleId);
        var request = Assert.IsType<BusinessConsoleUnshelveAlarmRequest>(telemetry.LastAlarmLifecycleRequest);
        Assert.Equal("org-001", request.OrganizationId);
    }

    [Fact]
    public async Task Telemetry_and_equipment_alarm_lists_forward_paging_and_filters()
    {
        var telemetry = new RecordingTelemetryFacadeClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var tagsResponse = await client.GetAsync("/api/business-console/v1/telemetry/tags?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&skip=5&take=10");
        var rulesResponse = await client.GetAsync("/api/business-console/v1/telemetry/alarm-rules?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&isEnabled=true&skip=6&take=11");
        var alarmsResponse = await client.GetAsync("/api/business-console/v1/telemetry/alarms?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-01&status=cleared&skip=7&take=12");
        var equipmentResponse = await client.GetAsync("/api/business-console/v1/equipment/alarms?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PRESS-02&status=raised&skip=8&take=13");

        Assert.Equal(HttpStatusCode.OK, tagsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rulesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alarmsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, equipmentResponse.StatusCode);
        Assert.Equal(new BusinessConsoleTelemetryTagListRequest("org-001", "env-dev", "DEV-PRESS-01", 5, 10), telemetry.LastTagListRequest);
        Assert.Equal(new BusinessConsoleTelemetryAlarmRuleListRequest("org-001", "env-dev", "DEV-PRESS-01", true, 6, 11), telemetry.LastAlarmRuleListRequest);
        Assert.Equal(new BusinessConsoleTelemetryAlarmListRequest("org-001", "env-dev", "DEV-PRESS-01", "cleared", 7, 12), telemetry.LastAlarmListRequest);
        Assert.Equal(new BusinessConsoleEquipmentAlarmListRequest("org-001", "env-dev", "DEV-PRESS-02", "raised", 8, 13), telemetry.LastEquipmentAlarmListRequest);

        Assert.Equal(42, ReadTotal(await tagsResponse.Content.ReadAsStringAsync()));
        Assert.Equal(42, ReadTotal(await rulesResponse.Content.ReadAsStringAsync()));
        Assert.Equal(42, ReadTotal(await alarmsResponse.Content.ReadAsStringAsync()));
        Assert.Equal(42, ReadTotal(await equipmentResponse.Content.ReadAsStringAsync()));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private sealed class RecordingAppHubClient : IBusinessAppHubClient
    {
        public string? LastToken { get; private set; }
        public BusinessConsoleConnectorCollectionHealthRequest? LastRequest { get; private set; }
        public BusinessConsoleConnectorCollectionHealthListRequest? LastListRequest { get; private set; }
        public Task<BusinessConsoleConnectorCollectionHealthResponse> GetCollectionHealthAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthRequest request, CancellationToken cancellationToken)
        {
            LastToken = internalBearerToken;
            LastRequest = request;
            return Task.FromResult(new BusinessConsoleConnectorCollectionHealthResponse(
                request.ConnectorId,
                "stale",
                DateTimeOffset.Parse("2026-07-13T01:05:00Z"),
                null,
                null,
                null,
                null,
                null,
                "opcua",
                new BusinessConsoleConnectorConnectionState(
                    "lost",
                    DateTimeOffset.Parse("2026-07-13T01:00:00Z"),
                    DisconnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:00:00Z"),
                    ReasonCategory: "network",
                    DiagnosticCode: "connection-lost"),
                "offline",
                "field-connection",
                DateTimeOffset.Parse("2026-07-13T01:05:06Z")));
        }

        public Task<BusinessConsoleConnectorCollectionHealthListResponse> GetCollectionHealthListAsync(string internalBearerToken, BusinessConsoleConnectorCollectionHealthListRequest request, CancellationToken cancellationToken)
        {
            LastToken = internalBearerToken;
            LastListRequest = request;
            return Task.FromResult(new BusinessConsoleConnectorCollectionHealthListResponse(
                [
                    new BusinessConsoleConnectorCollectionHealthListItem(
                        "modbus-main", "Modbus Main", "stale", "offline", null, null, null, 50, 9, 2,
                        Guid.Parse("22222222-2222-2222-2222-222222222222"), "modbus",
                        new BusinessConsoleConnectorConnectionState("alive", DateTimeOffset.Parse("2026-07-13T00:55:00Z"), ConnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T00:50:00Z")),
                        "host-liveness",
                        DateTimeOffset.Parse("2026-07-13T01:00:06Z")),
                    new BusinessConsoleConnectorCollectionHealthListItem(
                        "mqtt-main", "MQTT Main", "stale", "fault", null, null, null, 70, 0, 1,
                        Guid.Parse("44444444-4444-4444-4444-444444444444"), "mqtt",
                        new BusinessConsoleConnectorConnectionState("alive", DateTimeOffset.Parse("2026-07-13T01:05:00Z"), ConnectedSinceUtc: DateTimeOffset.Parse("2026-07-13T01:00:00Z")),
                        null,
                        DateTimeOffset.Parse("2026-07-13T01:05:06Z")),
                    new BusinessConsoleConnectorCollectionHealthListItem(
                        "legacy-main", "Legacy Main", "unknown", null, null, null, DateTimeOffset.Parse("2026-07-13T01:04:59Z"), 10, 0, 0,
                        Guid.Parse("99999999-9999-9999-9999-999999999999"), "opcua", null, null),
                ],
                3));
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class RecordingMaintenanceUpdateHandler : HttpMessageHandler
    {
        public List<RecordedMaintenanceUpdateRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedMaintenanceUpdateRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Parameter,
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            var planId = request.RequestUri.AbsolutePath.Split('/').Last();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { data = new { planId } }),
            };
        }
    }

    private sealed record RecordedMaintenanceUpdateRequest(
        HttpMethod Method,
        string Path,
        string? BearerToken,
        string Body);

    private static int ReadTotal(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("total").GetInt32();
    }
}

internal sealed class RecordingMaintenanceFacadeClient : IBusinessMaintenanceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleMaintenanceWorkOrderListRequest? LastWorkOrderListRequest { get; private set; }

    public BusinessConsoleMaintenanceListRequest? LastInspectionListRequest { get; private set; }

    public BusinessConsoleMaintenanceListRequest? LastSparePartListRequest { get; private set; }

    public string? LastWorkOrderDetailId { get; private set; }

    public BusinessConsoleEquipmentAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public JsonElement LastCreateWorkOrderRequest { get; private set; }

    public string? LastCompleteWorkOrderId { get; private set; }

    public JsonElement LastCompleteWorkOrderRequest { get; private set; }

    public JsonElement LastCreatePlanRequest { get; private set; }

    public string? LastUpdatePlanId { get; private set; }

    public JsonElement LastUpdatePlanRequest { get; private set; }

    public BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest? LastGenerateDueRequest { get; private set; }

    public string? LastReliabilityDeviceAssetId { get; private set; }

    public BusinessConsoleQueryMaintenanceAssetReliabilityRequest? LastReliabilityRequest { get; private set; }

    public BusinessConsoleQueryMaintenanceReliabilitySummaryRequest? LastReliabilitySummaryRequest { get; private set; }

    public BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest? LastInspectionMeasurementTrendRequest { get; private set; }

    public JsonElement LastRecordInspectionRequest { get; private set; }

    public JsonElement LastCreateSparePartRequest { get; private set; }

    public IReadOnlyCollection<BusinessConsoleMaintenanceWorkOrderItem>? WorkOrderItems { get; init; }

    public Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateWorkOrderRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleCreateMaintenanceWorkOrderResponse("wo-maint-created"));
    }

    public Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteWorkOrderId = workOrderId;
        LastCompleteWorkOrderRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleCompleteMaintenanceWorkOrderResponse(true));
    }

    public Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastWorkOrderListRequest = request;
        var items = WorkOrderItems ??
        [
            new BusinessConsoleMaintenanceWorkOrderItem(
                "wo-maint-001",
                "DEV-PRESS-01",
                "high",
                "Open",
                "alarm-001",
                "alarm-001",
                DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture)),
        ];
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderListResponse(
            items, request.Skip, request.Take, items.Count));
    }

    public Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastWorkOrderDetailId = workOrderId;
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderItem(
            workOrderId,
            "DEV-PRESS-01",
            "high",
            "Open",
            "alarm-001",
            "alarm-001",
            DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture)));
    }

    public BusinessConsoleMaintenancePlanListRequest? LastPlanListRequest { get; private set; }

    public Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenancePlanListRequest request,
        CancellationToken cancellationToken)
    {
        LastPlanListRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenancePlanListResponse(
        [
            new BusinessConsoleMaintenancePlanItem(
                "plan-001",
                "DEV-PRESS-01",
                "PM-PRESS",
                null,
                new DateOnly(2026, 6, 1),
                null,
                1000m,
                1000m,
                0m),
        ], request.Skip, request.Take, 1));
    }

    public Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastInspectionListRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenanceInspectionListResponse(
        [
            new BusinessConsoleMaintenanceInspectionItem(
                "inspection-001",
                "plan-001",
                null,
                "inspector-001",
                "passed",
                DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture)),
        ], request.Skip, request.Take, 1));
    }

    public Task<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse> QueryInspectionMeasurementTrendAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastInspectionMeasurementTrendRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenanceInspectionMeasurementTrendResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.CharacteristicCode,
            request.WindowStartUtc,
            request.WindowEndUtc,
            [
                new BusinessConsoleMaintenanceInspectionMeasurementTrendItem(
                    "inspection-001",
                    "plan-001",
                    null,
                    DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture),
                    65m,
                    "C",
                    0m,
                    70m,
                    true),
            ]));
    }

    public Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastSparePartListRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenanceSparePartListResponse(
        [
            new BusinessConsoleMaintenanceSparePartItem(
                "spare-line-001",
                "wo-maint-001",
                "DEV-PRESS-01",
                "SPARE-001",
                2m,
                "EA"),
        ], request.Skip, request.Take, 1));
    }

    public Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateSparePartRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleCreateMaintenanceSparePartResponse("spare-line-created"));
    }

    public Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreatePlanRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleCreateMaintenancePlanResponse("plan-created"));
    }

    public Task<BusinessConsoleUpdateMaintenancePlanResponse> UpdatePlanAsync(
        string internalBearerToken,
        string planId,
        BusinessConsoleUpdateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastUpdatePlanId = planId;
        LastUpdatePlanRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleUpdateMaintenancePlanResponse(planId));
    }

    public Task<BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse> GenerateDueWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastGenerateDueRequest = request;
        return Task.FromResult(new BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse(2, ["wo-pm-001", "wo-pm-002"]));
    }

    public Task<BusinessConsoleAssetReliabilityResponse> QueryAssetReliabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleQueryMaintenanceAssetReliabilityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastReliabilityDeviceAssetId = deviceAssetId;
        LastReliabilityRequest = request;
        return Task.FromResult(new BusinessConsoleAssetReliabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            deviceAssetId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            2,
            2,
            24.5m,
            35m,
            "oee",
            true));
    }

    public Task<BusinessConsoleMaintenanceReliabilitySummaryResponse> QueryReliabilitySummaryAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastReliabilitySummaryRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenanceReliabilitySummaryResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            [
                new BusinessConsoleMaintenanceReliabilitySummaryItem(
                    "DEV-PRESS-01",
                    "worker-001",
                    "CNY",
                    2,
                    120,
                    95,
                    130m,
                    35m,
                    165m),
            ]));
    }

    public Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRecordInspectionRequest = JsonSerializer.SerializeToElement(request, JsonOptions);
        return Task.FromResult(new BusinessConsoleRecordMaintenanceInspectionResponse("inspection-created"));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        return Task.FromResult(BusinessGatewayProxyTests.CreateAvailabilityResponse(
            BusinessGatewayProxyTests.CreateWindow(
                "DEV-PRESS-01",
                "wo-maint-001",
                EquipmentRuntimeSourceType.Downtime,
                EquipmentRuntimeReasonCodes.Downtime,
                EquipmentRuntimeSeverity.Blocked,
                "2026-06-01T09:00:00Z",
                "2026-06-01T10:00:00Z")));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        return Task.FromResult(BusinessGatewayProxyTests.CreateAvailabilityResponse(
            BusinessGatewayProxyTests.CreateWindow(
                deviceAssetId,
                "inspection-001",
                EquipmentRuntimeSourceType.Inspection,
                EquipmentRuntimeReasonCodes.InspectionRequired,
                EquipmentRuntimeSeverity.Blocked,
                "2026-06-01T11:00:00Z",
                "2026-06-01T12:00:00Z")));
    }
}

internal sealed class RecordingTelemetryFacadeClient : IBusinessIndustrialTelemetryClient
{
    public string? LastInternalToken { get; private set; }

    public BusinessConsoleTelemetryTagListRequest? LastTagListRequest { get; private set; }

    public BusinessConsoleTelemetryAlarmRuleListRequest? LastAlarmRuleListRequest { get; private set; }

    public BusinessConsoleTelemetryAlarmListRequest? LastAlarmListRequest { get; private set; }

    public BusinessConsoleEquipmentAlarmListRequest? LastEquipmentAlarmListRequest { get; private set; }

    public string? LastAlarmLifecycleId { get; private set; }

    public object? LastAlarmLifecycleRequest { get; private set; }

    public string? LastHistoryDeviceAssetId { get; private set; }

    public BusinessConsoleTelemetryHistoryRequest? LastHistoryRequest { get; private set; }
    public BusinessConsoleTelemetryRuntimeHoursRequest? LastRuntimeHoursRequest { get; private set; }

    public Task<BusinessConsoleConnectorTagCoverageResponse> GetConnectorTagCoverageAsync(
        string internalBearerToken,
        BusinessConsoleConnectorTagCoverageRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleConnectorTagCoverageResponse(
            request.ConnectorId,
            "unavailable",
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            []));
    }

    public Task<BusinessConsoleTelemetryRuntimeHoursResponse> QueryRuntimeHoursAsync(string internalBearerToken, BusinessConsoleTelemetryRuntimeHoursRequest request, CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRuntimeHoursRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryRuntimeHoursResponse(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.WindowStartUtc, request.WindowEndUtc, 3, 2.5m, 3m, true,
            [new BusinessConsoleTelemetryRuntimeHoursDailyItem("2026-06-01", 2.5m, 3m, 3)]));
    }

    public Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastTagListRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryTagListResponse(
        [
            new BusinessConsoleTelemetryTagItem("tag-001", "org-001", "env-dev", "DEV-PRESS-01", "temperature", "decimal", "C", "1m"),
        ], 42));
    }

    public Task<BusinessConsoleTelemetryTagCurrentValueResponse> GetTagCurrentValueAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagCurrentValueRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryTagCurrentValueResponse(
            request.DeviceAssetId, request.TagKey, HasSample: false, Value: null, OccurredAtUtc: null));
    }

    public Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmListRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryAlarmEventListResponse(
        [
            new BusinessConsoleTelemetryAlarmEventItem(
                "alarm-001",
                "org-001",
                "env-dev",
                "DEV-PRESS-01",
                "TEMP_HIGH",
                "critical",
                "raised",
                DateTimeOffset.Parse("2026-06-01T08:20:00Z", CultureInfo.InvariantCulture),
                null,
                "EXT-ALARM-001"),
        ], 42));
    }

    public Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmRuleListRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryAlarmRuleListResponse([], 42));
    }

    public Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse("rule-001"));
    }

    public Task<BusinessConsoleRecordTelemetrySampleResponse> RecordSampleAsync(
        string internalBearerToken,
        BusinessConsoleRecordTelemetrySampleRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleRecordTelemetrySampleResponse("summary-001", "state-001"));
    }

    public Task<BusinessConsolePostTelemetryAlarmResponse> PostAlarmAsync(
        string internalBearerToken,
        BusinessConsolePostTelemetryAlarmRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsolePostTelemetryAlarmResponse("alarm-001"));
    }

    public Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastHistoryDeviceAssetId = deviceAssetId;
        LastHistoryRequest = request;
        return Task.FromResult(new BusinessConsoleTelemetryHistoryResponse(
        [
            new BusinessConsoleTelemetryHistoryItem(
                "summary",
                deviceAssetId,
                "temperature",
                "42",
                DateTimeOffset.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture)),
        ]));
    }

    public Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryOeeResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            0,
            0m,
            0m,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            ["production-facts-missing"]));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BusinessGatewayProxyTests.CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm));

    public Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BusinessGatewayProxyTests.CreateAvailabilityResponse("alarm-001", EquipmentRuntimeSourceType.Alarm));

    public Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EquipmentRuntimeCurrentStateResponse(
            1,
            request.OrganizationId,
            request.EnvironmentId,
            deviceAssetId,
            "RUNNING",
            DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture),
            true,
            []));

    public Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastEquipmentAlarmListRequest = request;
        return Task.FromResult(new BusinessConsoleEquipmentAlarmListPageResponse([], 42));
    }

    public Task<BusinessConsoleAlarmLifecycleResponse> AcknowledgeAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleAcknowledgeAlarmRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmLifecycleId = alarmEventId;
        LastAlarmLifecycleRequest = request;
        return Task.FromResult(new BusinessConsoleAlarmLifecycleResponse(alarmEventId));
    }

    public Task<BusinessConsoleAlarmLifecycleResponse> ShelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleShelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmLifecycleId = alarmEventId;
        LastAlarmLifecycleRequest = request;
        return Task.FromResult(new BusinessConsoleAlarmLifecycleResponse(alarmEventId));
    }

    public Task<BusinessConsoleAlarmLifecycleResponse> UnshelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleUnshelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastAlarmLifecycleId = alarmEventId;
        LastAlarmLifecycleRequest = request;
        return Task.FromResult(new BusinessConsoleAlarmLifecycleResponse(alarmEventId));
    }

    public Task<BusinessConsoleTelemetryDeviceControlCommandResponse> CreateDeviceControlCommandAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryDeviceControlCommandResponse(
            "op-task-001",
            "pending-approval",
            Approval: null));
    }

    public Task<BusinessConsoleTelemetryDeviceControlCommandDetail> GetDeviceControlCommandAsync(
        string internalBearerToken,
        string commandId,
        BusinessConsoleTelemetryDeviceControlCommandContextRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryDeviceControlCommandDetail(
            commandId,
            commandId,
            request.OrganizationId,
            request.EnvironmentId,
            "connector-host-001",
            "opcua-cell-01",
            "DEV-CNC-01",
            "write-tag",
            "spindle.speed",
            "80",
            null,
            "user-admin",
            "speed adjustment",
            "corr-device-control-001",
            "idem-device-control-001",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            "approval-pending",
            false,
            Approval: null,
            CurrentAttemptId: null,
            Attempts: []));
    }

    public Task<BusinessConsoleTelemetryDeviceControlCommandListResponse> ListDeviceControlCommandsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryDeviceControlCommandListResponse([], 0));
    }

    public Task<BusinessConsoleTelemetryDeviceControlBindingListResponse> ListDeviceControlBindingsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlBindingListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleTelemetryDeviceControlBindingListResponse([], 0));
    }

    public Task<BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse> CreateOrUpdateDeviceControlBindingAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse("binding-001"));
    }

    public Task<BusinessConsoleDisableTelemetryDeviceControlBindingResponse> DisableDeviceControlBindingAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleDisableTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleDisableTelemetryDeviceControlBindingResponse("binding-001"));
    }
}
