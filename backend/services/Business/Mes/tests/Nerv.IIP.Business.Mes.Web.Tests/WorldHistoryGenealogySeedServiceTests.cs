using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「追溯断点」块（产出批次谱系 / 报工物料消耗）的形状与幂等性证据。
///
/// 两张表此前恒为 0 行，「追溯查询」页三种查法都只能画出骨架，看不到消耗的物料批次
/// 与产出的成品批次。断言覆盖：条数关系、批号 / 领料单号号段、引用完整性、幂等，
/// 以及 **5 个 asOfDate 边界**（上线日 / 上线日+1 / 年中 / 演示当天 / 未来日）。
/// </summary>
public sealed class WorldHistoryGenealogySeedServiceTests
{
    private const double TestScale = 0.02d;

    /// <summary>每个成品的用料行数（活塞杆 / 缸筒 / 阀系 / 弹簧）。</summary>
    private const int ComponentsPerSku = 4;

    /// <summary>5 个 asOfDate 边界：上线日、上线日+1、年中、演示当天、未来日。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new()
        {
            { 2026, 1, 5 },
            { 2026, 1, 6 },
            { 2026, 4, 15 },
            { 2026, 7, 27 },
            { 2026, 12, 31 },
        };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Genealogy_seed_fills_both_tables_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);

        var report = await new WorldHistoryGenealogySeedService(dbContext).SeedAsync("org-001", "env-dev");

        // 两张表都不再是 0 行——这正是追溯页只有骨架的直接原因。
        Assert.True(report.OutputLotGenealogiesWritten > 0);
        Assert.True(report.MaterialConsumptionsWritten > 0);

        // 谱系一张工单一行（produced_lot_no 在 scope 内唯一），消耗一张工单 4 行。
        var expectedWorkOrders = await ReportedWorkOrderCountAsync(dbContext);
        Assert.Equal(expectedWorkOrders, report.OutputLotGenealogiesWritten);
        Assert.Equal(expectedWorkOrders * ComponentsPerSku, report.MaterialConsumptionsWritten);
        Assert.Equal(report.OutputLotGenealogiesWritten, await dbContext.OutputLotGenealogies.CountAsync());
        Assert.Equal(report.MaterialConsumptionsWritten, await dbContext.ProductionReportMaterialConsumptions.CountAsync());

        // 校验器（fail-closed）跑过并如实回报条数。
        Assert.Equal(report.OutputLotGenealogiesWritten, report.Validation.OutputLotGenealogiesChecked);
        Assert.Equal(report.MaterialConsumptionsWritten, report.Validation.MaterialConsumptionsChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Genealogy_seed_keeps_lot_number_segments_and_quantity_chain(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        await new WorldHistoryGenealogySeedService(dbContext).SeedAsync("org-001", "env-dev");

        var genealogies = await dbContext.OutputLotGenealogies.ToArrayAsync();
        Assert.NotEmpty(genealogies);
        Assert.All(genealogies, row =>
        {
            Assert.Matches(@"^LOT-WO-2026-[0-9R]\d{3,4}$", row.ProducedLotNo);
            Assert.Equal(WorldHistoryGenealogySpec.ProducedLotNo(row.WorkOrderId), row.ProducedLotNo);
            Assert.StartsWith(WorldHistoryGenealogySpec.ProductionReportNoPrefix, row.ReportNo, StringComparison.Ordinal);
            Assert.True(row.Quantity > 0m);
            Assert.Null(row.SerialNo);
            Assert.DoesNotContain("-DEMO-", row.ProducedLotNo, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", row.ProducedLotNo, StringComparison.Ordinal);
        });

        // 谱系数量 = 该工单的报工好品累计（追溯页上的数量链）。
        var goodByWorkOrder = (await dbContext.ProductionReports
                .Select(x => new { x.WorkOrderId, x.GoodQuantity })
                .ToArrayAsync())
            .GroupBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.GoodQuantity), StringComparer.Ordinal);
        Assert.All(genealogies, row => Assert.Equal(goodByWorkOrder[row.WorkOrderId], row.Quantity));

        var consumptions = await dbContext.ProductionReportMaterialConsumptions.ToArrayAsync();
        Assert.NotEmpty(consumptions);
        Assert.All(consumptions, row =>
        {
            // 物料批号必须与领料单确认的线边收料批号逐字相同，否则追溯断链。
            Assert.Equal(WorldHistoryGenealogySpec.MaterialLotNo(row.MaterialId, row.WorkOrderId), row.MaterialLotId);
            Assert.Matches(@"^MIR-WO-2026-[0-9R]\d{3,4}-\d{2}$", row.MaterialIssueRequestNo);
            Assert.Matches(@"^(SF|RM)-", row.MaterialId);
            Assert.Equal("pcs", row.UomCode);
            Assert.True(row.ConsumedQuantity > 0m);
            Assert.Null(row.InventoryPostingFailureCode);
        });

        // 每张工单 4 种物料，互不重复。
        Assert.All(
            consumptions.GroupBy(x => x.WorkOrderId, StringComparer.Ordinal),
            group => Assert.Equal(ComponentsPerSku, group.Select(x => x.MaterialId).Distinct(StringComparer.Ordinal).Count()));
    }

    /// <summary>谱系与消耗必须挂在**库里真实存在**的报工 / 工单 / 工序上——这是追溯可下钻的前提。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Genealogy_rows_anchor_on_real_reports_and_operations(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        await new WorldHistoryGenealogySeedService(dbContext).SeedAsync("org-001", "env-dev");

        var reportNos = (await dbContext.ProductionReports.Select(x => x.ReportNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var workOrderIds = (await dbContext.WorkOrders.Select(x => x.WorkOrderIdValue).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        var taskIds = (await dbContext.OperationTasks.Select(x => x.OperationTaskIdValue).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);

        var genealogies = await dbContext.OutputLotGenealogies.ToArrayAsync();
        Assert.All(genealogies, row =>
        {
            Assert.Contains(row.ReportNo, reportNos);
            Assert.Contains(row.WorkOrderId, workOrderIds);
            Assert.Contains(row.OperationTaskId, taskIds);
        });

        var consumptions = await dbContext.ProductionReportMaterialConsumptions.ToArrayAsync();
        Assert.All(consumptions, row =>
        {
            Assert.Contains(row.ReportNo, reportNos);
            Assert.Contains(row.WorkOrderId, workOrderIds);
            Assert.Contains(row.OperationTaskId, taskIds);
        });

        // 领料单号必须指向真实存在的领料单（追溯页从物料批次回到领料单那一跳）。
        var issueRequestNos = (await dbContext.MaterialIssueRequests.Select(x => x.RequestNo).ToArrayAsync())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(consumptions, row => Assert.Contains(row.MaterialIssueRequestNo, issueRequestNos));
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Genealogy_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, asOfDate, TestScale);
        var seed = new WorldHistoryGenealogySeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev");
        var genealogyCount = await dbContext.OutputLotGenealogies.CountAsync();
        var consumptionCount = await dbContext.ProductionReportMaterialConsumptions.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(0, second.OutputLotGenealogiesWritten);
        Assert.Equal(0, second.MaterialConsumptionsWritten);
        Assert.Equal(genealogyCount, await dbContext.OutputLotGenealogies.CountAsync());
        Assert.Equal(consumptionCount, await dbContext.ProductionReportMaterialConsumptions.CountAsync());
        Assert.True(first.OutputLotGenealogiesWritten > 0);
    }

    /// <summary>工单链没落库时宁可不写，也不造假报工号（外键会直接把 seed 打挂）。</summary>
    [Fact]
    public async Task Genealogy_rows_are_skipped_when_the_work_order_chain_is_missing()
    {
        await using var dbContext = WorldHistorySeedTestContext.Create();

        var report = await new WorldHistoryGenealogySeedService(dbContext).SeedAsync("org-001", "env-dev");

        Assert.Equal(0, report.OutputLotGenealogiesWritten);
        Assert.Equal(0, report.MaterialConsumptionsWritten);
    }

    /// <summary>@scale=1.0 的规模：谱系 = 有报工的工单数，消耗 = 谱系 × 4。</summary>
    [Fact]
    public void Full_scale_volumes_match_the_world_bible_shape()
    {
        var asOfDate = new DateOnly(2026, 7, 27);
        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, 1.0d)
            .Where(plan => plan.HasWorkOrder)
            .ToArray();

        // 已结案 / 已发货 / 在制的工单才有报工；已下达待开工与草稿废弃没有产出批次可追。
        var reportedOrderWorkOrders = plans.Count(plan =>
            plan.Stage is WorldHistoryOrderStage.Settled
                or WorldHistoryOrderStage.Shipped
                or WorldHistoryOrderStage.InProgress);
        var reworkWorkOrders = (int)Math.Round(
            plans.Length * WorldHistoryMesSpec.ReworkWorkOrderRatio, MidpointRounding.AwayFromZero);
        var expectedGenealogies = reportedOrderWorkOrders + reworkWorkOrders;

        Assert.InRange(expectedGenealogies, 2600, 4200);
        Assert.InRange(expectedGenealogies * ComponentsPerSku, 10400, 16800);
    }

    /// <summary>领料单号规则必须与工单链写库时一致（分批领料的工单消耗挂在首批上）。</summary>
    [Fact]
    public void Material_issue_request_numbers_follow_the_write_side_ordinal_rule()
    {
        Assert.Equal("MIR-WO-2026-00042-01", WorldHistoryGenealogySpec.MaterialIssueRequestNo("WO-2026-00042", 0, false));
        Assert.Equal("MIR-WO-2026-00042-04", WorldHistoryGenealogySpec.MaterialIssueRequestNo("WO-2026-00042", 3, false));
        Assert.Equal("MIR-WO-2026-00042-01", WorldHistoryGenealogySpec.MaterialIssueRequestNo("WO-2026-00042", 0, true));
        Assert.Equal("MIR-WO-2026-00042-07", WorldHistoryGenealogySpec.MaterialIssueRequestNo("WO-2026-00042", 3, true));

        var consumptions = WorldHistoryGenealogySpec.BuildConsumptions("WO-2026-00042", "FG-QJ-P1-L", 120m);
        Assert.Equal(ComponentsPerSku, consumptions.Count);
        Assert.All(consumptions, x => Assert.Equal(120m, x.ConsumedQuantity));
        Assert.Equal(
            WorldHistoryMesSpec.Components("FG-QJ-P1-L").Select(x => x.SkuCode),
            consumptions.Select(x => x.MaterialId));
        Assert.Empty(WorldHistoryGenealogySpec.BuildConsumptions("WO-2026-00042", "FG-QJ-P1-L", 0m));
    }

    private static async Task<int> ReportedWorkOrderCountAsync(ApplicationDbContext dbContext) =>
        (await dbContext.ProductionReports
            .Select(x => x.WorkOrderId)
            .Distinct()
            .ToArrayAsync())
        .Length;
}
