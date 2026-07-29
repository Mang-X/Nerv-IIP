using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Tests;

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
    ];

    private static DbUpdateException UniqueConflict(string constraintName) =>
        new("unique conflict", new FakePostgresException("23505", constraintName));

    private sealed class FakePostgresException(string sqlState, string constraintName) : Exception
    {
        public string SqlState { get; } = sqlState;

        public string ConstraintName { get; } = constraintName;
    }
}
