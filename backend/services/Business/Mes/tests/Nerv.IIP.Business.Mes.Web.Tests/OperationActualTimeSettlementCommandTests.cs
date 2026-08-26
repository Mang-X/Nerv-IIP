using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class OperationActualTimeSettlementCommandTests
{
    [Fact]
    public async Task Completion_settlement_covers_every_persisted_report_and_the_completing_report()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10m, 1,
            startedAtUtc.AddHours(8));
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-001",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-001", [], startedAtUtc,
            TimeSpan.FromHours(1), startedAtUtc, null);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var handler = new RecordProductionReportCommandHandler(
            dbContext,
            new NullMesOeeDimensionSnapshotProvider());
        var staged = await handler.Handle(
            new RecordProductionReportCommand(
                "org-001", "env-dev", "WO-001", "OP-001", 4m, 0m, false,
                startedAtUtc.AddMinutes(20), "report-stage-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        task.ClearDomainEvents();

        var completing = await handler.Handle(
            new RecordProductionReportCommand(
                "org-001", "env-dev", "WO-001", "OP-001", 6m, 0m, true,
                startedAtUtc.AddHours(1), "report-complete-001"),
            CancellationToken.None);

        var settled = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Equal(
            new[] { staged.ReportNo, completing.ReportNo }.Order(StringComparer.Ordinal),
            settled.Settlement.CoveredProductionReportNos);
    }

    [Fact]
    public async Task Workbench_completion_uses_the_same_coordinator_and_covers_existing_reports()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10m, 1,
            startedAtUtc.AddHours(8)));
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-001",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-001", [], startedAtUtc,
            TimeSpan.FromHours(1), startedAtUtc, null);
        dbContext.OperationTasks.Add(task);
        var report = ProductionReport.Record(
            "org-001", "env-dev", "PR-001", "WO-001", "OP-001",
            4m, 0m, false, startedAtUtc.AddMinutes(20));
        dbContext.ProductionReports.Add(report);
        await dbContext.SaveChangesAsync();
        task.ClearDomainEvents();
        report.ClearDomainEvents();

        await new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
            new ChangeOperationTaskStateCommand(
                "org-001", "env-dev", "OP-001", "complete", startedAtUtc.AddHours(1)),
            CancellationToken.None);

        var settled = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Equal(["PR-001"], settled.Settlement.CoveredProductionReportNos);
    }

    [Fact]
    public async Task Workbench_completion_without_reports_freezes_an_authoritative_empty_snapshot()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-26T01:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10m, 1,
            startedAtUtc.AddHours(8)));
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-001",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-001", [], startedAtUtc,
            TimeSpan.FromHours(1), startedAtUtc, null);
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();
        task.ClearDomainEvents();

        await new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
            new ChangeOperationTaskStateCommand(
                "org-001", "env-dev", "OP-001", "complete", startedAtUtc.AddHours(1)),
            CancellationToken.None);

        Assert.Equal(OperationTaskLifecycleStatus.Completed, task.Status);
        var settled = Assert.Single(task.GetDomainEvents().OfType<OperationActualTimeSettledDomainEvent>());
        Assert.Empty(settled.Settlement.CoveredProductionReportNos);
        Assert.Empty(Assert.Single(dbContext.OperationActualTimeSettlements.Local).CoveredReports);
    }
}
