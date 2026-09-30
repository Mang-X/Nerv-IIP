using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Coding;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// #3918 审核：分号会当场提交「幂等键 → 号 + 载荷指纹」绑定。控制台弹窗在打开时生成幂等键，
/// 提交被拒后用户在同一弹窗改正再提交，键不变而载荷变了——若被拒的请求已经占号，
/// 改正后的重提会撞指纹冲突，且白白占掉一个号。所以本地业务校验必须先于分号。
/// </summary>
public sealed class WmsCreateNumberingOrderTests
{
    private const string IntentKey = "console-create-intent-001";

    [Fact]
    public async Task Rejected_putaway_neither_consumes_a_number_nor_blocks_the_corrected_retry_with_the_same_key()
    {
        await using var provider = CreateProvider();
        InboundOrderId inboundId;
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var inbound = InboundOrder.Create(
                "org-001",
                "env-dev",
                "IN-NUMBERING-001",
                "purchase-order",
                "PO-NUMBERING-001",
                "SITE-01",
                [new InboundOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 10m, "LINE-SIDE", "LOT-001", null, "qualified", "company", null)]);
            seedDb.InboundOrders.Add(inbound);
            await seedDb.SaveChangesAsync();
            inboundId = inbound.Id;
        }

        var coding = new WmsCodingService(provider.GetRequiredService<IServiceScopeFactory>());
        async Task<string> AttemptAsync(decimal quantity)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var taskId = await new CreatePutawayTaskCommandHandler(db, coding).Handle(
                new CreatePutawayTaskCommand(inboundId, null, "LINE-001", "RECEIVING", "LINE-SIDE", quantity, IntentKey),
                CancellationToken.None);
            await db.SaveChangesAsync();
            return db.WarehouseTasks.Local.Single(x => x.Id == taskId).TaskNo;
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AttemptAsync(99m));
        var taskNo = await AttemptAsync(10m);

        Assert.Matches(@"^PUT-\d{8}-000001$", taskNo);
    }

    [Fact]
    public async Task Rejected_picking_neither_consumes_a_number_nor_blocks_the_corrected_retry_with_the_same_key()
    {
        await using var provider = CreateProvider();
        OutboundOrderId outboundId;
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var outbound = OutboundOrder.Create(
                "org-001",
                "env-dev",
                "OUT-NUMBERING-001",
                "sales-delivery",
                "SO-NUMBERING-001",
                "SITE-01",
                [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", null, null, "qualified", "company", "owner-001")]);
            seedDb.OutboundOrders.Add(outbound);
            await seedDb.SaveChangesAsync();
            outboundId = outbound.Id;
        }

        var coding = new WmsCodingService(provider.GetRequiredService<IServiceScopeFactory>());
        async Task<string> AttemptAsync(decimal quantity)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var taskId = await new CreatePickingTaskCommandHandler(db, null, coding).Handle(
                new CreatePickingTaskCommand(outboundId, null, "LINE-001", "LOC-A-01", "PACK-01", quantity, IntentKey),
                CancellationToken.None);
            await db.SaveChangesAsync();
            return db.WarehouseTasks.Local.Single(x => x.Id == taskId).TaskNo;
        }

        var rejected = await Assert.ThrowsAsync<KnownException>(() => AttemptAsync(99m));
        var taskNo = await AttemptAsync(4m);

        Assert.Equal("拣货数量不能超过出库行数量。", rejected.Message);
        Assert.Matches(@"^PICK-\d{8}-000001$", taskNo);
    }

    /// <summary>
    /// 审核 B2：任务已建好，入库单随后被取消（不再允许新建上架任务），同键重试必须仍返回原任务，
    /// 而不是被业务校验拒绝——重放必须排在校验之前。
    /// </summary>
    [Fact]
    public async Task Putaway_retry_after_the_inbound_order_left_open_still_replays_the_original_task()
    {
        await using var provider = CreateProvider();
        var inboundId = await SeedInboundAsync(provider);
        var coding = new WmsCodingService(provider.GetRequiredService<IServiceScopeFactory>());
        var command = new CreatePutawayTaskCommand(inboundId, null, "LINE-001", "RECEIVING", "LINE-SIDE", 10m, IntentKey);
        var firstId = await HandlePutawayAsync(provider, coding, command);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.InboundOrders.SingleAsync()).Cancel("供应商取消到货");
            await db.SaveChangesAsync();
        }

        var retriedId = await HandlePutawayAsync(provider, coding, command);

        Assert.Equal(firstId, retriedId);
    }

    [Fact]
    public async Task Picking_retry_after_the_outbound_order_left_open_still_replays_the_original_task()
    {
        await using var provider = CreateProvider();
        var outboundId = await SeedOutboundAsync(provider);
        var coding = new WmsCodingService(provider.GetRequiredService<IServiceScopeFactory>());
        var command = new CreatePickingTaskCommand(outboundId, null, "LINE-001", "LOC-A-01", "PACK-01", 4m, IntentKey);
        var firstId = await HandlePickingAsync(provider, coding, command);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.OutboundOrders.Include(x => x.Lines).SingleAsync()).Cancel("客户撤单");
            await db.SaveChangesAsync();
        }

        var retriedId = await HandlePickingAsync(provider, coding, command);

        Assert.Equal(firstId, retriedId);
    }

    private static async Task<WarehouseTaskId> HandlePutawayAsync(
        ServiceProvider provider,
        WmsCodingService coding,
        CreatePutawayTaskCommand command)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var taskId = await new CreatePutawayTaskCommandHandler(db, coding).Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        return taskId;
    }

    private static async Task<WarehouseTaskId> HandlePickingAsync(
        ServiceProvider provider,
        WmsCodingService coding,
        CreatePickingTaskCommand command)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var taskId = await new CreatePickingTaskCommandHandler(db, null, coding).Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        return taskId;
    }

    private static async Task<InboundOrderId> SeedInboundAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-NUMBERING-001",
            "purchase-order",
            "PO-NUMBERING-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 10m, "LINE-SIDE", "LOT-001", null, "qualified", "company", null)]);
        db.InboundOrders.Add(inbound);
        await db.SaveChangesAsync();
        return inbound.Id;
    }

    private static async Task<OutboundOrderId> SeedOutboundAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-NUMBERING-001",
            "sales-delivery",
            "SO-NUMBERING-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", null, null, "qualified", "company", "owner-001")]);
        db.OutboundOrders.Add(outbound);
        await db.SaveChangesAsync();
        return outbound.Id;
    }

    private static ServiceProvider CreateProvider()
    {
        var databaseName = $"wms-create-numbering-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddScoped<MediatR.IMediator, NoopMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}
