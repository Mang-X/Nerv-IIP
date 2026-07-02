using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

public sealed record MesMaterialRequirementSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal WorkOrderQuantity,
    DateTimeOffset CapturedAtUtc);

public sealed record MesMaterialRequirementSnapshotLine(
    string? OperationTaskId,
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    string UomCode,
    decimal AvailableQuantity,
    decimal StagedQuantity,
    string SourceSnapshotId);

public enum MesMaterialRequirementSnapshotStatus
{
    Captured,
    NoRequirements,
    Missing,
}

public sealed record MesMaterialRequirementSnapshotResult(
    MesMaterialRequirementSnapshotStatus Status,
    string SourceSystem,
    IReadOnlyCollection<MesMaterialRequirementSnapshotLine> Lines)
{
    public static MesMaterialRequirementSnapshotResult Captured(
        string sourceSystem,
        IReadOnlyCollection<MesMaterialRequirementSnapshotLine> lines) =>
        new(MesMaterialRequirementSnapshotStatus.Captured, sourceSystem, lines);

    public static MesMaterialRequirementSnapshotResult NoRequirements(string sourceSystem) =>
        new(MesMaterialRequirementSnapshotStatus.NoRequirements, sourceSystem, []);

    public static MesMaterialRequirementSnapshotResult Missing(string sourceSystem) =>
        new(MesMaterialRequirementSnapshotStatus.Missing, sourceSystem, []);
}

public interface IMesMaterialRequirementSnapshotProvider
{
    Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class NullMesMaterialRequirementSnapshotProvider : IMesMaterialRequirementSnapshotProvider
{
    public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesMaterialRequirementSnapshotResult.Missing("product-engineering:not-configured"));
    }
}

public sealed class MesProductEngineeringHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}

public sealed class MesInventoryHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}

public sealed class MesMasterDataHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}

public sealed class MesMaterialRequirementInventoryOptions
{
    public string DefaultSiteCode { get; init; } = "production";
    public IReadOnlyCollection<string>? SiteCodes { get; init; }
    public TimeSpan UomConversionCacheTtl { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
    MesProductEngineeringHttpClient productEngineeringClient,
    MesInventoryHttpClient inventoryClient,
    MesMasterDataHttpClient? masterDataClient = null,
    MesMaterialRequirementInventoryOptions? inventoryOptions = null,
    IInternalServiceTokenProvider? internalTokenProvider = null,
    ILogger<HttpMesProductEngineeringMaterialRequirementSnapshotProvider>? logger = null,
    IMemoryCache? uomConversionCache = null)
    : IMesMaterialRequirementSnapshotProvider
{
    private const string ActiveProductionVersionStatus = "active";
    private const string PublishedEngineeringStatus = "published";
    private readonly MesMaterialRequirementInventoryOptions inventoryOptions = inventoryOptions ?? new MesMaterialRequirementInventoryOptions();

    public HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
        MesProductEngineeringHttpClient productEngineeringClient,
        MesInventoryHttpClient inventoryClient,
        MesMaterialRequirementInventoryOptions? inventoryOptions = null,
        IInternalServiceTokenProvider? internalTokenProvider = null)
        : this(productEngineeringClient, inventoryClient, null, inventoryOptions, internalTokenProvider, null, null)
    {
    }

    public async Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            return MesMaterialRequirementSnapshotResult.Missing("product-engineering:missing-production-version");
        }

        var selectedVersion = await SendOptionalAsync<ResolveProductionVersionResponse>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            "/api/business/v1/engineering/production-versions/resolve?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuId),
                ("effectiveDate", DateOnly.FromDateTime(request.CapturedAtUtc.UtcDateTime)),
                ("lotSize", request.WorkOrderQuantity)),
            cancellationToken);
        if (selectedVersion is null)
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:production-version:{request.ProductionVersionId}");
        }

        if (!string.Equals(selectedVersion.ProductionVersionId, request.ProductionVersionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedVersion.Status, ActiveProductionVersionStatus, StringComparison.OrdinalIgnoreCase))
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:production-version:{request.ProductionVersionId}");
        }

        if (!TryParseVersionReference(selectedVersion.MbomVersionId, out var bomCode, out var revision))
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:mbom:{selectedVersion.MbomVersionId}");
        }

        var selectedBom = await SendAsync<ManufacturingBomListItem>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            $"/api/business/v1/engineering/manufacturing-boms/{EscapePath(bomCode)}/{EscapePath(revision)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            cancellationToken);
        if (!string.Equals(selectedBom.SkuCode, request.SkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectedBom.Status, PublishedEngineeringStatus, StringComparison.OrdinalIgnoreCase))
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:mbom:{selectedVersion.MbomVersionId}");
        }

        var requiredLines = SelectMaterialLines(selectedBom.MaterialLines)
            .Where(x => !x.IsPhantom)
            .Select(x =>
            {
                var yieldRate = x.YieldRate <= 0m ? 1m : x.YieldRate;
                var requiredQuantity = request.WorkOrderQuantity * x.Quantity * (1m + x.ScrapRate) / yieldRate;
                return new MaterialRequirementLineDraft(x.SkuCode, x.UnitOfMeasureCode, requiredQuantity);
            })
            .GroupBy(x => $"{x.MaterialId.ToUpperInvariant()}|{x.UomCode.ToUpperInvariant()}", StringComparer.Ordinal)
            .Select(x => new MaterialRequirementLineDraft(
                x.First().MaterialId,
                x.First().UomCode,
                x.Sum(y => y.RequiredQuantity)))
            .ToArray();
        if (requiredLines.Length == 0)
        {
            return MesMaterialRequirementSnapshotResult.NoRequirements($"product-engineering-http:{selectedVersion.ProductionVersionId}:{selectedVersion.MbomVersionId}");
        }

        var conversions = await GetUomConversionsAsync(request, requiredLines, cancellationToken);
        var lines = new List<MesMaterialRequirementSnapshotLine>(requiredLines.Length);
        foreach (var line in requiredLines)
        {
            var availableQuantity = await GetAvailableQuantityAsync(
                request,
                line.MaterialId,
                line.UomCode,
                conversions,
                cancellationToken);
            lines.Add(new MesMaterialRequirementSnapshotLine(
                null,
                line.MaterialId,
                null,
                line.RequiredQuantity,
                line.UomCode,
                availableQuantity,
                0m,
                $"{selectedVersion.MbomVersionId}:{line.MaterialId}"));
        }

        return MesMaterialRequirementSnapshotResult.Captured($"product-engineering-http:{selectedVersion.ProductionVersionId}:{selectedVersion.MbomVersionId}", lines);
    }

    private async Task<decimal> GetAvailableQuantityAsync(
        MesMaterialRequirementSnapshotRequest request,
        string materialId,
        string uomCode,
        IReadOnlyCollection<MesUomConversionSnapshot> conversions,
        CancellationToken cancellationToken)
    {
        var availableQuantity = 0m;
        var candidates = GetInventoryUomCandidates(uomCode, conversions);
        var siteCodes = GetSiteCodes();
        // Availability currently uses Inventory's exact GET contract; batch API work is intentionally left out of this #460 fix.
        foreach (var candidate in candidates)
        {
            foreach (var siteCode in siteCodes)
            {
                var availability = await SendAsync<StockAvailabilityResponse>(
                    inventoryClient.HttpClient,
                    "Inventory",
                    "/api/inventory/v1/availability?" + Query(
                        ("organizationId", request.OrganizationId),
                        ("environmentId", request.EnvironmentId),
                        ("skuCode", materialId),
                        ("uomCode", candidate.InventoryUomCode),
                        ("siteCode", siteCode)),
                    cancellationToken);
                availableQuantity += candidate.ToRequiredUom(Math.Max(0m, availability.AvailableQuantity));
            }
        }

        if (availableQuantity <= 0m)
        {
            logger?.LogWarning(
                "MES material availability returned zero availability for material {MaterialId} required UOM {UomCode}; queried sites {SiteCodes} and inventory UOM candidates {InventoryUomCodes}.",
                materialId,
                uomCode,
                string.Join(',', siteCodes),
                string.Join(',', candidates.Select(x => x.InventoryUomCode)));
        }

        return Math.Max(0m, availableQuantity);
    }

    private async Task<IReadOnlyCollection<MesUomConversionSnapshot>> GetUomConversionsAsync(
        MesMaterialRequirementSnapshotRequest request,
        IReadOnlyCollection<MaterialRequirementLineDraft> requiredLines,
        CancellationToken cancellationToken)
    {
        if (masterDataClient is null)
        {
            return [];
        }

        var requiredUoms = requiredLines
            .Select(x => NormalizeCode(x.UomCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capturedDate = DateOnly.FromDateTime(request.CapturedAtUtc.UtcDateTime);
        var cacheKey = $"mes-material-uom-conversions:{request.OrganizationId}:{request.EnvironmentId}:{capturedDate:O}";
        if (uomConversionCache is not null &&
            uomConversionCache.TryGetValue(cacheKey, out IReadOnlyCollection<MesUomConversionSnapshot>? cachedConversions) &&
            cachedConversions is not null)
        {
            return FilterRequiredConversions(cachedConversions, requiredUoms);
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
            .Where(x => (x.EffectiveFrom ?? DateOnly.MinValue) <= capturedDate)
            .Where(x => x.EffectiveTo is null || x.EffectiveTo.Value >= capturedDate)
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
                Math.Max(0, x.Precision ?? 0),
                string.IsNullOrWhiteSpace(x.RoundingMode) ? "half-up" : x.RoundingMode))
            .ToArray();

        uomConversionCache?.Set(
            cacheKey,
            allConversions,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = inventoryOptions.UomConversionCacheTtl });

        return FilterRequiredConversions(allConversions, requiredUoms);
    }

    private static IReadOnlyCollection<MesUomConversionSnapshot> FilterRequiredConversions(
        IReadOnlyCollection<MesUomConversionSnapshot> conversions,
        HashSet<string> requiredUoms)
    {
        return conversions
            .Where(x => requiredUoms.Contains(NormalizeCode(x.FromUomCode)) || requiredUoms.Contains(NormalizeCode(x.ToUomCode)))
            .ToArray();
    }

    private IReadOnlyCollection<string> GetSiteCodes()
    {
        var configured = inventoryOptions.SiteCodes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configured is { Length: > 0 })
        {
            return configured;
        }

        return [inventoryOptions.DefaultSiteCode];
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
                    quantity => ConvertForward(quantity, conversion)));
            }

            if (NormalizeCode(conversion.FromUomCode) == required)
            {
                candidates.Add(new InventoryUomCandidate(
                    conversion.ToUomCode,
                    quantity => ConvertInverse(quantity, conversion)));
            }
        }

        return candidates
            .GroupBy(x => NormalizeCode(x.InventoryUomCode), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static decimal ConvertForward(decimal quantity, MesUomConversionSnapshot conversion)
    {
        return FloorAvailability(quantity * conversion.Factor + conversion.Offset, conversion.Precision);
    }

    private static decimal ConvertInverse(decimal quantity, MesUomConversionSnapshot conversion)
    {
        return FloorAvailability((quantity - conversion.Offset) / conversion.Factor, conversion.Precision);
    }

    private static decimal FloorAvailability(decimal value, int precision)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        var digits = Math.Clamp(precision, 0, 12);
        var scale = (decimal)Math.Pow(10, digits);
        return Math.Floor(value * scale) / scale;
    }

    private async Task<T> SendAsync<T>(HttpClient client, string serviceName, string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendRequestAsync(client, serviceName, requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        return envelope?.Data ?? throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务返回空响应。");
    }

    private async Task<T?> SendOptionalAsync<T>(HttpClient client, string serviceName, string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendRequestAsync(client, serviceName, requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KnownException($"MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE: {serviceName} 物料齐套来源服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        return envelope?.Data;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpClient client, string serviceName, string requestUri, CancellationToken cancellationToken)
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
        return response;
    }

    private static IReadOnlyCollection<ManufacturingBomMaterialLineItem> SelectMaterialLines(
        IReadOnlyCollection<ManufacturingBomMaterialLineItem> materialLines)
    {
        var concreteLines = materialLines.Where(x => !x.IsPhantom).ToArray();
        var standalone = concreteLines.Where(x => string.IsNullOrWhiteSpace(x.AlternateGroup));
        var selectedAlternates = concreteLines
            .Where(x => !string.IsNullOrWhiteSpace(x.AlternateGroup))
            .GroupBy(x => x.AlternateGroup!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x
                .OrderBy(line => line.AlternatePriority ?? int.MaxValue)
                .ThenBy(line => line.SkuCode, StringComparer.OrdinalIgnoreCase)
                .First());
        return standalone.Concat(selectedAlternates).ToArray();
    }

    private static bool TryParseVersionReference(string versionId, out string code, out string revision)
    {
        code = string.Empty;
        revision = string.Empty;
        var parts = versionId.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        code = parts[0];
        revision = parts[1];
        return true;
    }

    private static string EscapePath(string value) => Uri.EscapeDataString(value);

    private static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}

internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

internal sealed record ResolveProductionVersionResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly EffectiveDate,
    decimal LotSize,
    string Status);

internal sealed record ListManufacturingBomsResponse(IReadOnlyCollection<ManufacturingBomListItem> Items);

internal sealed record ManufacturingBomListItem(
    string BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomVersionId,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineItem> MaterialLines,
    IReadOnlyCollection<ManufacturingBomRecipeLineItem> RecipeLines);

internal sealed record ManufacturingBomMaterialLineItem(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    decimal YieldRate = 1m,
    bool Backflush = false);

internal sealed record ManufacturingBomRecipeLineItem(string ParameterCode, string TargetValue, string UnitOfMeasureCode);

internal sealed record StockAvailabilityResponse(decimal AvailableQuantity);

internal sealed record MaterialRequirementLineDraft(string MaterialId, string UomCode, decimal RequiredQuantity);

internal sealed record MasterDataResourceListResponse(
    IReadOnlyCollection<MasterDataResourceListItem> Resources,
    int Total,
    bool Truncated = false,
    int? Limit = null);

internal sealed record MasterDataResourceListItem(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion,
    string? PartnerType = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? SiteCode = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? WorkCenterCode = null,
    string? Status = null,
    string? Category = null,
    string? MaterialType = null,
    string? CodeSet = null,
    string? BaseUomCode = null,
    string? TaxId = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    string? UserId = null,
    string? SkillCode = null,
    string? SkillLevel = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? FromUomCode = null,
    string? ToUomCode = null,
    decimal? Factor = null,
    decimal? Offset = null,
    int? Precision = null,
    string? RoundingMode = null);

internal sealed record MesUomConversionSnapshot(
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode);

internal sealed record InventoryUomCandidate(string InventoryUomCode, Func<decimal, decimal> ToRequiredUom);
