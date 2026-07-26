using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// L1 背景历史（仓储域侧）的常规门禁测试：形状、确定性、幂等、隔离、单据终态与 fail-closed。
/// 全量规模下的真实数据库耗时实测在 <see cref="WorldHistoryWmsSeedPostgresTests"/>（env-gated）。
/// </summary>
public sealed class WorldHistoryWmsSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够跑出四类单据，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_document_stream_matches_the_world_bible_shape()
    {
        var documents = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 1.0d);
        var workOrders = WorldHistoryPhase2Spec.BuildWorkOrderFacts(AsOfDate, 1.0d);
        var purchases = WorldHistoryProcurementSpec.BuildPurchasePlans(AsOfDate, 1.0d).Where(x => x.IsReceived).ToArray();
        var deliveries = WorldHistorySpec.BuildOrderPlans(AsOfDate, 1.0d).Count(x => x.HasDelivery);
        var issues = workOrders.Sum(fact => WorldHistoryPhase2Spec.MaterialIssues(fact).Count);

        foreach (var group in documents.InboundOrders.GroupBy(x => x.SourceDocumentType, StringComparer.Ordinal)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"wms-world-history-inbound-{group.Key}={group.Count()}");
        }

        foreach (var group in documents.OutboundOrders.GroupBy(x => x.SourceDocumentType, StringComparer.Ordinal)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"wms-world-history-outbound-{group.Key}={group.Count()}");
        }

        output.WriteLine($"wms-world-history-inbound-total={documents.InboundOrders.Count}");
        output.WriteLine($"wms-world-history-outbound-total={documents.OutboundOrders.Count}");
        output.WriteLine($"wms-world-history-tasks-total={documents.InboundOrders.Count + documents.OutboundOrders.Count}");

        Assert.Equal(purchases.Length + workOrders.Count(x => x.HasFinishedGoodsReceipt), documents.InboundOrders.Count);
        Assert.Equal(deliveries + issues, documents.OutboundOrders.Count);
        Assert.Equal(
            purchases.Length,
            documents.InboundOrders.Count(x => string.Equals(x.SourceDocumentType, WorldHistoryWmsSpec.PurchaseReceiptSourceType, StringComparison.Ordinal)));
        Assert.Equal(
            deliveries,
            documents.OutboundOrders.Count(x => string.Equals(x.SourceDocumentType, WorldHistoryWmsSpec.DeliveryOrderSourceType, StringComparison.Ordinal)));
    }

    [Fact]
    public void Document_numbers_are_unique_and_follow_the_reserved_segments()
    {
        var documents = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 0.2d);
        var orderNumbers = documents.InboundOrders.Select(x => x.InboundOrderNo)
            .Concat(documents.OutboundOrders.Select(x => x.OutboundOrderNo))
            .ToArray();
        var taskNumbers = documents.InboundOrders.Select(x => x.WarehouseTaskNo)
            .Concat(documents.OutboundOrders.Select(x => x.WarehouseTaskNo))
            .ToArray();

        Assert.Equal(orderNumbers.Length, orderNumbers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(taskNumbers.Length, taskNumbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(documents.InboundOrders, x => Assert.StartsWith("IB-", x.InboundOrderNo, StringComparison.Ordinal));
        Assert.All(documents.OutboundOrders, x => Assert.StartsWith("OB-", x.OutboundOrderNo, StringComparison.Ordinal));
        Assert.All(taskNumbers, x => Assert.StartsWith("WT-", x, StringComparison.Ordinal));
    }

    [Fact]
    public void All_document_timestamps_stay_inside_the_history_window_and_off_sunday()
    {
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var documents = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 0.2d);

        var moments = documents.InboundOrders
            .SelectMany(x => new[] { x.CreatedAtUtc, x.CompletedAtUtc, x.TaskCreatedAtUtc, x.TaskCompletedAtUtc })
            .Concat(documents.OutboundOrders
                .SelectMany(x => new[] { x.CreatedAtUtc, x.TaskCompletedAtUtc, x.CompletedAtUtc }));

        foreach (var moment in moments)
        {
            Assert.InRange(moment, lowerBound, upperBound);
            Assert.True(
                WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(moment.UtcDateTime)),
                $"仓储单据时间 {moment:O} 落在周日。");
        }
    }

    [Fact]
    public void Document_stream_is_deterministic_for_the_same_inputs()
    {
        var first = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 0.2d);
        var second = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 0.2d);

        Assert.Equal(first.InboundOrders, second.InboundOrders);
        Assert.Equal(first.OutboundOrders, second.OutboundOrders);
    }

    [Fact]
    public void Movement_idempotency_keys_align_with_the_inventory_side_contract()
    {
        var documents = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, 0.2d);

        foreach (var inbound in documents.InboundOrders)
        {
            var expected = string.Equals(inbound.SourceDocumentType, WorldHistoryWmsSpec.ProductionReceiptSourceType, StringComparison.Ordinal)
                // 完工入库两侧共用 MES 一期写下的 INV-{工单}。
                ? $"INV-{inbound.SourceDocumentId["FGR-".Length..]}"
                : WorldHistoryPhase2Spec.MovementKey(inbound.SourceDocumentId, WorldHistoryWmsSpec.ReceiptInPurpose);
            Assert.Equal(expected, inbound.MovementIdempotencyKey);
        }

        foreach (var outbound in documents.OutboundOrders)
        {
            var purpose = string.Equals(outbound.SourceDocumentType, WorldHistoryWmsSpec.DeliveryOrderSourceType, StringComparison.Ordinal)
                ? WorldHistoryWmsSpec.DeliveryOutPurpose
                : WorldHistoryWmsSpec.MaterialIssueOutPurpose;
            Assert.Equal(
                WorldHistoryPhase2Spec.MovementKey(outbound.SourceDocumentId, purpose),
                outbound.MovementIdempotencyKey);
        }
    }

    [Fact]
    public async Task Seed_writes_the_full_chain_and_reruns_without_writing_anything()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var documents = WorldHistoryWmsSpec.BuildDocuments(AsOfDate, SmallScale);
        output.WriteLine($"small-scale-inbound-orders={first.InboundOrdersWritten}");
        output.WriteLine($"small-scale-outbound-orders={first.OutboundOrdersWritten}");
        output.WriteLine($"small-scale-warehouse-tasks={first.WarehouseTasksWritten}");
        output.WriteLine($"small-scale-movement-requests={first.InventoryMovementRequestsWritten}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"small-scale-sample: {line}");
        }

        Assert.Equal(documents.InboundOrders.Count, first.InboundOrdersWritten);
        Assert.Equal(documents.OutboundOrders.Count, first.OutboundOrdersWritten);
        Assert.Equal(documents.InboundOrders.Count + documents.OutboundOrders.Count, first.WarehouseTasksWritten);
        Assert.Equal(documents.InboundOrders.Count + documents.OutboundOrders.Count, first.InventoryMovementRequestsWritten);

        Assert.Equal(0, second.InboundOrdersWritten);
        Assert.Equal(0, second.OutboundOrdersWritten);
        Assert.Equal(0, second.WarehouseTasksWritten);
        Assert.Equal(0, second.InventoryMovementRequestsWritten);
        Assert.Equal(documents.InboundOrders.Count, await db.InboundOrders.CountAsync());
        Assert.Equal(documents.OutboundOrders.Count, await db.OutboundOrders.CountAsync());
    }

    [Fact]
    public async Task Seeded_orders_reach_their_terminal_status_with_a_completed_task()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        Assert.All(
            await db.InboundOrders.ToArrayAsync(),
            order => Assert.Equal(InboundOrderStatus.Completed, order.Status));
        Assert.All(
            await db.OutboundOrders.ToArrayAsync(),
            order => Assert.Equal(OutboundOrderStatus.Completed, order.Status));

        var tasks = await db.WarehouseTasks.ToArrayAsync();
        Assert.NotEmpty(tasks);
        Assert.All(tasks, task =>
        {
            Assert.Equal(WarehouseTaskStatus.Completed, task.Status);
            Assert.Equal(task.PlannedQuantity, task.ExecutedQuantity);
        });
        Assert.Contains(tasks, task => task.TaskType == WarehouseTaskType.Putaway);
        Assert.Contains(tasks, task => task.TaskType == WarehouseTaskType.Picking);

        Assert.All(
            await db.InventoryMovementRequests.ToArrayAsync(),
            request => Assert.Equal(InventoryMovementRequestStatus.Posted, request.Status));
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var codes = (await db.InboundOrders.Select(x => x.InboundOrderNo).ToArrayAsync())
            .Concat(await db.OutboundOrders.Select(x => x.OutboundOrderNo).ToArrayAsync())
            .Concat(await db.WarehouseTasks.Select(x => x.TaskNo).ToArrayAsync())
            .ToArray();

        Assert.NotEmpty(codes);
        foreach (var code in codes)
        {
            Assert.DoesNotContain("-DEMO-", code, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_warehouse_task_disappears()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var task = await db.WarehouseTasks.FirstAsync();
        db.WarehouseTasks.Remove(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryWmsConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("没有任何仓储作业任务", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validator_fails_closed_when_an_inbound_order_disappears()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var order = await db.InboundOrders.FirstAsync();
        db.InboundOrders.Remove(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryWmsConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
