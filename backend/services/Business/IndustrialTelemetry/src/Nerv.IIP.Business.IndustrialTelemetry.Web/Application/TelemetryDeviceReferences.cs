using Microsoft.Extensions.Caching.Memory;
using Nerv.IIP.Contracts.MasterData;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application;

public sealed record TelemetryDeviceReference(string PublicId, string Code)
{
    public string[] Aliases => PublicId == Code ? [PublicId] : [PublicId, Code];
}

public interface ITelemetryDeviceReferences
{
    Task<TelemetryDeviceReference> ResolveAsync(string organizationId, string environmentId, string reference, CancellationToken cancellationToken);
}

public sealed class MasterDataTelemetryDeviceReferences(HttpClient client, IMemoryCache cache) : ITelemetryDeviceReferences
{
    public async Task<TelemetryDeviceReference> ResolveAsync(
        string organizationId, string environmentId, string reference, CancellationToken cancellationToken)
    {
        var key = (organizationId, environmentId, reference);
        if (cache.TryGetValue<TelemetryDeviceReference>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var query = new QueryString()
            .Add("organizationId", organizationId)
            .Add("environmentId", environmentId)
            .Add("resourceType", "device-asset")
            .Add("deviceAssetId", reference)
            .Add("includeDisabled", "true")
            .Add("all", "true");
        var response = await client.GetFromJsonAsync<ResponseData<ListMasterDataResourcesResponse>>(
            "/api/business/v1/master-data/resources" + query, cancellationToken);
        if (response is not { Success: true, Data.Truncated: false })
        {
            throw new InvalidOperationException("主数据未返回权威设备引用。");
        }

        var device = response.Data.Resources.SingleOrDefault();
        if (device is null)
        {
            // 尚未登记的采集来源继续以原引用存储，待主数据登记后可按编码读回。
            return new(reference, reference);
        }
        if (string.IsNullOrWhiteSpace(device.DeviceAssetId) || string.IsNullOrWhiteSpace(device.Code))
        {
            throw new InvalidOperationException("主数据设备缺少公开标识或编码。");
        }

        var resolved = new TelemetryDeviceReference(device.DeviceAssetId, device.Code);
        foreach (var alias in resolved.Aliases.Append(reference).Distinct(StringComparer.Ordinal))
        {
            cache.Set((organizationId, environmentId, alias), resolved, TimeSpan.FromMinutes(1));
        }
        return resolved;
    }
}
