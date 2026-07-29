using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WcsRetryCircuitCommandTests
{
    [Fact]
    public async Task Dispatch_rejects_a_retry_before_its_scheduled_time()
    {
        var now = new DateTimeOffset(2026, 7, 10, 0, 0, 30, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-RETRY-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var initialHandler = new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now.AddMinutes(-2)),
            new WcsTestTimeProvider(now.AddMinutes(-2)));
        await initialHandler.Handle(
            DispatchCommand(warehouseTask, "EXT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wcsTask = await dbContext.WcsTasks.SingleAsync();
        wcsTask.Fail("E001", "blocked aisle", now.UtcDateTime.AddSeconds(-30));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now),
            new WcsTestTimeProvider(now));

        var exception = await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => handler.Handle(
            DispatchCommand(warehouseTask, "EXT-002", warehouseTask.Version),
            CancellationToken.None));

        Assert.Contains("not due", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatch_fails_fast_with_a_clear_reason_when_the_device_circuit_is_open()
    {
        var now = new DateTimeOffset(2026, 7, 10, 0, 3, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-CIRCUIT-001");
        AddWorkPool(dbContext);
        var circuit = WcsDispatchCircuit.Create("org-001", "env-dev", "agv", "AGV-01");
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-2), 3);
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-1), 3);
        circuit.RecordFailure(now.UtcDateTime, 3);
        dbContext.AddRange(warehouseTask, circuit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now),
            new WcsTestTimeProvider(now));

        var exception = await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => handler.Handle(
            DispatchCommand(warehouseTask, "EXT-CIRCUIT-001", expectedVersion: 1),
            CancellationToken.None));

        Assert.Contains("circuit is open", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completed_task_resets_the_closed_device_circuit_failure_counter()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-SUCCESS-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var circuit = WcsDispatchCircuit.Create("org-001", "env-dev", "agv", "AGV-01");
        circuit.RecordFailure(DateTime.UtcNow.AddMinutes(-2), 3);
        circuit.RecordFailure(DateTime.UtcNow.AddMinutes(-1), 3);
        dbContext.Add(circuit);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-SUCCESS-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteWcsTaskCommandHandler(dbContext).Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "EXT-SUCCESS-001", "{\"actualQuantity\":3}"),
            CancellationToken.None);

        Assert.Equal(0, circuit.ConsecutiveFailureCount);
        Assert.False(circuit.IsOpen);
    }

    [Fact]
    public async Task Repeated_failure_callback_does_not_increment_the_device_circuit_twice()
    {
        var now = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-FAIL-IDEMPOTENT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now),
            new WcsTestTimeProvider(now)).Handle(
            DispatchCommand(
                warehouseTask,
                "EXT-FAIL-IDEMPOTENT-001",
                expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new FailWcsTaskCommandHandler(dbContext, new WcsTestTimeProvider(now));
        var command = new FailWcsTaskCommand("org-001", "env-dev", "EXT-FAIL-IDEMPOTENT-001", "E001", "blocked aisle");

        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.Handle(command, CancellationToken.None);

        var circuit = Assert.Single(dbContext.WcsDispatchCircuits.Local);
        Assert.Equal(1, circuit.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task Dispatch_claims_the_warehouse_task_before_manual_execution_can_start()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-WCS-CLAIM-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var wcsTaskId = await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-WCS-CLAIM-001", expectedVersion: 1),
            CancellationToken.None);

        Assert.Equal(WarehouseTaskExecutionChannel.Wcs, warehouseTask.ExecutionChannel);
        Assert.Equal(WarehouseTaskStatus.InProgress, warehouseTask.Status);
        Assert.Equal(wcsTaskId.Id.ToString("D"), warehouseTask.ExecutionClaimedBy);
        Assert.Throws<InvalidOperationException>(() =>
            warehouseTask.Start(
                "user-emp-049",
                warehouseTask.Version,
                claimPoolAssignment: true));
    }

    private static WarehouseTask CreateWarehouseTask(string taskNo) =>
        WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            taskNo,
            "IN-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-01",
            "RECV-01",
            "STAGE-01",
            3m,
            assignedPoolCode: "POOL-WCS");

    private static DispatchWcsTaskCommand DispatchCommand(
        WarehouseTask warehouseTask,
        string externalTaskId,
        long expectedVersion) =>
        new(
            warehouseTask.Id,
            "org-001",
            "env-dev",
            "user-wcs-manager",
            ["SITE-01"],
            expectedVersion,
            "agv",
            externalTaskId,
            "{}",
            "AGV-01");

    private static WarehouseWorkScopeAuthorizer CreateAuthorizer(
        ApplicationDbContext dbContext,
        DateTimeOffset? now = null) =>
        new(
            dbContext,
            new WcsTestTimeProvider(now ?? DateTimeOffset.UtcNow));

    private static void AddWorkPool(ApplicationDbContext dbContext) =>
        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            "POOL-WCS",
            "WCS 自动化池",
            "SITE-01"));
}

internal sealed class WcsTestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
