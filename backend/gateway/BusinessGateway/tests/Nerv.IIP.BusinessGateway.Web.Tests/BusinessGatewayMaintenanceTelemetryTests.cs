using System.Globalization;
using System.Net;
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

        var response = await client.GetAsync("/api/business-console/v1/maintenance/work-orders?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MaintenanceWorkOrdersRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal(new BusinessConsoleMaintenanceContextRequest("org-001", "env-dev"), maintenance.LastWorkOrderListRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("wo-maint-001", item.GetProperty("workOrderId").GetString());
        Assert.Equal("DEV-PRESS-01", item.GetProperty("deviceAssetId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("sourceAlarmId").GetString());
        Assert.Equal("alarm-001", item.GetProperty("relatedAlarmId").GetString());
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

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}

internal sealed class RecordingMaintenanceFacadeClient : IBusinessMaintenanceClient
{
    public string? LastInternalToken { get; private set; }

    public BusinessConsoleMaintenanceContextRequest? LastWorkOrderListRequest { get; private set; }

    public string? LastWorkOrderDetailId { get; private set; }

    public BusinessConsoleEquipmentAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceContextRequest request,
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
        ]));
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
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleMaintenancePlanListResponse(
        [
            new BusinessConsoleMaintenancePlanItem(
                "plan-001",
                "DEV-PRESS-01",
                "PM-PRESS",
                "weekly",
                new DateOnly(2026, 6, 1)),
        ]));

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

    public BusinessConsoleTelemetryAlarmListRequest? LastAlarmListRequest { get; private set; }

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
        ]));
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
        ]));
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

    public Task<BusinessConsoleEquipmentAlarmListResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleEquipmentAlarmListResponse([]));
}
