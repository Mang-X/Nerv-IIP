using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Auth;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockLocations;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryEndpointContractTests
{
    [Fact]
    public void Inventory_endpoints_expose_issue_131_routes_permissions_policies_and_operation_ids()
    {
        var contracts = InventoryEndpointContracts.All.ToArray();

        Assert.Equal(5, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/inventory/v1/locations"
            && x.PermissionCode == InventoryPermissionCodes.LocationsManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createOrUpdateInventoryLocation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/inventory/v1/movements"
            && x.PermissionCode == InventoryPermissionCodes.MovementsCreate
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "postInventoryMovement");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/inventory/v1/availability"
            && x.PermissionCode == InventoryPermissionCodes.LedgerRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "getInventoryAvailability");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/inventory/v1/count-tasks"
            && x.PermissionCode == InventoryPermissionCodes.CountsManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createInventoryCountTask");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/inventory/v1/count-tasks/{countTaskId}/adjustments"
            && x.PermissionCode == InventoryPermissionCodes.CountsManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "confirmInventoryCountAdjustment");
    }

    [Theory]
    [InlineData(typeof(CreateOrUpdateStockLocationEndpoint))]
    [InlineData(typeof(PostStockMovementEndpoint))]
    [InlineData(typeof(GetStockAvailabilityEndpoint))]
    [InlineData(typeof(CreateStockCountTaskEndpoint))]
    [InlineData(typeof(ConfirmStockCountAdjustmentEndpoint))]
    public void Inventory_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Availability_query_returns_on_hand_reserved_and_available_by_ledger_dimensions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
        ledger.ApplyMovement(DomainMovementFactory.Inbound(18m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetStockAvailabilityQueryHandler(dbContext);

        var response = await handler.Handle(new GetStockAvailabilityQuery(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001"), CancellationToken.None);

        Assert.Equal(18m, response.OnHandQuantity);
        Assert.Equal(0m, response.ReservedQuantity);
        Assert.Equal(18m, response.AvailableQuantity);
        Assert.Equal("LOT-001", response.LotNo);
        Assert.Single(response.Items);
        Assert.Equal("LOC-A-01", response.Items.Single().LocationCode);
        Assert.Equal(18m, response.Items.Single().AvailableQuantity);
    }

    [Fact]
    public async Task Availability_query_returns_dimension_lines_when_location_is_omitted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledgerA = DomainLedgerFactory.NewLedger();
        ledgerA.ApplyMovement(DomainMovementFactory.Inbound(18m));
        var ledgerB = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-01",
            "LOT-002",
            null,
            "qualified",
            "company",
            "owner-001");
        ledgerB.ApplyMovement(DomainMovementFactory.InboundForLocation("LOC-B-01", "LOT-002", 7m));
        dbContext.StockLedgers.AddRange(ledgerA, ledgerB);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetStockAvailabilityQueryHandler(dbContext).Handle(new GetStockAvailabilityQuery(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            null,
            null,
            null,
            "QUALIFIED",
            "COMPANY",
            "owner-001"), CancellationToken.None);

        Assert.Equal(25m, response.AvailableQuantity);
        Assert.Equal(2, response.Items.Count);
        Assert.Contains(response.Items, x => x.LocationCode == "LOC-A-01" && x.AvailableQuantity == 18m);
        Assert.Contains(response.Items, x => x.LocationCode == "LOC-B-01" && x.AvailableQuantity == 7m);
    }

    [Fact]
    public async Task Availability_query_rejects_unbounded_dimension_results()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var index = 0; index <= GetStockAvailabilityQueryHandler.MaxResultLines; index++)
        {
            var ledger = StockLedger.Create(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                $"LOC-{index:0000}",
                $"LOT-{index:0000}",
                null,
                "qualified",
                "company",
                "owner-001");
            ledger.ApplyMovement(DomainMovementFactory.InboundForLocation($"LOC-{index:0000}", $"LOT-{index:0000}", 1m));
            dbContext.StockLedgers.Add(ledger);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new GetStockAvailabilityQueryHandler(dbContext).Handle(new GetStockAvailabilityQuery(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                null,
                null,
                null,
                "qualified",
                "company",
                "owner-001"), CancellationToken.None));

        Assert.Contains(GetStockAvailabilityQueryHandler.MaxResultLines.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/v1/movements", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            movementType = "inbound",
            sourceService = "wms",
            sourceDocumentId = "DOC-001",
            sourceDocumentLineId = "LINE-001",
            idempotencyKey = "idem-in-001",
            skuCode = "SKU-FG-1000",
            uomCode = "kg",
            siteCode = "SITE-01",
            locationCode = "LOC-A-01",
            lotNo = "LOT-001",
            serialNo = (string?)null,
            qualityStatus = "qualified",
            ownerType = "company",
            ownerId = "owner-001",
            quantity = 5m,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Validators_reject_non_code_characters_for_inventory_codes()
    {
        var locationResult = new CreateStockLocationCommandValidator().Validate(
            new CreateStockLocationCommand("org-001", "env-dev", "LOC;DROP", "storage", "SITE-01", null, "active"));
        var movementResult = new PostStockMovementCommandValidator().Validate(
            NewPostMovementCommand("idem-in-001", 5m) with { SkuCode = "SKU#1" });

        Assert.False(locationResult.IsValid);
        Assert.Contains(locationResult.Errors, x => x.ErrorMessage.Contains("underscore", StringComparison.OrdinalIgnoreCase));
        Assert.False(movementResult.IsValid);
        Assert.Contains(movementResult.Errors, x => x.ErrorMessage.Contains("underscore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validators_limit_idempotency_keys_to_128_characters()
    {
        var movementResult = new PostStockMovementCommandValidator().Validate(
            NewPostMovementCommand(new string('a', 129), 5m));
        var adjustmentResult = new ConfirmStockCountAdjustmentCommandValidator().Validate(
            new ConfirmStockCountAdjustmentCommand(new StockCountTaskId(Guid.CreateVersion7()), 5m, new string('a', 129)));

        Assert.False(movementResult.IsValid);
        Assert.Contains(movementResult.Errors, x => x.ErrorMessage.Contains("128", StringComparison.OrdinalIgnoreCase));
        Assert.False(adjustmentResult.IsValid);
        Assert.Contains(adjustmentResult.Errors, x => x.ErrorMessage.Contains("128", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_movement_command_returns_existing_movement_for_duplicate_idempotency_key_with_same_payload()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new PostStockMovementCommandHandler(dbContext);
        var command = NewPostMovementCommand("idem-in-001", 5m);
        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var duplicate = await new PostStockMovementCommandHandler(secondDbContext).Handle(command, CancellationToken.None);

        Assert.Equal(first.MovementId, duplicate.MovementId);
        Assert.Equal(5m, duplicate.OnHandQuantity);
        Assert.Single(secondDbContext.StockMovements);
    }

    [Fact]
    public async Task Post_movement_command_does_not_create_empty_ledger_for_duplicate_existing_movement()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StockMovements.Add(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-001",
            "LINE-001",
            "idem-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            5m));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var duplicate = await new PostStockMovementCommandHandler(secondDbContext).Handle(
            NewPostMovementCommand("idem-in-001", 5m),
            CancellationToken.None);
        await secondDbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0m, duplicate.OnHandQuantity);
        Assert.Empty(secondDbContext.StockLedgers);
    }

    [Fact]
    public async Task Post_movement_command_rejects_duplicate_idempotency_key_with_conflicting_payload()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new PostStockMovementCommandHandler(dbContext);
        await handler.Handle(NewPostMovementCommand("idem-in-001", 5m), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new PostStockMovementCommandHandler(secondDbContext).Handle(NewPostMovementCommand("idem-in-001", 6m), CancellationToken.None));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stock_ledger_concurrent_updates_are_rejected()
    {
        await using var provider = CreateInMemoryProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ledger = DomainLedgerFactory.NewLedger();
            ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
            dbContext.StockLedgers.Add(ledger);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstLedger = await firstDbContext.StockLedgers.SingleAsync(CancellationToken.None);
        var secondLedger = await secondDbContext.StockLedgers.SingleAsync(CancellationToken.None);

        firstLedger.ApplyMovement(DomainMovementFactory.InboundWithIdempotency("idem-concurrent-001", 2m));
        secondLedger.ApplyMovement(DomainMovementFactory.InboundWithIdempotency("idem-concurrent-002", 3m));
        await firstDbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => secondDbContext.SaveChangesAsync(CancellationToken.None));
        Assert.Contains("concurrent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirm_count_adjustment_command_is_idempotent_for_same_counted_quantity()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
        dbContext.StockLedgers.Add(ledger);
        var task = DomainCountTaskFactory.NewTask(ledger);
        dbContext.StockCountTasks.Add(task);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ConfirmStockCountAdjustmentCommandHandler(dbContext);
        var command = new ConfirmStockCountAdjustmentCommand(task.Id, 7.5m, "idem-count-001");
        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = await new ConfirmStockCountAdjustmentCommandHandler(secondDbContext).Handle(command, CancellationToken.None);

        Assert.Equal(first.MovementId, second.MovementId);
        Assert.Equal(-2.5m, second.VarianceQuantity);
        Assert.Single(secondDbContext.StockCountAdjustments);
        Assert.Single(secondDbContext.StockMovements.Where(x => x.MovementType == "count-adjustment"));
    }

    [Fact]
    public async Task Confirm_count_adjustment_command_rejects_same_idempotency_key_with_conflicting_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
        dbContext.StockLedgers.Add(ledger);
        var task = DomainCountTaskFactory.NewTask(ledger);
        dbContext.StockCountTasks.Add(task);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ConfirmStockCountAdjustmentCommandHandler(dbContext).Handle(
            new ConfirmStockCountAdjustmentCommand(task.Id, 7.5m, "idem-count-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ConfirmStockCountAdjustmentCommandHandler(secondDbContext).Handle(
                new ConfirmStockCountAdjustmentCommand(task.Id, 7m, "idem-count-001"),
                CancellationToken.None));

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"inventory-api-contract-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static PostStockMovementCommand NewPostMovementCommand(string idempotencyKey, decimal quantity)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-001",
            "LINE-001",
            idempotencyKey,
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            quantity);
    }
}
