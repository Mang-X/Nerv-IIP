using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints;

/// <summary>
/// 按设备引用（设备公开 ID 或设备编码，MasterData 两种都认）读取设备主数据，用于读面补可读信息。
/// 补充信息属于可降级的读增强：按 <see cref="BusinessConsoleReadEnrichmentFailurePolicy"/> 降级为 null。
/// </summary>
internal static class DeviceAssetMasterDataLookup
{
    public static async Task<BusinessConsoleMasterDataResourceDetail?> FindAsync(
        string deviceAssetReference,
        IBusinessMasterDataClient masterData,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await masterData.GetResourceDetailAsync(
                internalBearerToken,
                new BusinessConsoleMasterDataResourceRequest(organizationId, environmentId, "device-asset", deviceAssetReference),
                cancellationToken);
        }
        catch (BusinessServiceProxyException exception) when (
            BusinessConsoleReadEnrichmentFailurePolicy.CanDegrade(exception.StatusCode))
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
