using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.Mes.Web.Application.Planning;

/// <summary>
/// 设备 → 工作中心的归属由 BusinessMasterData 拥有（<c>docs/architecture/business/equipment-status-event-flow.md</c>）。
/// MES 不再保存本地映射表（#3878 退役 <c>device_asset_work_center_mappings</c>），需要时直接向主数据查询设备当前的工作中心。
/// </summary>
public interface IMesDeviceWorkCenterResolver
{
    /// <summary>
    /// 返回主数据里该设备当前归属的工作中心编码；设备在主数据里不存在、无法唯一确定或没有工作中心时返回 <c>null</c>。
    /// 主数据不可达、超时或返回异常应答时抛出 <see cref="MesMasterDataUnavailableException"/>，
    /// 由消息系统按瞬时故障重试，调用方不得把它当成「没有归属」处理。
    /// </summary>
    Task<string?> ResolveAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken);
}

/// <summary>主数据暂时不可用（网络、超时、非预期应答）；属于瞬时故障，调用方应让它向上抛出以触发重试。</summary>
public sealed class MesMasterDataUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class HttpMesDeviceWorkCenterResolver(
    MesMasterDataHttpClient masterDataClient,
    IInternalServiceTokenProvider internalTokenProvider,
    ILogger<HttpMesDeviceWorkCenterResolver> logger)
    : IMesDeviceWorkCenterResolver
{
    public async Task<string?> ResolveAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceAssetId);

        var uri = "/api/business/v1/master-data/resources?" + string.Join(
            "&",
            new[]
            {
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("resourceType", "device-asset"),
                ("deviceAssetId", deviceAssetId.Trim()),
                ("all", "true"),
            }.Select(x => $"{Uri.EscapeDataString(x.Item1)}={Uri.EscapeDataString(x.Item2)}"));
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        HttpResponseMessage response;
        try
        {
            response = await masterDataClient.HttpClient.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new MesMasterDataUnavailableException("MasterData is unavailable while resolving device work center.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MesMasterDataUnavailableException("MasterData timed out while resolving device work center.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsAmbiguous(body))
                {
                    return Unresolved(organizationId, environmentId, deviceAssetId, "ambiguous");
                }

                throw new MesMasterDataUnavailableException(
                    $"MasterData returned HTTP {(int)response.StatusCode} while resolving device work center.");
            }

            ResponseData<ListMasterDataResourcesResponse>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<ResponseData<ListMasterDataResourcesResponse>>(cancellationToken);
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new MesMasterDataUnavailableException("MasterData returned an unreadable device resource response.", exception);
            }

            if (envelope?.Success is false)
            {
                if (IsAmbiguous(envelope.Message))
                {
                    return Unresolved(organizationId, environmentId, deviceAssetId, "ambiguous");
                }

                throw new MesMasterDataUnavailableException("MasterData rejected the device resource query.");
            }

            var data = envelope?.Data;
            if (data?.Resources is null || data.Truncated)
            {
                throw new MesMasterDataUnavailableException("MasterData returned an incomplete device resource response.");
            }

            if (data.Resources.Count == 0)
            {
                return Unresolved(organizationId, environmentId, deviceAssetId, "not-found");
            }

            if (data.Resources.Count != 1)
            {
                return Unresolved(organizationId, environmentId, deviceAssetId, "ambiguous");
            }

            var workCenterCode = data.Resources.Single()?.WorkCenterCode;
            return string.IsNullOrWhiteSpace(workCenterCode)
                ? Unresolved(organizationId, environmentId, deviceAssetId, "work-center-missing")
                : workCenterCode.Trim();
        }
    }

    private string? Unresolved(string organizationId, string environmentId, string deviceAssetId, string reason)
    {
        logger.LogDebug(
            "MasterData has no unique work center for device {DeviceAssetId} in {OrganizationId}/{EnvironmentId}: {Reason}.",
            deviceAssetId,
            organizationId,
            environmentId,
            reason);
        return null;
    }

    private static bool IsAmbiguous(string? message) =>
        message?.Contains("唯一", StringComparison.Ordinal) == true ||
        message?.Contains("多条", StringComparison.Ordinal) == true ||
        message?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("multiple", StringComparison.OrdinalIgnoreCase) == true;
}
