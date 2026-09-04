using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesReworkReadContractTests
{
    [Fact]
    public async Task Execution_reads_expose_authoritative_rework_source_without_reclassifying_standard_orders()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var requestedAtUtc = DateTimeOffset.Parse("2026-08-29T08:00:00Z");
        var rework = WorkOrder.CreateRework(
            "org-001",
            "env-dev",
            "WO-RW-001",
            "SKU-001",
            "PV-001",
            "PCS",
            3m,
            100,
            requestedAtUtc.AddDays(1),
            "WO-SOURCE-001",
            "OP-SOURCE-10",
            "DEF-001",
            "ncr-001",
            "NCR-2026-0001",
            "LOT-001",
            "SN-001",
            requestedAtUtc,
            "corr-001",
            "evt-rework-requested-001");
        var reworkTask = Assert.Single(rework.Release(
            requestedAtUtc,
            [new RoutingStepSnapshot("OP-RW", 10, "WC-001", [], TimeSpan.FromMinutes(30))]));
        reworkTask.Start(requestedAtUtc.AddMinutes(1));
        var standard = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STANDARD-001",
            "SKU-001",
            "PV-001",
            5m,
            10,
            requestedAtUtc.AddDays(1));
        var standardTask = Assert.Single(standard.Release(
            requestedAtUtc,
            [new RoutingStepSnapshot("OP-STANDARD", 10, "WC-001", [], TimeSpan.FromMinutes(30))]));
        db.WorkOrders.AddRange(rework, standard);
        db.OperationTasks.AddRange(reworkTask, standardTask);
        await db.SaveChangesAsync();

        var workOrders = await new ListMesWorkOrdersQueryHandler(db).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null),
            CancellationToken.None);
        AssertAuthority(workOrders.Items.Single(x => x.WorkOrderId == rework.WorkOrderIdValue));
        var standardRow = workOrders.Items.Single(x => x.WorkOrderId == standard.WorkOrderIdValue);
        Assert.Equal(WorkOrder.StandardType, standardRow.WorkOrderType);
        Assert.Null(standardRow.SourceWorkOrderId);
        Assert.Null(standardRow.SourceNcrId);
        Assert.Null(standardRow.SourceNcrCode);

        var detail = await new GetMesWorkOrderDetailQueryHandler(db).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", rework.WorkOrderIdValue),
            CancellationToken.None);
        AssertAuthority(detail);
        AssertAuthority(Assert.Single(detail.OperationTasks));

        var operations = await new ListOperationTasksQueryHandler(db).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                WorkOrderId: rework.WorkOrderIdValue),
            CancellationToken.None);
        AssertAuthority(Assert.Single(operations.Items));

        var reportable = await new ListReportableOperationTasksQueryHandler(db).Handle(
            new ListReportableOperationTasksQuery(
                "org-001",
                "env-dev",
                WorkOrderId: rework.WorkOrderIdValue),
            CancellationToken.None);
        AssertAuthority(Assert.Single(reportable.Items));
    }

    private static void AssertAuthority(MesWorkOrderExecutionFact item) => AssertAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertAuthority(MesWorkOrderDetailResponse item) => AssertAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertAuthority(MesOperationTaskRow item) => AssertAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertAuthority(
        string workOrderType,
        string? sourceWorkOrderId,
        string? sourceNcrId,
        string? sourceNcrCode)
    {
        Assert.Equal(WorkOrder.ReworkType, workOrderType);
        Assert.Equal("WO-SOURCE-001", sourceWorkOrderId);
        Assert.Equal("ncr-001", sourceNcrId);
        Assert.Equal("NCR-2026-0001", sourceNcrCode);
    }
}
