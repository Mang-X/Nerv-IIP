using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

/// <summary>
/// 完工入库的目标库位配置。**没有任何领域默认值**：成品仓站点、库位全部来自配置
/// （Aspire / appsettings），MES 不再把 <c>finished-goods</c> / <c>receiving</c>
/// 这类命名空间硬编码进库存过账请求（#1331，与 #1322 同款命名空间错配）。
/// </summary>
public sealed class MesFinishedGoodsReceiptLocationOptions
{
    /// <summary>
    /// 成品仓站点编码（例如 <c>SITE-001</c>）。默认复用权威站点键 <c>Inventory:SiteCode</c>，
    /// 只有成品仓独立成站点的部署才用 <c>Inventory:FinishedGoodsSiteCode</c> 覆盖。
    /// </summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>成品仓库位编码（例如 <c>WH-WB-FG-01</c>）。</summary>
    public string LocationCode { get; init; } = string.Empty;
}

/// <summary>完工入库的目标位置：成品仓站点 + 库位。</summary>
public sealed record MesFinishedGoodsReceiptLocation(string SiteCode, string LocationCode);

public interface IMesFinishedGoodsReceiptLocationResolver
{
    MesFinishedGoodsReceiptLocation Resolve();
}

/// <summary>
/// 配置驱动的完工入库位置解析：站点/库位任一缺失即显式失败
/// （<c>FINISHED_GOODS_LOCATION_UNCONFIGURED</c>），宁可入库申请当场报错，
/// 也不臆造一个库存里不存在的位置命名空间去制造静默错账。
/// </summary>
public sealed class ConfiguredMesFinishedGoodsReceiptLocationResolver(
    MesFinishedGoodsReceiptLocationOptions options)
    : IMesFinishedGoodsReceiptLocationResolver
{
    public MesFinishedGoodsReceiptLocation Resolve()
    {
        var siteCode = Trimmed(options.SiteCode);
        var locationCode = Trimmed(options.LocationCode);
        if (siteCode is null || locationCode is null)
        {
            throw new KnownException(
                "FINISHED_GOODS_LOCATION_UNCONFIGURED: 未配置完工入库的成品仓站点/库位（Inventory:SiteCode 或 Inventory:FinishedGoodsSiteCode、Inventory:FinishedGoodsLocationCode）。");
        }

        return new MesFinishedGoodsReceiptLocation(siteCode, locationCode);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
