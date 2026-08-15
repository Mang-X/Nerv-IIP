using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 线边收料验收用的真实库位组合：与库存种子事实（SITE-001 + WH-WB-RM-01 / WH-WB-LINE-01）同码，
/// 不再使用 <c>warehouse</c> / <c>line-side</c> 这类不存在的命名空间（#1322）。
/// </summary>
internal static class MaterialSupplyTestFixtures
{
    public const string SiteCode = "SITE-001";
    public const string SourceLocationCode = "WH-WB-RM-01";
    public const string LineSideLocationCode = "WH-WB-LINE-01";

    public static readonly MaterialTransferLocations Locations =
        new(SiteCode, SourceLocationCode, SiteCode, LineSideLocationCode);

    public static IMesMaterialSupplyLocationResolver Resolver { get; } = new StubResolver(Locations);

    private sealed class StubResolver(MaterialTransferLocations locations) : IMesMaterialSupplyLocationResolver
    {
        public Task<MaterialTransferLocations> ResolveAsync(
            MesMaterialSupplyLocationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(locations);
    }
}
