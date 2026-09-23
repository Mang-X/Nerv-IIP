using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public sealed record MesFoundationWorkCenter(string Code, string DisplayName);

public sealed record MesFoundationStockLocation(string LocationCode, string SiteCode, string LocationType);

/// <summary>
/// 生产准备检查读取的外部基础数据（#3771）。MES 不拥有这些数据：工作中心来自 MasterData，
/// 工作中心成本费率来自 ERP，库位主数据来自 Inventory。
/// 来源读失败一律抛 <c>FOUNDATION_SOURCE_UNAVAILABLE</c>，由网关把该检查区域显示为「来源服务不可用」，
/// 不能把读失败说成「缺数据」。
/// </summary>
public interface IMesFoundationSourceReader
{
    Task<IReadOnlyCollection<MesFoundationWorkCenter>> ListActiveWorkCentersAsync(
        string organizationId,
        string environmentId,
        string? siteCode,
        string? lineCode,
        string? workCenterCode,
        CancellationToken cancellationToken);

    Task<bool> HasEffectiveWorkCenterCostRateAsync(
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken);

    Task<MesFoundationStockLocation?> FindStockLocationAsync(
        string organizationId,
        string environmentId,
        string locationCode,
        CancellationToken cancellationToken);
}

public sealed class HttpMesFoundationSourceReader(
    MesMasterDataHttpClient masterDataClient,
    MesErpHttpClient erpClient,
    MesInventoryHttpClient inventoryClient,
    IInternalServiceTokenProvider internalTokenProvider) : IMesFoundationSourceReader
{
    public async Task<IReadOnlyCollection<MesFoundationWorkCenter>> ListActiveWorkCentersAsync(
        string organizationId,
        string environmentId,
        string? siteCode,
        string? lineCode,
        string? workCenterCode,
        CancellationToken cancellationToken)
    {
        var data = await GetAsync<ListMasterDataResourcesResponse>(
            masterDataClient.HttpClient,
            "MasterData 工作中心",
            "/api/business/v1/master-data/resources?" + Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("resourceType", "work-center"),
                ("siteCode", siteCode),
                ("lineCode", lineCode),
                ("all", "true")),
            cancellationToken);
        // MasterData 的 work-center 分支只按工厂/产线过滤，workCenterCode 参数只作用于挂在工作中心下的资源，
        // 所以按工作中心收窄在这里做。
        var code = workCenterCode?.Trim();
        return data.Resources
            .Where(x => x.Active && (string.IsNullOrEmpty(code) || string.Equals(x.Code, code, StringComparison.Ordinal)))
            .Select(x => new MesFoundationWorkCenter(x.Code, x.DisplayName))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<bool> HasEffectiveWorkCenterCostRateAsync(
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        var data = await GetAsync<ErpWorkCenterCostRateList>(
            erpClient.HttpClient,
            "ERP 工作中心费率",
            "/api/business/v1/erp/finance/work-center-cost-rates?" + Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("workCenterId", workCenterId)),
            cancellationToken);
        return data.CurrentEffectiveRevision is not null;
    }

    public async Task<MesFoundationStockLocation?> FindStockLocationAsync(
        string organizationId,
        string environmentId,
        string locationCode,
        CancellationToken cancellationToken)
    {
        // keyword 是包含匹配；库位编码在组织/环境内唯一，取精确相等的那一行。
        var data = await GetAsync<InventoryStockLocationList>(
            inventoryClient.HttpClient,
            "Inventory 库位",
            "/api/inventory/v1/locations?" + Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("keyword", locationCode),
                ("pageSize", "200")),
            cancellationToken);
        return data.Items
            .Where(x => string.Equals(x.LocationCode, locationCode, StringComparison.Ordinal))
            .Select(x => new MesFoundationStockLocation(x.LocationCode, x.SiteCode, x.LocationType))
            .FirstOrDefault();
    }

    private async Task<T> GetAsync<T>(
        HttpClient httpClient,
        string sourceName,
        string uri,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable(sourceName, exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(sourceName, exception.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable(sourceName, $"返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            ResponseData<T>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<ResponseData<T>>(cancellationToken);
            }
            catch (JsonException exception)
            {
                throw Unavailable(sourceName, exception.Message);
            }

            return envelope is { Success: true, Data: not null }
                ? envelope.Data
                : throw Unavailable(sourceName, "返回空响应或失败响应。");
        }
    }

    private static KnownException Unavailable(string sourceName, string detail) =>
        new($"FOUNDATION_SOURCE_UNAVAILABLE: {sourceName}读取失败，结果无法可靠确定。{detail}");

    private static string Query(params (string Name, string? Value)[] values) =>
        string.Join(
            "&",
            values
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value!.Trim())}"));

    private sealed record ErpWorkCenterCostRateList(int? CurrentEffectiveRevision);

    private sealed record InventoryStockLocationList(IReadOnlyCollection<InventoryStockLocationItem> Items);

    private sealed record InventoryStockLocationItem(string LocationCode, string SiteCode, string LocationType);
}
