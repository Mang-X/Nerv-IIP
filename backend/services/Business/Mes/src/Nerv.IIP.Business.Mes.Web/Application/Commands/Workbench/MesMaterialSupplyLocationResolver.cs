using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

/// <summary>
/// 线边收料的调拨库位配置。**没有任何领域默认值**：站点、来源候选库位、线边库位全部来自配置
/// （Aspire / appsettings），MES 不再把 <c>warehouse</c> / <c>line-side</c> 这类命名空间硬编码进过账请求（#1322）。
/// </summary>
public sealed class MesMaterialSupplyLocationOptions
{
    /// <summary>库存站点编码（例如 <c>SITE-001</c>）。</summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>发料来源候选库位，按优先级排列；实际取哪一个由库存实时持仓决定。</summary>
    public IReadOnlyList<string> SourceLocationCodes { get; init; } = [];

    /// <summary>线边站点编码；留空则回落到 <see cref="SiteCode"/>。</summary>
    public string? LineSideSiteCode { get; init; }

    /// <summary>工位线边库位编码。</summary>
    public string LineSideLocationCode { get; init; } = string.Empty;
}

public sealed record MesMaterialSupplyLocationRequest(
    string OrganizationId,
    string EnvironmentId,
    string MaterialId,
    string UomCode,
    string? MaterialLotId,
    decimal Quantity);

public interface IMesMaterialSupplyLocationResolver
{
    Task<MaterialTransferLocations> ResolveAsync(
        MesMaterialSupplyLocationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 来源库位解析：先按配置的候选库位顺序问库存「这个物料/批次现在真的在哪、还有多少」，
/// 选出第一个可用量够本次收料的库位；一个都不够就显式失败（<c>MATERIAL_SOURCE_LOCATION_UNAVAILABLE</c>），
/// 绝不退回到臆造库位去触发 Inventory 的 NEGATIVE_ON_HAND 静默回滚。
/// </summary>
public sealed class InventoryMesMaterialSupplyLocationResolver(
    MesMaterialSupplyLocationOptions options,
    MesInventoryHttpClient? inventoryClient = null,
    IInternalServiceTokenProvider? internalTokenProvider = null,
    ILogger<InventoryMesMaterialSupplyLocationResolver>? logger = null)
    : IMesMaterialSupplyLocationResolver
{
    public async Task<MaterialTransferLocations> ResolveAsync(
        MesMaterialSupplyLocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var siteCode = Trimmed(options.SiteCode);
        var lineSideLocationCode = Trimmed(options.LineSideLocationCode);
        var candidates = options.SourceLocationCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (siteCode is null || lineSideLocationCode is null || candidates.Length == 0)
        {
            throw new KnownException(
                "MATERIAL_SUPPLY_LOCATION_UNCONFIGURED: 未配置线边收料的库存站点/来源库位/线边库位（Inventory:SiteCode、Inventory:SourceLocationCodes、Inventory:LineSideLocationCode）。");
        }

        var lineSideSiteCode = Trimmed(options.LineSideSiteCode) ?? siteCode;
        var sourceLocationCode = await SelectSourceLocationAsync(request, siteCode, candidates, cancellationToken);
        return new MaterialTransferLocations(siteCode, sourceLocationCode, lineSideSiteCode, lineSideLocationCode);
    }

    private async Task<string> SelectSourceLocationAsync(
        MesMaterialSupplyLocationRequest request,
        string siteCode,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        if (inventoryClient is null)
        {
            // 仅在没有接入 Inventory 客户端的装配下成立（单测/离线夹具）：退回配置里的首选来源库位。
            // 运行时 Program.cs 一定注入 MesInventoryHttpClient，因此真实链路永远走下面的实时持仓查询。
            return candidates[0];
        }

        string? bestLocationCode = null;
        var bestQuantity = 0m;
        foreach (var locationCode in candidates)
        {
            var availability = await GetAvailabilityAsync(request, siteCode, locationCode, cancellationToken);
            if (availability >= request.Quantity)
            {
                return locationCode;
            }

            if (availability > bestQuantity)
            {
                bestQuantity = availability;
                bestLocationCode = locationCode;
            }
        }

        logger?.LogWarning(
            "MES line-side receipt found no source location holding the full quantity. Material={MaterialId}, Lot={MaterialLotId}, Quantity={Quantity}, BestLocation={BestLocation}, BestAvailable={BestAvailable}",
            request.MaterialId,
            request.MaterialLotId,
            request.Quantity,
            bestLocationCode,
            bestQuantity);
        throw new KnownException(
            $"MATERIAL_SOURCE_LOCATION_UNAVAILABLE: 物料 {request.MaterialId} 在站点 {siteCode} 的候选库位（{string.Join('/', candidates)}）可用量不足 {request.Quantity:0.######} {request.UomCode}，无法发起线边收料过账。");
    }

    private async Task<decimal> GetAvailabilityAsync(
        MesMaterialSupplyLocationRequest request,
        string siteCode,
        string locationCode,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"organizationId={Uri.EscapeDataString(request.OrganizationId)}",
            $"environmentId={Uri.EscapeDataString(request.EnvironmentId)}",
            $"skuCode={Uri.EscapeDataString(request.MaterialId)}",
            $"uomCode={Uri.EscapeDataString(request.UomCode)}",
            $"siteCode={Uri.EscapeDataString(siteCode)}",
            $"locationCode={Uri.EscapeDataString(locationCode)}",
        };
        if (!string.IsNullOrWhiteSpace(request.MaterialLotId))
        {
            query.Add($"lotNo={Uri.EscapeDataString(request.MaterialLotId)}");
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/inventory/v1/availability?" + string.Join('&', query));
        var token = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await inventoryClient!.HttpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException($"MATERIAL_SOURCE_LOCATION_UNAVAILABLE: Inventory 库存服务暂不可用。{exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException($"MATERIAL_SOURCE_LOCATION_UNAVAILABLE: Inventory 库存服务请求超时。{exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new KnownException(
                    $"MATERIAL_SOURCE_LOCATION_UNAVAILABLE: Inventory 库存服务返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<MesMaterialSupplyAvailabilityEnvelope>(cancellationToken);
            return Math.Max(0m, envelope?.Data?.AvailableQuantity ?? 0m);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record MesMaterialSupplyAvailabilityEnvelope(MesMaterialSupplyAvailability? Data);

internal sealed record MesMaterialSupplyAvailability(decimal AvailableQuantity);
