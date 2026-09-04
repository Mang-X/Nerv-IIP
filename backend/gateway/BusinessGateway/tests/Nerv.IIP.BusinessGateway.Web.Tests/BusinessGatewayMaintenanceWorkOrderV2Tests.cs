using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Maintenance;
using Nerv.IIP.ServiceAuth;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// Contract: #2969 / spec #2964. BusinessGateway 的 v2 工单创建代理必须与 v1 使用同一套授权、
// principal actor 注入、source alarm/设备一致性与 idempotency 规则，只有原因字段形状不同，
// 且 v2 的 reason code 必须原样转发到 Maintenance v2 路由。
public sealed class BusinessGatewayMaintenanceWorkOrderV2Tests
{
    private const string ReasonCode = "  Bearing-OVERHEAT  ";

    [Fact]
    public async Task V2_client_posts_to_the_maintenance_v2_route_and_preserves_the_reason_code_verbatim()
    {
        var handler = new RecordingCreateHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var response = await client.CreateWorkOrderV2Async(
            "internal-token-001",
            new BusinessConsoleCreateMaintenanceWorkOrderV2Request(
                "org-001",
                "env-dev",
                "DEV-PRESS-01",
                "high",
                SourceAlarmId: null,
                OpenedBy: "user-admin",
                IdempotencyKey: "repair-intent-v2-001",
                AssetUnavailableReasonCode: ReasonCode),
            CancellationToken.None);

        Assert.Equal("/api/business/v2/maintenance/work-orders", handler.LastPath);
        Assert.Equal(ReasonCode, handler.LastBody.GetProperty("assetUnavailableReasonCode").GetString());
        Assert.False(handler.LastBody.TryGetProperty("assetUnavailableReason", out _));
        Assert.Equal("019f0000-0000-7000-8000-000000000111", response.WorkOrderId);
        Assert.Equal("repair-intent-v2-001", response.OperationReceipt!.IdempotencyKey);
        Assert.Equal("maintenance.work-order.create", response.OperationReceipt.OperationType);
    }

    [Fact]
    public async Task V1_client_still_posts_to_the_maintenance_v1_route_with_the_free_text_reason()
    {
        var handler = new RecordingCreateHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        await client.CreateWorkOrderAsync(
            "internal-token-001",
            new BusinessConsoleCreateMaintenanceWorkOrderRequest(
                "org-001",
                "env-dev",
                "DEV-PRESS-01",
                "high",
                SourceAlarmId: null,
                OpenedBy: "user-admin",
                IdempotencyKey: "repair-intent-001",
                AssetUnavailableReason: "bearing temperature high"),
            CancellationToken.None);

        Assert.Equal("/api/business/v1/maintenance/work-orders", handler.LastPath);
        Assert.Equal("bearing temperature high", handler.LastBody.GetProperty("assetUnavailableReason").GetString());
        Assert.False(handler.LastBody.TryGetProperty("assetUnavailableReasonCode", out _));
    }

    [Fact]
    public async Task V2_client_rejects_a_downstream_response_that_is_not_a_freshly_opened_work_order()
    {
        var handler = new RecordingCreateHandler
        {
            ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        workOrderId = "019f0000-0000-7000-8000-000000000111",
                        status = "Completed",
                        changedAtUtc = "2026-08-31T10:00:00Z",
                    },
                }),
            },
        };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.CreateWorkOrderV2Async(
            "internal-token-001",
            V2Request(),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task V2_endpoint_injects_the_authenticated_principal_and_forwards_the_reason_code_unchanged()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync("/api/business-console/v2/maintenance/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            priority = "high",
            sourceAlarmId = (string?)null,
            openedBy = "untrusted-client",
            assetUnavailableReasonCode = ReasonCode,
            idempotencyKey = "maintenance-create-v2-test",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, maintenance.CreateWorkOrderCallCount);
        Assert.Equal("internal-test-token", maintenance.LastInternalToken);
        Assert.Equal("user-admin", maintenance.LastCreateWorkOrderV2Request!.OpenedBy);
        Assert.Equal(ReasonCode, maintenance.LastCreateWorkOrderV2Request.AssetUnavailableReasonCode);
        Assert.Contains(auth.Requirements, x =>
            x.PermissionCode == BusinessGatewayPermissions.MaintenanceWorkOrdersManage &&
            x.ResourceType == "maintenance-work-order" &&
            x.ResourceId == "DEV-PRESS-01");
    }

    [Fact]
    public async Task V2_endpoint_is_denied_without_the_work_order_manage_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        var maintenance = new RecordingMaintenanceFacadeClient();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMaintenanceClient>();
            services.AddSingleton<IBusinessMaintenanceClient>(maintenance);
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync("/api/business-console/v2/maintenance/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-PRESS-01",
            priority = "high",
            openedBy = "operator-001",
            assetUnavailableReasonCode = "bearing-overheat",
            idempotencyKey = "maintenance-create-v2-forbidden",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, maintenance.CreateWorkOrderCallCount);
        Assert.Equal(BusinessGatewayPermissions.MaintenanceWorkOrdersManage, auth.Requirements[0].PermissionCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("bearing-overheat", true)]
    public void V2_validator_accepts_an_absent_reason_code_and_rejects_an_empty_one(string? reasonCode, bool expectedValid)
    {
        var result = new BusinessConsoleCreateMaintenanceWorkOrderV2RequestValidator()
            .Validate(V2Request() with { AssetUnavailableReasonCode = reasonCode });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void V2_validator_bounds_the_reason_code_at_the_maintenance_v2_length(int length, bool expectedValid)
    {
        var result = new BusinessConsoleCreateMaintenanceWorkOrderV2RequestValidator()
            .Validate(V2Request() with { AssetUnavailableReasonCode = new string('c', length) });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void V2_validator_rejects_missing_or_blank_idempotency_keys(string? key)
    {
        var result = new BusinessConsoleCreateMaintenanceWorkOrderV2RequestValidator()
            .Validate(V2Request() with { IdempotencyKey = key! });

        Assert.False(result.IsValid);
    }

    private static BusinessConsoleCreateMaintenanceWorkOrderV2Request V2Request() => new(
        "org-001",
        "env-dev",
        "DEV-PRESS-01",
        "high",
        SourceAlarmId: null,
        OpenedBy: "user-admin",
        IdempotencyKey: "repair-intent-v2-001",
        AssetUnavailableReasonCode: "bearing-overheat");

    private sealed class RecordingCreateHandler : HttpMessageHandler
    {
        public string? LastPath { get; private set; }

        public JsonElement LastBody { get; private set; }

        public Func<HttpResponseMessage>? ResponseFactory { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return ResponseFactory?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        workOrderId = "019f0000-0000-7000-8000-000000000111",
                        status = "Open",
                        changedAtUtc = "2026-08-31T10:00:00Z",
                    },
                }),
            };
        }
    }
}
