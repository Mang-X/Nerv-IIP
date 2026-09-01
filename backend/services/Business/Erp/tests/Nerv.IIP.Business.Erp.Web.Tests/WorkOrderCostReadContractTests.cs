using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WorkOrderCostReadContractTests
{
    private static readonly DateTimeOffset CompletedAtUtc =
        DateTimeOffset.Parse("2026-08-31T15:00:00Z");

    [Fact]
    public async Task Machine_overhead_read_returns_applied_amounts_three_states_and_frozen_settlement_lineage()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var applicableRateId = new WorkCenterMachineOverheadRateId(Guid.CreateVersion7());
        var notApplicableRateId = new WorkCenterMachineOverheadRateId(Guid.CreateVersion7());
        var applicable = OperationMachineOverheadSettlement.CreateApplied(
            "org-machine", "env-machine", "WO-MACHINE", "OP-APPLIED", "WC-APPLIED", 2,
            CompletedAtUtc, "DEVICE-001", 2 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", applicableRateId,
            "2026-08", 7, "CNY", 30m, 10m, "evt-machine-applied", new string('a', 64));
        var explicitZero = OperationMachineOverheadSettlement.CreateApplied(
            "org-machine", "env-machine", "WO-MACHINE", "OP-ZERO", "WC-ZERO", 1,
            CompletedAtUtc, "DEVICE-ZERO", 0,
            "single-device-active-minus-explicit-pause-v1", applicableRateId,
            "2026-08", 7, "CNY", 30m, 10m, "evt-machine-zero", new string('b', 64));
        var notApplicable = OperationMachineOverheadSettlement.CreateNotApplicable(
            "org-machine", "env-machine", "WO-MACHINE", "OP-NOT-APPLICABLE", "WC-MANUAL", 1,
            CompletedAtUtc, notApplicableRateId, "2026-08", 3, "CNY",
            "evt-machine-not-applicable", new string('c', 64));
        var inactive = OperationMachineOverheadSettlement.CreateApplied(
            "org-machine", "env-machine", "WO-MACHINE", "OP-APPLIED", "WC-APPLIED", 1,
            CompletedAtUtc.AddHours(-1), "DEVICE-OLD", 9 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", applicableRateId,
            "2026-07", 6, "CNY", 30m, 10m, "evt-machine-old", new string('d', 64));
        var appliedState = OperationMachineOverheadSettlementState.Open("org-machine", "env-machine", "OP-APPLIED");
        appliedState.ApplySettlement(1);
        appliedState.ApplySettlement(2);
        var zeroState = OperationMachineOverheadSettlementState.Open("org-machine", "env-machine", "OP-ZERO");
        zeroState.ApplySettlement(1);
        var notApplicableState = OperationMachineOverheadSettlementState.Open(
            "org-machine", "env-machine", "OP-NOT-APPLICABLE");
        notApplicableState.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-machine", "env-machine", "WO-MACHINE", "FG-MACHINE");
        cost.RecordMachineOverhead(applicable);
        cost.RecordMachineOverhead(explicitZero);
        cost.RecordMachineOverhead(notApplicable);
        db.AddRange(cost, applicable, explicitZero, notApplicable, inactive,
            appliedState, zeroState, notApplicableState);
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new("org-machine", "env-machine", "WO-MACHINE"), CancellationToken.None);

        Assert.Equal(MachineOverheadReadStatus.Available, response.MachineCostStatus);
        Assert.Null(response.MachineCostUnavailableReason);
        Assert.Null(response.CurrencyCode);
        Assert.Equal("CNY", response.MachineCurrencyCode);
        Assert.Equal(2.000000m, response.ActualMachineHours);
        Assert.Equal(60.000000m, response.AppliedFixedMachineOverhead);
        Assert.Equal(20.000000m, response.AppliedVariableMachineOverhead);
        Assert.Equal(80.000000m, response.AppliedMachineOverheadTotal);
        Assert.Equal(3, response.MachineOverheadOperations.Count);
        Assert.Equal(1, response.MachineOverheadPageNumber);
        Assert.Equal(50, response.MachineOverheadPageSize);
        Assert.Equal(3, response.TotalMachineOverheadOperations);
        var operations = response.MachineOverheadOperations.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        var applied = operations["OP-APPLIED"];
        Assert.Equal(applicable.Id.ToString(), applied.SettlementId);
        Assert.Equal(2, applied.SettlementRevision);
        Assert.Equal("2026-08", applied.AccountingPeriodCode);
        Assert.Equal("CNY", applied.CurrencyCode);
        Assert.Equal(applicableRateId.ToString(), applied.WorkCenterMachineOverheadRateId);
        Assert.Equal(7, applied.RateRevision);
        Assert.Equal("evt-machine-applied", applied.SourceEventId);
        Assert.Equal(CompletedAtUtc, applied.CompletedAtUtc);
        Assert.Equal(0m, operations["OP-ZERO"].AppliedMachineOverheadTotal);
        Assert.Equal(MachineOverheadReadStatus.Available, operations["OP-ZERO"].Status);
        Assert.Equal(MachineOverheadReadStatus.NotApplicable, operations["OP-NOT-APPLICABLE"].Status);
        Assert.Equal("machine_overhead_not_applicable", operations["OP-NOT-APPLICABLE"].UnavailableReason);
        Assert.Null(operations["OP-NOT-APPLICABLE"].ActualMachineHours);
        Assert.Null(operations["OP-NOT-APPLICABLE"].AppliedMachineOverheadTotal);

        var secondPage = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new("org-machine", "env-machine", "WO-MACHINE", PageNumber: 2, PageSize: 1),
            CancellationToken.None);
        Assert.Equal(2, secondPage.MachineOverheadPageNumber);
        Assert.Equal(1, secondPage.MachineOverheadPageSize);
        Assert.Equal(3, secondPage.TotalMachineOverheadOperations);
        Assert.Equal("OP-NOT-APPLICABLE", Assert.Single(secondPage.MachineOverheadOperations).OperationTaskId);
    }

    [Fact]
    public async Task Work_order_with_only_not_applicable_machine_settlement_does_not_map_to_zero()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationMachineOverheadSettlement.CreateNotApplicable(
            "org-na", "env-na", "WO-NA", "OP-NA", "WC-MANUAL", 1, CompletedAtUtc,
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), "2026-08", 1, "CNY",
            "evt-na", new string('e', 64));
        var state = OperationMachineOverheadSettlementState.Open("org-na", "env-na", "OP-NA");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-na", "env-na", "WO-NA", "FG-NA");
        cost.RecordMachineOverhead(settlement);
        db.AddRange(cost, settlement, state);
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new("org-na", "env-na", "WO-NA"), CancellationToken.None);

        Assert.Equal(MachineOverheadReadStatus.NotApplicable, response.MachineCostStatus);
        Assert.Equal("machine_overhead_not_applicable", response.MachineCostUnavailableReason);
        Assert.Null(response.MachineCurrencyCode);
        Assert.Null(response.ActualMachineHours);
        Assert.Null(response.AppliedFixedMachineOverhead);
        Assert.Null(response.AppliedVariableMachineOverhead);
        Assert.Null(response.AppliedMachineOverheadTotal);
    }

    [Fact]
    public async Task Labor_and_machine_aggregates_keep_their_own_currency_codes()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var labor = OperationLaborSettlement.Create(
            "org-currency", "env-currency", "WO-CURRENCY", "OP-LABOR", "WC-LABOR", 1,
            CompletedAtUtc, TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "USD", 20m, "evt-labor-currency", "hash-labor-currency");
        var laborState = OperationLaborSettlementState.Open("org-currency", "env-currency", "OP-LABOR");
        laborState.ApplySettlement(1);
        var machine = OperationMachineOverheadSettlement.CreateApplied(
            "org-currency", "env-currency", "WO-CURRENCY", "OP-MACHINE", "WC-MACHINE", 1,
            CompletedAtUtc, "DEVICE-CURRENCY", TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1",
            new WorkCenterMachineOverheadRateId(Guid.CreateVersion7()), "2026-08", 1,
            "CNY", 30m, 10m, "evt-machine-currency", new string('f', 64));
        var machineState = OperationMachineOverheadSettlementState.Open(
            "org-currency", "env-currency", "OP-MACHINE");
        machineState.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-currency", "env-currency", "WO-CURRENCY", "FG-CURRENCY");
        cost.RecordActualLabor(labor);
        db.AddRange(cost, labor, laborState, machine, machineState);
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new("org-currency", "env-currency", "WO-CURRENCY"), CancellationToken.None);

        Assert.Equal("USD", response.CurrencyCode);
        Assert.Equal("CNY", response.MachineCurrencyCode);
        Assert.Equal(MachineOverheadReadStatus.Available, response.MachineCostStatus);
        Assert.Equal(40m, response.AppliedMachineOverheadTotal);
    }

    [Fact]
    public async Task Work_order_cost_read_uses_net_good_quantity_and_keeps_capitalization_variance_separate()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rate = WorkCenterCostRate.Define(
            "org-001", "env-prod", "WC-01", 60m, "CNY",
            CompletedAtUtc.AddMonths(-1), null, 7,
            "auditor:test", "approved standard rate", CompletedAtUtc.AddMonths(-1));
        var settlement = OperationLaborSettlement.Create(
            "org-001", "env-prod", "WO-001", "OP-001", "WC-01", 1,
            CompletedAtUtc, 5 * TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()), 7, "CNY", 60m,
            "evt-settled", "hash-settled");
        var state = OperationLaborSettlementState.Open("org-001", "env-prod", "OP-001");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-001", "env-prod", "WO-001", "FG-001");
        cost.RecordActualLabor(settlement);
        cost.RecordMaterial("MOVE-001", "RPT-001", "RM-001", 2m, 25m, CompletedAtUtc);
        cost.Capitalize("FG-MOVE-001", 10m, 32m, CompletedAtUtc);
        cost.Complete(10m, 1, 1, CompletedAtUtc.AddMinutes(1));
        db.AddRange(rate, settlement, state, cost,
            OperationLaborCoveredReport.Create("org-001", "env-prod", "WO-001", "OP-001", 1, "RPT-001"),
            OperationLaborReportSnapshot.Create(
                "org-001", "env-prod", "WO-001", "OP-001", "WC-01", "RPT-001",
                8m, 2m, 3m, "ea", 2m, CompletedAtUtc.AddMinutes(-5), false, null, "evt-report"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-001", "env-prod", "WO-001", 1, 50),
            CancellationToken.None);

        Assert.Equal("available", response.LaborVarianceStatus);
        Assert.Null(response.UnavailableReason);
        Assert.Equal(5.000000m, response.ActualLaborHours);
        Assert.Equal(300.000000m, response.ActualLaborCost);
        Assert.Equal(4.000000m, response.StandardLaborHours);
        Assert.Equal(240.000000m, response.StandardLaborCost);
        Assert.Equal(1.000000m, response.LaborEfficiencyVarianceHours);
        Assert.Equal(60.000000m, response.LaborEfficiencyVarianceAmount);
        Assert.Equal("unfavorable", response.LaborEfficiencyVarianceDirection);
        Assert.Equal(50.000000m, response.MaterialCost);
        Assert.Equal(350.000000m, response.TotalAccumulatedCost);
        Assert.Equal(320.000000m, response.CapitalizedCost);
        Assert.Equal(30.000000m, response.CapitalizationVarianceAmount);
        Assert.Equal("notApplicable", response.LaborRateVarianceStatus);
        Assert.Equal("actual_payroll_rate_not_modeled", response.LaborRateVarianceReason);
        Assert.Equal(MachineOverheadReadStatus.Unavailable, response.MachineCostStatus);
        Assert.Equal("operation_not_settled", response.MachineCostUnavailableReason);
        Assert.Null(response.AppliedMachineOverheadTotal);
        var operation = Assert.Single(response.Operations);
        Assert.Equal(7, operation.RateRevision);
        Assert.Equal("standard", operation.RateBasis);
        Assert.Equal(new[] { "RPT-001" }, operation.CoveredReports.Select(x => x.ReportNo));
    }

    [Fact]
    public async Task Missing_frozen_report_snapshot_is_unavailable_instead_of_zero()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rate = WorkCenterCostRate.Define(
            "org-001", "env-prod", "WC-01", 60m, "CNY",
            CompletedAtUtc.AddMonths(-1), null, 1,
            "auditor:test", "approved standard rate", CompletedAtUtc.AddMonths(-1));
        var settlement = OperationLaborSettlement.Create(
            "org-001", "env-prod", "WO-HIST", "OP-HIST", "WC-01", 1,
            CompletedAtUtc, TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()), 1, "CNY", 60m,
            "evt-hist", "hash-hist");
        var state = OperationLaborSettlementState.Open("org-001", "env-prod", "OP-HIST");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-001", "env-prod", "WO-HIST", "FG-HIST");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(rate, settlement, state, cost,
            OperationLaborCoveredReport.Create("org-001", "env-prod", "WO-HIST", "OP-HIST", 1, "RPT-MISSING"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-001", "env-prod", "WO-HIST", 1, 50),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("missing_report_snapshot", response.UnavailableReason);
        Assert.Equal(1.000000m, response.ActualLaborHours);
        Assert.Equal(60.000000m, response.ActualLaborCost);
        Assert.Null(response.StandardLaborHours);
        Assert.Null(response.LaborEfficiencyVarianceAmount);
    }

    [Fact]
    public async Task Read_is_environment_isolated_and_uses_only_the_active_settlement_revision()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldSettlement = OperationLaborSettlement.Create(
            "org-001", "env-prod", "WO-REV", "OP-REV", "WC-01", 1,
            CompletedAtUtc.AddHours(-1), TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-old", "hash-old");
        var activeSettlement = OperationLaborSettlement.Create(
            "org-001", "env-prod", "WO-REV", "OP-REV", "WC-01", 2,
            CompletedAtUtc, 3 * TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            2, "CNY", 60m, "evt-active", "hash-active");
        var state = OperationLaborSettlementState.Open("org-001", "env-prod", "OP-REV");
        state.ApplySettlement(1);
        state.ApplySettlement(2);
        var prodCost = WorkOrderCost.Open("org-001", "env-prod", "WO-REV", "FG-PROD");
        prodCost.RecordActualLabor(activeSettlement);
        prodCost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));

        var otherSettlement = OperationLaborSettlement.Create(
            "org-001", "env-test", "WO-REV", "OP-REV", "WC-01", 1,
            CompletedAtUtc, 9 * TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-other", "hash-other");
        var otherState = OperationLaborSettlementState.Open("org-001", "env-test", "OP-REV");
        otherState.ApplySettlement(1);
        var otherCost = WorkOrderCost.Open("org-001", "env-test", "WO-REV", "FG-TEST");
        otherCost.RecordActualLabor(otherSettlement);
        otherCost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));

        db.AddRange(oldSettlement, activeSettlement, state, prodCost,
            OperationLaborCoveredReport.Create("org-001", "env-prod", "WO-REV", "OP-REV", 1, "RPT-OLD"),
            OperationLaborCoveredReport.Create("org-001", "env-prod", "WO-REV", "OP-REV", 2, "RPT-ACTIVE"),
            OperationLaborReportSnapshot.Create(
                "org-001", "env-prod", "WO-REV", "OP-REV", "WC-01", "RPT-OLD",
                2m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-10), false, null, "evt-report-old"),
            OperationLaborReportSnapshot.Create(
                "org-001", "env-prod", "WO-REV", "OP-REV", "WC-01", "RPT-ACTIVE",
                4m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-5), false, null, "evt-report-active"),
            otherSettlement, otherState, otherCost,
            OperationLaborCoveredReport.Create("org-001", "env-test", "WO-REV", "OP-REV", 1, "RPT-OTHER"),
            OperationLaborReportSnapshot.Create(
                "org-001", "env-test", "WO-REV", "OP-REV", "WC-01", "RPT-OTHER",
                18m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-5), false, null, "evt-report-other"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-001", "env-prod", "WO-REV", 1, 50),
            CancellationToken.None);

        var operation = Assert.Single(response.Operations);
        Assert.Equal(2, operation.SettlementRevision);
        Assert.Equal(3.000000m, response.ActualLaborHours);
        Assert.Equal(2.000000m, response.StandardLaborHours);
        Assert.Equal(new[] { "RPT-ACTIVE" }, operation.CoveredReports.Select(x => x.ReportNo));
    }

    [Fact]
    public async Task Read_covers_zero_overproduction_reversal_from_original_snapshot_reopen_and_pagination_vectors()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cost = WorkOrderCost.Open("org-vectors", "env-vectors", "WO-VECTORS", "FG-VECTORS");

        OperationLaborSettlement Settlement(string operation, long revision, long ticks, string eventId)
            => OperationLaborSettlement.Create(
                "org-vectors", "env-vectors", "WO-VECTORS", operation, "WC-VECTORS", revision,
                CompletedAtUtc, ticks, new WorkCenterCostRateId(Guid.CreateVersion7()),
                1, "CNY", 60m, eventId, $"hash-{eventId}");
        OperationLaborSettlementState ActiveState(string operation, long revision, bool reopen = false)
        {
            var state = OperationLaborSettlementState.Open("org-vectors", "env-vectors", operation);
            if (reopen)
            {
                state.ApplySettlement(1);
                state.ApplyVoid(1);
            }
            state.ApplySettlement(revision);
            return state;
        }
        OperationLaborReportSnapshot Snapshot(
            string operation, string report, decimal good, bool reversal = false, string? reversedReport = null)
            => OperationLaborReportSnapshot.Create(
                "org-vectors", "env-vectors", "WO-VECTORS", operation, "WC-VECTORS", report,
                good, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-5), reversal, reversedReport,
                $"evt-{report}");
        OperationLaborCoveredReport Covered(string operation, long revision, string report)
            => OperationLaborCoveredReport.Create(
                "org-vectors", "env-vectors", "WO-VECTORS", operation, revision, report);

        var zero = Settlement("OP-ZERO", 1, 0, "evt-zero");
        var over = Settlement("OP-OVER", 1, 5 * TimeSpan.TicksPerHour, "evt-over");
        var reversal = Settlement("OP-REVERSAL", 1, TimeSpan.TicksPerHour, "evt-reversal");
        var reopenedOld = Settlement("OP-REOPEN", 1, TimeSpan.TicksPerHour, "evt-reopen-old");
        var reopened = Settlement("OP-REOPEN", 2, 2 * TimeSpan.TicksPerHour, "evt-reopen-active");
        cost.RecordActualLabor(zero);
        cost.RecordActualLabor(over);
        cost.RecordActualLabor(reversal);
        cost.RecordActualLabor(reopened);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));

        db.AddRange(cost,
            zero, ActiveState("OP-ZERO", 1), Covered("OP-ZERO", 1, "RPT-ZERO"), Snapshot("OP-ZERO", "RPT-ZERO", 0m),
            over, ActiveState("OP-OVER", 1), Covered("OP-OVER", 1, "RPT-OVER"), Snapshot("OP-OVER", "RPT-OVER", 12m),
            reversal, ActiveState("OP-REVERSAL", 1),
            Covered("OP-REVERSAL", 1, "RPT-REVERSAL-ORIGINAL"),
            Covered("OP-REVERSAL", 1, "RPT-REVERSAL-VOID"),
            Snapshot("OP-REVERSAL", "RPT-REVERSAL-ORIGINAL", 8m),
            Snapshot("OP-REVERSAL", "RPT-REVERSAL-VOID", 3m, true, "RPT-REVERSAL-ORIGINAL"),
            reopenedOld, reopened, ActiveState("OP-REOPEN", 2, reopen: true),
            Covered("OP-REOPEN", 1, "RPT-REOPEN-OLD"),
            Covered("OP-REOPEN", 2, "RPT-REOPEN-ACTIVE"),
            Snapshot("OP-REOPEN", "RPT-REOPEN-OLD", 2m),
            Snapshot("OP-REOPEN", "RPT-REOPEN-ACTIVE", 4m));
        await db.SaveChangesAsync();

        var handler = new GetWorkOrderCostVarianceQueryHandler(db);
        var response = await handler.Handle(
            new GetWorkOrderCostVarianceQuery("org-vectors", "env-vectors", "WO-VECTORS", 1, 100),
            CancellationToken.None);

        Assert.Equal("available", response.LaborVarianceStatus);
        Assert.Equal(4, response.TotalOperations);
        var operations = response.Operations.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        Assert.Equal((0m, 0m, "neutral"), (
            operations["OP-ZERO"].StandardLaborHours!.Value,
            operations["OP-ZERO"].LaborEfficiencyVarianceHours!.Value,
            operations["OP-ZERO"].LaborEfficiencyVarianceDirection!));
        Assert.Equal((6m, -1m, "favorable"), (
            operations["OP-OVER"].StandardLaborHours!.Value,
            operations["OP-OVER"].LaborEfficiencyVarianceHours!.Value,
            operations["OP-OVER"].LaborEfficiencyVarianceDirection!));
        Assert.Equal((0m, 1m, "unfavorable"), (
            operations["OP-REVERSAL"].StandardLaborHours!.Value,
            operations["OP-REVERSAL"].LaborEfficiencyVarianceHours!.Value,
            operations["OP-REVERSAL"].LaborEfficiencyVarianceDirection!));
        Assert.Equal(3m, operations["OP-REVERSAL"].CoveredReports
            .Single(x => x.ReportNo == "RPT-REVERSAL-VOID").GoodQuantity);
        Assert.Equal(2, operations["OP-REOPEN"].SettlementRevision);
        Assert.Equal(new[] { "RPT-REOPEN-ACTIVE" },
            operations["OP-REOPEN"].CoveredReports.Select(x => x.ReportNo));

        var secondPage = await handler.Handle(
            new GetWorkOrderCostVarianceQuery("org-vectors", "env-vectors", "WO-VECTORS", 2, 2),
            CancellationToken.None);
        Assert.Equal(4, secondPage.TotalOperations);
        Assert.Equal(2, secondPage.Operations.Count);
        Assert.Equal(new[] { "OP-REVERSAL", "OP-ZERO" }, secondPage.Operations.Select(x => x.OperationTaskId));
    }

    [Fact]
    public async Task Stage_reports_do_not_publish_final_variance_until_work_order_completion()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationLaborSettlement.Create(
            "org-stage", "env-stage", "WO-STAGE", "OP-STAGE", "WC-STAGE", 1,
            CompletedAtUtc, 2 * TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-stage-settlement", "hash-stage-settlement");
        var state = OperationLaborSettlementState.Open("org-stage", "env-stage", "OP-STAGE");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-stage", "env-stage", "WO-STAGE", "FG-STAGE");
        cost.RecordActualLabor(settlement);
        cost.RecordMaterial("MOVE-STAGE", "RPT-STAGE", "RM-STAGE", 1m, 25m, CompletedAtUtc);
        cost.Capitalize("FG-MOVE-STAGE", 1m, 20m, CompletedAtUtc);
        db.AddRange(cost, settlement, state,
            OperationLaborCoveredReport.Create(
                "org-stage", "env-stage", "WO-STAGE", "OP-STAGE", 1, "RPT-STAGE"),
            OperationLaborReportSnapshot.Create(
                "org-stage", "env-stage", "WO-STAGE", "OP-STAGE", "WC-STAGE", "RPT-STAGE",
                2m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-1), false, null, "evt-stage-report"));
        await db.SaveChangesAsync();

        var handler = new GetWorkOrderCostVarianceQueryHandler(db);
        var beforeCompletion = await handler.Handle(
            new GetWorkOrderCostVarianceQuery("org-stage", "env-stage", "WO-STAGE"),
            CancellationToken.None);

        Assert.Equal("unavailable", beforeCompletion.LaborVarianceStatus);
        Assert.Equal("work_order_not_completed", beforeCompletion.UnavailableReason);
        Assert.Equal(2.000000m, beforeCompletion.ActualLaborHours);
        Assert.Equal(25.000000m, beforeCompletion.MaterialCost);
        Assert.Equal(20.000000m, beforeCompletion.CapitalizedCost);
        Assert.Null(beforeCompletion.StandardLaborHours);
        Assert.Null(beforeCompletion.LaborEfficiencyVarianceAmount);
        Assert.Null(beforeCompletion.CapitalizationVarianceAmount);
        var provisionalOperation = Assert.Single(beforeCompletion.Operations);
        Assert.Equal("unavailable", provisionalOperation.Status);
        Assert.Equal("work_order_not_completed", provisionalOperation.UnavailableReason);
        Assert.Null(provisionalOperation.StandardLaborHours);
        Assert.Null(provisionalOperation.LaborEfficiencyVarianceAmount);

        cost.Complete(2m, 1, 1, CompletedAtUtc.AddMinutes(1));
        await db.SaveChangesAsync();

        var afterCompletion = await handler.Handle(
            new GetWorkOrderCostVarianceQuery("org-stage", "env-stage", "WO-STAGE"),
            CancellationToken.None);

        Assert.Equal("available", afterCompletion.LaborVarianceStatus);
        Assert.Null(afterCompletion.UnavailableReason);
        Assert.Equal(1.000000m, afterCompletion.StandardLaborHours);
        Assert.Equal(1.000000m, afterCompletion.LaborEfficiencyVarianceHours);
        Assert.Equal(60.000000m, afterCompletion.LaborEfficiencyVarianceAmount);
        Assert.Equal(125.000000m, afterCompletion.CapitalizationVarianceAmount);
        Assert.Equal("available", Assert.Single(afterCompletion.Operations).Status);
    }

    [Fact]
    public async Task Reversal_original_must_belong_to_the_current_settlement_membership()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationLaborSettlement.Create(
            "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", "WC-CURRENT", 1,
            CompletedAtUtc, TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-lineage-settlement", "hash-lineage-settlement");
        var state = OperationLaborSettlementState.Open("org-lineage", "env-lineage", "OP-CURRENT");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-lineage", "env-lineage", "WO-LINEAGE", "FG-LINEAGE");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(cost, settlement, state,
            OperationLaborCoveredReport.Create(
                "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", 1, "RPT-CURRENT"),
            OperationLaborCoveredReport.Create(
                "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", 1, "RPT-REVERSAL"),
            OperationLaborReportSnapshot.Create(
                "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", "WC-CURRENT", "RPT-CURRENT",
                2m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-2), false, null, "evt-current"),
            OperationLaborReportSnapshot.Create(
                "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", "WC-CURRENT", "RPT-REVERSAL",
                1m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-1), true, "RPT-NOT-COVERED", "evt-reversal"),
            OperationLaborReportSnapshot.Create(
                "org-lineage", "env-lineage", "WO-LINEAGE", "OP-CURRENT", "WC-CURRENT", "RPT-NOT-COVERED",
                1m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-3), false, null, "evt-other"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-lineage", "env-lineage", "WO-LINEAGE"),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("conflicting_reversal_snapshot", response.UnavailableReason);
        Assert.Equal("unavailable", Assert.Single(response.Operations).Status);
    }

    [Fact]
    public async Task Reversal_original_must_match_the_current_operation_and_work_center_scope()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationLaborSettlement.Create(
            "org-scope", "env-scope", "WO-SCOPE", "OP-CURRENT", "WC-CURRENT", 1,
            CompletedAtUtc, TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-scope-settlement", "hash-scope-settlement");
        var state = OperationLaborSettlementState.Open("org-scope", "env-scope", "OP-CURRENT");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-scope", "env-scope", "WO-SCOPE", "FG-SCOPE");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(cost, settlement, state,
            OperationLaborCoveredReport.Create(
                "org-scope", "env-scope", "WO-SCOPE", "OP-CURRENT", 1, "RPT-ORIGINAL"),
            OperationLaborCoveredReport.Create(
                "org-scope", "env-scope", "WO-SCOPE", "OP-CURRENT", 1, "RPT-REVERSAL"),
            OperationLaborReportSnapshot.Create(
                "org-scope", "env-scope", "WO-SCOPE", "OP-OTHER", "WC-OTHER", "RPT-ORIGINAL",
                1m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-2), false, null, "evt-scope-original"),
            OperationLaborReportSnapshot.Create(
                "org-scope", "env-scope", "WO-SCOPE", "OP-CURRENT", "WC-CURRENT", "RPT-REVERSAL",
                1m, 0m, 0m, "ea", 2m, CompletedAtUtc.AddMinutes(-1), true, "RPT-ORIGINAL", "evt-scope-reversal"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-scope", "env-scope", "WO-SCOPE"),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("report_scope_conflict", response.UnavailableReason);
        Assert.Equal("unavailable", Assert.Single(response.Operations).Status);
    }

    public static TheoryData<string, decimal[], decimal, decimal> NumericOverflowVectors => new()
    {
        { "sum", [decimal.MaxValue, decimal.MaxValue], 1m, 1m },
        { "division", [decimal.MaxValue], 0.1m, 1m },
        { "multiplication", [999_999_999_999.999999m], 0.000001m, 999_999_999_999.999999m },
    };

    [Theory]
    [MemberData(nameof(NumericOverflowVectors))]
    public async Task Decimal_sum_division_or_multiplication_overflow_fails_closed_as_unavailable(
        string vector,
        decimal[] goodQuantities,
        decimal theoreticalRate,
        decimal hourlyRate)
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrderId = $"WO-OVERFLOW-{vector}";
        var settlement = OperationLaborSettlement.Create(
            "org-overflow", "env-overflow", workOrderId, "OP-OVERFLOW", "WC-OVERFLOW", 1,
            CompletedAtUtc, 0, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", hourlyRate, $"evt-overflow-{vector}", $"hash-overflow-{vector}");
        var state = OperationLaborSettlementState.Open("org-overflow", "env-overflow", "OP-OVERFLOW");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-overflow", "env-overflow", workOrderId, "FG-OVERFLOW");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(cost, settlement, state);
        for (var index = 0; index < goodQuantities.Length; index++)
        {
            var reportNo = $"RPT-OVERFLOW-{index}";
            db.AddRange(
                OperationLaborCoveredReport.Create(
                    "org-overflow", "env-overflow", workOrderId, "OP-OVERFLOW", 1, reportNo),
                OperationLaborReportSnapshot.Create(
                    "org-overflow", "env-overflow", workOrderId, "OP-OVERFLOW", "WC-OVERFLOW", reportNo,
                    goodQuantities[index], 0m, 0m, "ea", theoreticalRate, CompletedAtUtc.AddMinutes(-index - 1),
                    false, null, $"evt-overflow-report-{index}"));
        }
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-overflow", "env-overflow", workOrderId),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("numeric_overflow", response.UnavailableReason);
        Assert.Null(response.StandardLaborHours);
        Assert.Null(response.LaborEfficiencyVarianceAmount);
        Assert.Equal("unavailable", Assert.Single(response.Operations).Status);
    }

    [Fact]
    public async Task Aggregate_numeric_overflow_preserves_operation_lineage_pagination_and_total_count()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cost = WorkOrderCost.Open("org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", "FG-OVERFLOW");
        cost.RecordMaterial("MOVE-OVERFLOW", "RPT-MATERIAL", "RM-OVERFLOW", 1m, 50m, CompletedAtUtc);
        cost.Capitalize("FG-MOVE-OVERFLOW", 1m, 30m, CompletedAtUtc);
        cost.Complete(1m, 1, 1, CompletedAtUtc.AddMinutes(1));

        OperationLaborSettlement Settlement(string operationTaskId, string sourceEventId)
            => OperationLaborSettlement.Create(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW",
                operationTaskId, $"WC-{operationTaskId}", 1, CompletedAtUtc, TimeSpan.TicksPerHour,
                new WorkCenterCostRateId(Guid.CreateVersion7()), 1, "CNY", decimal.MaxValue,
                sourceEventId, $"hash-{sourceEventId}");

        var first = Settlement("OP-A", "evt-overflow-a");
        var second = Settlement("OP-B", "evt-overflow-b");
        var firstState = OperationLaborSettlementState.Open(
            "org-aggregate-overflow", "env-aggregate-overflow", "OP-A");
        firstState.ApplySettlement(1);
        var secondState = OperationLaborSettlementState.Open(
            "org-aggregate-overflow", "env-aggregate-overflow", "OP-B");
        secondState.ApplySettlement(1);
        db.AddRange(cost, first, second, firstState, secondState,
            OperationLaborCoveredReport.Create(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", "OP-A", 1, "RPT-A"),
            OperationLaborReportSnapshot.Create(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", "OP-A", "WC-OP-A", "RPT-A",
                1m, 0m, 0m, "ea", 1m, CompletedAtUtc, false, null, "evt-report-a"),
            OperationLaborCoveredReport.Create(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", "OP-B", 1, "RPT-B"),
            OperationLaborReportSnapshot.Create(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", "OP-B", "WC-OP-B", "RPT-B",
                1m, 0m, 0m, "ea", 1m, CompletedAtUtc, false, null, "evt-report-b"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery(
                "org-aggregate-overflow", "env-aggregate-overflow", "WO-AGGREGATE-OVERFLOW", 2, 1),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("numeric_overflow", response.UnavailableReason);
        Assert.Equal(2.000000m, response.ActualLaborHours);
        Assert.Null(response.ActualLaborCost);
        Assert.Equal(2.000000m, response.StandardLaborHours);
        Assert.Null(response.StandardLaborCost);
        Assert.Equal(0.000000m, response.LaborEfficiencyVarianceHours);
        Assert.Null(response.LaborEfficiencyVarianceAmount);
        Assert.Equal(50.000000m, response.MaterialCost);
        Assert.Equal(50.000000m, response.TotalAccumulatedCost);
        Assert.Equal(30.000000m, response.CapitalizedCost);
        Assert.Equal(20.000000m, response.CapitalizationVarianceAmount);
        Assert.Equal(2, response.TotalOperations);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(1, response.PageSize);
        var operation = Assert.Single(response.Operations);
        Assert.Equal("OP-B", operation.OperationTaskId);
        Assert.Equal("WC-OP-B", operation.WorkCenterId);
        Assert.Equal(1, operation.SettlementRevision);
    }

    [Fact]
    public async Task Read_keeps_decimal_intermediates_unrounded_then_uses_six_digit_away_from_zero_boundaries()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationLaborSettlement.Create(
            "org-round", "env-round", "WO-ROUND", "OP-ROUND", "WC-ROUND", 1,
            CompletedAtUtc, 0, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-round-settlement", "hash-round-settlement");
        var state = OperationLaborSettlementState.Open("org-round", "env-round", "OP-ROUND");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-round", "env-round", "WO-ROUND", "FG-ROUND");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(cost, settlement, state,
            OperationLaborCoveredReport.Create(
                "org-round", "env-round", "WO-ROUND", "OP-ROUND", 1, "RPT-ROUND"),
            OperationLaborReportSnapshot.Create(
                "org-round", "env-round", "WO-ROUND", "OP-ROUND", "WC-ROUND", "RPT-ROUND",
                1m, 0m, 0m, "ea", 128m, CompletedAtUtc.AddMinutes(-1), false, null, "evt-round-report"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-round", "env-round", "WO-ROUND"),
            CancellationToken.None);

        Assert.Equal(0.007813m, response.StandardLaborHours);
        Assert.Equal(0.468750m, response.StandardLaborCost);
        Assert.Equal(-0.007813m, response.LaborEfficiencyVarianceHours);
        Assert.Equal(-0.468750m, response.LaborEfficiencyVarianceAmount);
        Assert.Equal("favorable", response.LaborEfficiencyVarianceDirection);
    }

    [Fact]
    public async Task Snapshot_numeric_scale_beyond_six_digits_fails_closed()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var settlement = OperationLaborSettlement.Create(
            "org-scale", "env-scale", "WO-SCALE", "OP-SCALE", "WC-SCALE", 1,
            CompletedAtUtc, TimeSpan.TicksPerHour, new WorkCenterCostRateId(Guid.CreateVersion7()),
            1, "CNY", 60m, "evt-scale-settlement", "hash-scale-settlement");
        var state = OperationLaborSettlementState.Open("org-scale", "env-scale", "OP-SCALE");
        state.ApplySettlement(1);
        var cost = WorkOrderCost.Open("org-scale", "env-scale", "WO-SCALE", "FG-SCALE");
        cost.RecordActualLabor(settlement);
        cost.Complete(1m, 1, 0, CompletedAtUtc.AddMinutes(1));
        db.AddRange(cost, settlement, state,
            OperationLaborCoveredReport.Create(
                "org-scale", "env-scale", "WO-SCALE", "OP-SCALE", 1, "RPT-SCALE"),
            OperationLaborReportSnapshot.Create(
                "org-scale", "env-scale", "WO-SCALE", "OP-SCALE", "WC-SCALE", "RPT-SCALE",
                10m, 0m, 0m, "ea", 5.0000004m, CompletedAtUtc.AddMinutes(-1), false, null, "evt-scale-report"));
        await db.SaveChangesAsync();

        var response = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new GetWorkOrderCostVarianceQuery("org-scale", "env-scale", "WO-SCALE"),
            CancellationToken.None);

        Assert.Equal("unavailable", response.LaborVarianceStatus);
        Assert.Equal("numeric_scale_out_of_range", response.UnavailableReason);
        var operation = Assert.Single(response.Operations);
        Assert.Equal("unavailable", operation.Status);
        Assert.Equal("numeric_scale_out_of_range", operation.UnavailableReason);
        Assert.Null(operation.StandardLaborHours);
    }

    [Fact]
    public void Public_contract_registers_scoped_finance_read_endpoint()
    {
        var contract = ErpFinanceEndpointContracts.Get<GetWorkOrderCostVarianceEndpoint>();

        Assert.Equal("GET", contract.HttpMethod);
        Assert.Equal("/api/business/v1/erp/finance/work-order-costs/{workOrderId}", contract.Route);
        Assert.Equal("business.erp.finance.read", contract.PermissionCode);
        Assert.Equal("getErpWorkOrderCostVariance", contract.OperationId);
    }

    [Fact]
    public async Task Http_contract_binds_claim_scope_and_preserves_explicit_zero_and_unavailable_nulls()
    {
        var sender = new CapturingSender("available");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestHostConfiguration()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-order-costs/WO-ZERO?pageNumber=2&pageSize=25");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-erp-machine-overhead-token");
        request.Headers.Add("X-Organization-Id", "org-test");
        request.Headers.Add("X-Environment-Id", "env-test");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(sender.Queries);
        Assert.Equal("org-test", query.OrganizationId);
        Assert.Equal("env-test", query.EnvironmentId);
        Assert.Equal("WO-ZERO", query.WorkOrderId);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(25, query.PageSize);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("available", data.GetProperty("laborVarianceStatus").GetString());
        Assert.Equal(0m, data.GetProperty("actualLaborHours").GetDecimal());
        Assert.Equal(0m, data.GetProperty("actualMachineHours").GetDecimal());
        Assert.Equal("available", data.GetProperty("machineCostStatus").GetString());
        Assert.Equal("CNY", data.GetProperty("machineCurrencyCode").GetString());
        Assert.Equal(0m, data.GetProperty("appliedFixedMachineOverhead").GetDecimal());
        Assert.Equal(0m, data.GetProperty("appliedVariableMachineOverhead").GetDecimal());
        Assert.Equal(0m, data.GetProperty("appliedMachineOverheadTotal").GetDecimal());
        Assert.Equal(2, data.GetProperty("machineOverheadPageNumber").GetInt32());
        Assert.Equal(25, data.GetProperty("machineOverheadPageSize").GetInt32());
        Assert.Equal(0, data.GetProperty("totalMachineOverheadOperations").GetInt32());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("machineOverheadOperations").ValueKind);
    }

    [Theory]
    [InlineData("notApplicable", "machine_overhead_not_applicable")]
    [InlineData("unavailable", "currency_conflict")]
    public async Task Http_contract_serializes_machine_three_state_nullability(
        string machineCostStatus,
        string reason)
    {
        var sender = new CapturingSender(machineCostStatus);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestHostConfiguration()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/business/v1/erp/finance/work-order-costs/WO-STATE");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-erp-machine-overhead-token");
        request.Headers.Add("X-Organization-Id", "org-test");
        request.Headers.Add("X-Environment-Id", "env-test");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(machineCostStatus, data.GetProperty("machineCostStatus").GetString());
        Assert.Equal(reason, data.GetProperty("machineCostUnavailableReason").GetString());
        foreach (var propertyName in new[]
        {
            "machineCurrencyCode", "actualMachineHours", "appliedFixedMachineOverhead",
            "appliedVariableMachineOverhead", "appliedMachineOverheadTotal"
        })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(propertyName).ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("machineOverheadOperations").ValueKind);
    }

    [Fact]
    public async Task OpenApi_exposes_work_order_cost_operation_and_three_state_fields()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(TestHostConfiguration()));
            });
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var operation = json.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/erp/finance/work-order-costs/{workOrderId}")
            .GetProperty("get");

        Assert.Equal("getErpWorkOrderCostVariance", operation.GetProperty("operationId").GetString());
        var serialized = operation.GetRawText();
        Assert.Contains("WorkOrderCostVarianceResponse", serialized, StringComparison.Ordinal);
        var schema = schemas
            .EnumerateObject()
            .Single(x => x.Name.EndsWith("WorkOrderCostVarianceResponse", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("workOrderId", out _));
        var properties = schema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("appliedFixedMachineOverhead", out _));
        Assert.True(properties.TryGetProperty("appliedVariableMachineOverhead", out _));
        Assert.True(properties.TryGetProperty("appliedMachineOverheadTotal", out _));
        Assert.True(properties.TryGetProperty("machineCurrencyCode", out _));
        Assert.True(properties.TryGetProperty("machineOverheadPageNumber", out _));
        Assert.True(properties.TryGetProperty("machineOverheadPageSize", out _));
        Assert.True(properties.TryGetProperty("totalMachineOverheadOperations", out _));
        Assert.True(properties.TryGetProperty("machineOverheadOperations", out _));
        Assert.False(properties.TryGetProperty("actualFixedMachineOverhead", out _));
        var machineOperationSchema = schemas
            .EnumerateObject()
            .Single(x => x.Name.EndsWith("OperationMachineOverheadItem", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("operationTaskId", out _));
        var machineOperationProperties = machineOperationSchema.Value.GetProperty("properties");
        AssertMachineOverheadStatusSchema(properties.GetProperty("machineCostStatus"), schemas);
        AssertMachineOverheadStatusSchema(machineOperationProperties.GetProperty("status"), schemas);
        foreach (var propertyName in new[]
        {
            "settlementId", "settlementRevision", "status", "unavailableReason", "actualMachineHours",
            "appliedFixedMachineOverhead", "appliedVariableMachineOverhead", "appliedMachineOverheadTotal",
            "accountingPeriodCode", "currencyCode", "deviceAssetId", "machineTimeBasisCode",
            "workCenterMachineOverheadRateId", "rateRevision", "completedAtUtc", "sourceEventId"
        })
            Assert.True(machineOperationProperties.TryGetProperty(propertyName, out _), propertyName);

        var required = schema.Value.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var propertyName in new[]
        {
            "machineCostStatus", "machineOverheadPageNumber", "machineOverheadPageSize",
            "totalMachineOverheadOperations", "machineOverheadOperations"
        })
            Assert.Contains(propertyName, required);
        foreach (var propertyName in new[]
        {
            "machineCostUnavailableReason", "machineCurrencyCode", "actualMachineHours",
            "appliedFixedMachineOverhead", "appliedVariableMachineOverhead", "appliedMachineOverheadTotal"
        })
        {
            Assert.DoesNotContain(propertyName, required);
            Assert.True(properties.GetProperty(propertyName).GetProperty("nullable").GetBoolean(), propertyName);
        }

        var operationRequired = machineOperationSchema.Value.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var propertyName in new[]
        {
            "operationTaskId", "workCenterId", "settlementId", "settlementRevision", "status",
            "accountingPeriodCode", "currencyCode", "workCenterMachineOverheadRateId", "rateRevision",
            "completedAtUtc", "sourceEventId"
        })
            Assert.Contains(propertyName, operationRequired);
        foreach (var propertyName in new[]
        {
            "unavailableReason", "actualMachineHours", "appliedFixedMachineOverhead",
            "appliedVariableMachineOverhead", "appliedMachineOverheadTotal", "deviceAssetId", "machineTimeBasisCode"
        })
        {
            Assert.DoesNotContain(propertyName, operationRequired);
            Assert.True(machineOperationProperties.GetProperty(propertyName).GetProperty("nullable").GetBoolean(), propertyName);
        }

        var periodOperation = json.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations")
            .GetProperty("get");
        Assert.Equal("listErpWorkCenterMachineOverheadReconciliations", periodOperation.GetProperty("operationId").GetString());
        var periodSchema = schemas
            .EnumerateObject()
            .Single(x => x.Name.EndsWith("ListWorkCenterMachineOverheadReconciliationsResponse", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("accountingPeriodCode", out _));
        var periodProperties = periodSchema.Value.GetProperty("properties");
        Assert.True(periodProperties.TryGetProperty("accountingPeriodStatus", out _));
        Assert.True(periodProperties.TryGetProperty("reconciliationStatus", out _));
        Assert.True(periodProperties.TryGetProperty("reconciliationUnavailableReason", out _));
        AssertMachineOverheadStatusSchema(periodProperties.GetProperty("reconciliationStatus"), schemas);

        var periodItemSchema = schemas.EnumerateObject()
            .Single(x => x.Name.EndsWith("WorkCenterMachineOverheadReconciliationItem", StringComparison.Ordinal)
                && x.Value.TryGetProperty("properties", out var candidateProperties)
                && candidateProperties.TryGetProperty("recordedAtUtc", out _));
        AssertMachineOverheadStatusSchema(
            periodItemSchema.Value.GetProperty("properties").GetProperty("reconciliationStatus"), schemas);
    }

    private static void AssertMachineOverheadStatusSchema(JsonElement propertySchema, JsonElement schemas)
    {
        var schema = ResolveSchema(propertySchema, schemas);
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            ["available", "notApplicable", "unavailable"],
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement schemas)
    {
        if (schema.TryGetProperty("$ref", out var schemaReference))
            return schemas.GetProperty(schemaReference.GetString()!.Split('/')[^1]);
        if (schema.TryGetProperty("allOf", out var inheritedSchemas))
            return ResolveSchema(Assert.Single(inheritedSchemas.EnumerateArray()), schemas);
        if (schema.TryGetProperty("oneOf", out var alternatives))
            return ResolveSchema(Assert.Single(alternatives.EnumerateArray()), schemas);
        return schema;
    }

    private sealed class CapturingSender(string machineCostStatus) : ISender
    {
        public List<GetWorkOrderCostVarianceQuery> Queries { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetWorkOrderCostVarianceQuery>(request);
            Queries.Add(query);
            var available = machineCostStatus == "available";
            var reason = machineCostStatus switch
            {
                "notApplicable" => "machine_overhead_not_applicable",
                "unavailable" => "currency_conflict",
                _ => null,
            };
            return Task.FromResult((TResponse)(object)new WorkOrderCostVarianceResponse(
                query.OrganizationId, query.EnvironmentId, query.WorkOrderId, "CNY", "actualOperation",
                "available", null, 0m, 0m, 0m, 0m, 0m, 0m, "neutral",
                "notApplicable", "actual_payroll_rate_not_modeled",
                0m, 0m, 0m, 0m, available ? 0m : null,
                Enum.Parse<MachineOverheadReadStatus>(machineCostStatus, ignoreCase: true), reason,
                available ? "CNY" : null, query.PageNumber, query.PageSize, 0, [],
                available ? 0m : null, available ? 0m : null, available ? 0m : null,
                query.PageNumber, query.PageSize, 0, []));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static Dictionary<string, string?> TestHostConfiguration() => new()
    {
        ["InternalService:BearerToken"] = "test-general-internal-token",
        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
        ["Persistence:AutoMigrate"] = "false",
    };
}
