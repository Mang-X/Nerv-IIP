using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public sealed record MesMaterialAvailabilityReadRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> MaterialIds,
    string UomCode,
    string? MaterialLotId,
    DateOnly EffectiveDate);

public sealed record MesMaterialAvailabilityReadResult(
    bool SourceAvailable,
    decimal AvailableQuantity,
    string? FailureMessage = null);

public interface IMesMaterialAvailabilityReader
{
    Task<MesMaterialAvailabilityReadResult> ReadAsync(
        MesMaterialAvailabilityReadRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpMesMaterialAvailabilityReader(
    MesInventoryHttpClient inventoryClient,
    MesMasterDataHttpClient? masterDataClient = null,
    MesMaterialRequirementInventoryOptions? inventoryOptions = null,
    IInternalServiceTokenProvider? internalTokenProvider = null,
    ILogger<HttpMesMaterialAvailabilityReader>? logger = null,
    IMemoryCache? uomConversionCache = null)
    : IMesMaterialAvailabilityReader
{
    private const int MaxConcurrentInventoryAvailabilityRequests = 8;
    private readonly MesMaterialRequirementInventoryOptions inventoryOptions = inventoryOptions ?? new();
    private readonly SemaphoreSlim inventoryThrottle = new(MaxConcurrentInventoryAvailabilityRequests);

    public async Task<MesMaterialAvailabilityReadResult> ReadAsync(
        MesMaterialAvailabilityReadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var conversions = await GetUomConversionsAsync(request, cancellationToken);
            var candidates = GetInventoryUomCandidates(request.UomCode, conversions);
            var siteCodes = GetSiteCodes();
            var quantities = await Task.WhenAll(request.MaterialIds.SelectMany(materialId =>
                candidates.SelectMany(candidate => siteCodes.Select(async siteCode =>
                {
                    await inventoryThrottle.WaitAsync(cancellationToken);
                    try
                    {
                        var availability = await SendAsync<StockAvailabilityResponse>(
                            inventoryClient.HttpClient,
                            "Inventory",
                            "/api/inventory/v1/availability?" + Query(
                                ("organizationId", request.OrganizationId),
                                ("environmentId", request.EnvironmentId),
                                ("skuCode", materialId),
                                ("uomCode", candidate.InventoryUomCode),
                                ("siteCode", siteCode),
                                ("lotNo", request.MaterialLotId)),
                            cancellationToken);
                        return candidate.ToRequiredUom(Math.Max(0m, availability.AvailableQuantity));
                    }
                    finally
                    {
                        inventoryThrottle.Release();
                    }
                }))));
            var availableQuantity = Math.Max(0m, quantities.Sum());
            if (availableQuantity <= 0m)
            {
                logger?.LogWarning(
                    "MES material availability returned zero availability for materials {MaterialIds} required UOM {UomCode}; queried sites {SiteCodes} and inventory UOM candidates {InventoryUomCodes}.",
                    string.Join(',', request.MaterialIds),
                    request.UomCode,
                    string.Join(',', siteCodes),
                    string.Join(',', candidates.Select(x => x.InventoryUomCode)));
            }

            return new MesMaterialAvailabilityReadResult(true, availableQuantity);
        }
        catch (KnownException exception)
        {
            return new MesMaterialAvailabilityReadResult(false, 0m, exception.Message);
        }
        catch (JsonException)
        {
            return new MesMaterialAvailabilityReadResult(false, 0m);
        }
        catch (NotSupportedException)
        {
            return new MesMaterialAvailabilityReadResult(false, 0m);
        }
    }

    private async Task<IReadOnlyCollection<MesUomConversionSnapshot>> GetUomConversionsAsync(
        MesMaterialAvailabilityReadRequest request,
        CancellationToken cancellationToken)
    {
        if (masterDataClient is null)
        {
            return [];
        }

        var cacheKey = $"mes-material-uom-conversions:{request.OrganizationId}:{request.EnvironmentId}:{request.EffectiveDate:O}";
        if (uomConversionCache is not null &&
            uomConversionCache.TryGetValue(cacheKey, out IReadOnlyCollection<MesUomConversionSnapshot>? cachedConversions) &&
            cachedConversions is not null)
        {
            return FilterRequiredConversions(cachedConversions, request.UomCode);
        }

        var response = await SendAsync<MasterDataResourceListResponse>(
            masterDataClient.HttpClient,
            "MasterData",
            "/api/business/v1/master-data/resources?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("resourceType", "uom-conversion"),
                ("all", true)),
            cancellationToken);
        if (response.Truncated)
        {
            var limit = response.Limit ?? response.Resources.Count;
            throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: MasterData UOM conversion list was truncated at {limit} of {response.Total}; MES cannot reliably normalize material availability.");
        }

        var allConversions = response.Resources
            .Where(x => x.Active)
            .Where(x => !string.IsNullOrWhiteSpace(x.FromUomCode) && !string.IsNullOrWhiteSpace(x.ToUomCode))
            .Where(x => x.Factor is > 0m)
            .Where(x => (x.EffectiveFrom ?? DateOnly.MinValue) <= request.EffectiveDate)
            .Where(x => x.EffectiveTo is null || x.EffectiveTo.Value >= request.EffectiveDate)
            .GroupBy(x => $"{NormalizeCode(x.FromUomCode!)}\u001f{NormalizeCode(x.ToUomCode!)}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x
                .OrderByDescending(y => y.EffectiveFrom ?? DateOnly.MinValue)
                .ThenBy(y => y.SnapshotVersion, StringComparer.Ordinal)
                .ThenBy(y => y.Code, StringComparer.Ordinal)
                .First())
            .Select(x => new MesUomConversionSnapshot(
                x.FromUomCode!,
                x.ToUomCode!,
                x.Factor!.Value,
                x.Offset ?? 0m,
                Math.Max(0, x.Precision ?? 0)))
            .ToArray();

        uomConversionCache?.Set(
            cacheKey,
            allConversions,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = inventoryOptions.UomConversionCacheTtl });
        return FilterRequiredConversions(allConversions, request.UomCode);
    }

    private static IReadOnlyCollection<MesUomConversionSnapshot> FilterRequiredConversions(
        IReadOnlyCollection<MesUomConversionSnapshot> conversions,
        string requiredUomCode)
    {
        var required = NormalizeCode(requiredUomCode);
        return conversions
            .Where(x => required == NormalizeCode(x.FromUomCode) || required == NormalizeCode(x.ToUomCode))
            .ToArray();
    }

    private IReadOnlyCollection<string> GetSiteCodes()
    {
        var configured = inventoryOptions.SiteCodes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return configured is { Length: > 0 } ? configured : [inventoryOptions.DefaultSiteCode];
    }

    private static IReadOnlyCollection<InventoryUomCandidate> GetInventoryUomCandidates(
        string requiredUomCode,
        IReadOnlyCollection<MesUomConversionSnapshot> conversions)
    {
        var required = NormalizeCode(requiredUomCode);
        var candidates = new List<InventoryUomCandidate>
        {
            new(requiredUomCode, static quantity => quantity),
        };
        foreach (var conversion in conversions)
        {
            if (NormalizeCode(conversion.ToUomCode) == required)
            {
                candidates.Add(new InventoryUomCandidate(
                    conversion.FromUomCode,
                    quantity => FloorAvailability(quantity * conversion.Factor + conversion.Offset, conversion.Precision)));
            }

            if (NormalizeCode(conversion.FromUomCode) == required)
            {
                candidates.Add(new InventoryUomCandidate(
                    conversion.ToUomCode,
                    quantity => FloorAvailability((quantity - conversion.Offset) / conversion.Factor, conversion.Precision)));
            }
        }

        return candidates
            .GroupBy(x => NormalizeCode(x.InventoryUomCode), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static decimal FloorAvailability(decimal value, int precision)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        var scale = (decimal)Math.Pow(10, Math.Clamp(precision, 0, 12));
        return Math.Floor(value * scale) / scale;
    }

    private async Task<T> SendAsync<T>(
        HttpClient client,
        string serviceName,
        string requestUri,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var token = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务暂不可用。{exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务请求超时。{exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseData<T>>(cancellationToken);
            return envelope?.Data ?? throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务返回空响应。");
        }
    }

    private static string Query(params (string Name, object? Value)[] values) =>
        string.Join('&', values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}"));

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private sealed record StockAvailabilityResponse(decimal AvailableQuantity);

    private sealed record MasterDataResourceListResponse(
        IReadOnlyCollection<MasterDataResourceListItem> Resources,
        int Total,
        bool Truncated = false,
        int? Limit = null);

    private sealed record MasterDataResourceListItem(
        string ResourceType,
        string Code,
        string DisplayName,
        bool Active,
        string SnapshotVersion,
        DateOnly? EffectiveFrom = null,
        DateOnly? EffectiveTo = null,
        string? FromUomCode = null,
        string? ToUomCode = null,
        decimal? Factor = null,
        decimal? Offset = null,
        int? Precision = null);

    private sealed record MesUomConversionSnapshot(
        string FromUomCode,
        string ToUomCode,
        decimal Factor,
        decimal Offset,
        int Precision);

    private sealed record InventoryUomCandidate(
        string InventoryUomCode,
        Func<decimal, decimal> ToRequiredUom);
}
