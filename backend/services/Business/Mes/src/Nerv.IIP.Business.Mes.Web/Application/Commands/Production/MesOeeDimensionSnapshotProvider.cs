using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record MesOeeDimensionSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterCode,
    string? DeviceAssetId,
    string? ShiftCode);

public sealed record MesOeeDimensionSnapshot(
    string WorkCenterCode,
    string? DeviceAssetId,
    string? SiteCode = null,
    string? WorkshopCode = null,
    string? LineCode = null,
    string? ShiftCode = null,
    string? SiteTimezone = null,
    TimeOnly? ShiftStartsAt = null,
    TimeOnly? ShiftEndsAt = null,
    bool? ShiftCrossesMidnight = null,
    int? ShiftPaidMinutes = null,
    int? ShiftBreakMinutes = null);

public interface IMesOeeDimensionSnapshotProvider
{
    Task<MesOeeDimensionSnapshot> CaptureAsync(
        MesOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class NullMesOeeDimensionSnapshotProvider : IMesOeeDimensionSnapshotProvider
{
    public Task<MesOeeDimensionSnapshot> CaptureAsync(
        MesOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MesOeeDimensionSnapshot(
            request.WorkCenterCode,
            request.DeviceAssetId,
            ShiftCode: request.ShiftCode));
}

public sealed class HttpMesOeeDimensionSnapshotProvider(
    MesMasterDataHttpClient masterDataClient,
    IInternalServiceTokenProvider? internalTokenProvider = null,
    ILogger<HttpMesOeeDimensionSnapshotProvider>? logger = null)
    : IMesOeeDimensionSnapshotProvider
{
    public async Task<MesOeeDimensionSnapshot> CaptureAsync(
        MesOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var fallback = new MesOeeDimensionSnapshot(
            request.WorkCenterCode,
            Normalize(request.DeviceAssetId),
            ShiftCode: Normalize(request.ShiftCode));
        if (fallback.DeviceAssetId is null)
        {
            return fallback;
        }

        var deviceResponse = await ListAsync(
            request,
            "device-asset",
            ("keyword", fallback.DeviceAssetId),
            cancellationToken);
        var devices = deviceResponse?.Resources.Where(x =>
            x.Active &&
            (string.Equals(x.Code, fallback.DeviceAssetId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.DeviceAssetId, fallback.DeviceAssetId, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToArray();
        var device = devices is { Length: 1 } ? devices[0] : null;
        if (device is null)
        {
            return fallback;
        }

        var siteCode = Normalize(device.SiteCode);
        var shiftCode = Normalize(request.ShiftCode);
        var siteTask = siteCode is null
            ? Task.FromResult<OeeMasterDataResourceListResponse?>(null)
            : ListAsync(request, "site", ("siteCode", siteCode), cancellationToken);
        var shiftTask = shiftCode is null
            ? Task.FromResult<OeeMasterDataResourceListResponse?>(null)
            : ListAsync(request, "shift", ("shiftCode", shiftCode), cancellationToken);
        await Task.WhenAll(siteTask, shiftTask);

        var sites = siteTask.Result?.Resources.Where(x =>
                x.Active && string.Equals(x.Code, siteCode, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        var shifts = shiftTask.Result?.Resources.Where(x =>
                x.Active && string.Equals(x.Code, shiftCode, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        var site = sites is { Length: 1 } ? sites[0] : null;
        var shift = shifts is { Length: 1 } ? shifts[0] : null;
        return new MesOeeDimensionSnapshot(
            Normalize(device.WorkCenterCode) ?? request.WorkCenterCode,
            fallback.DeviceAssetId,
            siteCode,
            Normalize(device.WorkshopCode),
            Normalize(device.LineCode),
            shiftCode,
            Normalize(site?.Timezone),
            shift?.StartsAt,
            shift?.EndsAt,
            shift?.CrossesMidnight,
            shift?.PaidMinutes,
            shift?.BreakMinutes);
    }

    private async Task<OeeMasterDataResourceListResponse?> ListAsync(
        MesOeeDimensionSnapshotRequest request,
        string resourceType,
        (string Name, string Value) filter,
        CancellationToken cancellationToken)
    {
        var requestUri = "/api/business/v1/master-data/resources?" + string.Join('&', new[]
        {
            Pair("organizationId", request.OrganizationId),
            Pair("environmentId", request.EnvironmentId),
            Pair("resourceType", resourceType),
            Pair(filter.Name, filter.Value),
            Pair("all", true.ToString(CultureInfo.InvariantCulture)),
        });
        using var message = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var token = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var response = await masterDataClient.HttpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning(
                    "MasterData OEE dimension snapshot request for {ResourceType} returned HTTP {StatusCode}; MES will record an explicit partial snapshot.",
                    resourceType,
                    (int)response.StatusCode);
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<OeeResponseDataEnvelope<OeeMasterDataResourceListResponse>>(cancellationToken);
            if (envelope?.Data is not { Truncated: false } data)
            {
                logger?.LogWarning(
                    "MasterData OEE dimension snapshot response for {ResourceType} was empty or truncated; MES will record an explicit partial snapshot.",
                    resourceType);
                return null;
            }

            return data;
        }
        catch (HttpRequestException exception)
        {
            logger?.LogWarning(exception, "MasterData OEE dimension snapshot request for {ResourceType} failed; MES will record an explicit partial snapshot.", resourceType);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(exception, "MasterData OEE dimension snapshot request for {ResourceType} timed out; MES will record an explicit partial snapshot.", resourceType);
            return null;
        }
    }

    private static string Pair(string name, string value) =>
        $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record OeeResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed record OeeMasterDataResourceListResponse(
        IReadOnlyCollection<OeeMasterDataResourceItem> Resources,
        int Total,
        bool Truncated = false,
        int? Limit = null);

    private sealed record OeeMasterDataResourceItem(
        string ResourceType,
        string Code,
        string DisplayName,
        bool Active,
        string SnapshotVersion,
        string? SiteCode = null,
        string? LineCode = null,
        string? WorkshopCode = null,
        string? WorkCenterCode = null,
        string? DeviceAssetId = null,
        string? Timezone = null,
        TimeOnly? StartsAt = null,
        TimeOnly? EndsAt = null,
        bool? CrossesMidnight = null,
        int? PaidMinutes = null,
        int? BreakMinutes = null);
}
