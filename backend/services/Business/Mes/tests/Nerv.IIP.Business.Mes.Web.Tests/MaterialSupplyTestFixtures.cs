using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>线边收料测试用的真实库位组合，与世界观历史种子（SITE-001 + WH-WB-*）同码。</summary>
internal static class MaterialSupplyTestFixtures
{
    public static readonly MaterialTransferLocations Locations =
        new("SITE-001", "WH-WB-RM-01", "SITE-001", "WH-WB-LINE-01");

    public static IMesMaterialSupplyLocationResolver Resolver { get; } = new StubResolver(Locations);

    /// <summary>补齐两条腿的库存过账回执：只有过账成功，已收数量与齐套才会跟着动（#1322）。</summary>
    public static async Task PostPendingReceiptAsync(
        Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext dbContext,
        string requestNo,
        DateTimeOffset postedAtUtc)
    {
        var request = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            dbContext.MaterialIssueRequests,
            x => x.RequestNo == requestNo);
        var token = request.PendingPostingToken!;
        request.MarkInventoryPosted(token, MaterialTransferLeg.WarehouseIssue, postedAtUtc);
        request.MarkInventoryPosted(token, MaterialTransferLeg.LineSideReceipt, postedAtUtc);
        await dbContext.SaveChangesAsync();
    }

    private sealed class StubResolver(MaterialTransferLocations locations) : IMesMaterialSupplyLocationResolver
    {
        public Task<MaterialTransferLocations> ResolveAsync(
            MesMaterialSupplyLocationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(locations);
    }
}
