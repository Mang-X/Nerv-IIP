using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

/// <summary>线边收料测试用的真实库位组合，与世界观历史种子（SITE-001 + WH-WB-*）同码。</summary>
internal static class MaterialSupplyTestFixtures
{
    public static readonly MaterialTransferLocations Locations =
        new("SITE-001", "WH-WB-RM-01", "SITE-001", "WH-WB-LINE-01");
}
