using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayMachineOverheadTests
{
    [Fact]
    public async Task Work_order_facade_preserves_scope_lineage_and_available_not_applicable_unavailable_zero_states()
    {
        var handler = new RecordingHandler(_ => JsonResponse(WorkOrderPayload()));
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var lease = Lease(auth, handler);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/erp/finance/work-order-costs/WO%2001?organizationId=org-001&environmentId=env-dev&pageNumber=2&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.ErpFinanceRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal("/api/business/v1/erp/finance/work-order-costs/WO%2001?pageNumber=2&pageSize=25", handler.PathAndQuery);
        Assert.Equal("org-001", handler.OrganizationId);
        Assert.Equal("env-dev", handler.EnvironmentId);
        Assert.Equal("Bearer internal-erp-token", handler.Authorization);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        Assert.Equal("unavailable", body["machineCostStatus"]!.GetValue<string>());
        Assert.Null(body["actualMachineHours"]);
        Assert.Null(body["appliedMachineOverheadTotal"]);
        Assert.Equal("notApplicable", body["machineOverheadOperations"]![0]!["status"]!.GetValue<string>());
        Assert.Null(body["machineOverheadOperations"]![0]!["appliedFixedMachineOverhead"]);
        Assert.Equal("available", body["machineOverheadOperations"]![1]!["status"]!.GetValue<string>());
        Assert.Equal(0m, body["machineOverheadOperations"]![1]!["actualMachineHours"]!.GetValue<decimal>());
        Assert.Equal(0m, body["machineOverheadOperations"]![1]!["appliedFixedMachineOverhead"]!.GetValue<decimal>());
        Assert.Equal("rate-002", body["machineOverheadOperations"]![1]!["workCenterMachineOverheadRateId"]!.GetValue<string>());
        Assert.Equal(7, body["machineOverheadOperations"]![1]!["rateRevision"]!.GetValue<int>());
    }

    [Fact]
    public async Task Period_facade_preserves_actual_applied_and_period_variance_fields()
    {
        var handler = new RecordingHandler(_ => JsonResponse(PeriodPayload()));
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/erp/finance/work-center-machine-overhead-reconciliations?organizationId=org-001&environmentId=env-dev&accountingPeriodCode=2026-08&workCenterId=WC-CNC-01&pageNumber=3&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations?accountingPeriodCode=2026-08&workCenterId=WC-CNC-01&pageNumber=3&pageSize=10",
            handler.PathAndQuery);
        Assert.Equal("org-001", handler.OrganizationId);
        Assert.Equal("env-dev", handler.EnvironmentId);

        var item = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!["items"]![0]!;
        Assert.Equal(1200m, item["actualFixedOverheadAmount"]!.GetValue<decimal>());
        Assert.Equal(900m, item["appliedFixedAmount"]!.GetValue<decimal>());
        Assert.Equal(300m, item["underOverAppliedFixedAmount"]!.GetValue<decimal>());
        Assert.Equal(0m, item["underOverAppliedVariableAmount"]!.GetValue<decimal>());
        Assert.Equal(300m, item["underOverAppliedTotalAmount"]!.GetValue<decimal>());
        Assert.Equal(11, item["revision"]!.GetValue<int>());
        Assert.Equal(7, item["rateRevision"]!.GetValue<int>());
        Assert.Equal("CNY", item["currencyCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task Facade_preserves_downstream_http_error_status_without_leaking_message()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent.Create(new
            {
                success = false,
                message = "SQL failed at C:/internal/schema.sql",
                code = 503,
            }),
        });
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), handler);
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/erp/finance/work-order-costs/WO-001?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("downstream-request-failed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("schema.sql", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Work_order_client_fails_closed_when_unavailable_null_is_mutated_to_zero()
    {
        var payload = JsonNode.Parse(JsonSerializer.Serialize(WorkOrderPayload()))!;
        payload["data"]!["actualMachineHours"] = 0m;
        var client = ClientReturning(payload.ToJsonString());

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.GetWorkOrderCostVarianceAsync(
                "internal-token",
                new("WO-001", "org-001", "env-dev"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    [Fact]
    public async Task Work_order_client_fails_closed_when_applied_field_is_renamed_to_actual()
    {
        var payload = JsonNode.Parse(JsonSerializer.Serialize(WorkOrderPayload()))!;
        var data = payload["data"]!.AsObject();
        data["actualFixedMachineOverhead"] = null;
        data.Remove("appliedFixedMachineOverhead");
        var client = ClientReturning(payload.ToJsonString());

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.GetWorkOrderCostVarianceAsync(
                "internal-token",
                new("WO-001", "org-001", "env-dev"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task Period_client_fails_closed_when_variance_field_is_deleted()
    {
        var payload = JsonNode.Parse(JsonSerializer.Serialize(PeriodPayload()))!;
        payload["data"]!["items"]![0]!.AsObject().Remove("underOverAppliedTotalAmount");
        var client = ClientReturning(payload.ToJsonString());

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.ListMachineOverheadReconciliationsAsync(
                "internal-token",
                new("org-001", "env-dev", "2026-08"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    private static BusinessGatewayTestHostLease Lease(
        IBusinessGatewayAuthorizationClient auth,
        RecordingHandler handler)
    {
        var downstream = new HttpClient(handler) { BaseAddress = new Uri("http://erp.local") };
        return BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessErpClient>();
            services.AddSingleton<IBusinessErpClient>(new HttpBusinessErpClient(downstream));
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-erp-token"));
        });
    }

    private static HttpBusinessErpClient ClientReturning(string json) =>
        new(new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }))
        {
            BaseAddress = new Uri("http://erp.local"),
        });

    private static object WorkOrderPayload() => new
    {
        success = true,
        data = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO 001",
            currencyCode = (string?)null,
            laborCostBasis = "actualOperation",
            laborVarianceStatus = "unavailable",
            unavailableReason = "operation_not_settled",
            actualLaborHours = (decimal?)null,
            actualLaborCost = (decimal?)null,
            standardLaborHours = (decimal?)null,
            standardLaborCost = (decimal?)null,
            laborEfficiencyVarianceHours = (decimal?)null,
            laborEfficiencyVarianceAmount = (decimal?)null,
            laborEfficiencyVarianceDirection = (string?)null,
            laborRateVarianceStatus = "notApplicable",
            laborRateVarianceReason = "actual_payroll_rate_not_modeled",
            materialCost = (decimal?)null,
            totalAccumulatedCost = (decimal?)null,
            capitalizedCost = (decimal?)null,
            capitalizationVarianceAmount = (decimal?)null,
            actualMachineHours = (decimal?)null,
            machineCostStatus = "unavailable",
            machineCostUnavailableReason = "currency_conflict",
            machineCurrencyCode = (string?)null,
            pageNumber = 2,
            pageSize = 25,
            totalOperations = 0,
            operations = Array.Empty<object>(),
            appliedFixedMachineOverhead = (decimal?)null,
            appliedVariableMachineOverhead = (decimal?)null,
            appliedMachineOverheadTotal = (decimal?)null,
            machineOverheadPageNumber = 2,
            machineOverheadPageSize = 25,
            totalMachineOverheadOperations = 2,
            machineOverheadOperations = new object[]
            {
                new
                {
                    operationTaskId = "OP-001",
                    workCenterId = "WC-CNC-01",
                    settlementId = "settlement-001",
                    settlementRevision = 3L,
                    status = "notApplicable",
                    unavailableReason = "machine_overhead_not_applicable",
                    actualMachineHours = (decimal?)null,
                    appliedFixedMachineOverhead = (decimal?)null,
                    appliedVariableMachineOverhead = (decimal?)null,
                    appliedMachineOverheadTotal = (decimal?)null,
                    accountingPeriodCode = "2026-08",
                    currencyCode = "CNY",
                    deviceAssetId = (string?)null,
                    machineTimeBasisCode = (string?)null,
                    workCenterMachineOverheadRateId = "rate-001",
                    rateRevision = 6,
                    completedAtUtc = "2026-08-31T08:00:00Z",
                    sourceEventId = "event-001",
                },
                new
                {
                    operationTaskId = "OP-002",
                    workCenterId = "WC-CNC-02",
                    settlementId = "settlement-002",
                    settlementRevision = 4L,
                    status = "available",
                    unavailableReason = (string?)null,
                    actualMachineHours = (decimal?)0m,
                    appliedFixedMachineOverhead = (decimal?)0m,
                    appliedVariableMachineOverhead = (decimal?)0m,
                    appliedMachineOverheadTotal = (decimal?)0m,
                    accountingPeriodCode = "2026-08",
                    currencyCode = "CNY",
                    deviceAssetId = "DEVICE-002",
                    machineTimeBasisCode = "runtime",
                    workCenterMachineOverheadRateId = "rate-002",
                    rateRevision = 7,
                    completedAtUtc = "2026-08-31T09:00:00Z",
                    sourceEventId = "event-002",
                },
            },
        },
    };

    private static object PeriodPayload() => new
    {
        success = true,
        data = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            accountingPeriodCode = "2026-08",
            workCenterId = "WC-CNC-01",
            pageNumber = 3,
            pageSize = 10,
            totalCount = 1,
            items = new[]
            {
                new
                {
                    id = "reconciliation-001",
                    workCenterId = "WC-CNC-01",
                    accountingPeriodCode = "2026-08",
                    revision = 11,
                    rateRevision = 7,
                    currencyCode = "CNY",
                    actualFixedOverheadAmount = 1200m,
                    actualVariableOverheadAmount = 600m,
                    actualTotalOverheadAmount = 1800m,
                    appliedMachineTicks = 36000000000L,
                    appliedMachineHours = 1m,
                    appliedFixedAmount = 900m,
                    appliedVariableAmount = 600m,
                    appliedTotalAmount = 1500m,
                    appliedRoundingDifferenceAmount = 0m,
                    underOverAppliedFixedAmount = 300m,
                    underOverAppliedVariableAmount = 0m,
                    underOverAppliedTotalAmount = 300m,
                    unallocatedFixedOverheadAmount = 0m,
                    overAppliedFixedOverheadAmount = 0m,
                    abnormalDowntimeTicks = 0L,
                    abnormalDowntimeHours = 0m,
                    abnormalDowntimeDisposition = "absorbed",
                    isReadyForClose = true,
                    reconciliationStatus = "available",
                    unavailableReason = (string?)null,
                    recordedBy = "internal-service:business-gateway",
                    sourceReference = "month-end-2026-08",
                    reason = "月结核对",
                    recordedAtUtc = "2026-08-31T10:00:00Z",
                },
            },
            accountingPeriodStatus = "open",
            reconciliationStatus = "available",
            reconciliationUnavailableReason = (string?)null,
        },
    };

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? OrganizationId { get; private set; }
        public string? EnvironmentId { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            PathAndQuery = request.RequestUri!.PathAndQuery;
            OrganizationId = request.Headers.TryGetValues("X-Organization-Id", out var organizations)
                ? organizations.Single()
                : null;
            EnvironmentId = request.Headers.TryGetValues("X-Environment-Id", out var environments)
                ? environments.Single()
                : null;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(responseFactory(request));
        }
    }
}
