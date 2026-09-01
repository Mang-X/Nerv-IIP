using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MachineOverheadReadHttpContractTests
{
    [Fact]
    public void Public_contract_registers_scoped_finance_read_endpoint()
    {
        var contract = ErpFinanceEndpointContracts.Get<GetWorkOrderCostVarianceEndpoint>();

        Assert.Equal("GET", contract.HttpMethod);
        Assert.Equal("/api/business/v1/erp/finance/work-order-costs/{workOrderId}", contract.Route);
        Assert.Equal("business.erp.finance.read", contract.PermissionCode);
        Assert.Equal("getErpWorkOrderCostVariance", contract.OperationId);
    }

    [Fact]
    public async Task Http_contract_binds_claim_scope_and_preserves_explicit_zero_and_unavailable_nulls()
    {
        var sender = new CapturingSender(MachineOverheadReadStatus.Available);
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "/api/business/v1/erp/finance/work-order-costs/WO-ZERO?pageNumber=2&pageSize=25");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(sender.Queries);
        Assert.Equal("org-test", query.OrganizationId);
        Assert.Equal("env-test", query.EnvironmentId);
        Assert.Equal("WO-ZERO", query.WorkOrderId);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(25, query.PageSize);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("available", data.GetProperty("laborVarianceStatus").GetString());
        Assert.Equal(0m, data.GetProperty("actualLaborHours").GetDecimal());
        Assert.Equal(0m, data.GetProperty("actualMachineHours").GetDecimal());
        Assert.Equal("available", data.GetProperty("machineCostStatus").GetString());
        Assert.Equal("CNY", data.GetProperty("machineCurrencyCode").GetString());
        Assert.Equal(0m, data.GetProperty("appliedFixedMachineOverhead").GetDecimal());
        Assert.Equal(0m, data.GetProperty("appliedVariableMachineOverhead").GetDecimal());
        Assert.Equal(0m, data.GetProperty("appliedMachineOverheadTotal").GetDecimal());
        Assert.Equal(2, data.GetProperty("machineOverheadPageNumber").GetInt32());
        Assert.Equal(25, data.GetProperty("machineOverheadPageSize").GetInt32());
        Assert.Equal(0, data.GetProperty("totalMachineOverheadOperations").GetInt32());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("machineOverheadOperations").ValueKind);
    }

    [Theory]
    [InlineData(MachineOverheadReadStatus.NotApplicable, "notApplicable", "machine_overhead_not_applicable")]
    [InlineData(MachineOverheadReadStatus.Unavailable, "unavailable", "currency_conflict")]
    public async Task Http_contract_serializes_machine_three_state_nullability(
        MachineOverheadReadStatus machineCostStatus,
        string expectedWireStatus,
        string reason)
    {
        var sender = new CapturingSender(machineCostStatus);
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var request = CreateRequest("/api/business/v1/erp/finance/work-order-costs/WO-STATE");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(expectedWireStatus, data.GetProperty("machineCostStatus").GetString());
        Assert.Equal(reason, data.GetProperty("machineCostUnavailableReason").GetString());
        foreach (var propertyName in new[]
        {
            "machineCurrencyCode", "actualMachineHours", "appliedFixedMachineOverhead",
            "appliedVariableMachineOverhead", "appliedMachineOverheadTotal"
        })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(propertyName).ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("machineOverheadOperations").ValueKind);
    }

    [Fact]
    public async Task Http_contract_rejects_undefined_numeric_machine_status()
    {
        var sender = new CapturingSender((MachineOverheadReadStatus)99);
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var request = CreateRequest("/api/business/v1/erp/finance/work-order-costs/WO-INVALID-STATUS");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("\"machineCostStatus\":99", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_exposes_work_order_cost_operation_and_three_state_fields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var operation = json.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/erp/finance/work-order-costs/{workOrderId}")
            .GetProperty("get");

        Assert.Equal("getErpWorkOrderCostVariance", operation.GetProperty("operationId").GetString());
        Assert.Contains("WorkOrderCostVarianceResponse", operation.GetRawText(), StringComparison.Ordinal);
        var schema = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith("WorkOrderCostVarianceResponse", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("workOrderId", out _));
        var properties = schema.Value.GetProperty("properties");
        foreach (var propertyName in new[]
        {
            "appliedFixedMachineOverhead", "appliedVariableMachineOverhead", "appliedMachineOverheadTotal",
            "machineCurrencyCode", "machineOverheadPageNumber", "machineOverheadPageSize",
            "totalMachineOverheadOperations", "machineOverheadOperations"
        })
            Assert.True(properties.TryGetProperty(propertyName, out _), propertyName);
        Assert.False(properties.TryGetProperty("actualFixedMachineOverhead", out _));

        var machineOperationSchema = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith("OperationMachineOverheadItem", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("operationTaskId", out _));
        var machineOperationProperties = machineOperationSchema.Value.GetProperty("properties");
        AssertMachineOverheadStatusSchema(properties.GetProperty("machineCostStatus"), schemas);
        AssertMachineOverheadStatusSchema(machineOperationProperties.GetProperty("status"), schemas);
        foreach (var propertyName in new[]
        {
            "settlementId", "settlementRevision", "status", "unavailableReason", "actualMachineHours",
            "appliedFixedMachineOverhead", "appliedVariableMachineOverhead", "appliedMachineOverheadTotal",
            "accountingPeriodCode", "currencyCode", "deviceAssetId", "machineTimeBasisCode",
            "workCenterMachineOverheadRateId", "rateRevision", "completedAtUtc", "sourceEventId"
        })
            Assert.True(machineOperationProperties.TryGetProperty(propertyName, out _), propertyName);

        var required = schema.Value.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var propertyName in new[]
        {
            "machineCostStatus", "machineOverheadPageNumber", "machineOverheadPageSize",
            "totalMachineOverheadOperations", "machineOverheadOperations"
        })
            Assert.Contains(propertyName, required);
        foreach (var propertyName in new[]
        {
            "machineCostUnavailableReason", "machineCurrencyCode", "actualMachineHours",
            "appliedFixedMachineOverhead", "appliedVariableMachineOverhead", "appliedMachineOverheadTotal"
        })
        {
            Assert.DoesNotContain(propertyName, required);
            Assert.True(properties.GetProperty(propertyName).GetProperty("nullable").GetBoolean(), propertyName);
        }

        var operationRequired = machineOperationSchema.Value.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var propertyName in new[]
        {
            "operationTaskId", "workCenterId", "settlementId", "settlementRevision", "status",
            "accountingPeriodCode", "currencyCode", "workCenterMachineOverheadRateId", "rateRevision",
            "completedAtUtc", "sourceEventId"
        })
            Assert.Contains(propertyName, operationRequired);
        foreach (var propertyName in new[]
        {
            "unavailableReason", "actualMachineHours", "appliedFixedMachineOverhead",
            "appliedVariableMachineOverhead", "appliedMachineOverheadTotal", "deviceAssetId", "machineTimeBasisCode"
        })
        {
            Assert.DoesNotContain(propertyName, operationRequired);
            Assert.True(machineOperationProperties.GetProperty(propertyName).GetProperty("nullable").GetBoolean(), propertyName);
        }

        var periodOperation = json.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations")
            .GetProperty("get");
        Assert.Equal("listErpWorkCenterMachineOverheadReconciliations", periodOperation.GetProperty("operationId").GetString());
        var periodSchema = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith("ListWorkCenterMachineOverheadReconciliationsResponse", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("accountingPeriodCode", out _));
        var periodProperties = periodSchema.Value.GetProperty("properties");
        Assert.True(periodProperties.TryGetProperty("accountingPeriodStatus", out _));
        Assert.True(periodProperties.TryGetProperty("reconciliationStatus", out _));
        Assert.True(periodProperties.TryGetProperty("reconciliationUnavailableReason", out _));
        AssertMachineOverheadStatusSchema(periodProperties.GetProperty("reconciliationStatus"), schemas);

        var periodItemSchema = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith("WorkCenterMachineOverheadReconciliationItem", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("recordedAtUtc", out _));
        AssertMachineOverheadStatusSchema(
            periodItemSchema.Value.GetProperty("properties").GetProperty("reconciliationStatus"), schemas);
    }

    private static void AssertMachineOverheadStatusSchema(JsonElement propertySchema, JsonElement schemas)
    {
        var schema = ResolveSchema(propertySchema, schemas);
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            ["available", "notApplicable", "unavailable"],
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement schemas)
    {
        if (schema.TryGetProperty("$ref", out var schemaReference))
            return schemas.GetProperty(schemaReference.GetString()!.Split('/')[^1]);
        if (schema.TryGetProperty("allOf", out var inheritedSchemas))
            return ResolveSchema(Assert.Single(inheritedSchemas.EnumerateArray()), schemas);
        if (schema.TryGetProperty("oneOf", out var alternatives))
            return ResolveSchema(Assert.Single(alternatives.EnumerateArray()), schemas);
        return schema;
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingSender? sender = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestHostConfiguration()));
            if (sender is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            }
        });

    private static HttpRequestMessage CreateRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-erp-machine-overhead-token");
        request.Headers.Add("X-Organization-Id", "org-test");
        request.Headers.Add("X-Environment-Id", "env-test");
        return request;
    }

    private sealed class CapturingSender(MachineOverheadReadStatus machineCostStatus) : ISender
    {
        public List<GetWorkOrderCostVarianceQuery> Queries { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetWorkOrderCostVarianceQuery>(request);
            Queries.Add(query);
            var available = machineCostStatus == MachineOverheadReadStatus.Available;
            var reason = machineCostStatus switch
            {
                MachineOverheadReadStatus.NotApplicable => "machine_overhead_not_applicable",
                MachineOverheadReadStatus.Unavailable => "currency_conflict",
                _ => null,
            };
            return Task.FromResult((TResponse)(object)new WorkOrderCostVarianceResponse(
                query.OrganizationId, query.EnvironmentId, query.WorkOrderId, "CNY", "actualOperation",
                "available", null, 0m, 0m, 0m, 0m, 0m, 0m, "neutral",
                "notApplicable", "actual_payroll_rate_not_modeled",
                0m, 0m, 0m, 0m, available ? 0m : null, machineCostStatus, reason,
                available ? "CNY" : null, query.PageNumber, query.PageSize, 0, [],
                available ? 0m : null, available ? 0m : null, available ? 0m : null,
                query.PageNumber, query.PageSize, 0, []));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static Dictionary<string, string?> TestHostConfiguration() => new()
    {
        ["InternalService:BearerToken"] = "test-general-internal-token",
        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
        ["Persistence:AutoMigrate"] = "false",
    };
}
