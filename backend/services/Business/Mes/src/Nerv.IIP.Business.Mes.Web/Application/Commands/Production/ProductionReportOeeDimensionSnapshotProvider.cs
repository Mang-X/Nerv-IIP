using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record ProductionReportOeeDimensionSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetReference,
    string WorkCenterId,
    string? ShiftCode);

public interface IProductionReportOeeDimensionSnapshotProvider
{
    Task<ProductionReportOeeDimensionSnapshot> CaptureAsync(
        ProductionReportOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpProductionReportOeeDimensionSnapshotProvider(
    MesMasterDataHttpClient masterDataClient,
    IInternalServiceTokenProvider internalTokenProvider)
    : IProductionReportOeeDimensionSnapshotProvider
{
    public async Task<ProductionReportOeeDimensionSnapshot> CaptureAsync(
        ProductionReportOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var rawDeviceReference = Normalize(request.DeviceAssetReference);
        if (rawDeviceReference is null)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                "device-missing");
        }

        var deviceResult = await GetResourceAsync(
            request,
            "device-asset",
            ("deviceAssetId", rawDeviceReference),
            cancellationToken);
        if (!deviceResult.IsSuccess)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                deviceResult.FailureReason!);
        }

        var device = deviceResult.Resource!;
        if (!Guid.TryParse(device.DeviceAssetId, out var canonicalDeviceId))
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                "device-invalid-canonical-id");
        }

        var workCenterCode = Normalize(device.WorkCenterCode);
        if (workCenterCode is null)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                "device-work-center-missing");
        }

        var siteCode = Normalize(device.SiteCode);
        if (siteCode is null)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                "device-site-missing");
        }

        var siteResult = await GetResourceAsync(
            request,
            "site",
            ("siteCode", siteCode),
            cancellationToken);
        if (!siteResult.IsSuccess)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                Prefix("site", siteResult.FailureReason!));
        }

        var siteTimezone = Normalize(siteResult.Resource!.Timezone);
        if (siteTimezone is null)
        {
            return ProductionReportOeeDimensionSnapshot.Degraded(
                rawDeviceReference,
                request.WorkCenterId,
                "site-timezone-missing");
        }

        MasterDataResourceItem? shift = null;
        var shiftCode = Normalize(request.ShiftCode);
        if (shiftCode is not null)
        {
            var shiftResult = await GetResourceAsync(
                request,
                "shift",
                ("shiftCode", shiftCode),
                cancellationToken);
            if (!shiftResult.IsSuccess)
            {
                return ProductionReportOeeDimensionSnapshot.Degraded(
                    rawDeviceReference,
                    request.WorkCenterId,
                    Prefix("shift", shiftResult.FailureReason!));
            }

            shift = shiftResult.Resource;
            if (shift!.StartsAt is null ||
                shift.EndsAt is null ||
                shift.CrossesMidnight is null ||
                shift.PaidMinutes is null ||
                shift.BreakMinutes is null)
            {
                return ProductionReportOeeDimensionSnapshot.Degraded(
                    rawDeviceReference,
                    request.WorkCenterId,
                    "shift-definition-invalid");
            }
        }

        return ProductionReportOeeDimensionSnapshot.Resolved(
            canonicalDeviceId.ToString(),
            workCenterCode,
            siteCode,
            Normalize(device.WorkshopCode),
            Normalize(device.LineCode),
            siteTimezone,
            shiftCode,
            shift?.StartsAt,
            shift?.EndsAt,
            shift?.CrossesMidnight,
            shift?.PaidMinutes,
            shift?.BreakMinutes);
    }

    private async Task<ResourceResult> GetResourceAsync(
        ProductionReportOeeDimensionSnapshotRequest request,
        string resourceType,
        (string Name, string Value) exactFilter,
        CancellationToken cancellationToken)
    {
        var uri = "/api/business/v1/master-data/resources?" + Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("resourceType", resourceType),
            (exactFilter.Name, exactFilter.Value),
            ("all", true));
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        HttpResponseMessage response;
        try
        {
            response = await masterDataClient.HttpClient.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ResourceResult.Failed("master-data-unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResourceResult.Failed("master-data-timeout");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ResourceResult.Failed(
                    IsAmbiguous(body)
                        ? "ambiguous"
                        : $"master-data-http-{(int)response.StatusCode}");
            }

            ResponseData<ListMasterDataResourcesResponse>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<ResponseData<ListMasterDataResourcesResponse>>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                return ResourceResult.Failed("master-data-invalid-response");
            }

            if (envelope?.Success is false)
            {
                return ResourceResult.Failed(
                    IsAmbiguous(envelope.Message)
                        ? "ambiguous"
                        : "master-data-invalid-response");
            }

            var data = envelope?.Data;
            if (data?.Resources is null)
            {
                return ResourceResult.Failed("master-data-invalid-response");
            }

            if (data.Truncated)
            {
                return ResourceResult.Failed("truncated");
            }

            if (data.Total == 0 && data.Resources.Count == 0)
            {
                return ResourceResult.Failed("not-found");
            }

            if (data.Total != data.Resources.Count)
            {
                return ResourceResult.Failed("master-data-invalid-response");
            }

            if (data.Total != 1)
            {
                return ResourceResult.Failed("ambiguous");
            }

            var resource = data.Resources.Single();
            return resource is null
                ? ResourceResult.Failed("master-data-invalid-response")
                : ResourceResult.Succeeded(resource);
        }
    }

    private static string Query(params (string Name, object? Value)[] values) =>
        string.Join(
            "&",
            values
                .Where(x => x.Value is not null)
                .Select(x =>
                {
                    var text = x.Value is bool flag
                        ? flag.ToString().ToLowerInvariant()
                        : Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)!;
                    return $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(text)}";
                }));

    private static string Prefix(string resource, string failureReason) =>
        failureReason.StartsWith("master-data-", StringComparison.Ordinal)
            ? failureReason
            : $"{resource}-{failureReason}";

    private static bool IsAmbiguous(string? message) =>
        message?.Contains("唯一", StringComparison.Ordinal) == true ||
        message?.Contains("多条", StringComparison.Ordinal) == true ||
        message?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("multiple", StringComparison.OrdinalIgnoreCase) == true;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ResourceResult(MasterDataResourceItem? Resource, string? FailureReason)
    {
        public bool IsSuccess => Resource is not null;

        public static ResourceResult Succeeded(MasterDataResourceItem resource) => new(resource, null);

        public static ResourceResult Failed(string reason) => new(null, reason);
    }

}
