using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public abstract class ErpEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureErpContract(ErpEndpointContract contract)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by ERP endpoints.");
        }

        Tags("Business ERP");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreatePurchaseRequisitionFromSuggestionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string? IdempotencyKey = null);

public sealed record CreatePurchaseRequisitionFromSuggestionResponse(
    PurchaseRequisitionId PurchaseRequisitionId,
    string RequisitionNo);

public sealed record CreateRequestForQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RfqNo,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<RfqCommandLine> Lines,
    string? IdempotencyKey = null);

public sealed record CreateRequestForQuotationResponse(RequestForQuotationId RequestForQuotationId);

public sealed record ConvertPurchaseRequisitionsToPurchaseOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> PurchaseRequisitionNos,
    string? PurchaseOrderNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<string>? RfqSupplierCodes = null,
    string? RfqNo = null,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY");

public sealed record ConvertPurchaseRequisitionsToPurchaseOrderResponse(
    string Status,
    PurchaseOrderId? PurchaseOrderId = null,
    string? PurchaseOrderNo = null,
    string? RfqNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<ConvertedPurchaseOrderLineResult>? Lines = null);

public sealed record ReceiveSupplierQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string RfqNo,
    string SupplierCode,
    IReadOnlyCollection<SupplierQuotationCommandLine> Lines,
    string? IdempotencyKey = null);

public sealed record ReceiveSupplierQuotationResponse(SupplierQuotationId SupplierQuotationId);

public sealed record CreatePurchaseOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyCollection<PurchaseOrderCommandLine> Lines,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY");

public sealed record CreatePurchaseOrderResponse(PurchaseOrderId PurchaseOrderId);

public sealed record RequestPurchaseOrderChangeRequest(string OrganizationId, string EnvironmentId, string PurchaseOrderNo, IReadOnlyCollection<PurchaseOrderLineChangeDraft> Lines, string? Reason = null, string StartedBy = "system:erp");
public sealed record RequestPurchaseOrderChangeResponse(string ApprovalChainId);
public sealed record ClosePurchaseOrderLineRequest(string OrganizationId, string EnvironmentId, string PurchaseOrderNo, string LineNo, string Reason);
public sealed record CancelPurchaseOrderRequest(string OrganizationId, string EnvironmentId, string PurchaseOrderNo, string Reason);

public sealed record RecordPurchaseReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseReceiptCommandLine> Lines,
    string? IdempotencyKey = null,
    decimal ExchangeRate = 1m);

public sealed record RecordPurchaseReceiptResponse(PurchaseReceiptId PurchaseReceiptId);

public sealed record RecordSupplierInvoiceRequest(
    string OrganizationId,
    string EnvironmentId,
    string? InvoiceNo,
    string PurchaseOrderNo,
    string PurchaseReceiptNo,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal QuantityTolerance,
    decimal AmountTolerance,
    IReadOnlyCollection<SupplierInvoiceCommandLine> Lines,
    string? PayableNo = null,
    string? IdempotencyKey = null,
    decimal? PriceTolerancePercent = null,
    decimal ExchangeRate = 1m);

public sealed record RecordSupplierInvoiceResponse(SupplierInvoiceId SupplierInvoiceId);

public sealed record ReleaseSupplierInvoicePaymentHoldRequest(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo,
    string? PayableNo = null,
    string? IdempotencyKey = null);

public sealed record ReleaseSupplierInvoicePaymentHoldResponse(SupplierInvoiceId SupplierInvoiceId);

public sealed record VoidSupplierInvoicePaymentHoldRequest(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo);

public sealed record VoidSupplierInvoicePaymentHoldResponse(SupplierInvoiceId SupplierInvoiceId);

public sealed record ListPurchaseOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

public sealed record GetPurchaseReceiptSourceDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseReceiptNo);

public sealed record ListRequestsForQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

public sealed record ListPurchaseRequisitionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

public sealed class CreatePurchaseRequisitionFromSuggestionEndpoint(ISender sender)
    : ErpEndpoint<CreatePurchaseRequisitionFromSuggestionRequest, ResponseData<CreatePurchaseRequisitionFromSuggestionResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<CreatePurchaseRequisitionFromSuggestionEndpoint>());
    }

    public override async Task HandleAsync(CreatePurchaseRequisitionFromSuggestionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreatePurchaseRequisitionFromSuggestionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.RequisitionNo,
            req.SuggestionId,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.Quantity,
            req.RequiredDate,
            req.IdempotencyKey), ct);
        await Send.OkAsync(new CreatePurchaseRequisitionFromSuggestionResponse(result.PurchaseRequisitionId, result.RequisitionNo).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateRequestForQuotationEndpoint(ISender sender)
    : ErpEndpoint<CreateRequestForQuotationRequest, ResponseData<CreateRequestForQuotationResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<CreateRequestForQuotationEndpoint>());
    }

    public override async Task HandleAsync(CreateRequestForQuotationRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateRequestForQuotationCommand(req.OrganizationId, req.EnvironmentId, req.RfqNo, req.SupplierCodes, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateRequestForQuotationResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ConvertPurchaseRequisitionsToPurchaseOrderEndpoint(ISender sender)
    : ErpEndpoint<ConvertPurchaseRequisitionsToPurchaseOrderRequest, ResponseData<ConvertPurchaseRequisitionsToPurchaseOrderResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ConvertPurchaseRequisitionsToPurchaseOrderEndpoint>());
    }

    public override async Task HandleAsync(ConvertPurchaseRequisitionsToPurchaseOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ConvertPurchaseRequisitionsToPurchaseOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.PurchaseRequisitionNos,
            req.PurchaseOrderNo,
            req.SupplierCode,
            req.RfqSupplierCodes,
            req.RfqNo,
            req.IdempotencyKey,
            req.CurrencyCode), ct);
        await Send.OkAsync(new ConvertPurchaseRequisitionsToPurchaseOrderResponse(
            result.Status.ToString(),
            result.PurchaseOrderId,
            result.PurchaseOrderNo,
            result.RfqNo,
            result.SupplierCode,
            result.Lines).AsResponseData(), cancellation: ct);
    }
}

public sealed class ReceiveSupplierQuotationEndpoint(ISender sender)
    : ErpEndpoint<ReceiveSupplierQuotationRequest, ResponseData<ReceiveSupplierQuotationResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ReceiveSupplierQuotationEndpoint>());
    }

    public override async Task HandleAsync(ReceiveSupplierQuotationRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new ReceiveSupplierQuotationCommand(req.OrganizationId, req.EnvironmentId, req.QuotationNo, req.RfqNo, req.SupplierCode, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new ReceiveSupplierQuotationResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListRequestsForQuotationEndpoint(ISender sender)
    : ErpEndpoint<ListRequestsForQuotationRequest, ResponseData<ListRequestsForQuotationResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ListRequestsForQuotationEndpoint>());
    }

    public override async Task HandleAsync(ListRequestsForQuotationRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListRequestsForQuotationQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPurchaseRequisitionsEndpoint(ISender sender)
    : ErpEndpoint<ListPurchaseRequisitionsRequest, ResponseData<ListPurchaseRequisitionsResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ListPurchaseRequisitionsEndpoint>());
    }

    public override async Task HandleAsync(ListPurchaseRequisitionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListPurchaseRequisitionsQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreatePurchaseOrderEndpoint(ISender sender)
    : ErpEndpoint<CreatePurchaseOrderRequest, ResponseData<CreatePurchaseOrderResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<CreatePurchaseOrderEndpoint>());
    }

    public override async Task HandleAsync(CreatePurchaseOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreatePurchaseOrderCommand(req.OrganizationId, req.EnvironmentId, req.PurchaseOrderNo, req.SupplierCode, req.SiteCode, req.Lines, req.IdempotencyKey, req.CurrencyCode), ct);
        await Send.OkAsync(new CreatePurchaseOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class RequestPurchaseOrderChangeEndpoint(ISender sender)
    : ErpEndpoint<RequestPurchaseOrderChangeRequest, ResponseData<RequestPurchaseOrderChangeResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpProcurementEndpointContracts.Get<RequestPurchaseOrderChangeEndpoint>());

    public override async Task HandleAsync(RequestPurchaseOrderChangeRequest req, CancellationToken ct)
    {
        var approvalChainId = await sender.Send(new RequestPurchaseOrderChangeCommand(req.OrganizationId, req.EnvironmentId, req.PurchaseOrderNo, req.Lines, req.Reason, req.StartedBy), ct);
        await Send.OkAsync(new RequestPurchaseOrderChangeResponse(approvalChainId).AsResponseData(), cancellation: ct);
    }
}

public sealed class ClosePurchaseOrderLineEndpoint(ISender sender) : ErpEndpoint<ClosePurchaseOrderLineRequest, ResponseData<string>>
{
    public override void Configure() => ConfigureErpContract(ErpProcurementEndpointContracts.Get<ClosePurchaseOrderLineEndpoint>());

    public override async Task HandleAsync(ClosePurchaseOrderLineRequest req, CancellationToken ct)
    {
        await sender.Send(new ClosePurchaseOrderLineCommand(req.OrganizationId, req.EnvironmentId, req.PurchaseOrderNo, req.LineNo, req.Reason), ct);
        await Send.OkAsync("closed".AsResponseData(), cancellation: ct);
    }
}

public sealed class CancelPurchaseOrderEndpoint(ISender sender) : ErpEndpoint<CancelPurchaseOrderRequest, ResponseData<string>>
{
    public override void Configure() => ConfigureErpContract(ErpProcurementEndpointContracts.Get<CancelPurchaseOrderEndpoint>());

    public override async Task HandleAsync(CancelPurchaseOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CancelPurchaseOrderCommand(req.OrganizationId, req.EnvironmentId, req.PurchaseOrderNo, req.Reason), ct);
        await Send.OkAsync("cancelled".AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordPurchaseReceiptEndpoint(ISender sender)
    : ErpEndpoint<RecordPurchaseReceiptRequest, ResponseData<RecordPurchaseReceiptResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<RecordPurchaseReceiptEndpoint>());
    }

    public override async Task HandleAsync(RecordPurchaseReceiptRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new RecordPurchaseReceiptCommand(req.OrganizationId, req.EnvironmentId, req.PurchaseReceiptNo, req.PurchaseOrderNo, req.Lines, req.IdempotencyKey, req.ExchangeRate), ct);
        await Send.OkAsync(new RecordPurchaseReceiptResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetPurchaseReceiptSourceDocumentEndpoint(ISender sender)
    : ErpEndpoint<GetPurchaseReceiptSourceDocumentRequest, ResponseData<PurchaseReceiptSourceDocumentResponse?>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<GetPurchaseReceiptSourceDocumentEndpoint>());
    }

    public override async Task HandleAsync(GetPurchaseReceiptSourceDocumentRequest req, CancellationToken ct)
    {
        var purchaseReceiptNo = Route<string>("purchaseReceiptNo") ?? req.PurchaseReceiptNo;
        var response = await sender.Send(
            new GetPurchaseReceiptSourceDocumentQuery(req.OrganizationId, req.EnvironmentId, purchaseReceiptNo),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordSupplierInvoiceEndpoint(ISender sender)
    : ErpEndpoint<RecordSupplierInvoiceRequest, ResponseData<RecordSupplierInvoiceResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<RecordSupplierInvoiceEndpoint>());
    }

    public override async Task HandleAsync(RecordSupplierInvoiceRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new RecordSupplierInvoiceCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.InvoiceNo,
            req.PurchaseOrderNo,
            req.PurchaseReceiptNo,
            req.InvoiceDate,
            req.DueDate,
            req.CurrencyCode,
            req.QuantityTolerance,
            req.AmountTolerance,
            req.Lines,
            req.PayableNo,
            req.IdempotencyKey,
            req.PriceTolerancePercent,
            req.ExchangeRate), ct);
        await Send.OkAsync(new RecordSupplierInvoiceResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ReleaseSupplierInvoicePaymentHoldEndpoint(ISender sender)
    : ErpEndpoint<ReleaseSupplierInvoicePaymentHoldRequest, ResponseData<ReleaseSupplierInvoicePaymentHoldResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ReleaseSupplierInvoicePaymentHoldEndpoint>());
    }

    public override async Task HandleAsync(ReleaseSupplierInvoicePaymentHoldRequest req, CancellationToken ct)
    {
        var invoiceNo = Route<string>("invoiceNo") ?? req.InvoiceNo;
        var id = await sender.Send(new ReleaseSupplierInvoicePaymentHoldCommand(
            req.OrganizationId,
            req.EnvironmentId,
            invoiceNo,
            req.PayableNo,
            req.IdempotencyKey ?? $"supplier-invoice-release:{req.OrganizationId}:{req.EnvironmentId}:{invoiceNo}"), ct);
        await Send.OkAsync(new ReleaseSupplierInvoicePaymentHoldResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class VoidSupplierInvoicePaymentHoldEndpoint(ISender sender)
    : ErpEndpoint<VoidSupplierInvoicePaymentHoldRequest, ResponseData<VoidSupplierInvoicePaymentHoldResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<VoidSupplierInvoicePaymentHoldEndpoint>());
    }

    public override async Task HandleAsync(VoidSupplierInvoicePaymentHoldRequest req, CancellationToken ct)
    {
        var invoiceNo = Route<string>("invoiceNo") ?? req.InvoiceNo;
        var id = await sender.Send(new VoidSupplierInvoicePaymentHoldCommand(
            req.OrganizationId,
            req.EnvironmentId,
            invoiceNo), ct);
        await Send.OkAsync(new VoidSupplierInvoicePaymentHoldResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPurchaseOrdersEndpoint(ISender sender)
    : ErpEndpoint<ListPurchaseOrdersRequest, ResponseData<ListPurchaseOrdersResponse>>
{
    public override void Configure()
    {
        ConfigureErpContract(ErpProcurementEndpointContracts.Get<ListPurchaseOrdersEndpoint>());
    }

    public override async Task HandleAsync(ListPurchaseOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListPurchaseOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record ErpEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string AuthorizationPolicy,
    string OperationId);

public static class ErpProcurementEndpointContracts
{
    public static readonly IReadOnlyCollection<ErpEndpointContract> All =
    [
        new(typeof(CreatePurchaseRequisitionFromSuggestionEndpoint), "POST", "/api/business/v1/erp/purchase-requisitions/from-suggestion", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "createErpPurchaseRequisitionFromSuggestion"),
        new(typeof(ListPurchaseRequisitionsEndpoint), "GET", "/api/business/v1/erp/purchase-requisitions", ErpPermissionCodes.ProcurementRead, InternalServiceAuthorizationPolicy.Name, "listErpPurchaseRequisitions"),
        new(typeof(ConvertPurchaseRequisitionsToPurchaseOrderEndpoint), "POST", "/api/business/v1/erp/purchase-requisitions/convert-to-purchase-order", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "convertErpPurchaseRequisitionsToPurchaseOrder"),
        new(typeof(CreateRequestForQuotationEndpoint), "POST", "/api/business/v1/erp/rfqs", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "createErpRequestForQuotation"),
        new(typeof(ReceiveSupplierQuotationEndpoint), "POST", "/api/business/v1/erp/supplier-quotations", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "receiveErpSupplierQuotation"),
        new(typeof(ListRequestsForQuotationEndpoint), "GET", "/api/business/v1/erp/rfqs", ErpPermissionCodes.ProcurementRead, InternalServiceAuthorizationPolicy.Name, "listErpRequestsForQuotation"),
        new(typeof(CreatePurchaseOrderEndpoint), "POST", "/api/business/v1/erp/purchase-orders", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "createErpPurchaseOrder"),
        new(typeof(RequestPurchaseOrderChangeEndpoint), "POST", "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/changes", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "requestErpPurchaseOrderChange"),
        new(typeof(ClosePurchaseOrderLineEndpoint), "POST", "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/lines/{lineNo}/final-delivery", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "closeErpPurchaseOrderLineFinalDelivery"),
        new(typeof(CancelPurchaseOrderEndpoint), "POST", "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/cancel", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "cancelErpPurchaseOrder"),
        new(typeof(RecordPurchaseReceiptEndpoint), "POST", "/api/business/v1/erp/purchase-receipts", ErpPermissionCodes.ProcurementManage, InternalServiceAuthorizationPolicy.Name, "recordErpPurchaseReceipt"),
        new(typeof(GetPurchaseReceiptSourceDocumentEndpoint), "GET", "/api/business/v1/erp/purchase-receipts/{purchaseReceiptNo}/source-document", ErpPermissionCodes.ProcurementRead, InternalServiceAuthorizationPolicy.Name, "getErpPurchaseReceiptSourceDocument"),
        new(typeof(RecordSupplierInvoiceEndpoint), "POST", "/api/business/v1/erp/supplier-invoices", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "recordErpSupplierInvoice"),
        new(typeof(ReleaseSupplierInvoicePaymentHoldEndpoint), "POST", "/api/business/v1/erp/supplier-invoices/{invoiceNo}/release-payment-hold", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "releaseErpSupplierInvoicePaymentHold"),
        new(typeof(VoidSupplierInvoicePaymentHoldEndpoint), "POST", "/api/business/v1/erp/supplier-invoices/{invoiceNo}/void-payment-hold", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "voidErpSupplierInvoicePaymentHold"),
        new(typeof(ListPurchaseOrdersEndpoint), "GET", "/api/business/v1/erp/purchase-orders", ErpPermissionCodes.ProcurementRead, InternalServiceAuthorizationPolicy.Name, "listErpPurchaseOrders"),
    ];

    public static ErpEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }
}

public static class ErpEndpointContracts
{
    public static IReadOnlyCollection<ErpEndpointContract> All =>
        ErpProcurementEndpointContracts.All
            .Concat(ErpSalesEndpointContracts.All)
            .Concat(ErpFinanceEndpointContracts.All)
            .ToArray();

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out ErpEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
