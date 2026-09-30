using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public interface IMaterialDeliverySourcesClient
{
    Task<IReadOnlyCollection<MaterialDeliverySupplySource>> GetSupplyAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
    Task<MaterialDeliverySourcesResponse> GetSchedulingAsync(string organizationId, string environmentId, string planId,
        IReadOnlyCollection<MaterialDeliverySourceSelection> sources, CancellationToken cancellationToken);
}

public sealed class HttpMaterialDeliverySourcesClient(IHttpClientFactory clients, IInternalServiceTokenProvider tokenProvider)
    : IMaterialDeliverySourcesClient
{
    public async Task<IReadOnlyCollection<MaterialDeliverySupplySource>> GetSupplyAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var scope = $"organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}";
        var requisitions = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in ReadPagesAsync($"/api/business/v1/erp/purchase-requisitions?{scope}", cancellationToken))
            requisitions.Add(row.GetProperty("requisitionNo").GetString()!, row.GetProperty("suggestionId").GetString()!);
        var result = new List<MaterialDeliverySupplySource>();
        await foreach (var order in ReadPagesAsync($"/api/business/v1/erp/purchase-orders?{scope}&status=Released", cancellationToken))
        {
            foreach (var line in order.GetProperty("lines").EnumerateArray())
            {
                var open = line.GetProperty("openQuantity").GetDecimal();
                if (open <= 0) continue;
                result.Add(new(order.GetProperty("purchaseOrderNo").GetString()!, line.GetProperty("lineNo").GetString()!,
                    order.GetProperty("siteCode").GetString()!, line.GetProperty("skuCode").GetString()!, line.GetProperty("uomCode").GetString()!,
                    line.GetProperty("promisedDate").Deserialize<DateOnly>(), open,
                    line.GetProperty("sources").EnumerateArray().Select(source => new MaterialDeliveryPurchaseSource(
                        source.GetProperty("purchaseRequisitionNo").GetString()!, source.GetProperty("purchaseRequisitionLineNo").GetString()!,
                        source.GetProperty("quantity").GetDecimal(), requisitions.GetValueOrDefault(source.GetProperty("purchaseRequisitionNo").GetString()!))).ToArray()));
            }
        }
        return result;
    }

    private async IAsyncEnumerable<JsonElement> ReadPagesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var skip = 0;
        int total;
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}&skip={skip}&take=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
            using var response = await clients.CreateClient("material-delivery-erp").SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.GetProperty("success").GetBoolean()) throw new HttpRequestException("ERP 物料交付来源查询失败。");
            var data = root.GetProperty("data");
            total = data.GetProperty("total").GetInt32();
            var rows = data.GetProperty("items");
            foreach (var row in rows.EnumerateArray()) yield return row.Clone();
            skip += 100;
        } while (skip < total);
    }

    public async Task<MaterialDeliverySourcesResponse> GetSchedulingAsync(string organizationId, string environmentId, string planId,
        IReadOnlyCollection<MaterialDeliverySourceSelection> sources, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/business/internal/v1/scheduling/plans/{Uri.EscapeDataString(planId)}/material-delivery-sources")
        { Content = JsonContent.Create(new { OrganizationId = organizationId, EnvironmentId = environmentId, Sources = sources }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        using var response = await clients.CreateClient("material-delivery-scheduling").SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MaterialDeliverySourcesResponse>>(cancellationToken);
        if (envelope is not { Success: true, Data: not null }) throw new HttpRequestException("APS 物料交付来源查询失败。");
        return envelope.Data;
    }
}
