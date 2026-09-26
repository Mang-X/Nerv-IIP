using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceLifecycleConflictTests
{
    [Fact]
    public async Task Complete_rejects_a_persisted_completed_work_order_before_other_validation()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "medium",
            "operator-001");
        workOrder.Complete("fixed", "equipment-failure", 10, []);
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();
        var completedAtUtc = workOrder.CompletedAtUtc;
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<MaintenanceLifecycleConflictException>(() =>
            handler.Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    workOrder.Id,
                    "must-not-overwrite",
                    "missing-reason",
                    20,
                    []),
                CancellationToken.None));

        Assert.Equal("complete", exception.Action);
        Assert.Equal(nameof(MaintenanceWorkOrderStatus.Completed), exception.CurrentStatus);
        Assert.Equal("fixed", workOrder.CompletionResult);
        Assert.Equal(completedAtUtc, workOrder.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_fails_closed_for_an_undefined_persisted_status_before_other_validation()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "medium",
            "operator-001");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(workOrder).Property(x => x.Status).CurrentValue = (MaintenanceWorkOrderStatus)999;
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<MaintenanceLifecycleConflictException>(() =>
            handler.Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    workOrder.Id,
                    "must-not-write",
                    "missing-reason",
                    20,
                    []),
                CancellationToken.None));

        Assert.Equal("complete", exception.Action);
        Assert.Equal("999", exception.CurrentStatus);
        Assert.Equal((MaintenanceWorkOrderStatus)999, workOrder.Status);
        Assert.Null(workOrder.CompletionResult);
        Assert.Null(workOrder.DowntimeReasonCode);
        Assert.Null(workOrder.DowntimeMinutes);
        Assert.Null(workOrder.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_keeps_missing_work_order_and_downtime_reason_as_known_400_errors()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var handler = new CompleteMaintenanceWorkOrderCommandHandler(dbContext);

        var missingWorkOrder = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                    "fixed",
                    "equipment-failure",
                    10,
                    []),
                CancellationToken.None));

        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "medium",
            "operator-001");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var missingReason = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    workOrder.Id,
                    "fixed",
                    "missing-reason",
                    10,
                    []),
                CancellationToken.None));

        Assert.IsNotType<MaintenanceLifecycleConflictException>(missingWorkOrder);
        Assert.IsNotType<MaintenanceLifecycleConflictException>(missingReason);
        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
    }

    [Fact]
    public async Task Complete_rejects_a_work_order_owned_by_another_business_scope_without_mutation()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        var workOrder = MaintenanceWorkOrder.OpenManual(
            "org-b",
            "env-b",
            "DEV-TENANT-B",
            "medium",
            "operator-b");
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() =>
            new CompleteMaintenanceWorkOrderCommandHandler(dbContext).Handle(
                new CompleteMaintenanceWorkOrderCommand(
                    workOrder.Id,
                    "fixed",
                    "equipment-failure",
                    10,
                    [],
                    OrganizationId: "org-a",
                    EnvironmentId: "env-a"),
                CancellationToken.None));

        Assert.Equal(MaintenanceWorkOrderStatus.Open, workOrder.Status);
        Assert.Null(workOrder.CompletionResult);
        Assert.Empty(workOrder.SparePartLines);
    }

    [Fact]
    public void Complete_validator_keeps_field_and_cost_failures_in_the_validation_path()
    {
        var result = new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                string.Empty,
                string.Empty,
                0,
                [new MaintenanceSparePartInput(string.Empty, 0, new string('x', 51))],
                ActualLaborMinutes: 0,
                SparePartCostAmount: -1,
                ExternalServiceCostAmount: -1,
                CostCurrencyCode: new string('x', 11),
                ActualTechnicianUserId: new string('x', 151)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.Result), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.DowntimeReasonCode), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.DowntimeMinutes), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.ActualLaborMinutes), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.SparePartCostAmount), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.ExternalServiceCostAmount), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.CostCurrencyCode), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CompleteMaintenanceWorkOrderCommand.ActualTechnicianUserId), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(MaintenanceSparePartInput.SkuCode), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(MaintenanceSparePartInput.Quantity), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(MaintenanceSparePartInput.UomCode), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Regression for #1285: a spare part line without a unit of measure used to be accepted and the
    /// integration event converter then invented one. The unit drives the inventory issue posting, so the
    /// write is rejected up front instead.
    /// </summary>
    [Fact]
    public void Complete_validator_rejects_a_spare_part_line_without_a_unit_of_measure()
    {
        var result = new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "fixed",
                "equipment-failure",
                10,
                [new MaintenanceSparePartInput("SPARE-001", 2m, null)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(MaintenanceSparePartInput.UomCode), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Same rule on the single spare-part registration path (regression for #1285).
    /// </summary>
    [Fact]
    public void Create_spare_part_validator_rejects_a_missing_unit_of_measure()
    {
        var result = new CreateMaintenanceSparePartCommandValidator().Validate(
            new CreateMaintenanceSparePartCommand(
                "org-001",
                "env-dev",
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "SPARE-001",
                2m,
                null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => string.Equals(x.PropertyName, nameof(CreateMaintenanceSparePartCommand.UomCode), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Complete_http_endpoint_returns_409_with_a_safe_envelope()
    {
        await using var factory = CreateFactory(
            new MaintenanceLifecycleConflictException(
                "complete",
                nameof(MaintenanceWorkOrderStatus.Completed)));
        using var client = CreateAuthorizedClient(factory);
        var workOrderId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/maintenance/work-orders/{workOrderId}/complete",
            new
            {
                workOrderId,
                organizationId = "org-001",
                environmentId = "env-dev",
                result = "fixed",
                downtimeReasonCode = "equipment-failure",
                downtimeMinutes = 10,
                spareParts = Array.Empty<object>(),
                idempotencyKey = "complete-lifecycle-conflict",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"message\":\"lifecycle-conflict\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(MaintenanceWorkOrderStatus.Completed), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Complete_http_endpoint_keeps_known_errors_as_400()
    {
        await using var factory = CreateFactory(new KnownException("missing-downtime-reason"));
        using var client = CreateAuthorizedClient(factory);
        var workOrderId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/maintenance/work-orders/{workOrderId}/complete",
            new
            {
                workOrderId,
                organizationId = "org-001",
                environmentId = "env-dev",
                result = "fixed",
                downtimeReasonCode = "missing-reason",
                downtimeMinutes = 10,
                spareParts = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(Exception exception)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new ThrowingSender(exception));
                });
            });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        return client;
    }

    private sealed class ThrowingSender(Exception exception) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<TResponse>(exception);
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException(exception);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<object?>(exception);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
