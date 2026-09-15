using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S7：集成事件消费侧 5 个建凭证位点的凭证号改走 <c>CodeAllocator</c> 的
/// <c>journal-voucher</c> 规则，以及**分配失败时的 gate-and-skip**。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类打的轴</b>：⛔ 不是「有没有调分配器」（那一格用 <c>Assert.Contains</c> 之类的存在性判据也会绿），
/// 而是「**产出的是什么**」——逐个位点把落库凭证号与改前那个派生串直接对撞（<c>Assert.NotEqual</c>），
/// 再钉分配器形状。把 <c>TryAllocateAsync</c> 换回 <c>ErpVoucherNoPolicy.Compose</c> 时这两条都红。
/// </para>
/// <para>
/// <b>失败路径怎么造出来</b>：<c>ConsumerJournalVoucherNumber</c> 的指纹是幂等键的函数，
/// 所以正常调用**造不出**指纹冲突。用例直接往 <c>code_idempotency_keys</c> 写一行同键、异指纹的记录——
/// 这是「外部篡改」，不是正常业务路径，正是为了够到那条 <see cref="KnownException"/> 分支。
/// ⚠️ 这要求分配器是**落库**那一套（DI 装配），无参 <c>new ErpCodingService()</c> 的进程内字典读不到这行。
/// </para>
/// </remarks>
public sealed class ConsumerJournalVoucherNumberAllocationTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";

    /// <summary>
    /// 位点 ①（<c>PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual</c>）：
    /// 凭证号来自分配器，⛔ 不再是 <c>JV-GRIR-{收货单号}</c>；来源两列不动。
    /// </summary>
    [Fact]
    public async Task GrIr_accrual_posts_an_allocated_voucher_number_instead_of_the_receipt_derived_one()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var integrationEvent = await SeedReceiptAsync(dbContext, "PO-S7-001", "RCV-S7-001");

        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(dbContext, deadLetters, coding)
            .HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = Assert.Single(await dbContext.JournalVouchers.ToListAsync());
        Assert.Equal(JournalVoucherSourceType.GoodsReceiptIrAccrual.Code, voucher.SourceType);
        Assert.Equal("RCV-S7-001", voucher.SourceNo);
        AllocatedVoucherNo.AssertShape(voucher.VoucherNo);
        Assert.NotEqual("JV-GRIR-RCV-S7-001", voucher.VoucherNo);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// CAP 重投的重放读数（InMemory lane）：换一个 <c>EventId</c> 再投——inbox 短路不了，
    /// 走到 S5 的来源两列查重 ⇒ 仍只有一张凭证，且凭证号**没变**（分配器按来源键重放同一个号）。
    /// ⚠️ InMemory 看不见唯一索引，所以这条只证明「代码路径不再记第二张」；
    /// 「索引也挡得住」属真库 lane，见 <c>ErpCostAccountingPostgresAcceptanceTests</c> 与
    /// <c>ErpReturnClosurePostgresAcceptanceTests</c>。
    /// </summary>
    [Fact]
    public async Task GrIr_accrual_redelivery_under_a_new_event_id_keeps_exactly_one_voucher_with_the_same_number()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var integrationEvent = await SeedReceiptAsync(dbContext, "PO-S7-002", "RCV-S7-002");
        var handler = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(dbContext, deadLetters, coding);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var firstVoucherNo = (await dbContext.JournalVouchers.SingleAsync()).VoucherNo;

        await handler.HandleAsync(
            integrationEvent with { EventId = "evt-s7-002-redelivered", IdempotencyKey = "s7-002-redelivered" },
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = Assert.Single(await dbContext.JournalVouchers.ToListAsync());
        Assert.Equal(firstVoucherNo, voucher.VoucherNo);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        // 分配器本身也按来源键重放：同一个 (GRIR, RCV-S7-002) 再要一次号，拿回同一个码。
        var replay = await ConsumerJournalVoucherNumber.TryAllocateAsync(
            coding, OrganizationId, EnvironmentId,
            JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S7-002", CancellationToken.None);
        Assert.Equal(firstVoucherNo, replay.Code);
    }

    /// <summary>
    /// ⭐ gate-and-skip：凭证号分配不下来时，⛔ 不抛异常（CAP 消费者里会逃逸成 poison message，#877），
    /// 而是写死信、不建凭证、不留已处理记录。
    /// </summary>
    [Fact]
    public async Task GrIr_accrual_dead_letters_and_posts_nothing_when_the_voucher_number_cannot_be_allocated()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var integrationEvent = await SeedReceiptAsync(dbContext, "PO-S7-003", "RCV-S7-003");
        await PoisonAllocationAsync(dbContext, JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S7-003");

        // ⛔ 没有 Assert.ThrowsAsync：这一行本身就是断言——handler 必须正常返回。
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(dbContext, deadLetters, coding)
            .HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await dbContext.JournalVouchers.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal(ConsumerJournalVoucherNumber.AllocationFailureCode, deadLetter.FailureCode);
        Assert.Contains("RCV-S7-003", deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 位点 ④（<c>StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost</c> 的资本化凭证）：
    /// 凭证号来自分配器，⛔ 不再是 <c>JV-WOC-{工单号}-{移动号}</c>。
    /// </summary>
    [Fact]
    public async Task Work_order_capitalization_posts_an_allocated_voucher_number()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var cost = WorkOrderCost.Open(OrganizationId, EnvironmentId, "WO-S7-004", "FG-S7");
        cost.RecordLabor("RPT-S7-004", "WC-S7", 1m, 80m, "CNY", false, DateTimeOffset.Parse("2026-09-15T00:00:00Z"));
        cost.Complete(4m, 1, 0, DateTimeOffset.Parse("2026-09-15T01:00:00Z"));
        dbContext.WorkOrderCosts.Add(cost);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(dbContext, deadLetters, dbContext, coding)
            .HandleAsync(FinishedGoodsReceipt("MOVE-S7-004", "WO-S7-004"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = Assert.Single(await dbContext.JournalVouchers.ToListAsync());
        Assert.Equal(JournalVoucherSourceType.WorkOrderCapitalization.Code, voucher.SourceType);
        Assert.Equal("MOVE-S7-004", voucher.SourceNo);
        AllocatedVoucherNo.AssertShape(voucher.VoucherNo);
        Assert.NotEqual("JV-WOC-WO-S7-004-MOVE-S7-004", voucher.VoucherNo);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// 位点 ⑤（<c>CostVariancePosting.PostLateAdjustmentAsync</c>）：凭证号来自分配器，
    /// ⛔ 不再是 <c>JV-WOCADJ-{工单号}-{来源号}</c>；来源单号仍取触发标识。
    /// </summary>
    [Fact]
    public async Task Late_cost_adjustment_posts_an_allocated_voucher_number()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var cost = await SeedCapitalizedCostAsync(dbContext, "WO-S7-005");

        Assert.True(await CostVariancePosting.PostLateAdjustmentAsync(
            dbContext, coding, cost, -25m, "RPT-S7-005", DateTimeOffset.Parse("2026-09-15T02:00:00Z"), CancellationToken.None));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = Assert.Single(await dbContext.JournalVouchers
            .Where(x => x.SourceType == JournalVoucherSourceType.WorkOrderCostAdjustment.Code)
            .ToListAsync());
        Assert.Equal("RPT-S7-005", voucher.SourceNo);
        AllocatedVoucherNo.AssertShape(voucher.VoucherNo);
        Assert.NotEqual("JV-WOCADJ-WO-S7-005-RPT-S7-005", voucher.VoucherNo);
    }

    /// <summary>
    /// ⭐ 位点 ⑤ 的 gate-and-skip：拿不到号时返回 <see langword="false"/> 且**零副作用**——
    /// 不建凭证、不补科目、不动 WIP 清理额。分配放在两条早退之后、任何写入之前，
    /// 所以不会留下「补了科目 / 清了 WIP 却没凭证」的半截状态。
    /// </summary>
    [Fact]
    public async Task Late_cost_adjustment_skips_without_side_effects_when_the_voucher_number_cannot_be_allocated()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var cost = await SeedCapitalizedCostAsync(dbContext, "WO-S7-006");
        var wipClearedBefore = cost.WipClearedCost;
        await PoisonAllocationAsync(dbContext, JournalVoucherSourceType.WorkOrderCostAdjustment, "RPT-S7-006");

        Assert.False(await CostVariancePosting.PostLateAdjustmentAsync(
            dbContext, coding, cost, -25m, "RPT-S7-006", DateTimeOffset.Parse("2026-09-15T02:00:00Z"), CancellationToken.None));

        Assert.Empty(dbContext.ChangeTracker.Entries<JournalVoucher>());
        Assert.Empty(dbContext.ChangeTracker.Entries<GLAccount>());
        Assert.Equal(wipClearedBefore, cost.WipClearedCost);
    }

    /// <summary>
    /// <c>costDelta == 0</c> 的空转仍返回 <see langword="true"/>（已完成，不是失败），
    /// 且**不向分配器要号**——否则每个零差异结算都会白烧一个凭证号。
    /// ⛔ 这一格与上面那条 false 不是同一件事：只断言 false 那条时，把返回值写死成
    /// <c>return voucherAllocation.Code is not null</c> 之后再无条件 return 也会绿。
    /// </summary>
    [Fact]
    public async Task Zero_delta_late_adjustment_completes_without_asking_for_a_number()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var cost = await SeedCapitalizedCostAsync(dbContext, "WO-S7-007");
        await PoisonAllocationAsync(dbContext, JournalVoucherSourceType.WorkOrderCostAdjustment, "RPT-S7-007");

        // 这个来源键已被投毒：只要真去要号就会拿到 null 并返回 false。返回 true 即证明没去要。
        Assert.True(await CostVariancePosting.PostLateAdjustmentAsync(
            dbContext, coding, cost, 0m, "RPT-S7-007", DateTimeOffset.Parse("2026-09-15T02:00:00Z"), CancellationToken.None));
        Assert.Empty(dbContext.ChangeTracker.Entries<JournalVoucher>());
    }

    /// <summary>
    /// 本类自建装配而不用 <c>ErpTestProvider.CreateInMemoryProvider</c>：
    /// 位点 ④ 走 <c>CostingIntegrationEventUnitOfWork</c> 会开事务，InMemory provider 默认把
    /// <c>TransactionIgnoredWarning</c> 升成异常。⛔ 不改共用的 <c>ErpTestProvider</c>（那会放宽别人的用例）。
    /// ⭐ 分配器必须由 DI 解析：<c>ErpCodingService(ApplicationDbContext, IServiceScopeFactory)</c>
    /// 才是**落库**那一套，本类的投毒行只有它读得到。
    /// </summary>
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"erp-s7-consumer-voucher-no-{Guid.CreateVersion7():N}";
        // ⴛ 不注册真 MediatR handler：本类只看凭证号与死信，
        // 而资本化路径会发领域事件，其 handler 要的依赖不在本装配里。
        services.AddSingleton<IMediator, AllocationNoopMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<ErpCodingService>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 往 <c>code_idempotency_keys</c> 写一行「同幂等键、异指纹」的记录，把该来源键的分配打成失败。
    /// </summary>
    private static async Task PoisonAllocationAsync(
        ApplicationDbContext dbContext,
        JournalVoucherSourceType sourceType,
        string sourceNo)
    {
        dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            OrganizationId,
            EnvironmentId,
            ConsumerJournalVoucherNumber.RuleKey,
            ConsumerJournalVoucherNumber.IdempotencyKeyOf(sourceType, sourceNo),
            "JV-19700101-000000",
            "tampered-fingerprint",
            DateTimeOffset.Parse("2026-09-15T00:00:00Z")));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<WorkOrderCost> SeedCapitalizedCostAsync(ApplicationDbContext dbContext, string workOrderId)
    {
        var cost = WorkOrderCost.Open(OrganizationId, EnvironmentId, workOrderId, "FG-S7");
        cost.RecordLabor($"RPT-{workOrderId}", "WC-S7", 1m, 100m, "CNY", false, DateTimeOffset.Parse("2026-09-15T00:00:00Z"));
        cost.Complete(1m, 1, 0, DateTimeOffset.Parse("2026-09-15T01:00:00Z"));
        cost.Capitalize($"MOVE-{workOrderId}", 1m, 100m, DateTimeOffset.Parse("2026-09-15T01:30:00Z"));
        cost.RecordWipClearance(100m);
        dbContext.WorkOrderCosts.Add(cost);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        return await dbContext.WorkOrderCosts.Include(x => x.Details).SingleAsync(x => x.WorkOrderId == workOrderId);
    }

    private static StockMovementPostedIntegrationEvent FinishedGoodsReceipt(string movementId, string workOrderId)
    {
        var postedAtUtc = DateTimeOffset.Parse("2026-09-15T02:00:00Z");
        return new StockMovementPostedIntegrationEvent(
            $"evt-{movementId}", InventoryIntegrationEventTypes.StockMovementPosted, 1, postedAtUtc,
            InventoryIntegrationEventSources.BusinessInventory, workOrderId, workOrderId,
            OrganizationId, EnvironmentId, "inventory", $"idem-{movementId}",
            new StockMovementPostedPayload(
                movementId, "inbound", InventoryIntegrationEventSources.BusinessMes, $"FGR-{movementId}", workOrderId,
                $"mes:finished-goods-receipt:{movementId}", "FG-S7", "ea", "production", "fg-store", null, null,
                "unrestricted", "organization", OrganizationId, 4m, postedAtUtc, 20m, 80m));
    }

    private static async Task<PurchaseReceiptRecordedIntegrationEvent> SeedReceiptAsync(
        ApplicationDbContext dbContext,
        string purchaseOrderNo,
        string purchaseReceiptNo)
    {
        var order = PurchaseOrder.Create(
            OrganizationId, EnvironmentId, purchaseOrderNo, "SUP-001", "SITE-01", "CNY",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-001", "kg", 5m, 10m, new DateOnly(2026, 9, 1))]);
        order.MarkApprovalRequested($"approval-{purchaseOrderNo}");
        order.ReleaseAfterApproval($"approval-{purchaseOrderNo}");
        var receipt = PurchaseReceipt.Record(
            order,
            purchaseReceiptNo,
            [new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted", "LOC-01", null)],
            1m);
        dbContext.PurchaseOrders.Add(order);
        dbContext.PurchaseReceipts.Add(receipt);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
    }

    private sealed class AllocationNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
