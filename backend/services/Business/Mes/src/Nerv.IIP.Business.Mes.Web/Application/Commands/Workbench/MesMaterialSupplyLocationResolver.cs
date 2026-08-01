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

    /// <summary>发料来源候选库位；解析器按库位编码升序确定分配顺序。</summary>
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
/// 按库位编码与批次编码的确定性顺序切分来源明细；合计可用量不足就显式失败
/// （<c>MATERIAL_SOURCE_LOCATION_UNAVAILABLE</c>），绝不退回到臆造库位去触发
/// Inventory 的 NEGATIVE_ON_HAND 静默回滚。
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
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (siteCode is null || lineSideLocationCode is null || candidates.Length == 0)
        {
            throw new KnownException(
                "MATERIAL_SUPPLY_LOCATION_UNCONFIGURED: 未配置线边收料的库存站点/来源库位/线边库位（Inventory:SiteCode、Inventory:SourceLocationCodes、Inventory:LineSideLocationCode）。");
        }

        var lineSideSiteCode = Trimmed(options.LineSideSiteCode) ?? siteCode;
        var sourceAllocations = await SelectSourceAllocationsAsync(request, siteCode, candidates, cancellationToken);
        return new MaterialTransferLocations(
            siteCode,
            sourceAllocations[0].SourceLocationCode,
            lineSideSiteCode,
            lineSideLocationCode,
            sourceAllocations);
    }

    private async Task<IReadOnlyList<MaterialTransferAllocation>> SelectSourceAllocationsAsync(
        MesMaterialSupplyLocationRequest request,
        string siteCode,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        if (inventoryClient is null)
        {
            // 仅在没有接入 Inventory 客户端的装配下成立（单测/离线夹具）：退回配置里的首选来源库位。
            // 运行时 Program.cs 一定注入 MesInventoryHttpClient，因此真实链路永远走下面的实时持仓查询。
            return [new MaterialTransferAllocation(siteCode, candidates[0], request.MaterialLotId, request.Quantity)];
        }

        var remaining = request.Quantity;
        var availableTotal = 0m;
        var allocations = new List<MaterialTransferAllocation>();
        foreach (var locationCode in candidates)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var availability = await GetAvailabilityAsync(request, siteCode, locationCode, cancellationToken);
            var inventoryLines = availability.Items ?? [];
            IEnumerable<MesMaterialSupplyAvailabilityLine> lines;
            if (inventoryLines.Count > 0)
            {
                lines = inventoryLines
                    .Where(x => x.MovementAllowed && x.AvailableQuantity > 0m)
                    .OrderBy(x => x.LotNo ?? string.Empty, StringComparer.Ordinal);
            }
            else
            {
                lines = availability.AvailableQuantity > 0m
                    ? [new MesMaterialSupplyAvailabilityLine(null, availability.AvailableQuantity)]
                    : [];
            }
            foreach (var line in lines)
            {
                var available = Math.Max(0m, line.AvailableQuantity);
                availableTotal += available;
                if (remaining <= 0m)
                {
                    continue;
                }

                var allocated = Math.Min(remaining, available);
                allocations.Add(new MaterialTransferAllocation(siteCode, locationCode, line.LotNo, allocated));
                remaining -= allocated;
            }
        }

        if (remaining <= 0m)
        {
            return allocations;
        }

        logger?.LogWarning(
            "MES line-side receipt found insufficient source availability. Material={MaterialId}, Lot={MaterialLotId}, Quantity={Quantity}, AvailableTotal={AvailableTotal}",
            request.MaterialId,
            request.MaterialLotId,
            request.Quantity,
            availableTotal);
        throw new KnownException(
            $"MATERIAL_SOURCE_LOCATION_UNAVAILABLE: 需求{request.Quantity:0.######}{request.UomCode}，候选库位合计可用{availableTotal:0.######}{request.UomCode}。");
    }

    private async Task<MesMaterialSupplyAvailability> GetAvailabilityAsync(
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
        // MES 的 MaterialLotId 是线边追溯批次，不一定等于 Inventory 的来源批次。
        // 来源批次由 Inventory 返回的 dimension lines 决定；这里不能拿工单批号精确过滤库存。

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
            return envelope?.Data ?? new MesMaterialSupplyAvailability(0m, []);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record MesMaterialSupplyAvailabilityEnvelope(MesMaterialSupplyAvailability? Data);

internal sealed record MesMaterialSupplyAvailability(
    decimal AvailableQuantity,
    IReadOnlyList<MesMaterialSupplyAvailabilityLine>? Items);

internal sealed record MesMaterialSupplyAvailabilityLine(
    string? LotNo,
    decimal AvailableQuantity,
    bool MovementAllowed = true);
