using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public sealed class MesMaterialRequirementInventoryOptions
{
    public string DefaultSiteCode { get; init; } = "production";
}

public sealed class HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
    MesProductEngineeringHttpClient productEngineeringClient,
    MesInventoryHttpClient inventoryClient,
    MesMaterialRequirementInventoryOptions? inventoryOptions = null,
    IInternalServiceTokenProvider? internalTokenProvider = null)
    : IMesMaterialRequirementSnapshotProvider
{
    private const string ActiveProductionVersionStatus = "active";
    private const string PublishedEngineeringStatus = "published";
    private readonly MesMaterialRequirementInventoryOptions inventoryOptions = inventoryOptions ?? new MesMaterialRequirementInventoryOptions();

    public async Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            return MesMaterialRequirementSnapshotResult.Missing("product-engineering:missing-production-version");
        }

        var productionVersions = await SendAsync<ListProductionVersionsResponse>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuId),
                ("status", ActiveProductionVersionStatus)),
            cancellationToken);
        var selectedVersion = productionVersions.Items
            .FirstOrDefault(x => string.Equals(x.ProductionVersionId, request.ProductionVersionId, StringComparison.OrdinalIgnoreCase));
        if (selectedVersion is null)
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

        var lines = new List<MesMaterialRequirementSnapshotLine>(requiredLines.Length);
        foreach (var line in requiredLines)
        {
            var availableQuantity = await GetAvailableQuantityAsync(
                request,
                line.MaterialId,
                line.UomCode,
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
        CancellationToken cancellationToken)
    {
        var availability = await SendAsync<StockAvailabilityResponse>(
            inventoryClient.HttpClient,
            "Inventory",
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", materialId),
                ("uomCode", uomCode),
                ("siteCode", inventoryOptions.DefaultSiteCode)),
            cancellationToken);
        return Math.Max(0m, availability.AvailableQuantity);
    }

    private async Task<T> SendAsync<T>(HttpClient client, string serviceName, string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var token = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException($"{serviceName} returned an empty response envelope.");
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
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

internal sealed record ListProductionVersionsResponse(IReadOnlyCollection<ProductionVersionListItem> Items);

internal sealed record ProductionVersionListItem(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault,
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
