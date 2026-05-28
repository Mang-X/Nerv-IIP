using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.ServiceAuth;
using static Nerv.IIP.Business.DemandPlanning.Web.Application.Planning.PlanningHttpQuery;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public interface IPlanningInputSnapshotProvider
{
    Task<PlanningInputSnapshotResult> GetSnapshotAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken);
}

public sealed record PlanningInputSnapshotResult(
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    IReadOnlyCollection<DemandSnapshot> Demands,
    IReadOnlyCollection<InventoryAvailabilitySnapshot> Availability,
    IReadOnlyCollection<ProductionVersionSnapshot> ProductionVersions,
    IReadOnlyCollection<BomComponentSnapshot> BomComponents);

public sealed record PlanningProductEngineeringSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<string> ParentSkuCodes);

public sealed record PlanningProductEngineeringSnapshot(
    string SnapshotSource,
    IReadOnlyCollection<ProductionVersionSnapshot> ProductionVersions,
    IReadOnlyCollection<BomComponentSnapshot> BomComponents);

public interface IPlanningProductEngineeringSnapshotClient
{
    Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
        string internalBearerToken,
        PlanningProductEngineeringSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed record PlanningInventorySnapshotItem(string SkuCode, string UomCode, string SiteCode);

public sealed record PlanningInventorySnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<PlanningInventorySnapshotItem> Items);

public sealed record PlanningInventorySnapshot(
    string SnapshotSource,
    IReadOnlyCollection<InventoryAvailabilitySnapshot> Availability);

public interface IPlanningInventorySnapshotClient
{
    Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
        string internalBearerToken,
        PlanningInventorySnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class DemandPlanningUpstreamInputSnapshotProvider(
    ApplicationDbContext dbContext,
    IPlanningProductEngineeringSnapshotClient productEngineering,
    IPlanningInventorySnapshotClient inventory,
    IInternalServiceTokenProvider? internalTokenProvider = null) : IPlanningInputSnapshotProvider
{
    public async Task<PlanningInputSnapshotResult> GetSnapshotAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken)
    {
        var demands = await LoadDemandsAsync(organizationId, environmentId, horizonStart, horizonEnd, cancellationToken);
        if (demands.Count == 0)
        {
            return new PlanningInputSnapshotResult(
                "product-engineering-http:0",
                "inventory-http:0",
                demands,
                [],
                [],
                []);
        }

        var internalBearerToken = internalTokenProvider?.BearerToken ?? string.Empty;
        var engineering = await productEngineering.GetSnapshotAsync(
            internalBearerToken,
            new PlanningProductEngineeringSnapshotRequest(
                organizationId,
                environmentId,
                horizonStart,
                horizonEnd,
                demands.Select(x => x.SkuCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()),
            cancellationToken);
        var availabilityItems = demands
            .Select(x => new PlanningInventorySnapshotItem(x.SkuCode, x.UomCode, x.SiteCode))
            .Concat(engineering.BomComponents.SelectMany(component => demands
                .Where(demand => string.Equals(demand.SkuCode, component.ParentSkuCode, StringComparison.OrdinalIgnoreCase))
                .Select(demand => new PlanningInventorySnapshotItem(component.ComponentSkuCode, component.ComponentUomCode, demand.SiteCode))))
            .DistinctBy(x => $"{x.SkuCode}\u001f{x.UomCode}\u001f{x.SiteCode}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SiteCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inventorySnapshot = await inventory.GetAvailabilitySnapshotAsync(
            internalBearerToken,
            new PlanningInventorySnapshotRequest(organizationId, environmentId, availabilityItems),
            cancellationToken);

        return new PlanningInputSnapshotResult(
            engineering.SnapshotSource,
            inventorySnapshot.SnapshotSource,
            demands,
            inventorySnapshot.Availability,
            engineering.ProductionVersions,
            engineering.BomComponents);
    }

    private async Task<IReadOnlyCollection<DemandSnapshot>> LoadDemandsAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken)
    {
        return await dbContext.DemandSources
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DueDate >= horizonStart
                && x.DueDate <= horizonEnd)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SourceReference)
            .Select(x => new DemandSnapshot(x.SourceReference, x.SkuCode, x.UomCode, x.SiteCode, x.Quantity, x.DueDate))
            .ToListAsync(cancellationToken);
    }
}

public sealed class HttpPlanningProductEngineeringSnapshotClient(HttpClient httpClient)
    : IPlanningProductEngineeringSnapshotClient
{
    public async Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
        string internalBearerToken,
        PlanningProductEngineeringSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(request.ParentSkuCodes.Select(skuCode =>
            GetSkuSnapshotAsync(internalBearerToken, request, skuCode, cancellationToken)));
        var versions = snapshots
            .Where(snapshot => snapshot.Version is not null)
            .Select(snapshot => snapshot.Version!)
            .ToArray();
        var components = snapshots
            .SelectMany(snapshot => snapshot.Components)
            .ToArray();

        return new PlanningProductEngineeringSnapshot(
            $"product-engineering-http:{versions.Length + components.Length}",
            versions,
            components);
    }

    private async Task<ProductEngineeringSkuSnapshot> GetSkuSnapshotAsync(
        string internalBearerToken,
        PlanningProductEngineeringSnapshotRequest request,
        string skuCode,
        CancellationToken cancellationToken)
    {
        var productionVersionsTask = SendAsync<ListProductionVersionsResponse>(
            internalBearerToken,
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", skuCode),
                ("status", "active")),
            cancellationToken);
        var manufacturingBomsTask = SendAsync<ListManufacturingBomsResponse>(
            internalBearerToken,
            "/api/business/v1/engineering/manufacturing-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", skuCode),
                ("status", "Published")),
            cancellationToken);
        await Task.WhenAll(productionVersionsTask, manufacturingBomsTask);

        var selectedVersion = productionVersionsTask.Result.Items
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Priority)
            .FirstOrDefault();
        var version = selectedVersion is null
            ? null
            : new ProductionVersionSnapshot(
                selectedVersion.SkuCode,
                selectedVersion.ProductionVersionId,
                selectedVersion.MbomVersionId,
                selectedVersion.RoutingVersionId);
        var selectedBom = manufacturingBomsTask.Result.Items
            .OrderByDescending(x => string.Equals(x.BomCode, selectedVersion?.MbomVersionId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.BomCode)
            .FirstOrDefault();
        var components = selectedBom is null
            ? []
            : selectedBom.MaterialLines.Select(line => new BomComponentSnapshot(
                selectedBom.SkuCode,
                line.SkuCode,
                line.UnitOfMeasureCode,
                line.Quantity * (1 + line.ScrapRate)))
                .ToArray();

        return new ProductEngineeringSkuSnapshot(version, components);
    }

    private async Task<T> SendAsync<T>(string internalBearerToken, string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(internalBearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("ProductEngineering returned an empty response envelope.");
    }
}

public sealed class HttpPlanningInventorySnapshotClient(HttpClient httpClient) : IPlanningInventorySnapshotClient
{
    public async Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
        string internalBearerToken,
        PlanningInventorySnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var availability = await Task.WhenAll(request.Items.Select(item =>
            GetAvailabilityAsync(internalBearerToken, request.OrganizationId, request.EnvironmentId, item, cancellationToken)));

        return new PlanningInventorySnapshot($"inventory-http:{availability.Length}", availability);
    }

    private async Task<InventoryAvailabilitySnapshot> GetAvailabilityAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        PlanningInventorySnapshotItem item,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("skuCode", item.SkuCode),
                ("uomCode", item.UomCode),
                ("siteCode", item.SiteCode)));
        if (!string.IsNullOrWhiteSpace(internalBearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<InventoryAvailabilityResponse>>(cancellationToken);
        var body = envelope?.Data ?? throw new InvalidOperationException("Inventory returned an empty response envelope.");
        return new InventoryAvailabilitySnapshot(body.SkuCode, body.UomCode, body.SiteCode, body.AvailableQuantity);
    }
}

internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
internal sealed record ProductEngineeringSkuSnapshot(
    ProductionVersionSnapshot? Version,
    IReadOnlyCollection<BomComponentSnapshot> Components);

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
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineItem> MaterialLines);

internal sealed record ManufacturingBomMaterialLineItem(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

internal sealed record InventoryAvailabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

public sealed class DemandPlanningFixtureInputSnapshotProvider(ApplicationDbContext dbContext) : IPlanningInputSnapshotProvider
{
    public async Task<PlanningInputSnapshotResult> GetSnapshotAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken)
    {
        var demands = await dbContext.DemandSources
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DueDate >= horizonStart
                && x.DueDate <= horizonEnd)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SourceReference)
            .Select(x => new DemandSnapshot(x.SourceReference, x.SkuCode, x.UomCode, x.SiteCode, x.Quantity, x.DueDate))
            .ToListAsync(cancellationToken);

        return new PlanningInputSnapshotResult(
            "fixture-production-engineering-snapshot",
            "fixture-inventory-availability-snapshot",
            demands,
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
            ],
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
            ]);
    }
}

internal static class PlanningHttpQuery
{
    public static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
