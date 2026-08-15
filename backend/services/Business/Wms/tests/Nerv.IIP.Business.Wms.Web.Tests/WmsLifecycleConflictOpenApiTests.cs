using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsLifecycleConflictOpenApiTests
{
    [Fact]
    public void Persistence_backstop_only_classifies_the_inventory_movement_idempotency_constraint()
    {
        using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var constraintName = dbContext.Model.FindEntityType(typeof(InventoryMovementRequest))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(InventoryMovementRequest.OrganizationId),
                    nameof(InventoryMovementRequest.EnvironmentId),
                    nameof(InventoryMovementRequest.SourceDocumentId),
                    nameof(InventoryMovementRequest.IdempotencyKey),
                ]))
            .GetDatabaseName()!;

        Assert.True(WmsIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict(constraintName),
            dbContext));
        Assert.False(WmsIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_unrelated_wms_constraint"),
            dbContext));
    }

    [Fact]
    public void Warehouse_task_action_receipt_recovery_only_classifies_its_owned_unique_constraint()
    {
        using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var constraintName = dbContext.Model.FindEntityType(typeof(WarehouseTaskActionReceipt))!
            .GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [
                        nameof(WarehouseTaskActionReceipt.OrganizationId),
                        nameof(WarehouseTaskActionReceipt.EnvironmentId),
                        nameof(WarehouseTaskActionReceipt.WarehouseTaskId),
                        nameof(WarehouseTaskActionReceipt.Action),
                        nameof(WarehouseTaskActionReceipt.IdempotencyKey),
                    ]))
            .GetDatabaseName()!;

        Assert.True(WarehouseTaskActionReceiptPersistenceConflicts.IsTargetConflict(
            UniqueConflict(constraintName),
            dbContext));
        Assert.False(WarehouseTaskActionReceiptPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_unrelated_wms_constraint"),
            dbContext));
        Assert.False(WarehouseTaskActionReceiptPersistenceConflicts.IsTargetConflict(
            UniqueConflict(constraintName, "40001"),
            dbContext));
    }

    [Fact]
    public async Task Wcs_dispatch_backstop_only_maps_its_owned_unique_constraint_to_lifecycle_conflict()
    {
        using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        await using var dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var constraintName = dbContext.Model.FindEntityType(typeof(WcsTask))!
            .GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(WcsTask.WarehouseTaskId)]))
            .GetDatabaseName()!;
        Assert.True(WmsWcsDispatchPersistenceConflicts.IsTargetConflict(
            UniqueConflict(constraintName),
            dbContext));
        Assert.False(WmsWcsDispatchPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_unrelated_wms_constraint"),
            dbContext));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new WmsLifecycleConflictMiddleware(
            _ => throw UniqueConflict(constraintName),
            NullLogger<WmsLifecycleConflictMiddleware>.Instance);

        await middleware.InvokeAsync(context, dbContext);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<WmsLifecycleConflictResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(
            new WmsLifecycleConflictResponse(
                false,
                WmsLifecycleConflictException.SafeCode),
            response);
    }

    [Fact]
    public async Task Completion_contracts_declare_conflict_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ConflictRoutes)
        {
            var schemaReference = paths.GetProperty(route)
                .GetProperty("post")
                .GetProperty("responses")
                .GetProperty("409")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString();

            Assert.Equal(
                "#/components/schemas/NervIIPBusinessWmsWebApplicationErrorsWmsLifecycleConflictResponse",
                schemaReference);
        }
    }

    [Fact]
    public async Task Assigned_resource_completion_contracts_declare_authorization_and_business_validation_responses()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in AssignedResourceCompletionRoutes)
        {
            var responses = paths.GetProperty(route)
                .GetProperty("post")
                .GetProperty("responses");

            Assert.True(responses.TryGetProperty("403", out _));
            Assert.True(responses.TryGetProperty("409", out _));
            Assert.True(responses.TryGetProperty("422", out _));
        }
    }

    private static readonly string[] AssignedResourceCompletionRoutes =
    [
        "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete",
        "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete",
        "/api/business/v1/wms/count-executions/{countExecutionId}/complete",
    ];

    private static readonly string[] ConflictRoutes =
    [
        "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete",
        "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete",
        "/api/business/v1/wms/count-executions/{countExecutionId}/complete",
        "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/start",
        "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/progress",
        "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/exception",
        "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/complete",
        "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/start",
        "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/progress",
        "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/exception",
        "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/complete",
        "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch",
    ];

    private static DbUpdateException UniqueConflict(
        string constraintName,
        string sqlState = "23505") =>
        new("unique conflict", new FakePostgresException(sqlState, constraintName));

    private sealed class FakePostgresException(string sqlState, string constraintName) : Exception
    {
        public string SqlState { get; } = sqlState;

        public string ConstraintName { get; } = constraintName;
    }
}
