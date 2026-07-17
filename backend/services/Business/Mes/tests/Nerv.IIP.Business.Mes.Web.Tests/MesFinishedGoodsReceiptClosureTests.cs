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

    // 批次追溯完整性：即使未超工单完工数量，单个产出批次的累计入库申请也不得超过该批次产量。
    [Fact]
    public async Task Create_finished_goods_receipt_request_rejects_quantity_above_the_referenced_produced_lot()
    {
        await using var dbContext = CreateDbContext(nameof(Create_finished_goods_receipt_request_rejects_quantity_above_the_referenced_produced_lot));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-702", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(now);
        workOrder.RecordProductionProgress(10m, 0m, now.AddMinutes(30));
        dbContext.WorkOrders.Add(workOrder);
        // 工单完工 10，但 LOT-A 仅产出 1、LOT-B 产出 9。
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-702", "OP-10", "PRPT-A", "LOT-A", null, 1m, now));
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-702", "OP-10", "PRPT-B", "LOT-B", null, 9m, now));
        // LOT-A 已登记 1（用满批次产量）。
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
            "org-001", "env-dev", "FGR-A1", "WO-702", "SKU-FG", 1m, "PCS", now.AddMinutes(35), "LOT-A", null, 12.34m));
        await dbContext.SaveChangesAsync();

        // 再对 LOT-A 登记 1（工单总量 2 ≤ 10，但批次累计 2 > 批次产量 1）→ 应被批次级校验拒绝。
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext, new MesCodingService()).Handle(
                new CreateFinishedGoodsReceiptRequestCommand(
                    "org-001", "env-dev", "WO-702", "SKU-FG", 1m, "PCS", now.AddMinutes(40), 12.34m,
                    "receipt-batch-over", "LOT-A"),
                CancellationToken.None));

        Assert.Contains("超过该产出批次可入库数量", exception.Message);
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
        // 无入库申请时剩余可入库量 = 批次产量。
        Assert.Equal(4m, lotB.RemainingQuantity);
        Assert.DoesNotContain(result.Items, x => x.ProducedLotNo == "LOT-X");
    }

    // 剩余可入库量 = 批次产量 − 非取消入库申请累计；已耗尽批次不出现在读面（否则页面选中后提交必然失败）。
    [Fact]
    public async Task List_receivable_produced_lots_reports_remaining_quantity_and_excludes_exhausted_lots()
    {
        await using var dbContext = CreateDbContext(nameof(List_receivable_produced_lots_reports_remaining_quantity_and_excludes_exhausted_lots));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-703", "OP-10", "PRPT-A", "LOT-A", null, 5m, now));
        dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create("org-001", "env-dev", "WO-703", "OP-20", "PRPT-B", "LOT-B", null, 5m, now));
        // LOT-A 已全额入库（耗尽）；LOT-B 部分入库 2（剩余 3）。取消申请不计入。
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-A5", "WO-703", "SKU-FG", 5m, "PCS", now.AddMinutes(10), "LOT-A", null, 1m));
        dbContext.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-B2", "WO-703", "SKU-FG", 2m, "PCS", now.AddMinutes(11), "LOT-B", null, 1m));
        var cancelled = FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-BX", "WO-703", "SKU-FG", 3m, "PCS", now.AddMinutes(12), "LOT-B", null, 1m);
        cancelled.Cancel();
        dbContext.FinishedGoodsReceiptRequests.Add(cancelled);
        await dbContext.SaveChangesAsync();

        var result = await new ListReceivableProducedLotsQueryHandler(dbContext).Handle(
            new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-703"), CancellationToken.None);

        // LOT-A 耗尽被排除，仅剩 LOT-B 剩余 3（5 − 2，取消的 3 不计）。
        var only = Assert.Single(result.Items);
        Assert.Equal("LOT-B", only.ProducedLotNo);
        Assert.Equal(3m, only.RemainingQuantity);
        Assert.Equal(5m, only.Quantity);
    }

    // 读面直接查 OutputLotGenealogies：genealogy 行被删除后即从结果消失。报工冲销正是通过
    // ReverseProductionReportCommandHandler.RemoveRange 删除原报工的 genealogy（该行为由冲销 handler 自身测试覆盖），
    // 因此已冲销批次天然不再出现——本用例只断言读面对 genealogy 删除的反映，不重复跑冲销链路。
    [Fact]
    public async Task List_receivable_produced_lots_reflects_genealogy_removal_so_removed_lots_disappear()
    {
        await using var dbContext = CreateDbContext(nameof(List_receivable_produced_lots_reflects_genealogy_removal_so_removed_lots_disappear));
        var now = DateTimeOffset.Parse("2026-07-04T08:00:00Z");
        var kept = OutputLotGenealogy.Create("org-001", "env-dev", "WO-701", "OP-10", "PRPT-1", "LOT-KEEP", null, 6m, now);
        var removed = OutputLotGenealogy.Create("org-001", "env-dev", "WO-701", "OP-20", "PRPT-2", "LOT-REMOVED", null, 4m, now);
        dbContext.OutputLotGenealogies.AddRange(kept, removed);
        await dbContext.SaveChangesAsync();

        var handler = new ListReceivableProducedLotsQueryHandler(dbContext);
        var before = await handler.Handle(new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-701"), CancellationToken.None);
        Assert.Equal(2, before.Items.Count);

        // 冲销 handler 对读面的等价效果：删除被冲销批次的 genealogy。
        dbContext.OutputLotGenealogies.Remove(removed);
        await dbContext.SaveChangesAsync();

        var after = await handler.Handle(new ListReceivableProducedLotsQuery("org-001", "env-dev", "WO-701"), CancellationToken.None);
        Assert.Equal(new[] { "LOT-KEEP" }, after.Items.Select(x => x.ProducedLotNo).ToArray());
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
