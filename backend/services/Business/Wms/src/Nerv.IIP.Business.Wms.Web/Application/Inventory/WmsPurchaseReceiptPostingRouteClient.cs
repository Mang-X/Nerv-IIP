using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public interface IWmsPurchaseReceiptPostingRouteClient
{
    Task<PurchaseReceiptInventoryPostingRoute?> GetAsync(
        string organizationId, string environmentId, string receiptNo,
        CancellationToken cancellationToken);
}

public sealed class HttpWmsPurchaseReceiptPostingRouteClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IWmsPurchaseReceiptPostingRouteClient
{
    public async Task<PurchaseReceiptInventoryPostingRoute?> GetAsync(
        string organizationId, string environmentId, string receiptNo,
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
        if (data.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // 只映射本消费方需要的事实，不复制 ERP 的整份来源单据 DTO。
        return data.GetProperty("inventoryPostingRoute").Deserialize<PurchaseReceiptInventoryPostingRoute>();
    }
}
