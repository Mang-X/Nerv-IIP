namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public interface IBusinessBarcodeLabelClient
{
    Task<BusinessConsoleBarcodeRuleListResponse> ListRulesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeRuleListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateBarcodeRuleResponse> CreateOrUpdateRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeRuleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodeTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeTemplateListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateBarcodeTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateBarcodePrintBatchResponse> CreatePrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleCreateBarcodePrintBatchRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodePrintLifecycleResponse> DispatchPrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleDispatchBarcodePrintBatchRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReprintBarcodeLabelResponse> ReprintLabelAsync(
        string internalBearerToken,
        BusinessConsoleReprintBarcodeLabelRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodePrintLifecycleResponse> VoidLabelAsync(
        string internalBearerToken,
        BusinessConsoleVoidBarcodeLabelRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodePrintBatchResponse> GetPrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodePrintBatchListResponse> ListPrintBatchesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordBarcodeScanResponse> RecordScanAsync(
        string internalBearerToken,
        BusinessConsoleRecordBarcodeScanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBarcodeScanListResponse> ListScansAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeScanListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessBarcodeLabelClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessBarcodeLabelClient
{
    public Task<BusinessConsoleBarcodeRuleListResponse> ListRulesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeRuleListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodeRuleListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/barcodes/rules?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateOrUpdateBarcodeRuleResponse> CreateOrUpdateRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeRuleRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateOrUpdateBarcodeRuleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/barcodes/rules",
            request,
            cancellationToken);

    public Task<BusinessConsoleBarcodeTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeTemplateListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodeTemplateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/barcodes/templates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateOrUpdateBarcodeTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateOrUpdateBarcodeTemplateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/barcodes/templates",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateBarcodePrintBatchResponse> CreatePrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleCreateBarcodePrintBatchRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateBarcodePrintBatchResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/barcodes/print-batches",
            request,
            cancellationToken);

    public Task<BusinessConsoleBarcodePrintLifecycleResponse> DispatchPrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleDispatchBarcodePrintBatchRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodePrintLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/barcodes/print-batches/{Uri.EscapeDataString(request.PrintBatchId)}/dispatch",
            new DownstreamDispatchBarcodePrintBatchRequest(
                request.PrintBatchId,
                request.OrganizationId,
                request.EnvironmentId,
                request.PrinterId),
            cancellationToken);

    public Task<BusinessConsoleReprintBarcodeLabelResponse> ReprintLabelAsync(
        string internalBearerToken,
        BusinessConsoleReprintBarcodeLabelRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReprintBarcodeLabelResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/barcodes/print-batches/{Uri.EscapeDataString(request.PrintBatchId)}/items/{request.SequenceNo}/reprint",
            new DownstreamReprintBarcodeLabelRequest(
                request.PrintBatchId,
                request.SequenceNo,
                request.OrganizationId,
                request.EnvironmentId,
                request.PrinterId),
            cancellationToken);

    public Task<BusinessConsoleBarcodePrintLifecycleResponse> VoidLabelAsync(
        string internalBearerToken,
        BusinessConsoleVoidBarcodeLabelRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodePrintLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/barcodes/print-batches/{Uri.EscapeDataString(request.PrintBatchId)}/items/{request.SequenceNo}/void",
            new DownstreamVoidBarcodeLabelRequest(
                request.PrintBatchId,
                request.SequenceNo,
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason),
            cancellationToken);

    public Task<BusinessConsoleBarcodePrintBatchResponse> GetPrintBatchAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodePrintBatchResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/barcodes/print-batches/{Uri.EscapeDataString(request.PrintBatchId)}",
            null,
            cancellationToken);

    public Task<BusinessConsoleBarcodePrintBatchListResponse> ListPrintBatchesAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodePrintBatchListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/barcodes/print-batches?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("sourceDocumentType", request.SourceDocumentType),
                ("sourceDocumentId", request.SourceDocumentId),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleRecordBarcodeScanResponse> RecordScanAsync(
        string internalBearerToken,
        BusinessConsoleRecordBarcodeScanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordBarcodeScanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/barcodes/scans",
            request,
            cancellationToken);

    public Task<BusinessConsoleBarcodeScanListResponse> ListScansAsync(
        string internalBearerToken,
        BusinessConsoleBarcodeScanListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBarcodeScanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/barcodes/scans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceCode", request.DeviceCode),
                ("scannedValue", request.ScannedValue),
                ("sourceWorkflow", request.SourceWorkflow),
                ("sourceDocumentId", request.SourceDocumentId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    private sealed record DownstreamDispatchBarcodePrintBatchRequest(
        string PrintBatchId,
        string OrganizationId,
        string EnvironmentId,
        string PrinterId);

    private sealed record DownstreamReprintBarcodeLabelRequest(
        string PrintBatchId,
        int SequenceNo,
        string OrganizationId,
        string EnvironmentId,
        string PrinterId);

    private sealed record DownstreamVoidBarcodeLabelRequest(
        string PrintBatchId,
        int SequenceNo,
        string OrganizationId,
        string EnvironmentId,
        string Reason);
}
