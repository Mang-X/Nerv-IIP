using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

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

        var handler = new RecordProductionReportCommandHandler(dbContext);
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
}
