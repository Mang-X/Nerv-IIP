using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesFinishedGoodsReceiptClosureTests
{
    [Fact]
    public async Task Create_finished_goods_receipt_request_rejects_cumulative_quantity_above_completed_quantity()
    {
        await using var dbContext = CreateDbContext(nameof(Create_finished_goods_receipt_request_rejects_cumulative_quantity_above_completed_quantity));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-697", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        workOrder.RecordProductionProgress(8m, 0m, now.AddMinutes(30));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
            "org-001",
            "env-dev",
            "WO-697",
            "OP-10",
            "PRPT-001",
            "LOT-FG-001",
            null,
            8m,
            now.AddMinutes(30)));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-EXISTING",
            "WO-697",
            "SKU-FG",
            5m,
            "PCS",
            now.AddMinutes(35),
            "LOT-FG-001",
            null,
            12.34m));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext, new MesCodingService()).Handle(
                new CreateFinishedGoodsReceiptRequestCommand(
                    "org-001",
                    "env-dev",
                    "WO-697",
                    "SKU-FG",
                    4m,
                    "PCS",
                    now.AddMinutes(40),
                    12.34m,
                    "receipt-over-completed",
                    "LOT-FG-001"),
                CancellationToken.None));

        Assert.Contains("累计完工入库申请数量超过工单完工数量", exception.Message);
    }

    // 证明「页面请求可以成功」：引用工单真实报工产出（OutputLotGenealogy 中的 producedLotNo）+ 数量在完工量内时创建成功并持久化该批次。
    [Fact]
    public async Task Create_finished_goods_receipt_request_succeeds_when_referencing_a_real_produced_lot_within_completed_quantity()
    {
        await using var dbContext = CreateDbContext(nameof(Create_finished_goods_receipt_request_succeeds_when_referencing_a_real_produced_lot_within_completed_quantity));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-698", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        workOrder.RecordProductionProgress(8m, 0m, now.AddMinutes(30));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
            "org-001",
            "env-dev",
            "WO-698",
            "OP-10",
            "PRPT-001",
            "LOT-FG-698",
            null,
            8m,
            now.AddMinutes(30)));
        await dbContext.SaveChangesAsync();

        var result = await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext, new MesCodingService()).Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-698",
                "SKU-FG",
                5m,
                "PCS",
                now.AddMinutes(40),
                12.34m,
                "receipt-real-lot",
                "LOT-FG-698"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(result.RequestNo));
        var persisted = await dbContext.FinishedGoodsReceiptRequests.SingleAsync(x => x.RequestNo == result.RequestNo);
        Assert.Equal("LOT-FG-698", persisted.ProducedLotNo);
        Assert.Equal("WO-698", persisted.WorkOrderId);
        Assert.Equal(5m, persisted.Quantity);
    }

    // 记录前置门禁：缺产出批次时，handler 在数量校验之前即拒绝——这正是页面必须携带真实 producedLotNo 才能到达超量校验的原因。
    [Fact]
    public async Task Create_finished_goods_receipt_request_rejects_missing_produced_lot_before_quantity_check()
    {
        await using var dbContext = CreateDbContext(nameof(Create_finished_goods_receipt_request_rejects_missing_produced_lot_before_quantity_check));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-699", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        workOrder.RecordProductionProgress(8m, 0m, now.AddMinutes(30));
        dbContext.WorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext, new MesCodingService()).Handle(
                new CreateFinishedGoodsReceiptRequestCommand(
                    "org-001",
                    "env-dev",
                    "WO-699",
                    "SKU-FG",
                    5m,
                    "PCS",
                    now.AddMinutes(40),
                    12.34m,
                    "receipt-missing-lot",
                    ProducedLotNo: null),
                CancellationToken.None));

        Assert.Contains("完工入库申请必须引用 MES 已生成的产出批次", exception.Message);
    }

    // 权威产出批次来源：查 OutputLotGenealogies（完工入库创建端点校验批次存在性的同一张表），按工单服务端过滤。
    [Fact]
    public async Task List_receivable_produced_lots_returns_live_genealogies_scoped_to_the_work_order()
    {
        await using var dbContext = CreateDbContext(nameof(List_receivable_produced_lots_returns_live_genealogies_scoped_to_the_work_order));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-700", "OP-10", "PRPT-1", "LOT-A", null, 6m, now));
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-700", "OP-20", "PRPT-2", "LOT-B", "SN-9", 4m, now.AddMinutes(5)));
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-OTHER", "OP-10", "PRPT-3", "LOT-X", null, 3m, now));
        await dbContext.SaveChangesAsync();

        var result = await new ListReceivableProducedLotsQueryHandler(dbContext).Handle(
            new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-700"), CancellationToken.None);

        Assert.Equal(new[] { "LOT-A", "LOT-B" }, result.Items.Select(x => x.ProducedLotNo).ToArray());
        var lotB = result.Items.Single(x => x.ProducedLotNo == "LOT-B");
        Assert.Equal("PRPT-2", lotB.ReportNo);
        Assert.Equal("OP-20", lotB.OperationTaskId);
        Assert.Equal(4m, lotB.Quantity);
        Assert.Equal("SN-9", lotB.SerialNo);
        Assert.DoesNotContain(result.Items, x => x.ProducedLotNo == "LOT-X");
    }

    // 报工冲销会删除对应 OutputLotGenealogy（ReverseProductionReportCommandHandler.RemoveRange）；因读面直接查该表，
    // 已冲销批次天然不再出现——杜绝旧方案里选中后端已判定不存在批次导致的“产出批次不存在”稳定失败。
    [Fact]
    public async Task List_receivable_produced_lots_excludes_a_reversed_lot_whose_genealogy_was_removed()
    {
        await using var dbContext = CreateDbContext(nameof(List_receivable_produced_lots_excludes_a_reversed_lot_whose_genealogy_was_removed));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var kept = OutputLotGenealogy.Create("org-001", "env-dev", "WO-701", "OP-10", "PRPT-1", "LOT-KEEP", null, 6m, now);
        var reversed = OutputLotGenealogy.Create("org-001", "env-dev", "WO-701", "OP-20", "PRPT-2", "LOT-REVERSED", null, 4m, now);
        dbContext.OutputLotGenealogies.AddRange(kept, reversed);
        await dbContext.SaveChangesAsync();

        var handler = new ListReceivableProducedLotsQueryHandler(dbContext);
        var before = await handler.Handle(new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-701"), CancellationToken.None);
        Assert.Equal(2, before.Items.Count);

        // 等价于冲销该报工对读面的影响：移除被冲销批次的 genealogy。
        dbContext.OutputLotGenealogies.Remove(reversed);
        await dbContext.SaveChangesAsync();

        var afterReversal = await handler.Handle(new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-701"), CancellationToken.None);
        Assert.Equal(new[] { "LOT-KEEP" }, afterReversal.Items.Select(x => x.ProducedLotNo).ToArray());
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
