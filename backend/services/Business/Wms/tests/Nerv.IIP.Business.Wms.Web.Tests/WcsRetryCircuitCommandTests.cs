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

        Assert.Equal(WmsUnprocessableReasonCodes.WcsRetryNotDue, exception.ReasonCode);
        Assert.Equal(WcsTaskStatus.Failed, wcsTask.Status);
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

    [Theory]
    [InlineData("""{"actualQuantity":3,"executedQuantity":2}""")]
    [InlineData("""{"actualQuantity":"3","executedQuantity":3}""")]
    [InlineData("""{"actualQuantity":3,"executedQuantity":"3"}""")]
    public async Task Completion_rejects_disagreeing_or_non_numeric_dual_quantity_fields_without_mutation(
        string completionPayloadJson)
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-DUAL-QUANTITY-REJECT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-DUAL-QUANTITY-REJECT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            new CompleteWcsTaskCommandHandler(dbContext).Handle(
                new CompleteWcsTaskCommand(
                    "org-001",
                    "env-dev",
                    "EXT-DUAL-QUANTITY-REJECT-001",
                    completionPayloadJson),
                CancellationToken.None));

        Assert.Equal(0m, warehouseTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.InProgress, warehouseTask.Status);
        Assert.Equal(WcsTaskStatus.Dispatched, dbContext.WcsTasks.Single().Status);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public async Task Completion_quantity_outside_the_recorded_to_planned_range_is_rejected_with_a_reason_code(
        int reportedQuantity)
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-QTY-RANGE-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext, CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-QTY-RANGE-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new CompleteWcsTaskCommandHandler(dbContext).Handle(
            new CompleteWcsTaskCommand("org-001", "env-dev", "EXT-QTY-RANGE-001", """{"actualQuantity":2}"""),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 计划 3、已记录 2：报 4 超计划，报 1 倒退。
        var exception = await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            new CompleteWcsTaskCommandHandler(dbContext).Handle(
                new CompleteWcsTaskCommand(
                    "org-001",
                    "env-dev",
                    "EXT-QTY-RANGE-001",
                    $$"""{"actualQuantity":{{reportedQuantity}}}"""),
                CancellationToken.None));

        Assert.Equal(WmsUnprocessableReasonCodes.WcsCompletionQuantityOutOfRange, exception.ReasonCode);
        Assert.Equal(2m, warehouseTask.ExecutedQuantity);
    }

    [Fact]
    public async Task Completion_accepts_matching_numeric_dual_quantity_fields()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-DUAL-QUANTITY-ACCEPT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-DUAL-QUANTITY-ACCEPT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CompleteWcsTaskCommandHandler(dbContext).Handle(
            new CompleteWcsTaskCommand(
                "org-001",
                "env-dev",
                "EXT-DUAL-QUANTITY-ACCEPT-001",
                """{"actualQuantity":3,"executedQuantity":3}"""),
            CancellationToken.None);

        Assert.Equal(3m, warehouseTask.ExecutedQuantity);
        Assert.Equal(WarehouseTaskStatus.Completed, warehouseTask.Status);
        Assert.Equal(WcsTaskStatus.Completed, dbContext.WcsTasks.Single().Status);
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

    [Fact]
    public async Task Redispatch_without_payload_resends_the_original_dispatch_payload()
    {
        const string originalPayload = """{"taskNo":"WT-REDISPATCH-001","from":"RECV-01","to":"STAGE-01"}""";
        var now = new DateTimeOffset(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-REDISPATCH-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now.AddHours(-1)),
            new WcsTestTimeProvider(now.AddHours(-1))).Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-001", expectedVersion: 1) with { PayloadJson = originalPayload },
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wcsTask = await dbContext.WcsTasks.SingleAsync();
        wcsTask.Fail("E001", "blocked aisle", now.UtcDateTime.AddHours(-1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now),
            new WcsTestTimeProvider(now)).Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-001", warehouseTask.Version) with { PayloadJson = null },
            CancellationToken.None);

        Assert.Equal(WcsTaskStatus.Dispatched, wcsTask.Status);
        Assert.Equal(2, wcsTask.AttemptCount);
        Assert.Equal(originalPayload, wcsTask.PayloadJson);
    }

    [Fact]
    public async Task Redispatch_of_a_task_that_has_not_failed_is_refused_instead_of_reported_as_sent()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-REDISPATCH-LIVE-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new DispatchWcsTaskCommandHandler(dbContext, CreateAuthorizer(dbContext));
        await handler.Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-LIVE-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => handler.Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-LIVE-001", warehouseTask.Version) with { PayloadJson = null },
            CancellationToken.None));

        Assert.Equal(WmsUnprocessableReasonCodes.WcsRedispatchRequiresFailedTask, exception.ReasonCode);
        Assert.Equal(1, (await dbContext.WcsTasks.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Redispatch_checks_the_circuit_of_the_device_the_task_was_sent_to()
    {
        var now = new DateTimeOffset(2026, 7, 10, 2, 0, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-REDISPATCH-CIRCUIT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now.AddHours(-1)),
            new WcsTestTimeProvider(now.AddHours(-1))).Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-CIRCUIT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wcsTask = await dbContext.WcsTasks.SingleAsync();
        wcsTask.Fail("E001", "blocked aisle", now.UtcDateTime.AddHours(-1));
        // 任务发给的是 AGV-01；这台设备的熔断已打开。
        var circuit = WcsDispatchCircuit.Create("org-001", "env-dev", "agv", "AGV-01");
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-3), 3);
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-2), 3);
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-1), 3);
        dbContext.Add(circuit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 控制台重派不带设备号。
        var exception = await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new DispatchWcsTaskCommandHandler(
                dbContext,
                CreateAuthorizer(dbContext, now),
                new WcsTestTimeProvider(now)).Handle(
                DispatchCommand(warehouseTask, "EXT-REDISPATCH-CIRCUIT-001", warehouseTask.Version)
                    with { PayloadJson = null, DeviceId = null },
                CancellationToken.None));

        Assert.Equal(WmsUnprocessableReasonCodes.WcsDeviceCircuitOpen, exception.ReasonCode);
        Assert.Equal(WcsTaskStatus.Failed, wcsTask.Status);
    }

    [Fact]
    public async Task Redispatch_after_the_retry_budget_is_spent_reports_the_limit()
    {
        var now = new DateTimeOffset(2026, 7, 10, 3, 0, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-REDISPATCH-LIMIT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext, now.AddHours(-1)),
            new WcsTestTimeProvider(now.AddHours(-1))).Handle(
            DispatchCommand(warehouseTask, "EXT-REDISPATCH-LIMIT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wcsTask = await dbContext.WcsTasks.SingleAsync();
        wcsTask.Fail("E001", "blocked aisle", now.UtcDateTime.AddHours(-1), maxRetryAttempts: 1);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.True(wcsTask.IsTerminalFailure);

        var exception = await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new DispatchWcsTaskCommandHandler(
                dbContext,
                CreateAuthorizer(dbContext, now),
                new WcsTestTimeProvider(now)).Handle(
                DispatchCommand(warehouseTask, "EXT-REDISPATCH-LIMIT-001", warehouseTask.Version) with { PayloadJson = null },
                CancellationToken.None));

        Assert.Equal(WmsUnprocessableReasonCodes.WcsRetryLimitReached, exception.ReasonCode);
        Assert.Equal(WcsTaskStatus.Failed, wcsTask.Status);
    }

    [Fact]
    public async Task Completion_repeating_the_recorded_quantity_is_accepted()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-QTY-REPEAT-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new DispatchWcsTaskCommandHandler(dbContext, CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-QTY-REPEAT-001", expectedVersion: 1),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteWcsTaskCommandHandler(dbContext);
        var command = new CompleteWcsTaskCommand("org-001", "env-dev", "EXT-QTY-REPEAT-001", """{"actualQuantity":2}""");
        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 设备重复回报同一累计数量（下界本身）不是倒退。
        await handler.Handle(command, CancellationToken.None);

        Assert.Equal(2m, warehouseTask.ExecutedQuantity);
    }

    [Fact]
    public async Task First_dispatch_without_payload_is_rejected_because_there_is_nothing_to_resend()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-FIRST-NO-PAYLOAD-001");
        AddWorkPool(dbContext);
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<WmsUnprocessableException>(() => new DispatchWcsTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            DispatchCommand(warehouseTask, "EXT-FIRST-NO-PAYLOAD-001", expectedVersion: 1) with { PayloadJson = null },
            CancellationToken.None));

        Assert.Empty(dbContext.WcsTasks.Local);
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
