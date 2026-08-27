using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductionReportOeeDimensionSnapshotCommandTests
{
    // Contract: DomainInvariant + Regression. Authority: Issue #2602 acceptance.
    // Returning the request alias instead of the canonical GUID must fail this command-to-EF readback.
    [Fact]
    public async Task Record_command_persists_the_authoritative_event_time_dimension_snapshot()
    {
        await using var services = MesTestProvider.CreateInMemoryProvider();
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-27T23:30:00Z");
        using (var setupScope = services.CreateScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-OEE-001", "SKU-001", "PV-001", 10m, 10,
                reportedAtUtc.AddHours(8)));
            var task = OperationTask.Create(
                "org-001", "env-dev", "WO-OEE-001", "OP-OEE-10",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-LEGACY", [],
                reportedAtUtc.AddHours(-1), TimeSpan.FromHours(1),
                reportedAtUtc.AddHours(-1), null);
            task.Assign(null, "DEV-CNC-ALIAS", "EARLY", reportedAtUtc.AddHours(-2));
            task.ClearDomainEvents();
            dbContext.OperationTasks.Add(task);
            await dbContext.SaveChangesAsync();

            var snapshot = ProductionReportOeeDimensionSnapshot.Resolved(
                "019c9c62-9987-7af2-8fa2-3fd936098265",
                "WC-MACH",
                "SITE-SH",
                "WS-MACH",
                "LINE-CNC",
                "Asia/Shanghai",
                "EARLY",
                new TimeOnly(8, 0),
                new TimeOnly(16, 0),
                false,
                450,
                30);
            var handler = new RecordProductionReportCommandHandler(
                dbContext,
                null,
                new StubSnapshotProvider(snapshot));

            await handler.Handle(
                new RecordProductionReportCommand(
                    "org-001", "env-dev", "WO-OEE-001", "OP-OEE-10",
                    1m, 0m, false, reportedAtUtc, "oee-snapshot-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        using var assertionScope = services.CreateScope();
        var report = await assertionScope.ServiceProvider
            .GetRequiredService<Infrastructure.ApplicationDbContext>()
            .ProductionReports
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("resolved", report.OeeDimensionResolutionStatus);
        Assert.Null(report.OeeDimensionDegradedReason);
        Assert.Equal("019c9c62-9987-7af2-8fa2-3fd936098265", report.OeeDeviceAssetId);
        Assert.Equal("WC-MACH", report.OeeWorkCenterId);
        Assert.Equal("SITE-SH", report.OeeSiteCode);
        Assert.Equal("WS-MACH", report.OeeWorkshopCode);
        Assert.Equal("LINE-CNC", report.OeeLineCode);
        Assert.Equal("Asia/Shanghai", report.OeeSiteTimezone);
        Assert.Equal("EARLY", report.OeeShiftCode);
        Assert.Equal(new TimeOnly(8, 0), report.OeeShiftStartsAt);
        Assert.Equal(new TimeOnly(16, 0), report.OeeShiftEndsAt);
        Assert.False(report.OeeShiftCrossesMidnight);
        Assert.Equal(450, report.OeeShiftPaidMinutes);
        Assert.Equal(30, report.OeeShiftBreakMinutes);
    }

    [Fact]
    public void Reversal_reuses_the_original_dimension_snapshot_without_current_master_data()
    {
        var captured = ProductionReportOeeDimensionSnapshot.Resolved(
            "019c9c62-9987-7af2-8fa2-3fd936098265",
            "WC-MACH",
            "SITE-SH",
            "WS-MACH",
            "LINE-CNC",
            "Asia/Shanghai",
            "NIGHT",
            new TimeOnly(20, 0),
            new TimeOnly(4, 0),
            true,
            450,
            30);
        var original = ProductionReport.Record(
            "org-001", "env-dev", "PR-001", "WO-001", "OP-001",
            1m, 0m, false, DateTimeOffset.Parse("2026-08-27T20:30:00Z"),
            oeeProjection: new ProductionReportOeeProjection(
                "WC-LEGACY", "DEV-ALIAS", "PCS", 10m),
            oeeDimensionSnapshot: captured);

        var reversal = ProductionReport.Reverse(
            original,
            "PR-REV-001",
            DateTimeOffset.Parse("2026-08-28T01:00:00Z"),
            "更正",
            "user:operator-001");

        Assert.Equal(original.GetOeeDimensionSnapshot(), reversal.GetOeeDimensionSnapshot());
        Assert.Equal("019c9c62-9987-7af2-8fa2-3fd936098265", reversal.OeeDeviceAssetId);
        Assert.Equal("WC-MACH", reversal.OeeWorkCenterId);
    }

    private sealed class StubSnapshotProvider(ProductionReportOeeDimensionSnapshot snapshot)
        : IProductionReportOeeDimensionSnapshotProvider
    {
        public Task<ProductionReportOeeDimensionSnapshot> CaptureAsync(
            ProductionReportOeeDimensionSnapshotRequest request,
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}
