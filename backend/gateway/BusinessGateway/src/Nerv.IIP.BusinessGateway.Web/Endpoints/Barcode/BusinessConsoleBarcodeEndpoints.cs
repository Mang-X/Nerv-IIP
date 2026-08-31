using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/rules")]
[BusinessGatewayOperationId("listBusinessConsoleBarcodeRules")]
public sealed class ListBusinessConsoleBarcodeRulesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodeRuleListRequest, BusinessConsoleBarcodeRuleListResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeTemplatesManage)
{
    protected override string OrganizationId(BusinessConsoleBarcodeRuleListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodeRuleListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleBarcodeRuleListResponse> ForwardAsync(
        BusinessConsoleBarcodeRuleListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ListRulesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/rules")]
[BusinessGatewayOperationId("createOrUpdateBusinessConsoleBarcodeRule")]
public sealed class CreateOrUpdateBusinessConsoleBarcodeRuleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateOrUpdateBarcodeRuleRequest, BusinessConsoleCreateOrUpdateBarcodeRuleResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeTemplatesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateOrUpdateBarcodeRuleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateOrUpdateBarcodeRuleRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateOrUpdateBarcodeRuleResponse> ForwardAsync(
        BusinessConsoleCreateOrUpdateBarcodeRuleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.CreateOrUpdateRuleAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/templates")]
[BusinessGatewayOperationId("listBusinessConsoleBarcodeTemplates")]
public sealed class ListBusinessConsoleBarcodeTemplatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodeTemplateListRequest, BusinessConsoleBarcodeTemplateListResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeTemplatesManage)
{
    protected override string OrganizationId(BusinessConsoleBarcodeTemplateListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodeTemplateListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleBarcodeTemplateListResponse> ForwardAsync(
        BusinessConsoleBarcodeTemplateListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ListTemplatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/templates")]
[BusinessGatewayOperationId("createOrUpdateBusinessConsoleBarcodeTemplate")]
public sealed class CreateOrUpdateBusinessConsoleBarcodeTemplateEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateOrUpdateBarcodeTemplateRequest, BusinessConsoleCreateOrUpdateBarcodeTemplateResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeTemplatesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateOrUpdateBarcodeTemplateResponse> ForwardAsync(
        BusinessConsoleCreateOrUpdateBarcodeTemplateRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.CreateOrUpdateTemplateAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/print-batches")]
[BusinessGatewayOperationId("createBusinessConsoleBarcodePrintBatch")]
public sealed class CreateBusinessConsoleBarcodePrintBatchEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateBarcodePrintBatchRequest, BusinessConsoleCreateBarcodePrintBatchResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleCreateBarcodePrintBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateBarcodePrintBatchRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateBarcodePrintBatchResponse> ForwardAsync(
        BusinessConsoleCreateBarcodePrintBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.CreatePrintBatchAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/print-batches")]
[BusinessGatewayOperationId("listBusinessConsoleBarcodePrintBatches")]
public sealed class ListBusinessConsoleBarcodePrintBatchesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodePrintBatchListRequest, BusinessConsoleBarcodePrintBatchListResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleBarcodePrintBatchListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodePrintBatchListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleBarcodePrintBatchListResponse> ForwardAsync(
        BusinessConsoleBarcodePrintBatchListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ListPrintBatchesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/print-batches/{printBatchId}")]
[BusinessGatewayOperationId("getBusinessConsoleBarcodePrintBatch")]
public sealed class GetBusinessConsoleBarcodePrintBatchEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodePrintBatchRequest, BusinessConsoleBarcodePrintBatchResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleBarcodePrintBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodePrintBatchRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleBarcodePrintBatchRequest request) => "barcode-print-batch";

    protected override string? ResourceId(BusinessConsoleBarcodePrintBatchRequest request) =>
        Route<string>("printBatchId") ?? request.PrintBatchId;

    protected override Task<BusinessConsoleBarcodePrintBatchResponse> ForwardAsync(
        BusinessConsoleBarcodePrintBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with { PrintBatchId = Route<string>("printBatchId") ?? request.PrintBatchId };
        return barcode.GetPrintBatchAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/print-batches/{printBatchId}/dispatch")]
[BusinessGatewayOperationId("dispatchBusinessConsoleBarcodePrintBatch")]
public sealed class DispatchBusinessConsoleBarcodePrintBatchEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleDispatchBarcodePrintBatchRequest, BusinessConsoleBarcodePrintLifecycleResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleDispatchBarcodePrintBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleDispatchBarcodePrintBatchRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleDispatchBarcodePrintBatchRequest request) => "barcode-print-batch";

    protected override string? ResourceId(BusinessConsoleDispatchBarcodePrintBatchRequest request) => request.PrintBatchId;

    protected override Task<BusinessConsoleBarcodePrintLifecycleResponse> ForwardAsync(
        BusinessConsoleDispatchBarcodePrintBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with
        {
            Body = request.Body with { PrintBatchId = request.PrintBatchId },
        };
        return barcode.DispatchPrintBatchAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/print-batches/{printBatchId}/items/{sequenceNo}/reprint")]
[BusinessGatewayOperationId("reprintBusinessConsoleBarcodeLabel")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status502BadGateway)]
public sealed class ReprintBusinessConsoleBarcodeLabelEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleReprintBarcodeLabelRequest, BusinessConsoleReprintBarcodeLabelResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleReprintBarcodeLabelRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleReprintBarcodeLabelRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleReprintBarcodeLabelRequest request) => "barcode-print-batch";

    protected override string? ResourceId(BusinessConsoleReprintBarcodeLabelRequest request) => request.PrintBatchId;

    protected override Task<BusinessConsoleReprintBarcodeLabelResponse> ForwardAsync(
        BusinessConsoleReprintBarcodeLabelRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with
        {
            Body = request.Body with
            {
                PrintBatchId = request.PrintBatchId,
                SequenceNo = request.SequenceNo,
            },
        };
        return barcode.ReprintLabelAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/print-batches/{printBatchId}/items/{sequenceNo}/void")]
[BusinessGatewayOperationId("voidBusinessConsoleBarcodeLabel")]
public sealed class VoidBusinessConsoleBarcodeLabelEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleVoidBarcodeLabelRequest, BusinessConsoleBarcodePrintLifecycleResponse>(
        auth,
        BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleVoidBarcodeLabelRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleVoidBarcodeLabelRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleVoidBarcodeLabelRequest request) => "barcode-print-batch";

    protected override string? ResourceId(BusinessConsoleVoidBarcodeLabelRequest request) => request.PrintBatchId;

    protected override Task<BusinessConsoleBarcodePrintLifecycleResponse> ForwardAsync(
        BusinessConsoleVoidBarcodeLabelRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var downstreamRequest = request with
        {
            Body = request.Body with
            {
                PrintBatchId = request.PrintBatchId,
                SequenceNo = request.SequenceNo,
            },
        };
        return barcode.VoidLabelAsync(tokenProvider.BearerToken, downstreamRequest, cancellationToken);
    }
}

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/scans")]
[BusinessGatewayOperationId("recordBusinessConsoleBarcodeScan")]
public sealed class RecordBusinessConsoleBarcodeScanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordBarcodeScanRequest, BusinessConsoleRecordBarcodeScanResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeScansWrite)
{
    protected override string OrganizationId(BusinessConsoleRecordBarcodeScanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordBarcodeScanRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleRecordBarcodeScanResponse> ForwardAsync(
        BusinessConsoleRecordBarcodeScanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.RecordScanAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/scans")]
[BusinessGatewayOperationId("listBusinessConsoleBarcodeScans")]
public sealed class ListBusinessConsoleBarcodeScansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodeScanListRequest, BusinessConsoleBarcodeScanListResponse>(
        auth,
        BusinessGatewayPermissions.BarcodeScansWrite)
{
    protected override string OrganizationId(BusinessConsoleBarcodeScanListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleBarcodeScanListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleBarcodeScanListResponse> ForwardAsync(
        BusinessConsoleBarcodeScanListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ListScansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleBarcodeRuleListRequestValidator : Validator<BusinessConsoleBarcodeRuleListRequest>
{
    public BusinessConsoleBarcodeRuleListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Keyword).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleBarcodeTemplateListRequestValidator : Validator<BusinessConsoleBarcodeTemplateListRequest>
{
    public BusinessConsoleBarcodeTemplateListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleBarcodePrintBatchListRequestValidator : Validator<BusinessConsoleBarcodePrintBatchListRequest>
{
    public BusinessConsoleBarcodePrintBatchListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleBarcodeScanListRequestValidator : Validator<BusinessConsoleBarcodeScanListRequest>
{
    public BusinessConsoleBarcodeScanListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceCode).MaximumLength(100);
        RuleFor(x => x.ScannedValue).MaximumLength(200);
        RuleFor(x => x.SourceWorkflow).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleDispatchBarcodePrintBatchRequestValidator : Validator<BusinessConsoleDispatchBarcodePrintBatchRequest>
{
    public BusinessConsoleDispatchBarcodePrintBatchRequestValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.PrinterId).NotEmpty().MaximumLength(100).When(x => x.Body is not null);
    }
}

public sealed class BusinessConsoleReprintBarcodeLabelRequestValidator : Validator<BusinessConsoleReprintBarcodeLabelRequest>
{
    public BusinessConsoleReprintBarcodeLabelRequestValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.PrinterId).NotEmpty().MaximumLength(100).When(x => x.Body is not null);
    }
}

public sealed class BusinessConsoleVoidBarcodeLabelRequestValidator : Validator<BusinessConsoleVoidBarcodeLabelRequest>
{
    public BusinessConsoleVoidBarcodeLabelRequestValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Reason).NotEmpty().MaximumLength(500).When(x => x.Body is not null);
    }
}
