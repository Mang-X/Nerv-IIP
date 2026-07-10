using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.Wms;

public interface IWmsInboundCancellationClient
{
    Task CancelOpenInboundOrdersForPurchaseOrderAsync(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string reason,
        CancellationToken cancellationToken);
}

public sealed class HttpWmsInboundCancellationClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IWmsInboundCancellationClient
{
    public async Task CancelOpenInboundOrdersForPurchaseOrderAsync(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string reason,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/wms/inbound-orders/cancel-by-source")
        {
            Content = JsonContent.Create(new CancelInboundOrdersForSourceHttpRequest(
                organizationId,
                environmentId,
                "purchase-order",
                purchaseOrderNo,
                reason)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record CancelInboundOrdersForSourceHttpRequest(
        string OrganizationId,
        string EnvironmentId,
        string SourceDocumentType,
        string SourceDocumentId,
        string Reason);
}
