using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.Wms;

public interface IWmsOutboundCancellationClient
{
    Task CancelForDeliveryOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> deliveryOrderNos,
        string reason,
        CancellationToken cancellationToken);
}

public sealed class HttpWmsOutboundCancellationClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IWmsOutboundCancellationClient
{
    public async Task CancelForDeliveryOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> deliveryOrderNos,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var deliveryOrderNo in deliveryOrderNos.Distinct(StringComparer.Ordinal))
        {
            var item = await FindOpenOutboundOrderAsync(organizationId, environmentId, deliveryOrderNo, cancellationToken);
            if (item is null)
            {
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(item.OutboundOrderId)}/cancel")
            {
                Content = JsonContent.Create(new CancelOutboundOrderHttpRequest(item.OutboundOrderId, reason)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<OutboundOrderItem?> FindOpenOutboundOrderAsync(
        string organizationId,
        string environmentId,
        string deliveryOrderNo,
        CancellationToken cancellationToken)
    {
        var path = $"/api/business/v1/wms/outbound-orders?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&status=Open&keyword={Uri.EscapeDataString(deliveryOrderNo)}&skip=0&take=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<OutboundOrderListResponse>>(cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? "WMS did not return outbound order lookup data.");
        }

        return envelope.Data.Items.SingleOrDefault(x => string.Equals(x.OutboundOrderNo, deliveryOrderNo, StringComparison.Ordinal));
    }

    private sealed record CancelOutboundOrderHttpRequest(string OutboundOrderId, string Reason);
    private sealed record OutboundOrderItem(string OutboundOrderId, string OutboundOrderNo, string Status, DateTime CreatedAtUtc);
    private sealed record OutboundOrderListResponse(IReadOnlyCollection<OutboundOrderItem> Items, int Total);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
