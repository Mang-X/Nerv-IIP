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

public sealed class HttpMesProductEngineeringMaterialRequirementSnapshotProvider(
    HttpClient httpClient,
    IInternalServiceTokenProvider? internalTokenProvider = null)
    : IMesMaterialRequirementSnapshotProvider
{
    public async Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            return MesMaterialRequirementSnapshotResult.Missing("product-engineering:missing-production-version");
        }

        var productionVersions = await SendAsync<ListProductionVersionsResponse>(
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuId),
                ("status", "active")),
            cancellationToken);
        var selectedVersion = productionVersions.Items
            .FirstOrDefault(x => string.Equals(x.ProductionVersionId, request.ProductionVersionId, StringComparison.OrdinalIgnoreCase));
        if (selectedVersion is null)
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:production-version:{request.ProductionVersionId}");
        }

        var manufacturingBoms = await SendAsync<ListManufacturingBomsResponse>(
            "/api/business/v1/engineering/manufacturing-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuId),
                ("status", "Published"),
                ("take", 500)),
            cancellationToken);
        var selectedBom = manufacturingBoms.Items
            .OrderByDescending(x => string.Equals(x.BomCode, selectedVersion.MbomVersionId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.BomCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (selectedBom is null || !string.Equals(selectedBom.BomCode, selectedVersion.MbomVersionId, StringComparison.OrdinalIgnoreCase))
        {
            return MesMaterialRequirementSnapshotResult.Missing($"product-engineering:mbom:{selectedVersion.MbomVersionId}");
        }

        var lines = selectedBom.MaterialLines
            .Where(x => !x.IsPhantom)
            .Select(x =>
            {
                var yieldRate = x.YieldRate <= 0m ? 1m : x.YieldRate;
                var requiredQuantity = request.WorkOrderQuantity * x.Quantity * (1m + x.ScrapRate) / yieldRate;
                return new MesMaterialRequirementSnapshotLine(
                    null,
                    x.SkuCode,
                    null,
                    requiredQuantity,
                    x.UnitOfMeasureCode,
                    0m,
                    0m,
                    $"{selectedBom.BomCode}:{selectedBom.Revision}:{x.SkuCode}");
            })
            .ToArray();

        return lines.Length == 0
            ? MesMaterialRequirementSnapshotResult.NoRequirements($"product-engineering-http:{selectedVersion.ProductionVersionId}:{selectedBom.BomCode}")
            : MesMaterialRequirementSnapshotResult.Captured($"product-engineering-http:{selectedVersion.ProductionVersionId}:{selectedBom.BomCode}", lines);
    }

    private async Task<T> SendAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var token = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("ProductEngineering returned an empty response envelope.");
    }

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
