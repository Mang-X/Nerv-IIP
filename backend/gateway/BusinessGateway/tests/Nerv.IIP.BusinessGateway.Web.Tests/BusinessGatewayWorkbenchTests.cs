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
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayWorkbenchTests
{
    [Fact]
    public async Task Workbench_summary_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Workbench_summary_aggregates_allowed_sources_and_filters_denied_sources_at_read_time()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.MesWorkOrdersRead,
            BusinessGatewayPermissions.IiotAlarmsRead);
        var approval = new RecordingApprovalClient();
        var notification = new RecordingNotificationClient();
        var quality = new RecordingQualityClient();
        var inventory = new RecordingInventoryClient();
        var telemetry = new RecordingIndustrialTelemetryClient();
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessApprovalClient>();
            services.AddSingleton<IBusinessApprovalClient>(approval);
            services.RemoveAll<IBusinessNotificationClient>();
            services.AddSingleton<IBusinessNotificationClient>(notification);
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IBusinessIndustrialTelemetryClient>();
            services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev&take=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sensitive", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("org-001", data.GetProperty("organizationId").GetString());
        Assert.Equal(0, approval.CallCount);
        Assert.Equal(0, notification.MessageCallCount);
        Assert.Equal(0, notification.TaskCallCount);
        Assert.Equal(0, quality.NcrListCallCount);
        Assert.Equal(0, inventory.AvailabilityCallCount);
        Assert.Equal("internal-test-token", telemetry.LastInternalToken);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        AssertSourceStatus(data, "BusinessMES", "available");
        AssertSourceStatus(data, "IndustrialTelemetry", "available");
        AssertSourceStatus(data, "BusinessApproval", "forbidden");
        AssertSourceStatus(data, "Notification", "forbidden");
        AssertSourceStatus(data, "BusinessQuality", "forbidden");
        AssertSourceStatus(data, "BusinessInventory", "unsupported");
        Assert.Equal(1, data.GetProperty("alerts").GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("kpis").EnumerateArray().Count(kpi => kpi.GetProperty("source").GetString() == "BusinessMES"));
    }

    [Fact]
    public async Task Workbench_summary_uses_principal_context_for_approval_and_notification_queries()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.ApprovalsRead,
            BusinessGatewayPermissions.NotificationMessagesRead,
            BusinessGatewayPermissions.NotificationTasksRead);
        var approval = new RecordingApprovalClient();
        var notification = new RecordingNotificationClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessApprovalClient>();
            services.AddSingleton<IBusinessApprovalClient>(approval);
            services.RemoveAll<IBusinessNotificationClient>();
            services.AddSingleton<IBusinessNotificationClient>(notification);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new BusinessConsoleApprovalTaskListRequest("org-001", "env-dev", "user", "user-admin"), approval.LastRequest);
        Assert.Equal(new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user-admin", "unread", 20), notification.LastMessagesRequest);
        Assert.Equal(new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user-admin", "open", 20), notification.LastTasksRequest);
    }

    [Fact]
    public async Task Workbench_summary_uses_maximum_source_take_for_kpi_counts()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.QualityNcrRead,
            BusinessGatewayPermissions.MesWorkOrdersRead);
        var quality = new RecordingQualityClient();
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev&take=75");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, quality.LastNcrListRequest!.Take);
        Assert.Equal(100, mes.LastWorkOrderListRequest!.Take);
    }

    [Fact]
    public async Task Workbench_summary_does_not_query_notifications_when_authorized_principal_cannot_be_resolved()
    {
        var auth = new NullPrincipalNotificationAuthorizationClient();
        var notification = new RecordingNotificationClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessNotificationClient>();
            services.AddSingleton<IBusinessNotificationClient>(notification);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, notification.MessageCallCount);
        Assert.Equal(0, notification.TaskCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSourceStatus(document.RootElement.GetProperty("data"), "Notification", "unavailable");
    }

    [Fact]
    public async Task Workbench_summary_reports_tasks_permission_when_only_notification_tasks_source_fails()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.NotificationTasksRead);
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessNotificationClient>();
            services.AddSingleton<IBusinessNotificationClient>(new ThrowingNotificationClient());
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var notificationStatus = document.RootElement.GetProperty("data")
            .GetProperty("sourceStatuses")
            .EnumerateArray()
            .Single(sourceStatus => sourceStatus.GetProperty("source").GetString() == "Notification");
        Assert.Equal("unavailable", notificationStatus.GetProperty("status").GetString());
        Assert.Equal(BusinessGatewayPermissions.NotificationTasksRead, notificationStatus.GetProperty("permissionCode").GetString());
    }

    private static void AssertSourceStatus(JsonElement data, string source, string status)
    {
        var item = data.GetProperty("sourceStatuses")
            .EnumerateArray()
            .Single(sourceStatus => sourceStatus.GetProperty("source").GetString() == source);
        Assert.Equal(status, item.GetProperty("status").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IBusinessGatewayAuthorizationClient auth,
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

    private sealed class NullPrincipalNotificationAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                requirement.PermissionCode is BusinessGatewayPermissions.NotificationMessagesRead or BusinessGatewayPermissions.NotificationTasksRead
                    ? new BusinessGatewayAuthorizationResult(true, null, "user", null, null)
                    : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
    }
}

internal sealed class RecordingApprovalClient : IBusinessApprovalClient
{
    public int CallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleApprovalTaskListRequest? LastRequest { get; private set; }

    public Task<BusinessConsoleApprovalTaskListResponse> ListPendingTasksAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTaskListRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastInternalToken = internalBearerToken;
        LastRequest = request;
        return Task.FromResult(new BusinessConsoleApprovalTaskListResponse(
        [
            new BusinessConsoleApprovalTaskItem(
                "chain-sensitive-001",
                1,
                "Sensitive approval step",
                "BusinessERP",
                "purchase-order",
                "PO-SECRET-001",
                null,
                DateTimeOffset.Parse("2026-06-03T03:00:00Z")),
        ]));
    }
}

internal sealed class RecordingNotificationClient : IBusinessNotificationClient
{
    public int MessageCallCount { get; private set; }

    public int TaskCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleNotificationListRequest? LastMessagesRequest { get; private set; }

    public BusinessConsoleNotificationListRequest? LastTasksRequest { get; private set; }

    public Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken)
    {
        MessageCallCount++;
        LastInternalToken = internalBearerToken;
        LastMessagesRequest = request;
        return Task.FromResult(new NotificationMessageListResponse(
        [
            new NotificationMessageResponse(
                "message-001",
                "intent-001",
                "user-admin",
                "unread",
                "warning",
                "Sensitive supplier overdue",
                "Sensitive amount 1000000",
                null,
                DateTimeOffset.Parse("2026-06-03T01:00:00Z"),
                null),
        ]));
    }

    public Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken)
    {
        TaskCallCount++;
        LastInternalToken = internalBearerToken;
        LastTasksRequest = request;
        return Task.FromResult(new NotificationTaskListResponse(
        [
            new NotificationTaskResponse(
                "task-001",
                "message-001",
                "user-admin",
                "approve",
                "open",
                "Sensitive action",
                DateTimeOffset.Parse("2026-06-03T02:00:00Z")),
        ]));
    }
}

internal sealed class ThrowingNotificationClient : IBusinessNotificationClient
{
    public Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("notification messages unavailable");

    public Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("notification tasks unavailable");
}
