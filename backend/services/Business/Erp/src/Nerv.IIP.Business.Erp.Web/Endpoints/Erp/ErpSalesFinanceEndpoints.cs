using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.Queries.SalesFinance;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record OpenOpportunityRequest(string OrganizationId, string EnvironmentId, string? OpportunityNo, string CustomerCode, string Topic, string? IdempotencyKey = null);
public sealed record OpenOpportunityResponse(OpportunityId OpportunityId);
public sealed record CreateQuotationRequest(string OrganizationId, string EnvironmentId, string? QuotationNo, string CustomerCode, DateOnly ExpiresOn, IReadOnlyCollection<QuotationCommandLine> Lines, string? IdempotencyKey = null);
public sealed record CreateQuotationResponse(QuotationId QuotationId);
public sealed record ApproveQuotationRequest(string OrganizationId, string EnvironmentId, string QuotationNo);
public sealed record CreateSalesOrderRequest(string OrganizationId, string EnvironmentId, string? SalesOrderNo, string QuotationNo, string? IdempotencyKey = null, decimal? CustomerCreditLimit = null);
public sealed record CreateSalesOrderResponse(SalesOrderId SalesOrderId);
public sealed record ReleaseDeliveryOrderRequest(string OrganizationId, string EnvironmentId, string? DeliveryOrderNo, string SalesOrderNo, IReadOnlyCollection<DeliveryOrderCommandLine> Lines, string? IdempotencyKey = null);
public sealed record ReleaseDeliveryOrderResponse(DeliveryOrderId DeliveryOrderId);
public sealed record ListSalesOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);
public sealed record ListSalesDocumentsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

public sealed record CreateAccountPayableRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    string CurrencyCode,
    DateOnly? InvoiceDate = null,
    DateOnly? DueDate = null,
    string? PaymentTermCode = null,
    string? IdempotencyKey = null);
public sealed record CreateAccountPayableResponse(AccountPayableId AccountPayableId);
public sealed record CreateAccountReceivableRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    string CurrencyCode,
    DateOnly? InvoiceDate = null,
    DateOnly? DueDate = null,
    string? PaymentTermCode = null,
    string? IdempotencyKey = null);
public sealed record CreateAccountReceivableResponse(AccountReceivableId AccountReceivableId);
public sealed record CreateCostCandidateRequest(string OrganizationId, string EnvironmentId, string? CandidateNo, string SourceType, string SourceDocumentNo, decimal Amount, string CurrencyCode, string? IdempotencyKey = null);
public sealed record CreateCostCandidateResponse(CostCandidateId CostCandidateId);
public sealed record PostJournalVoucherRequest(string OrganizationId, string EnvironmentId, string? VoucherNo, DateOnly PostingDate, IReadOnlyCollection<JournalVoucherCommandLine> Lines, string? IdempotencyKey = null);
public sealed record PostJournalVoucherResponse(JournalVoucherId JournalVoucherId);
public sealed record RegisterAccountPayablePaymentRequest(string OrganizationId, string EnvironmentId, string PayableNo, decimal Amount, DateOnly PaymentDate, string CashAccountCode, string IdempotencyKey);
public sealed record RegisterAccountReceivableCollectionRequest(string OrganizationId, string EnvironmentId, string ReceivableNo, decimal Amount, DateOnly CollectionDate, string CashAccountCode, string IdempotencyKey);
public sealed record GetFinanceSummaryRequest(string OrganizationId, string EnvironmentId);
public sealed record ListFinanceDocumentsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100,
    DateOnly? AsOfDate = null);
public sealed record GetAccountPayableBySourceDocumentRequest(string OrganizationId, string EnvironmentId, string SourceDocumentNo);
public sealed record GetAccountReceivableBySourceDocumentRequest(string OrganizationId, string EnvironmentId, string SourceDocumentNo);
public sealed record GetCostCandidateBySourceDocumentRequest(string OrganizationId, string EnvironmentId, string? SourceType, string SourceDocumentNo);

public sealed class OpenOpportunityEndpoint(ISender sender) : ErpEndpoint<OpenOpportunityRequest, ResponseData<OpenOpportunityResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<OpenOpportunityEndpoint>());

    public override async Task HandleAsync(OpenOpportunityRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new OpenOpportunityCommand(req.OrganizationId, req.EnvironmentId, req.OpportunityNo, req.CustomerCode, req.Topic, req.IdempotencyKey), ct);
        await Send.OkAsync(new OpenOpportunityResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListOpportunitiesEndpoint(ISender sender) : ErpEndpoint<ListSalesDocumentsRequest, ResponseData<ListOpportunitiesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ListOpportunitiesEndpoint>());

    public override async Task HandleAsync(ListSalesDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListOpportunitiesQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateQuotationEndpoint(ISender sender) : ErpEndpoint<CreateQuotationRequest, ResponseData<CreateQuotationResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<CreateQuotationEndpoint>());

    public override async Task HandleAsync(CreateQuotationRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateQuotationCommand(req.OrganizationId, req.EnvironmentId, req.QuotationNo, req.CustomerCode, req.ExpiresOn, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateQuotationResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListQuotationsEndpoint(ISender sender) : ErpEndpoint<ListSalesDocumentsRequest, ResponseData<ListQuotationsResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ListQuotationsEndpoint>());

    public override async Task HandleAsync(ListSalesDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListQuotationsQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ApproveQuotationEndpoint(ISender sender) : ErpEndpoint<ApproveQuotationRequest, ResponseData<string>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ApproveQuotationEndpoint>());

    public override async Task HandleAsync(ApproveQuotationRequest req, CancellationToken ct)
    {
        await sender.Send(new ApproveQuotationCommand(req.OrganizationId, req.EnvironmentId, req.QuotationNo), ct);
        await Send.OkAsync("approved".AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateSalesOrderEndpoint(ISender sender) : ErpEndpoint<CreateSalesOrderRequest, ResponseData<CreateSalesOrderResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<CreateSalesOrderEndpoint>());

    public override async Task HandleAsync(CreateSalesOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateSalesOrderCommand(req.OrganizationId, req.EnvironmentId, req.SalesOrderNo, req.QuotationNo, req.IdempotencyKey, req.CustomerCreditLimit), ct);
        await Send.OkAsync(new CreateSalesOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ReleaseDeliveryOrderEndpoint(ISender sender) : ErpEndpoint<ReleaseDeliveryOrderRequest, ResponseData<ReleaseDeliveryOrderResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ReleaseDeliveryOrderEndpoint>());

    public override async Task HandleAsync(ReleaseDeliveryOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new ReleaseDeliveryOrderCommand(req.OrganizationId, req.EnvironmentId, req.DeliveryOrderNo, req.SalesOrderNo, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new ReleaseDeliveryOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListDeliveryOrdersEndpoint(ISender sender) : ErpEndpoint<ListSalesDocumentsRequest, ResponseData<ListDeliveryOrdersResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ListDeliveryOrdersEndpoint>());

    public override async Task HandleAsync(ListSalesDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListDeliveryOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSalesOrdersEndpoint(ISender sender) : ErpEndpoint<ListSalesOrdersRequest, ResponseData<ListSalesOrdersResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpSalesEndpointContracts.Get<ListSalesOrdersEndpoint>());

    public override async Task HandleAsync(ListSalesOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSalesOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateAccountPayableEndpoint(ISender sender) : ErpEndpoint<CreateAccountPayableRequest, ResponseData<CreateAccountPayableResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<CreateAccountPayableEndpoint>());

    public override async Task HandleAsync(CreateAccountPayableRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateAccountPayableCommand(req.OrganizationId, req.EnvironmentId, req.PayableNo, req.SourceDocumentNo, req.SupplierCode, req.Amount, req.CurrencyCode, req.InvoiceDate, req.DueDate, req.PaymentTermCode, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateAccountPayableResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateAccountReceivableEndpoint(ISender sender) : ErpEndpoint<CreateAccountReceivableRequest, ResponseData<CreateAccountReceivableResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<CreateAccountReceivableEndpoint>());

    public override async Task HandleAsync(CreateAccountReceivableRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateAccountReceivableCommand(req.OrganizationId, req.EnvironmentId, req.ReceivableNo, req.SourceDocumentNo, req.CustomerCode, req.Amount, req.CurrencyCode, req.InvoiceDate, req.DueDate, req.PaymentTermCode, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateAccountReceivableResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateCostCandidateEndpoint(ISender sender) : ErpEndpoint<CreateCostCandidateRequest, ResponseData<CreateCostCandidateResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<CreateCostCandidateEndpoint>());

    public override async Task HandleAsync(CreateCostCandidateRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateCostCandidateCommand(req.OrganizationId, req.EnvironmentId, req.CandidateNo, req.SourceType, req.SourceDocumentNo, req.Amount, req.CurrencyCode, req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateCostCandidateResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class PostJournalVoucherEndpoint(ISender sender) : ErpEndpoint<PostJournalVoucherRequest, ResponseData<PostJournalVoucherResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<PostJournalVoucherEndpoint>());

    public override async Task HandleAsync(PostJournalVoucherRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new PostJournalVoucherCommand(req.OrganizationId, req.EnvironmentId, req.VoucherNo, req.PostingDate, req.Lines, req.IdempotencyKey), ct);
        await Send.OkAsync(new PostJournalVoucherResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListJournalVouchersEndpoint(ISender sender) : ErpEndpoint<ListFinanceDocumentsRequest, ResponseData<ListJournalVouchersResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListJournalVouchersEndpoint>());

    public override async Task HandleAsync(ListFinanceDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListJournalVouchersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RegisterAccountPayablePaymentEndpoint(ISender sender) : ErpEndpoint<RegisterAccountPayablePaymentRequest, ResponseData<string>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<RegisterAccountPayablePaymentEndpoint>());

    public override async Task HandleAsync(RegisterAccountPayablePaymentRequest req, CancellationToken ct)
    {
        await sender.Send(new RegisterAccountPayablePaymentCommand(req.OrganizationId, req.EnvironmentId, req.PayableNo, req.Amount, req.PaymentDate, req.CashAccountCode, req.IdempotencyKey), ct);
        await Send.OkAsync("registered".AsResponseData(), cancellation: ct);
    }
}

public sealed class RegisterAccountReceivableCollectionEndpoint(ISender sender) : ErpEndpoint<RegisterAccountReceivableCollectionRequest, ResponseData<string>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<RegisterAccountReceivableCollectionEndpoint>());

    public override async Task HandleAsync(RegisterAccountReceivableCollectionRequest req, CancellationToken ct)
    {
        await sender.Send(new RegisterAccountReceivableCollectionCommand(req.OrganizationId, req.EnvironmentId, req.ReceivableNo, req.Amount, req.CollectionDate, req.CashAccountCode, req.IdempotencyKey), ct);
        await Send.OkAsync("registered".AsResponseData(), cancellation: ct);
    }
}

public sealed class GetFinanceSummaryEndpoint(ISender sender) : ErpEndpoint<GetFinanceSummaryRequest, ResponseData<FinanceSummaryResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetFinanceSummaryEndpoint>());

    public override async Task HandleAsync(GetFinanceSummaryRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetFinanceSummaryQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListAccountPayablesEndpoint(ISender sender) : ErpEndpoint<ListFinanceDocumentsRequest, ResponseData<ListAccountPayablesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListAccountPayablesEndpoint>());

    public override async Task HandleAsync(ListFinanceDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListAccountPayablesQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take, req.AsOfDate), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListAccountReceivablesEndpoint(ISender sender) : ErpEndpoint<ListFinanceDocumentsRequest, ResponseData<ListAccountReceivablesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListAccountReceivablesEndpoint>());

    public override async Task HandleAsync(ListFinanceDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListAccountReceivablesQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take, req.AsOfDate), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListCostCandidatesEndpoint(ISender sender) : ErpEndpoint<ListFinanceDocumentsRequest, ResponseData<ListCostCandidatesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListCostCandidatesEndpoint>());

    public override async Task HandleAsync(ListFinanceDocumentsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCostCandidatesQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Keyword, req.Skip, req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetAccountPayableBySourceDocumentEndpoint(ISender sender) : ErpEndpoint<GetAccountPayableBySourceDocumentRequest, ResponseData<AccountPayableSourceDocumentResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetAccountPayableBySourceDocumentEndpoint>());

    public override async Task HandleAsync(GetAccountPayableBySourceDocumentRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetAccountPayableBySourceDocumentQuery(req.OrganizationId, req.EnvironmentId, req.SourceDocumentNo), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetAccountReceivableBySourceDocumentEndpoint(ISender sender) : ErpEndpoint<GetAccountReceivableBySourceDocumentRequest, ResponseData<AccountReceivableSourceDocumentResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetAccountReceivableBySourceDocumentEndpoint>());

    public override async Task HandleAsync(GetAccountReceivableBySourceDocumentRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetAccountReceivableBySourceDocumentQuery(req.OrganizationId, req.EnvironmentId, req.SourceDocumentNo), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetCostCandidateBySourceDocumentEndpoint(ISender sender) : ErpEndpoint<GetCostCandidateBySourceDocumentRequest, ResponseData<CostCandidateSourceDocumentResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetCostCandidateBySourceDocumentEndpoint>());

    public override async Task HandleAsync(GetCostCandidateBySourceDocumentRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetCostCandidateBySourceDocumentQuery(req.OrganizationId, req.EnvironmentId, req.SourceType, req.SourceDocumentNo), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public static class ErpSalesEndpointContracts
{
    public static readonly IReadOnlyCollection<ErpEndpointContract> All =
    [
        new(typeof(OpenOpportunityEndpoint), "POST", "/api/business/v1/erp/opportunities", ErpPermissionCodes.SalesManage, InternalServiceAuthorizationPolicy.Name, "openErpOpportunity"),
        new(typeof(ListOpportunitiesEndpoint), "GET", "/api/business/v1/erp/opportunities", ErpPermissionCodes.SalesRead, InternalServiceAuthorizationPolicy.Name, "listErpOpportunities"),
        new(typeof(CreateQuotationEndpoint), "POST", "/api/business/v1/erp/quotations", ErpPermissionCodes.SalesManage, InternalServiceAuthorizationPolicy.Name, "createErpQuotation"),
        new(typeof(ListQuotationsEndpoint), "GET", "/api/business/v1/erp/quotations", ErpPermissionCodes.SalesRead, InternalServiceAuthorizationPolicy.Name, "listErpQuotations"),
        new(typeof(ApproveQuotationEndpoint), "POST", "/api/business/v1/erp/quotations/{quotationId}/approve", ErpPermissionCodes.SalesManage, InternalServiceAuthorizationPolicy.Name, "approveErpQuotation"),
        new(typeof(CreateSalesOrderEndpoint), "POST", "/api/business/v1/erp/sales-orders", ErpPermissionCodes.SalesManage, InternalServiceAuthorizationPolicy.Name, "createErpSalesOrder"),
        new(typeof(ReleaseDeliveryOrderEndpoint), "POST", "/api/business/v1/erp/delivery-orders", ErpPermissionCodes.SalesManage, InternalServiceAuthorizationPolicy.Name, "releaseErpDeliveryOrder"),
        new(typeof(ListDeliveryOrdersEndpoint), "GET", "/api/business/v1/erp/delivery-orders", ErpPermissionCodes.SalesRead, InternalServiceAuthorizationPolicy.Name, "listErpDeliveryOrders"),
        new(typeof(ListSalesOrdersEndpoint), "GET", "/api/business/v1/erp/sales-orders", ErpPermissionCodes.SalesRead, InternalServiceAuthorizationPolicy.Name, "listErpSalesOrders"),
    ];

    public static ErpEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));
}

public static class ErpFinanceEndpointContracts
{
    public static readonly IReadOnlyCollection<ErpEndpointContract> All =
    [
        new(typeof(CreateAccountPayableEndpoint), "POST", "/api/business/v1/erp/finance/payables", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "createErpAccountPayable"),
        new(typeof(CreateAccountReceivableEndpoint), "POST", "/api/business/v1/erp/finance/receivables", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "createErpAccountReceivable"),
        new(typeof(CreateCostCandidateEndpoint), "POST", "/api/business/v1/erp/finance/cost-candidates", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "createErpCostCandidate"),
        new(typeof(PostJournalVoucherEndpoint), "POST", "/api/business/v1/erp/finance/vouchers", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "postErpJournalVoucher"),
        new(typeof(RegisterAccountPayablePaymentEndpoint), "POST", "/api/business/v1/erp/finance/payables/payment", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "registerErpAccountPayablePayment"),
        new(typeof(RegisterAccountReceivableCollectionEndpoint), "POST", "/api/business/v1/erp/finance/receivables/collection", ErpPermissionCodes.FinanceManage, InternalServiceAuthorizationPolicy.Name, "registerErpAccountReceivableCollection"),
        new(typeof(ListJournalVouchersEndpoint), "GET", "/api/business/v1/erp/finance/vouchers", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "listErpJournalVouchers"),
        new(typeof(GetFinanceSummaryEndpoint), "GET", "/api/business/v1/erp/finance/summary", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "getErpFinanceSummary"),
        new(typeof(ListAccountPayablesEndpoint), "GET", "/api/business/v1/erp/finance/payables", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "listErpAccountPayables"),
        new(typeof(ListAccountReceivablesEndpoint), "GET", "/api/business/v1/erp/finance/receivables", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "listErpAccountReceivables"),
        new(typeof(ListCostCandidatesEndpoint), "GET", "/api/business/v1/erp/finance/cost-candidates", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "listErpCostCandidates"),
        new(typeof(GetAccountPayableBySourceDocumentEndpoint), "GET", "/api/business/v1/erp/finance/payables/by-source", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "getErpPayableBySourceDocument"),
        new(typeof(GetAccountReceivableBySourceDocumentEndpoint), "GET", "/api/business/v1/erp/finance/receivables/by-source", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "getErpReceivableBySourceDocument"),
        new(typeof(GetCostCandidateBySourceDocumentEndpoint), "GET", "/api/business/v1/erp/finance/cost-candidates/by-source", ErpPermissionCodes.FinanceRead, InternalServiceAuthorizationPolicy.Name, "getErpCostCandidateBySourceDocument"),
    ];

    public static ErpEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));
}
