using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductionReportOeeProjectionFactoryTests
{
    [Fact]
    public void Create_uses_the_event_time_work_center_snapshot_with_its_hierarchy()
    {
        var task = CreateTask("WC-A");
        var snapshot = new MesOeeDimensionSnapshot(
            "WC-B",
            "DEV-01",
            "SITE-B",
            "WORKSHOP-B",
            "LINE-B",
            "SHIFT-B");

        var projection = ProductionReportOeeProjectionFactory.Create(task, snapshot);

        Assert.Equal("WC-B", projection.WorkCenterId);
        Assert.Equal("SITE-B", projection.SiteCode);
        Assert.Equal("WORKSHOP-B", projection.WorkshopCode);
        Assert.Equal("LINE-B", projection.LineCode);
    }

    [Fact]
    public void Create_does_not_mix_the_task_work_center_with_hierarchy_from_an_invalid_snapshot()
    {
        var task = CreateTask("WC-A");
        var snapshot = new MesOeeDimensionSnapshot(
            " ",
            "DEV-01",
            "SITE-B",
            "WORKSHOP-B",
            "LINE-B",
            "SHIFT-B");

        var projection = ProductionReportOeeProjectionFactory.Create(task, snapshot);

        Assert.Equal("WC-A", projection.WorkCenterId);
        Assert.Null(projection.SiteCode);
        Assert.Null(projection.WorkshopCode);
        Assert.Null(projection.LineCode);
    }

    private static OperationTask CreateTask(string workCenterId) => OperationTask.Queue(
        "org-001",
        "env-dev",
        "WO-001",
        "OP-10",
        10,
        workCenterId,
        [],
        DateTimeOffset.Parse("2026-07-10T08:00:00Z"),
        TimeSpan.FromHours(2),
        uomCode: "PCS",
        plannedQuantity: 20m);
}
