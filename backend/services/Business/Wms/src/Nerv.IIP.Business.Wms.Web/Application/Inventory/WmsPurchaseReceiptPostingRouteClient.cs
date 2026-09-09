using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public interface IWmsPurchaseReceiptPostingRouteClient
{
    Task<IReadOnlyDictionary<string, decimal>> GetUnitCostsAsync(
        string organizationId, string environmentId, string receiptNo,
        IReadOnlyCollection<InboundOrderLine> lines,
        CancellationToken cancellationToken);
}

public sealed class HttpWmsPurchaseReceiptPostingRouteClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IWmsPurchaseReceiptPostingRouteClient
{
    public async Task<IReadOnlyDictionary<string, decimal>> GetUnitCostsAsync(
        string organizationId, string environmentId, string receiptNo,
        IReadOnlyCollection<InboundOrderLine> lines,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/business/v1/erp/purchase-receipts/{Uri.EscapeDataString(receiptNo)}/source-document?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var envelope = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        if (envelope is null || !envelope.RootElement.GetProperty("success").GetBoolean())
        {
            throw new HttpRequestException("采购收货来源读取失败，无法完成入库。");
        }

        var data = envelope.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null
            || data.GetProperty("inventoryPostingRoute").Deserialize<PurchaseReceiptInventoryPostingRoute>() != PurchaseReceiptInventoryPostingRoute.Wms)
        {
            throw new KnownException("采购收货来源不存在或未选择 WMS 库存过账路径，无法完成入库。");
        }

        // 组织/环境由 ERP 的 scoped 查询校验；消费方只映射精确收货行，不复制完整公开 DTO。
        if (data.GetProperty("purchaseReceiptNo").GetString() != receiptNo)
            throw new KnownException("采购收货来源单据不匹配，无法完成入库。");

        var sourceLines = data.GetProperty("lines").EnumerateArray().ToArray();
        var unitCosts = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var matches = sourceLines.Where(source => source.GetProperty("lineNo").GetString() == line.LineNo).ToArray();
            if (matches.Length != 1
                || matches[0].GetProperty("skuCode").GetString() != line.SkuCode
                || matches[0].GetProperty("uomCode").GetString() != line.UomCode
                || matches[0].GetProperty("receivedQuantity").GetDecimal() != line.ReceivedQuantity)
                throw new KnownException("采购收货来源行、物料、单位或数量不匹配，无法完成入库。");

            if (!matches[0].TryGetProperty("estimatedUnitCost", out var cost)
                || cost.ValueKind == JsonValueKind.Null)
                throw new KnownException("采购收货缺少冻结暂估成本，无法完成入库。");
            unitCosts.Add(line.LineNo, cost.GetDecimal());
        }
        return unitCosts;
    }
}
