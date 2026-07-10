using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.Wms;

public interface IWmsOutboundCancellationClient
{
    Task<IReadOnlyCollection<WmsOutboundCancellationResult>> CancelForDeliveryOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> deliveryOrderNos,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record WmsOutboundCancellationResult(string DeliveryOrderNo, WmsOutboundCancellationStatus Status);

public enum WmsOutboundCancellationStatus
{
    Cancellable,
    Cancelled,
    NotFound,
    NotCancellable,
}

public sealed class HttpWmsOutboundCancellationClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IWmsOutboundCancellationClient
{
    public async Task<IReadOnlyCollection<WmsOutboundCancellationResult>> CancelForDeliveryOrdersAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> deliveryOrderNos,
        string reason,
        CancellationToken cancellationToken)
    {
        var candidates = new List<WmsOutboundCancellationCandidate>();
        foreach (var deliveryOrderNo in deliveryOrderNos.Distinct(StringComparer.Ordinal))
        {
            var item = await FindOutboundOrderAsync(organizationId, environmentId, deliveryOrderNo, cancellationToken);
            candidates.Add(new WmsOutboundCancellationCandidate(deliveryOrderNo, item));
        }

        if (candidates.Any(x => x.Item is null || !IsOpen(x.Item.Status)))
        {
            return candidates.Select(ToPreflightResult).ToArray();
        }

        var results = new List<WmsOutboundCancellationResult>();
        foreach (var candidate in candidates)
        {
            var item = candidate.Item;
            if (item is null)
            {
                results.Add(new WmsOutboundCancellationResult(candidate.DeliveryOrderNo, WmsOutboundCancellationStatus.NotFound));
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/business/v1/wms/outbound-orders/{Uri.EscapeDataString(item.OutboundOrderId)}/cancel")
            {
                Content = JsonContent.Create(new CancelOutboundOrderHttpRequest(item.OutboundOrderId, reason)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            results.Add(new WmsOutboundCancellationResult(candidate.DeliveryOrderNo, WmsOutboundCancellationStatus.Cancelled));
        }

        return results;
    }

    private static WmsOutboundCancellationResult ToPreflightResult(WmsOutboundCancellationCandidate candidate)
    {
        if (candidate.Item is null)
        {
            return new WmsOutboundCancellationResult(candidate.DeliveryOrderNo, WmsOutboundCancellationStatus.NotFound);
        }

        return new WmsOutboundCancellationResult(
            candidate.DeliveryOrderNo,
            IsOpen(candidate.Item.Status) ? WmsOutboundCancellationStatus.Cancellable : WmsOutboundCancellationStatus.NotCancellable);
    }

    private static bool IsOpen(string status)
    {
        return string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OutboundOrderItem?> FindOutboundOrderAsync(
        string organizationId,
        string environmentId,
        string deliveryOrderNo,
        CancellationToken cancellationToken)
    {
        var path = $"/api/business/v1/wms/outbound-orders?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&keyword={Uri.EscapeDataString(deliveryOrderNo)}&skip=0&take=100";
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
    private sealed record WmsOutboundCancellationCandidate(string DeliveryOrderNo, OutboundOrderItem? Item);
    private sealed record OutboundOrderItem(string OutboundOrderId, string OutboundOrderNo, string Status, DateTime CreatedAtUtc);
    private sealed record OutboundOrderListResponse(IReadOnlyCollection<OutboundOrderItem> Items, int Total);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
