using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

public interface IInspectionUomConversionClient
{
    Task<IReadOnlyCollection<InspectionUomConversion>> GetConversionsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class NullInspectionUomConversionClient : IInspectionUomConversionClient
{
    public static readonly NullInspectionUomConversionClient Instance = new();

    private NullInspectionUomConversionClient()
    {
    }

    public Task<IReadOnlyCollection<InspectionUomConversion>> GetConversionsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<InspectionUomConversion>>([]);
    }
}

public interface IInspectionSourceDocumentVerifier
{
    Task<InspectionSourceDocumentVerification> VerifyAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        CancellationToken cancellationToken);
}

public sealed record InspectionSourceDocumentVerification(
    bool Exists,
    string? SkuCode = null,
    decimal? Quantity = null,
    string? Message = null);

public sealed class NullInspectionSourceDocumentVerifier : IInspectionSourceDocumentVerifier
{
    public static readonly NullInspectionSourceDocumentVerifier Instance = new();

    private NullInspectionSourceDocumentVerifier()
    {
    }

    public Task<InspectionSourceDocumentVerification> VerifyAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionSourceDocumentVerification(true, skuCode, inspectedQuantity));
    }
}

public interface IErpPurchaseReceiptFactClient
{
    Task<ErpPurchaseReceiptFact?> GetPurchaseReceiptAsync(
        string organizationId,
        string environmentId,
        string purchaseReceiptNo,
        CancellationToken cancellationToken);
}

public sealed record ErpPurchaseReceiptFact(
    string PurchaseReceiptNo,
    string Status,
    IReadOnlyCollection<ErpPurchaseReceiptLineFact> Lines);

public sealed record ErpPurchaseReceiptLineFact(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string? LotNo,
    string Status);

public sealed class ErpPurchaseReceiptInspectionSourceDocumentVerifier(
    IErpPurchaseReceiptFactClient receiptFactClient) : IInspectionSourceDocumentVerifier
{
    private static readonly string[] SupportedSourceServices =
    [
        "purchase-receipt",
        "erp-purchase-receipt",
        "business-erp",
        "erp",
        "business-wms",
        "wms",
        "inbound-receipt",
    ];

    public async Task<InspectionSourceDocumentVerification> VerifyAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(sourceType, "receiving", StringComparison.OrdinalIgnoreCase))
        {
            return new InspectionSourceDocumentVerification(true, skuCode, inspectedQuantity);
        }

        if (!SupportedSourceServices.Contains(sourceService, StringComparer.OrdinalIgnoreCase))
        {
            return new InspectionSourceDocumentVerification(
                false,
                Message: $"Receiving inspection source service '{sourceService}' is not backed by ERP purchase receipt facts.");
        }

        var receipt = await receiptFactClient.GetPurchaseReceiptAsync(
            organizationId,
            environmentId,
            sourceDocumentId,
            cancellationToken);
        if (receipt is null)
        {
            return new InspectionSourceDocumentVerification(
                false,
                Message: $"ERP purchase receipt source document '{sourceDocumentId}' was not found.");
        }

        var matchingLines = receipt.Lines
            .Where(line => string.Equals(line.SkuCode, skuCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingLines.Length > 0)
        {
            return new InspectionSourceDocumentVerification(
                true,
                matchingLines[0].SkuCode,
                matchingLines.Sum(line => line.ReceivedQuantity));
        }

        var firstLine = receipt.Lines.FirstOrDefault();
        return new InspectionSourceDocumentVerification(
            true,
            firstLine?.SkuCode,
            firstLine?.ReceivedQuantity,
            $"ERP purchase receipt '{sourceDocumentId}' does not contain SKU '{skuCode}'.");
    }
}

public sealed class HttpErpPurchaseReceiptFactClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IErpPurchaseReceiptFactClient
{
    public async Task<ErpPurchaseReceiptFact?> GetPurchaseReceiptAsync(
        string organizationId,
        string environmentId,
        string purchaseReceiptNo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/erp/purchase-receipts/{Uri.EscapeDataString(purchaseReceiptNo)}/source-document?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ErpPurchaseReceiptFact?>>(
            cancellationToken);
        return envelope?.Data;
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
