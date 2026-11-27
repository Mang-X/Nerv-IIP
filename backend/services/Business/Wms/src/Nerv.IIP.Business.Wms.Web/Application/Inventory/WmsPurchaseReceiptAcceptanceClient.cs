using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public interface IWmsPurchaseReceiptAcceptanceClient
{
    Task<string> GetOrAcceptAsync(InboundOrder inbound, Guid batchId, CancellationToken cancellationToken);
}

public sealed class HttpWmsPurchaseReceiptAcceptanceClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IWmsPurchaseReceiptAcceptanceClient
{
    private const string Route = "/api/business/v1/erp/wms-purchase-receipt-acceptances";

    public async Task<string> GetOrAcceptAsync(InboundOrder inbound, Guid batchId, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = $"{Route}/{batchId:D}?organizationId={Uri.EscapeDataString(inbound.OrganizationId)}&environmentId={Uri.EscapeDataString(inbound.EnvironmentId)}";
            using var query = CreateRequest(HttpMethod.Get, lookup);
            using var found = await httpClient.SendAsync(query, cancellationToken);
            if (found.StatusCode != HttpStatusCode.NotFound)
            {
                return await ReadReceiptNoAsync(found, inbound, batchId, cancellationToken);
            }

            using var submit = CreateRequest(HttpMethod.Post, Route);
            submit.Content = JsonContent.Create(new
            {
                inbound.OrganizationId,
                inbound.EnvironmentId,
                WmsReceiptBatchId = batchId,
                PurchaseOrderNo = inbound.SourceDocumentId,
                inbound.SiteCode,
                ExchangeRate = inbound.ReceiptExchangeRate!.Value,
                Lines = inbound.Lines.OrderBy(line => line.LineNo, StringComparer.Ordinal).Select(line => new
                {
                    PurchaseOrderLineNo = line.LineNo,
                    ReceivedQuantity = line.RequestedReceiptQuantity!.Value,
                    LocationCode = line.StagingLocationCode,
                    line.LotNo,
                }),
            });
            using var accepted = await httpClient.SendAsync(submit, cancellationToken);
            return await ReadReceiptNoAsync(accepted, inbound, batchId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new KnownException("ERP 收货接收未确认，请用原请求重试。", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KnownException("ERP 收货接收超时，请用原请求重试。", exception);
        }
        catch (JsonException exception)
        {
            throw new KnownException("ERP 收货接收结果无效，请用原请求重试。", exception);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        return request;
    }

    private static async Task<string> ReadReceiptNoAsync(HttpResponseMessage response, InboundOrder inbound, Guid batchId,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new WmsLifecycleConflictException("confirm-inbound-receipt", "erp-acceptance-conflict");
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<AcceptanceEnvelope>(cancellationToken);
        if (envelope is not { Success: true, Data: { } result }
            || result.WmsReceiptBatchId != batchId
            || result.PurchaseOrderNo != inbound.SourceDocumentId
            || result.SiteCode != inbound.SiteCode
            || result.ExchangeRate != inbound.ReceiptExchangeRate
            || string.IsNullOrWhiteSpace(result.PurchaseReceiptNo)
            || result.PurchaseReceiptNo.Length > 100
            || result.Lines is null || result.Lines.Length != inbound.Lines.Count
            || inbound.Lines.Any(line => !result.Lines.Any(accepted => accepted is not null
                && accepted.PurchaseOrderLineNo == line.LineNo
                && accepted.SkuCode == line.SkuCode
                && accepted.UomCode == line.UomCode
                && accepted.ReceivedQuantity == line.RequestedReceiptQuantity
                && accepted.LocationCode == line.StagingLocationCode
                && accepted.LotNo == line.LotNo)))
        {
            throw new KnownException("ERP 已提交收货与 WMS 实收批次不匹配，无法绑定。");
        }

        return result.PurchaseReceiptNo;
    }

    private sealed record AcceptanceEnvelope(bool Success, Acceptance? Data);

    private sealed record Acceptance(Guid WmsReceiptBatchId, string? PurchaseReceiptNo, string? PurchaseOrderNo,
        string? SiteCode, decimal ExchangeRate, AcceptedLine[]? Lines);

    private sealed record AcceptedLine(string PurchaseOrderLineNo, string SkuCode, string UomCode, decimal ReceivedQuantity,
        string LocationCode, string? LotNo);
}
