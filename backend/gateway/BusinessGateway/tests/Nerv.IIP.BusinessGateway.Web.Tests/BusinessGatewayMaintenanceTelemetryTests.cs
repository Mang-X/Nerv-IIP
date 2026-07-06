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
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayMaintenanceTelemetryTests
{
    [Fact]
    public async Task Maintenance_work_order_list_uses_maintenance_permission_and_preserves_device_alarm_context()
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

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev&skip=5&take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MaintenanceWorkOrdersRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal(new BusinessConsoleMaintenanceListRequest("org-001", "env-dev", 5, 10), maintenance.LastWorkOrderListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("wo-maint-001", item.GetProperty("workOrderId").GetString());
        Assert.Equal("DEV-PRESS-01", item.GetProperty("deviceAssetId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("sourceAlarmId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("relatedAlarmId").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("data").GetProperty("skip").GetInt32());
        Assert.Equal(10, document.RootElement.GetProperty("data").GetProperty("take").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Maintenance_work_order_detail_reads_existing_work_order_surface_by_id()
    {
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
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

        var plansResponse = await client.GetAsync("/api/business-console/v1/maintenance/plans?organizationId=org-001&environmentId=env-dev");
        var windowsResponse = await client.GetAsync("/api/business-console/v1/maintenance/availability-windows?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-PRESS-01");

        Assert.Equal(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, windowsResponse.StatusCode);
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
        Assert.Equal("plan-001", maintenance.LastRecordInspectionRequest.GetProperty("planId").GetString());
        Assert.Equal("wo-maint-001", maintenance.LastRecordInspectionRequest.GetProperty("workOrderId").GetString());
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

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

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

    public BusinessConsoleMaintenanceListRequest? LastWorkOrderListRequest { get; private set; }

    public BusinessConsoleMaintenanceListRequest? LastInspectionListRequest { get; private set; }

    public BusinessConsoleMaintenanceListRequest? LastSparePartListRequest { get; private set; }

    public string? LastWorkOrderDetailId { get; private set; }

    public BusinessConsoleEquipmentAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public JsonElement LastCreateWorkOrderRequest { get; private set; }

    public string? LastCompleteWorkOrderId { get; private set; }

    public JsonElement LastCompleteWorkOrderRequest { get; private set; }

    public JsonElement LastCreatePlanRequest { get; private set; }

    public BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest? LastGenerateDueRequest { get; private set; }

    public string? LastReliabilityDeviceAssetId { get; private set; }

    public BusinessConsoleQueryMaintenanceAssetReliabilityRequest? LastReliabilityRequest { get; private set; }

    public BusinessConsoleQueryMaintenanceReliabilitySummaryRequest? LastReliabilitySummaryRequest { get; private set; }

    public BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest? LastInspectionMeasurementTrendRequest { get; private set; }

    public JsonElement LastRecordInspectionRequest { get; private set; }

    public JsonElement LastCreateSparePartRequest { get; private set; }

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
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastWorkOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleMaintenanceWorkOrderListResponse(
        [
            new BusinessConsoleMaintenanceWorkOrderItem(
                "wo-maint-001",
                "DEV-PRESS-01",
                "high",
                "Open",
                "alarm-001",
                "alarm-001",
                DateTimeOffset.Parse("2026-06-01T08:10:00Z", CultureInfo.InvariantCulture)),
        ], request.Skip, request.Take, 1));
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

    public Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleMaintenancePlanListResponse(
        [
            new BusinessConsoleMaintenancePlanItem(
                "plan-001",
                "DEV-PRESS-01",
                "PM-PRESS",
                "weekly",
                new DateOnly(2026, 6, 1)),
        ], request.Skip, request.Take, 1));

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

    public string? LastHistoryDeviceAssetId { get; private set; }

    public BusinessConsoleTelemetryHistoryRequest? LastHistoryRequest { get; private set; }

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
            0m,
            0m,
            0m,
            false,
            false));
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
}
