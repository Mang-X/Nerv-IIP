using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class ErpCostAccountingAggregateTests
{
    [Fact]
    public void GL_account_normalizes_code_type_and_parent_link()
    {
        var account = GLAccount.Create("org-001", "env-dev", " 1405 ", "Work in process", GLAccountType.Asset, " 1400 ");

        Assert.Equal("1405", account.Code);
        Assert.Equal("1400", account.ParentCode);
        Assert.Equal(GLAccountType.Asset, account.Type);
    }

    [Fact]
    public void Work_order_cost_settlement_reconciles_inputs_capitalization_and_variance()
    {
        var cost = WorkOrderCost.Open("org-001", "env-dev", "WO-001", "FG-001");
        cost.RecordLabor("RPT-001", "WC-01", 2m, 50m, false, new DateTimeOffset(2026, 7, 11, 1, 0, 0, TimeSpan.Zero));
        cost.RecordMaterial("MOVE-001", "RPT-001", "RM-001", 3m, 20m, new DateTimeOffset(2026, 7, 11, 2, 0, 0, TimeSpan.Zero));

        cost.Complete(8m, 1, 1, new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero));
        cost.Capitalize("MOVE-FG-001", 8m, 19m, new DateTimeOffset(2026, 7, 11, 4, 0, 0, TimeSpan.Zero));

        Assert.Equal(100m, cost.LaborCost);
        Assert.Equal(60m, cost.MaterialCost);
        Assert.Equal(160m, cost.TotalAccumulatedCost);
        Assert.Equal(152m, cost.CapitalizedCost);
        Assert.Equal(8m, cost.VarianceCost);
        Assert.True(cost.IsReconciled);
        Assert.Equal(2, cost.Details.Count);
    }

    [Fact]
    public void Work_order_cost_reversal_nets_prior_labor_report()
    {
        var cost = WorkOrderCost.Open("org-001", "env-dev", "WO-002", "FG-002");
        cost.RecordLabor("RPT-002", "WC-01", 1.5m, 40m, false, DateTimeOffset.UtcNow);
        cost.RecordLabor("RPT-002-R", "WC-01", 1.5m, 40m, true, DateTimeOffset.UtcNow);

        Assert.Equal(0m, cost.LaborCost);
    }

    [Fact]
    public void Work_order_cost_material_reversal_nets_actual_moving_average_cost()
    {
        var cost = WorkOrderCost.Open("org-001", "env-dev", "WO-003", "FG-003");
        cost.RecordMaterial("MOVE-OUT", "RPT-003", "RM-003", 3m, 20m, DateTimeOffset.UtcNow);
        cost.RecordMaterial("MOVE-REV", "RPT-003-R", "RM-003", -3m, 20m, DateTimeOffset.UtcNow);

        Assert.Equal(0m, cost.MaterialCost);
    }
}
