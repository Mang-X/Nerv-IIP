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
        Assert.Equal(new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user:user-admin", "unread", 20), notification.LastMessagesRequest);
        Assert.Equal(new BusinessConsoleNotificationListRequest("org-001", "env-dev", "user:user-admin", "open", 20), notification.LastTasksRequest);
    }

    [Fact]
    public async Task Workbench_summary_uses_maximum_source_take_for_kpi_counts()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.QualityNcrRead,
            BusinessGatewayPermissions.MesWorkOrdersRead);
        var quality = new RecordingQualityClient { NcrTotal = 137 };
        var mes = new RecordingMesClient { WorkOrdersTotal = 246 };
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var kpis = document.RootElement.GetProperty("data").GetProperty("kpis").EnumerateArray();
        Assert.Equal(137, kpis.Single(kpi => kpi.GetProperty("key").GetString() == "openNcrs").GetProperty("value").GetInt32());
        kpis = document.RootElement.GetProperty("data").GetProperty("kpis").EnumerateArray();
        Assert.Equal(246, kpis.Single(kpi => kpi.GetProperty("key").GetString() == "releasedWorkOrders").GetProperty("value").GetInt32());
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

        var health = await client.GetStringAsync("/health");
        Assert.Equal("Healthy", health);
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

    [Fact]
    public async Task Workbench_summary_keeps_other_sources_available_and_health_reports_degraded_source()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.NotificationTasksRead,
            BusinessGatewayPermissions.MesWorkOrdersRead);
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessNotificationClient>();
            services.AddSingleton<IBusinessNotificationClient>(new ThrowingNotificationClient());
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        AssertSourceStatus(data, "Notification", "unavailable");
        AssertSourceStatus(data, "BusinessMES", "available");
        Assert.Equal(1, mes.WorkOrderListCallCount);

        var health = await client.GetStringAsync("/health");
        Assert.Contains("Degraded", health, StringComparison.Ordinal);
        Assert.Contains("Notification", health, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workbench_summary_keeps_other_sources_available_when_one_source_authorization_check_fails()
    {
        var auth = new ThrowingApprovalAuthorizationClient();
        var approval = new RecordingApprovalClient();
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessApprovalClient>();
            services.AddSingleton<IBusinessApprovalClient>(approval);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        AssertSourceStatus(data, "BusinessApproval", "unavailable");
        AssertSourceStatus(data, "BusinessMES", "available");
        Assert.Equal(0, approval.CallCount);
        Assert.Equal(1, mes.WorkOrderListCallCount);

        var approvalStatus = data.GetProperty("sourceStatuses")
            .EnumerateArray()
            .Single(sourceStatus => sourceStatus.GetProperty("source").GetString() == "BusinessApproval");
        Assert.Equal("authorization-unavailable", approvalStatus.GetProperty("reason").GetString());
        Assert.Contains("IAM", await client.GetStringAsync("/health"), StringComparison.Ordinal);
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
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
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

    private sealed class ThrowingApprovalAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            CancellationToken cancellationToken)
        {
            if (requirement.PermissionCode == BusinessGatewayPermissions.ApprovalsRead)
            {
                throw new HttpRequestException("iam unavailable");
            }

            return Task.FromResult(
                requirement.PermissionCode == BusinessGatewayPermissions.MesWorkOrdersRead
                    ? BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
                    : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
        }
    }
}

internal sealed class RecordingApprovalClient : IBusinessApprovalClient
{
    public int CallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleApprovalTaskListRequest? LastRequest { get; private set; }

    public BusinessConsoleApprovalTemplateListRequest? LastTemplateListRequest { get; private set; }

    public BusinessConsoleApprovalChainListRequest? LastChainListRequest { get; private set; }

    public BusinessConsoleApprovalDecisionListRequest? LastDecisionListRequest { get; private set; }

    public BusinessConsoleResolveApprovalStepRequest? LastResolveStepRequest { get; private set; }

    public BusinessConsoleApprovalDelegationListRequest? LastDelegationListRequest { get; private set; }

    public BusinessConsoleStartApprovalChainRequest? LastStartChainRequest { get; private set; }

    public BusinessConsoleCreateApprovalDelegationRequest? LastCreateDelegationRequest { get; private set; }

    public BusinessConsoleRevokeApprovalDelegationRequest? LastRevokeDelegationRequest { get; private set; }

    public Task<BusinessConsoleApprovalTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTemplateListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastTemplateListRequest = request;
        return Task.FromResult(new BusinessConsoleApprovalTemplateListResponse(
        [
            new BusinessConsoleApprovalTemplateItem(
                "template-001",
                request.OrganizationId,
                request.EnvironmentId,
                "purchase-order-default",
                request.DocumentType ?? "purchase-order",
                1,
                true,
                []),
        ],
        1));
    }

    public Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleCreateOrUpdateApprovalTemplateResponse("template-001"));
    }

    public Task<BusinessConsoleStartApprovalChainResponse> StartChainAsync(
        string internalBearerToken,
        BusinessConsoleStartApprovalChainRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastStartChainRequest = request;
        return Task.FromResult(new BusinessConsoleStartApprovalChainResponse("chain-001"));
    }

    public Task<BusinessConsoleApprovalChainListResponse> ListChainsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastChainListRequest = request;
        return Task.FromResult(new BusinessConsoleApprovalChainListResponse(
        [
            new BusinessConsoleApprovalChainItem(
                "chain-001",
                request.OrganizationId,
                request.EnvironmentId,
                "purchase-order-default",
                1,
                request.Status ?? "pending",
                request.SourceService ?? "BusinessERP",
                request.DocumentType ?? "purchase-order",
                request.DocumentId ?? "PO-001",
                null,
                request.StartedBy ?? "u-requester",
                DateTimeOffset.Parse("2026-06-03T01:00:00Z"),
                null),
        ],
        1));
    }

    public Task<BusinessConsoleApprovalChainResponse> GetChainAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleApprovalChainResponse(
            request.ChainId,
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-order-default",
            1,
            "pending",
            "BusinessERP",
            "purchase-order",
            "PO-001",
            null,
            [],
            []));
    }

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
            ],
            1));
    }

    public Task<BusinessConsoleApprovalDecisionListResponse> ListDecisionsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDecisionListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDecisionListRequest = request;
        return Task.FromResult(new BusinessConsoleApprovalDecisionListResponse(
        [
            new BusinessConsoleApprovalDecisionListItem(
                "decision-001",
                request.ChainId ?? "chain-001",
                1,
                request.ActorType ?? "user",
                request.ActorRef ?? "u-manager",
                request.Decision ?? "approve",
                "ok",
                DateTimeOffset.Parse("2026-06-03T02:00:00Z"),
                "BusinessERP",
                request.DocumentType ?? "purchase-order",
                request.DocumentId ?? "PO-001",
                null),
        ],
        1));
    }

    public Task<BusinessConsoleResolveApprovalStepResponse> ResolveStepAsync(
        string internalBearerToken,
        BusinessConsoleResolveApprovalStepRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastResolveStepRequest = request;
        return Task.FromResult(new BusinessConsoleResolveApprovalStepResponse("decision-001"));
    }

    public Task<BusinessConsoleApprovalDelegationListResponse> ListDelegationsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDelegationListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDelegationListRequest = request;
        return Task.FromResult(new BusinessConsoleApprovalDelegationListResponse(
        [
            new BusinessConsoleApprovalDelegationItem(
                "delegation-001",
                request.OrganizationId,
                request.EnvironmentId,
                "user",
                request.DelegatorActorRef ?? "u-manager",
                "user",
                request.DelegateActorRef ?? "u-backup",
                request.DocumentType ?? "purchase-order",
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
                request.Status ?? "active",
                "travel",
                "u-manager",
                DateTimeOffset.Parse("2026-05-31T00:00:00Z"),
                null,
                null),
        ],
        1));
    }

    public Task<BusinessConsoleCreateApprovalDelegationResponse> CreateDelegationAsync(
        string internalBearerToken,
        BusinessConsoleCreateApprovalDelegationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateDelegationRequest = request;
        return Task.FromResult(new BusinessConsoleCreateApprovalDelegationResponse("delegation-001"));
    }

    public Task<BusinessConsoleAcceptedResponse> RevokeDelegationAsync(
        string internalBearerToken,
        string delegationId,
        BusinessConsoleRevokeApprovalDelegationRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRevokeDelegationRequest = request with { DelegationId = delegationId };
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
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
