using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public static class MesMaterialAvailabilitySources
{
    public const string ErpPurchaseOrderPromisedDate = "erp.purchase-order-promised-date";
}

public sealed record MesMaterialReadinessLiveCoverageRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MesMaterialReadinessLiveCoverageRequestItem> Items);

public sealed record MesMaterialReadinessLiveCoverageRequestItem(
    string MaterialId,
    string? MaterialLotId,
    string UomCode,
    decimal RequiredQuantity,
    decimal FrozenAvailableQuantity,
    decimal StagedQuantity,
    decimal ReceivedQuantity,
    IReadOnlyCollection<string> SubstituteMaterialIds);

public sealed record MesMaterialReadinessLiveCoverageResult(
    bool InventoryAvailable,
    bool ErpAvailable,
    IReadOnlyCollection<MesMaterialReadinessLiveCoverageItem> Items);

public sealed record MesMaterialReadinessLiveCoverageItem(
    string MaterialId,
    string? MaterialLotId,
    decimal AvailableQuantity,
    DateTimeOffset? ExpectedAvailableAtUtc,
    string? ExpectedAvailabilitySource);

public interface IMesMaterialReadinessLiveCoverageProvider
{
    Task<MesMaterialReadinessLiveCoverageResult> ResolveAsync(
        MesMaterialReadinessLiveCoverageRequest request,
        CancellationToken cancellationToken);
}

public sealed record MesMaterialAvailabilityReadRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> MaterialIds,
    string UomCode,
    string? MaterialLotId,
    DateOnly EffectiveDate);

public sealed record MesMaterialAvailabilityReadResult(
    bool SourceAvailable,
    decimal AvailableQuantity);

public interface IMesMaterialAvailabilityReader
{
    Task<MesMaterialAvailabilityReadResult> ReadAsync(
        MesMaterialAvailabilityReadRequest request,
        CancellationToken cancellationToken);
}

public sealed class MesErpHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}

public sealed class HttpMesMaterialReadinessLiveCoverageProvider(
    IMesMaterialAvailabilityReader inventoryReader,
    MesErpHttpClient erpClient,
    IInternalServiceTokenProvider internalTokenProvider,
    ILogger<HttpMesMaterialReadinessLiveCoverageProvider> logger,
    TimeProvider? timeProvider = null)
    : IMesMaterialReadinessLiveCoverageProvider
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MesMaterialReadinessLiveCoverageResult> ResolveAsync(
        MesMaterialReadinessLiveCoverageRequest request,
        CancellationToken cancellationToken)
    {
        var effectiveDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var inventoryResults = await Task.WhenAll(request.Items.Select(async item =>
        {
            var materialIds = new[] { item.MaterialId }
                .Concat(item.SubstituteMaterialIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var availability = await inventoryReader.ReadAsync(
                new MesMaterialAvailabilityReadRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    materialIds,
                    item.UomCode,
                    item.MaterialLotId,
                    effectiveDate),
                cancellationToken);
            return (Item: item, Availability: availability);
        }));

        if (inventoryResults.Any(x => !x.Availability.SourceAvailable))
        {
            logger.LogWarning(
                "MES material readiness live Inventory overlay is unavailable for organization {OrganizationId}, environment {EnvironmentId}.",
                request.OrganizationId,
                request.EnvironmentId);
            return new MesMaterialReadinessLiveCoverageResult(
                InventoryAvailable: false,
                ErpAvailable: false,
                request.Items.Select(item => new MesMaterialReadinessLiveCoverageItem(
                    item.MaterialId,
                    item.MaterialLotId,
                    0m,
                    null,
                    null)).ToArray());
        }

        var currentRows = inventoryResults.Select(x => new CurrentAvailability(
            x.Item,
            Math.Max(0m, x.Availability.AvailableQuantity)))
            .ToArray();
        var shortages = currentRows
            .Select(x => new
            {
                x.Item.MaterialId,
                Quantity = Math.Max(
                    0m,
                    x.Item.RequiredQuantity - x.AvailableQuantity - x.Item.StagedQuantity - x.Item.ReceivedQuantity),
            })
            .Where(x => x.Quantity > 0m)
            .GroupBy(x => x.MaterialId, StringComparer.Ordinal)
            .Select(group => new MaterialSupplyEtaRequestItem(group.Key, group.Sum(x => x.Quantity)))
            .OrderBy(x => x.SkuCode, StringComparer.Ordinal)
            .ToArray();
        if (shortages.Length == 0)
        {
            return ToResult(currentRows, ErpAvailable: true, []);
        }

        var etaResponse = await ResolveEtasAsync(request, shortages, cancellationToken);
        if (etaResponse is null)
        {
            logger.LogWarning(
                "MES material readiness ERP ETA overlay is unavailable for organization {OrganizationId}, environment {EnvironmentId}.",
                request.OrganizationId,
                request.EnvironmentId);
            return ToResult(currentRows, ErpAvailable: false, []);
        }

        return ToResult(currentRows, ErpAvailable: true, etaResponse.Items);
    }

    private async Task<ResolveMaterialSupplyEtasResponse?> ResolveEtasAsync(
        MesMaterialReadinessLiveCoverageRequest request,
        IReadOnlyCollection<MaterialSupplyEtaRequestItem> shortages,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/business/v1/erp/material-supply-etas/resolve")
        {
            Content = JsonContent.Create(new ResolveMaterialSupplyEtasRequest(
                request.OrganizationId,
                request.EnvironmentId,
                shortages)),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            internalTokenProvider.BearerToken);

        try
        {
            using var response = await erpClient.HttpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ResolveMaterialSupplyEtasResponse>>(
                cancellationToken);
            return envelope is { Success: true, Data: not null } ? envelope.Data : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static MesMaterialReadinessLiveCoverageResult ToResult(
        IReadOnlyCollection<CurrentAvailability> currentRows,
        bool ErpAvailable,
        IReadOnlyCollection<MaterialSupplyEtaResponseItem> etas)
    {
        var etaByMaterial = etas.ToDictionary(x => x.SkuCode, StringComparer.Ordinal);
        return new MesMaterialReadinessLiveCoverageResult(
            InventoryAvailable: true,
            ErpAvailable,
            currentRows.Select(row =>
            {
                etaByMaterial.TryGetValue(row.Item.MaterialId, out var eta);
                DateTimeOffset? expectedAtUtc = eta?.ExpectedAvailableDate is { } date
                    ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                    : null;
                return new MesMaterialReadinessLiveCoverageItem(
                    row.Item.MaterialId,
                    row.Item.MaterialLotId,
                    row.AvailableQuantity,
                    expectedAtUtc,
                    expectedAtUtc is null ? null : MesMaterialAvailabilitySources.ErpPurchaseOrderPromisedDate);
            }).ToArray());
    }

    private sealed record CurrentAvailability(
        MesMaterialReadinessLiveCoverageRequestItem Item,
        decimal AvailableQuantity);

    private sealed record ResolveMaterialSupplyEtasRequest(
        string OrganizationId,
        string EnvironmentId,
        IReadOnlyCollection<MaterialSupplyEtaRequestItem> Items);

    private sealed record MaterialSupplyEtaRequestItem(string SkuCode, decimal ShortageQuantity);

    private sealed record ResolveMaterialSupplyEtasResponse(
        IReadOnlyCollection<MaterialSupplyEtaResponseItem> Items);

    private sealed record MaterialSupplyEtaResponseItem(
        string SkuCode,
        decimal ShortageQuantity,
        decimal OpenPurchaseQuantity,
        DateOnly? ExpectedAvailableDate);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
