using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
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
