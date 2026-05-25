using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.BarcodeRules;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.LabelTemplates;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

public abstract class BarcodeLabelEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureBarcodeLabelContract(BarcodeLabelEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by BarcodeLabel endpoints.");
        }

        Tags("Business BarcodeLabel");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreateOrUpdateBarcodeRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status);

public sealed record CreateOrUpdateBarcodeRuleResponse(BarcodeRuleId BarcodeRuleId);

public sealed record CreateOrUpdateLabelTemplateRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string Status);

public sealed record CreateOrUpdateLabelTemplateResponse(LabelTemplateId TemplateId);

public sealed record ListLabelTemplatesRequest(string OrganizationId, string EnvironmentId, string? Status);

public sealed record ListLabelTemplatesResponse(IReadOnlyCollection<LabelTemplateSummary> Templates);

public sealed record CreateLabelPrintBatchRequest(
    string OrganizationId,
    string EnvironmentId,
    BarcodeRuleId BarcodeRuleId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string LabelValuesJson,
    int RequestedQuantity);

public sealed record CreateLabelPrintBatchResponse(LabelPrintBatchId PrintBatchId);

public sealed record GetLabelPrintBatchRequest(LabelPrintBatchId PrintBatchId);

public sealed record GetLabelPrintBatchResponse(LabelPrintBatchDetail PrintBatch);

public sealed record RecordScanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey,
    string Result,
    string? RejectionReason);

public sealed record RecordScanResponse(ScanRecordId ScanRecordId);

public sealed record ListScansRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceCode,
    string? ScannedValue,
    string? SourceWorkflow,
    string? SourceDocumentId);

public sealed record ListScansResponse(IReadOnlyCollection<ScanRecordSummary> Scans);

public sealed class CreateOrUpdateBarcodeRuleEndpoint(ISender sender)
    : BarcodeLabelEndpoint<CreateOrUpdateBarcodeRuleRequest, ResponseData<CreateOrUpdateBarcodeRuleResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<CreateOrUpdateBarcodeRuleEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateBarcodeRuleRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateBarcodeRuleCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.RuleCode,
            req.BarcodeType,
            req.Prefix,
            req.Length,
            req.ChecksumRule,
            req.AllowedSourceDocumentTypes,
            req.Status), ct);
        await Send.OkAsync(new CreateOrUpdateBarcodeRuleResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOrUpdateLabelTemplateEndpoint(ISender sender)
    : BarcodeLabelEndpoint<CreateOrUpdateLabelTemplateRequest, ResponseData<CreateOrUpdateLabelTemplateResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<CreateOrUpdateLabelTemplateEndpoint>());
    }

    public override async Task HandleAsync(CreateOrUpdateLabelTemplateRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateLabelTemplateCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.TemplateCode,
            req.TemplateName,
            req.TemplateFileId,
            req.VariableSchemaJson,
            req.Status), ct);
        await Send.OkAsync(new CreateOrUpdateLabelTemplateResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListLabelTemplatesEndpoint(ISender sender)
    : BarcodeLabelEndpoint<ListLabelTemplatesRequest, ResponseData<ListLabelTemplatesResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<ListLabelTemplatesEndpoint>());
    }

    public override async Task HandleAsync(ListLabelTemplatesRequest req, CancellationToken ct)
    {
        var templates = await sender.Send(new ListLabelTemplatesQuery(req.OrganizationId, req.EnvironmentId, req.Status), ct);
        await Send.OkAsync(new ListLabelTemplatesResponse(templates).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateLabelPrintBatchEndpoint(ISender sender)
    : BarcodeLabelEndpoint<CreateLabelPrintBatchRequest, ResponseData<CreateLabelPrintBatchResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<CreateLabelPrintBatchEndpoint>());
    }

    public override async Task HandleAsync(CreateLabelPrintBatchRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateLabelPrintBatchCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.BarcodeRuleId,
            req.LabelTemplateId,
            req.SourceDocumentType,
            req.SourceDocumentId,
            req.IdempotencyKey,
            req.LabelValuesJson,
            req.RequestedQuantity), ct);
        await Send.OkAsync(new CreateLabelPrintBatchResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetLabelPrintBatchEndpoint(ISender sender)
    : BarcodeLabelEndpoint<GetLabelPrintBatchRequest, ResponseData<GetLabelPrintBatchResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<GetLabelPrintBatchEndpoint>());
    }

    public override async Task HandleAsync(GetLabelPrintBatchRequest req, CancellationToken ct)
    {
        var batch = await sender.Send(new GetLabelPrintBatchQuery(req.PrintBatchId), ct);
        await Send.OkAsync(new GetLabelPrintBatchResponse(batch).AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordScanEndpoint(ISender sender)
    : BarcodeLabelEndpoint<RecordScanRequest, ResponseData<RecordScanResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<RecordScanEndpoint>());
    }

    public override async Task HandleAsync(RecordScanRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new RecordScanCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DeviceCode,
            req.ScannedValue,
            req.SourceWorkflow,
            req.SourceDocumentId,
            req.IdempotencyKey,
            req.Result,
            req.RejectionReason), ct);
        await Send.OkAsync(new RecordScanResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListScansEndpoint(ISender sender)
    : BarcodeLabelEndpoint<ListScansRequest, ResponseData<ListScansResponse>>
{
    public override void Configure()
    {
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<ListScansEndpoint>());
    }

    public override async Task HandleAsync(ListScansRequest req, CancellationToken ct)
    {
        var scans = await sender.Send(new ListScansQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.DeviceCode,
            req.ScannedValue,
            req.SourceWorkflow,
            req.SourceDocumentId), ct);
        await Send.OkAsync(new ListScansResponse(scans).AsResponseData(), cancellation: ct);
    }
}

public sealed record BarcodeLabelEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class BarcodeLabelEndpointContracts
{
    public static readonly IReadOnlyCollection<BarcodeLabelEndpointContract> All =
    [
        new(typeof(CreateOrUpdateBarcodeRuleEndpoint), "POST", "/api/business/v1/barcodes/rules", BarcodeLabelPermissionCodes.TemplatesManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateBusinessBarcodeRule"),
        new(typeof(CreateOrUpdateLabelTemplateEndpoint), "POST", "/api/business/v1/barcodes/templates", BarcodeLabelPermissionCodes.TemplatesManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateBusinessBarcodeTemplate"),
        new(typeof(ListLabelTemplatesEndpoint), "GET", "/api/business/v1/barcodes/templates", BarcodeLabelPermissionCodes.TemplatesManage, InternalServiceAuthorizationPolicy.Name, "listBusinessBarcodeTemplates"),
        new(typeof(CreateLabelPrintBatchEndpoint), "POST", "/api/business/v1/barcodes/print-batches", BarcodeLabelPermissionCodes.Print, InternalServiceAuthorizationPolicy.Name, "createBusinessBarcodePrintBatch"),
        new(typeof(GetLabelPrintBatchEndpoint), "GET", "/api/business/v1/barcodes/print-batches/{printBatchId}", BarcodeLabelPermissionCodes.Print, InternalServiceAuthorizationPolicy.Name, "getBusinessBarcodePrintBatch"),
        new(typeof(RecordScanEndpoint), "POST", "/api/business/v1/barcodes/scans", BarcodeLabelPermissionCodes.ScansWrite, InternalServiceAuthorizationPolicy.Name, "recordBusinessBarcodeScan"),
        new(typeof(ListScansEndpoint), "GET", "/api/business/v1/barcodes/scans", BarcodeLabelPermissionCodes.ScansWrite, InternalServiceAuthorizationPolicy.Name, "listBusinessBarcodeScans"),
    ];

    public static BarcodeLabelEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out BarcodeLabelEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
