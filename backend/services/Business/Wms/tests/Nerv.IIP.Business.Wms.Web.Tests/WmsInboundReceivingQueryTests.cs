using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Queries;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 覆盖 MAN-459 #813 审核修复的后端读面：
///  - ListInboundOrders 单据级派生质检状态 + 上架放行（聚合含免检行，供列表状态标/上架门禁一次查询）。
///  - ListReceivingQualityGates includeNotRequired（默认排除免检行=质检工作清单；true 返回全部收货行）。
/// </summary>
public sealed class WmsInboundReceivingQueryTests
{
    private static ApplicationDbContext CreateContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static InboundOrder InboundOrder(string inboundOrderNo, params InboundOrderLineDraft[] lines) =>
        Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            "asn",
            $"SRC-{inboundOrderNo}",
            "SITE-1",
            lines);

    private static InboundOrderLineDraft Line(string lineNo, string qualityStatus) =>
        new(lineNo, $"SKU-{lineNo}", "kg", 5m, "LOC-STAGE", $"LOT-{lineNo}", null, qualityStatus, "company", "owner-001");

    [Fact]
    public async Task ListInboundOrders_derives_order_level_quality_status_and_putaway_release()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = nameof(ListInboundOrders_derives_order_level_quality_status_and_putaway_release);
        await using (var seed = CreateContext(databaseName, databaseRoot))
        {
            // A：一行需检（pending）+ 一行免检 → 单据级 pending，未放行上架。
            seed.InboundOrders.Add(InboundOrder("IN-Q-A", Line("1", "quality"), Line("2", "unrestricted")));
            // B：仅免检行 → 单据级 not-required，放行上架（无待检/不合格）。
            seed.InboundOrders.Add(InboundOrder("IN-Q-B", Line("1", "unrestricted")));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext(databaseName, databaseRoot);
        var result = await new ListInboundOrdersQueryHandler(context)
            .Handle(new ListInboundOrdersQuery("org-001", "env-dev"), CancellationToken.None);

        var a = result.Items.Single(x => x.InboundOrderNo == "IN-Q-A");
        Assert.Equal(InboundQualityGateStatuses.Pending, a.QualityGateStatus);
        Assert.False(a.IsReleasedForPutaway);

        var b = result.Items.Single(x => x.InboundOrderNo == "IN-Q-B");
        Assert.Equal(InboundQualityGateStatuses.NotRequired, b.QualityGateStatus);
        Assert.True(b.IsReleasedForPutaway);
    }

    [Fact]
    public async Task ListReceivingQualityGates_includeNotRequired_toggles_exempt_lines()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = nameof(ListReceivingQualityGates_includeNotRequired_toggles_exempt_lines);
        await using (var seed = CreateContext(databaseName, databaseRoot))
        {
            seed.InboundOrders.Add(InboundOrder("IN-G-A", Line("1", "quality"), Line("2", "unrestricted")));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext(databaseName, databaseRoot);
        var handler = new ListReceivingQualityGatesQueryHandler(context);

        // 默认（质检工作清单）：排除免检行，仅需检行。
        var workList = await handler.Handle(
            new ListReceivingQualityGatesQuery("org-001", "env-dev"),
            CancellationToken.None);
        Assert.Single(workList.Items);
        Assert.All(workList.Items, x => Assert.NotEqual(InboundQualityGateStatuses.NotRequired, x.QualityGateStatus));

        // includeNotRequired：返回全部收货行（含免检）。
        var allLines = await handler.Handle(
            new ListReceivingQualityGatesQuery("org-001", "env-dev", IncludeNotRequired: true),
            CancellationToken.None);
        Assert.Equal(2, allLines.Items.Count);
        Assert.Contains(allLines.Items, x => x.QualityGateStatus == InboundQualityGateStatuses.NotRequired);
    }
}
