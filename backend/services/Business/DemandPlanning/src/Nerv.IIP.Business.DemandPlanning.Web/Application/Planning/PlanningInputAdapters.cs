using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    IReadOnlyCollection<BomComponentSnapshot> BomComponents,
    IReadOnlyCollection<ScheduledReceiptSnapshot> ScheduledReceipts,
    IReadOnlyCollection<PlanningParameterSnapshot> PlanningParameters);

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

public sealed record PlanningScheduledReceiptSnapshotItem(string SkuCode, string UomCode, string SiteCode);

public sealed record PlanningScheduledReceiptSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<PlanningScheduledReceiptSnapshotItem> Items);

public sealed record PlanningScheduledReceiptSnapshot(
    string SnapshotSource,
    IReadOnlyCollection<ScheduledReceiptSnapshot> ScheduledReceipts);

public interface IPlanningScheduledReceiptSnapshotClient
{
    Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
        string internalBearerToken,
        PlanningScheduledReceiptSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed record PlanningParameterSnapshotItem(string SkuCode, string UomCode, string SiteCode);

public sealed record PlanningParameterSnapshotRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<PlanningParameterSnapshotItem> Items);

public sealed record PlanningParameterSnapshotResult(
    string SnapshotSource,
    IReadOnlyCollection<PlanningParameterSnapshot> PlanningParameters);

public sealed class OptionalPlanningSnapshotException : Exception
{
    public OptionalPlanningSnapshotException(string message)
        : base(message)
    {
    }

    public OptionalPlanningSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IPlanningParameterSnapshotClient
{
    Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
        string internalBearerToken,
        PlanningParameterSnapshotRequest request,
        CancellationToken cancellationToken);
}

public sealed class DemandPlanningUpstreamInputSnapshotProvider(
    ApplicationDbContext dbContext,
    IPlanningProductEngineeringSnapshotClient productEngineering,
    IPlanningInventorySnapshotClient inventory,
    IPlanningScheduledReceiptSnapshotClient? scheduledReceiptClient = null,
    IPlanningParameterSnapshotClient? planningParameterClient = null,
    IInternalServiceTokenProvider? internalTokenProvider = null,
    ILogger<DemandPlanningUpstreamInputSnapshotProvider>? logger = null) : IPlanningInputSnapshotProvider
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
                [],
                [],
                []);
        }

        var internalBearerToken = internalTokenProvider?.BearerToken ?? string.Empty;
        var engineering = await LoadEngineeringClosureAsync(internalBearerToken, organizationId, environmentId, horizonStart, horizonEnd, demands, cancellationToken);
        var availabilityItems = demands
            .Select(x => new PlanningInventorySnapshotItem(x.SkuCode, x.UomCode, x.SiteCode))
            .Concat(engineering.BomComponents.SelectMany(component => demands.Select(demand =>
                new PlanningInventorySnapshotItem(component.ComponentSkuCode, component.ComponentUomCode, demand.SiteCode))))
            .DistinctBy(x => $"{x.SkuCode}\u001f{x.UomCode}\u001f{x.SiteCode}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SiteCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inventorySnapshot = await inventory.GetAvailabilitySnapshotAsync(
            internalBearerToken,
            new PlanningInventorySnapshotRequest(organizationId, environmentId, availabilityItems),
            cancellationToken);
        var scheduledReceipts = await LoadScheduledReceiptsOrEmptyAsync(
            internalBearerToken,
            organizationId,
            environmentId,
            horizonStart,
            horizonEnd,
            availabilityItems,
            cancellationToken);
        var planningParameters = await LoadPlanningParametersOrEmptyAsync(
            internalBearerToken,
            organizationId,
            environmentId,
            availabilityItems,
            cancellationToken);

        // Keep optional adapter source segments as "<source>:<status>" and use
        // ":error" for degraded optional inputs; MrpRun derives its explicit
        // input degradation sources from this persisted snapshot metadata.
        return new PlanningInputSnapshotResult(
            engineering.SnapshotSource,
            $"{inventorySnapshot.SnapshotSource};{scheduledReceipts.SnapshotSource};{planningParameters.SnapshotSource}",
            demands,
            inventorySnapshot.Availability,
            engineering.ProductionVersions,
            engineering.BomComponents,
            scheduledReceipts.ScheduledReceipts,
            planningParameters.PlanningParameters);
    }

    private async Task<PlanningScheduledReceiptSnapshot> LoadScheduledReceiptsOrEmptyAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        IReadOnlyCollection<PlanningInventorySnapshotItem> availabilityItems,
        CancellationToken cancellationToken)
    {
        if (scheduledReceiptClient is null)
        {
            return new PlanningScheduledReceiptSnapshot("scheduled-receipts:none", []);
        }

        try
        {
            return await scheduledReceiptClient.GetScheduledReceiptsAsync(
                internalBearerToken,
                new PlanningScheduledReceiptSnapshotRequest(
                    organizationId,
                    environmentId,
                    horizonStart,
                    horizonEnd,
                    availabilityItems.Select(x => new PlanningScheduledReceiptSnapshotItem(x.SkuCode, x.UomCode, x.SiteCode)).ToArray()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsOptionalPlanningSourceFailure(ex))
        {
            logger?.LogWarning(ex, "DemandPlanning MRP scheduled receipt snapshot failed; continuing with an empty optional snapshot.");
            return new PlanningScheduledReceiptSnapshot("scheduled-receipts:error", []);
        }
    }

    private async Task<PlanningParameterSnapshotResult> LoadPlanningParametersOrEmptyAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<PlanningInventorySnapshotItem> availabilityItems,
        CancellationToken cancellationToken)
    {
        if (planningParameterClient is null)
        {
            return new PlanningParameterSnapshotResult("master-data-planning-parameters:none", []);
        }

        try
        {
            return await planningParameterClient.GetPlanningParametersAsync(
                internalBearerToken,
                new PlanningParameterSnapshotRequest(
                    organizationId,
                    environmentId,
                    availabilityItems.Select(x => new PlanningParameterSnapshotItem(x.SkuCode, x.UomCode, x.SiteCode)).ToArray()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsOptionalPlanningSourceFailure(ex))
        {
            logger?.LogWarning(ex, "DemandPlanning MRP planning parameter snapshot failed; continuing with an empty optional snapshot.");
            return new PlanningParameterSnapshotResult("master-data-planning-parameters:error", []);
        }
    }

    private static bool IsOptionalPlanningSourceFailure(Exception exception)
    {
        return exception is OptionalPlanningSnapshotException
            or HttpRequestException
            or TaskCanceledException;
    }

    private async Task<PlanningProductEngineeringSnapshot> LoadEngineeringClosureAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        IReadOnlyCollection<DemandSnapshot> demands,
        CancellationToken cancellationToken)
    {
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = demands.Select(x => x.SkuCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var versions = new List<ProductionVersionSnapshot>();
        var components = new List<BomComponentSnapshot>();
        var snapshotSources = new List<string>();

        while (pending.Count > 0)
        {
            var parents = pending
                .Where(requested.Add)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            pending = [];
            if (parents.Length == 0)
            {
                break;
            }

            var snapshot = await productEngineering.GetSnapshotAsync(
                internalBearerToken,
                new PlanningProductEngineeringSnapshotRequest(organizationId, environmentId, horizonStart, horizonEnd, parents),
                cancellationToken);
            snapshotSources.Add(snapshot.SnapshotSource);
            versions.AddRange(snapshot.ProductionVersions);
            components.AddRange(snapshot.BomComponents);
            // BOM lines do not expose make/buy; one component lookahead discovers child production versions.
            pending.AddRange(snapshot.BomComponents
                .Where(x => !requested.Contains(x.ComponentSkuCode))
                .Select(x => x.ComponentSkuCode));
        }

        return new PlanningProductEngineeringSnapshot(
            string.Join(';', snapshotSources.Distinct(StringComparer.OrdinalIgnoreCase)),
            versions
                .DistinctBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            components
                .DistinctBy(x => $"{x.ParentSkuCode}\u001f{x.ComponentSkuCode}\u001f{x.ComponentUomCode}", StringComparer.OrdinalIgnoreCase)
                .ToArray());
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
                selectedVersion.RoutingVersionId,
                selectedVersion.LotSizeMin,
                selectedVersion.LotSizeMax,
                null);
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

public sealed class HttpPlanningErpScheduledReceiptSnapshotClient(HttpClient httpClient) : IPlanningScheduledReceiptSnapshotClient
{
    public async Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
        string internalBearerToken,
        PlanningScheduledReceiptSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return new PlanningScheduledReceiptSnapshot("erp-purchase-orders:0", []);
        }

        var orders = new List<ErpPurchaseOrderItem>();
        const int PageSize = 500;
        var skip = 0;
        while (true)
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/business/v1/erp/purchase-orders?" + PlanningHttpQuery.Query(
                    ("organizationId", request.OrganizationId),
                    ("environmentId", request.EnvironmentId),
                    ("status", "Released"),
                    ("skip", skip),
                    ("take", PageSize)));
            if (!string.IsNullOrWhiteSpace(internalBearerToken))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
            }

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await OptionalPlanningSnapshotHttp.ReadEnvelopeDataAsync<ErpPurchaseOrdersResponse>(
                response,
                "ERP returned an invalid purchase order response.",
                "ERP returned an empty purchase order response envelope.",
                cancellationToken);
            orders.AddRange(body.Items);
            skip += PageSize;
            if (body.Items.Count == 0 || skip >= body.Total)
            {
                break;
            }
        }

        var itemKeys = request.Items
            .Select(x => $"{x.SkuCode}\u001f{x.UomCode}\u001f{x.SiteCode}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var receipts = orders
            .SelectMany(order => order.Lines
                .Select(line => new
                {
                    order.PurchaseOrderNo,
                    order.SiteCode,
                    line.LineNo,
                    line.SkuCode,
                    line.UomCode,
                    Quantity = line.OrderedQuantity - line.ReceivedQuantity,
                    line.PromisedDate,
                }))
            .Where(x => x.Quantity > 0)
            .Where(x => x.PromisedDate <= request.HorizonEnd)
            .Where(x => itemKeys.Contains($"{x.SkuCode}\u001f{x.UomCode}\u001f{x.SiteCode}"))
            .Select(x => new ScheduledReceiptSnapshot(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.Quantity,
                x.PromisedDate,
                "erp",
                "purchase-order",
                $"{x.PurchaseOrderNo}:{x.LineNo}"))
            .ToArray();

        return new PlanningScheduledReceiptSnapshot($"erp-purchase-orders:{receipts.Length}", receipts);
    }
}

public sealed class HttpPlanningMasterDataPlanningParameterSnapshotClient(HttpClient httpClient) : IPlanningParameterSnapshotClient
{
    private const int MaxConcurrentSkuDetailRequests = 8;

    public async Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
        string internalBearerToken,
        PlanningParameterSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return new PlanningParameterSnapshotResult("master-data-planning-parameters:0", []);
        }

        using var throttle = new SemaphoreSlim(MaxConcurrentSkuDetailRequests);
        var details = await Task.WhenAll(request.Items
            .Select(x => x.SkuCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(skuCode => GetSkuPlanningDetailThrottledAsync(
                internalBearerToken,
                request.OrganizationId,
                request.EnvironmentId,
                skuCode,
                throttle,
                cancellationToken)));
        var detailBySku = details
            .Where(x => x is not null)
            .Where(x => IsPlanningParameterEligible(x!))
            .ToDictionary(x => x!.Code, x => x!, StringComparer.OrdinalIgnoreCase);
        var parameters = request.Items
            .Where(x => detailBySku.ContainsKey(x.SkuCode))
            .Select(x =>
            {
                var detail = detailBySku[x.SkuCode];
                return new PlanningParameterSnapshot(
                    x.SkuCode,
                    x.UomCode,
                    x.SiteCode,
                    ComputeLeadTimeDays(detail),
                    Math.Max(0, detail.SafetyStockQuantity ?? 0m),
                    detail.MinimumLotSize,
                    detail.MaximumLotSize,
                    detail.LotSizeMultiple);
            })
            .ToArray();

        return new PlanningParameterSnapshotResult($"master-data-planning-parameters:{parameters.Length}", parameters);
    }

    private async Task<MasterDataSkuPlanningDetail?> GetSkuPlanningDetailThrottledAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        string skuCode,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            return await GetSkuPlanningDetailAsync(internalBearerToken, organizationId, environmentId, skuCode, cancellationToken);
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<MasterDataSkuPlanningDetail?> GetSkuPlanningDetailAsync(
        string internalBearerToken,
        string organizationId,
        string environmentId,
        string skuCode,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/business/v1/master-data/resources/sku/" + Uri.EscapeDataString(skuCode) + "?" + Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId)));
        if (!string.IsNullOrWhiteSpace(internalBearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await OptionalPlanningSnapshotHttp.ReadEnvelopeDataOrDefaultAsync<MasterDataSkuPlanningDetail>(
            response,
            "MasterData returned an invalid SKU planning detail response.",
            cancellationToken);
    }

    private static int ComputeLeadTimeDays(MasterDataSkuPlanningDetail detail)
    {
        var goodsReceiptDays = Math.Max(0, detail.GoodsReceiptProcessingTimeDays ?? 0);
        var sourceLeadTimes = new[]
            {
                detail.PlannedDeliveryTimeDays,
                detail.InHouseProductionTimeDays,
            }
            .Where(x => x is > 0)
            .Select(x => x!.Value)
            .ToArray();
        return goodsReceiptDays + (sourceLeadTimes.Length == 0 ? 0 : sourceLeadTimes.Max());
    }

    private static bool IsPlanningParameterEligible(MasterDataSkuPlanningDetail detail)
    {
        var lifecycleStatus = string.IsNullOrWhiteSpace(detail.LifecycleStatus)
            ? "active"
            : detail.LifecycleStatus.Trim();
        var hasPlanningUsage = (detail.PurchasingEnabled ?? true)
            || (detail.ManufacturingEnabled ?? true)
            || (detail.SalesEnabled ?? true);
        return detail.Active
            && string.Equals(lifecycleStatus, "active", StringComparison.OrdinalIgnoreCase)
            && hasPlanningUsage;
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

internal static class OptionalPlanningSnapshotHttp
{
    public static async Task<T> ReadEnvelopeDataAsync<T>(
        HttpResponseMessage response,
        string invalidResponseMessage,
        string emptyEnvelopeMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        var envelope = await ReadEnvelopeAsync<T>(response, invalidResponseMessage, cancellationToken);
        return envelope?.Data ?? throw new OptionalPlanningSnapshotException(emptyEnvelopeMessage);
    }

    public static async Task<T?> ReadEnvelopeDataOrDefaultAsync<T>(
        HttpResponseMessage response,
        string invalidResponseMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        var envelope = await ReadEnvelopeAsync<T>(response, invalidResponseMessage, cancellationToken);
        return envelope?.Data;
    }

    private static async Task<ResponseDataEnvelope<T>?> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        string invalidResponseMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new OptionalPlanningSnapshotException(invalidResponseMessage, ex);
        }
        catch (NotSupportedException ex)
        {
            throw new OptionalPlanningSnapshotException(invalidResponseMessage, ex);
        }
    }
}

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

internal sealed record ErpPurchaseOrdersResponse(IReadOnlyCollection<ErpPurchaseOrderItem> Items, int Total);

internal sealed record ErpPurchaseOrderItem(
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    string Status,
    decimal TotalAmount,
    IReadOnlyCollection<ErpPurchaseOrderLineItem> Lines);

internal sealed record ErpPurchaseOrderLineItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

internal sealed record MasterDataSkuPlanningDetail(
    string Code,
    bool Active,
    string? BaseUomCode,
    string? InventoryUomCode,
    string? PurchaseUomCode,
    string? ManufacturingUomCode,
    string? ProcurementType,
    string? MrpType,
    string? LotSizingPolicy,
    decimal? MinimumLotSize,
    decimal? MaximumLotSize,
    decimal? LotSizeMultiple,
    decimal? SafetyStockQuantity,
    decimal? ReorderPointQuantity,
    int? PlannedDeliveryTimeDays,
    int? InHouseProductionTimeDays,
    int? GoodsReceiptProcessingTimeDays,
    string? LifecycleStatus,
    bool? PurchasingEnabled,
    bool? ManufacturingEnabled,
    bool? SalesEnabled);

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
            ],
            [],
            []);
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
