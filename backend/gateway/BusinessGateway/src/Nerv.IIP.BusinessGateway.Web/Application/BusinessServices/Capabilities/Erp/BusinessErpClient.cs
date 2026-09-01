using System.Net;
using System.Globalization;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public interface IBusinessErpClient
{
    Task<BusinessConsoleCreateErpPurchaseRequisitionResponse> CreatePurchaseRequisitionFromSuggestionAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseRequisitionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpRequestForQuotationResponse> CreateRequestForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpRequestForQuotationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleConvertErpPurchaseRequisitionsResponse> ConvertPurchaseRequisitionsToPurchaseOrderAsync(
        string internalBearerToken,
        BusinessConsoleConvertErpPurchaseRequisitionsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReceiveErpSupplierQuotationResponse> ReceiveSupplierQuotationAsync(
        string internalBearerToken,
        BusinessConsoleReceiveErpSupplierQuotationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpRequestForQuotationListResponse> ListRequestsForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpSupplierQuotationListResponse> ListSupplierQuotationsAsync(
        string internalBearerToken,
        BusinessConsoleErpSupplierQuotationListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpPurchaseRequisitionListResponse> ListPurchaseRequisitionsAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpPurchaseOrderListResponse> ListPurchaseOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpPurchaseOrderResponse> CreatePurchaseOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordErpPurchaseReceiptResponse> RecordPurchaseReceiptAsync(
        string internalBearerToken,
        BusinessConsoleRecordErpPurchaseReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpSalesOrderListResponse> ListSalesOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpOpportunityListResponse> ListOpportunitiesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpQuotationListResponse> ListQuotationsAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpDeliveryOrderListResponse> ListDeliveryOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpPayableListResponse> ListPayablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpReceivableListResponse> ListReceivablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpCostCandidateListResponse> ListCostCandidatesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleConfigureErpWorkCenterCostRateResponse> ConfigureWorkCenterCostRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterCostRateRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkCenterCostRateListResponse> ListWorkCenterCostRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterCostRatesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkOrderCostVarianceResponse> GetWorkOrderCostVarianceAsync(
        string internalBearerToken,
        BusinessConsoleGetErpWorkOrderCostVarianceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpMachineOverheadReconciliationListResponse> ListMachineOverheadReconciliationsAsync(
        string internalBearerToken,
        BusinessConsoleListErpMachineOverheadReconciliationsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpJournalVoucherListResponse> ListJournalVouchersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpTrialBalanceResponse> GetTrialBalanceAsync(
        string internalBearerToken,
        BusinessConsoleErpPeriodRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpMonthEndChecklistResponse> GetMonthEndChecklistAsync(
        string internalBearerToken,
        BusinessConsoleErpPeriodRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleOpenErpOpportunityResponse> OpenOpportunityAsync(
        string internalBearerToken,
        BusinessConsoleOpenErpOpportunityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpQuotationResponse> CreateQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpQuotationRequest request,
        CancellationToken cancellationToken);

    Task<string> ApproveQuotationAsync(
        string internalBearerToken,
        BusinessConsoleApproveErpQuotationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpSalesOrderResponse> CreateSalesOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpSalesOrderRequest request,
        CancellationToken cancellationToken);

    Task<string> ReleaseSalesOrderCreditHoldAsync(
        string internalBearerToken,
        BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReleaseErpDeliveryOrderResponse> ReleaseDeliveryOrderAsync(
        string internalBearerToken,
        BusinessConsoleReleaseErpDeliveryOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpAccountPayableResponse> CreateAccountPayableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountPayableRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpAccountReceivableResponse> CreateAccountReceivableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountReceivableRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateErpCostCandidateResponse> CreateCostCandidateAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpCostCandidateRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostErpJournalVoucherResponse> PostJournalVoucherAsync(
        string internalBearerToken,
        BusinessConsolePostErpJournalVoucherRequest request,
        CancellationToken cancellationToken);

    Task<string> ApprovePaymentExecutionAsync(
        string internalBearerToken,
        BusinessConsoleApproveErpPaymentExecutionRequest request,
        CancellationToken cancellationToken);

    Task<string> ExecutePaymentExecutionAsync(
        string internalBearerToken,
        BusinessConsoleExecuteErpPaymentExecutionRequest request,
        CancellationToken cancellationToken);

    Task<string> RegisterCashReceiptAsync(
        string internalBearerToken,
        BusinessConsoleRegisterErpCashReceiptRequest request,
        CancellationToken cancellationToken);

    Task<string> MatchCashReceiptAsync(
        string internalBearerToken,
        BusinessConsoleMatchErpCashReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleOpenErpAccountingPeriodResponse> OpenAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleOpenErpAccountingPeriodRequest request,
        CancellationToken cancellationToken);

    Task<string> CloseAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleCloseErpAccountingPeriodRequest request,
        CancellationToken cancellationToken);

    Task<string> ReopenAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleReopenErpAccountingPeriodRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpFinanceSummaryResponse> GetFinanceSummaryAsync(
        string internalBearerToken,
        BusinessConsoleErpContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpPayableSourceDocumentResponse> GetPayableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpReceivableSourceDocumentResponse> GetReceivableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpCostCandidateSourceDocumentResponse> GetCostCandidateBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken);
}
public sealed class HttpBusinessErpClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessErpClient
{
    public Task<BusinessConsoleCreateErpPurchaseRequisitionResponse> CreatePurchaseRequisitionFromSuggestionAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseRequisitionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpPurchaseRequisitionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/purchase-requisitions/from-suggestion",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpRequestForQuotationResponse> CreateRequestForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpRequestForQuotationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpRequestForQuotationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/rfqs",
            request,
            cancellationToken);

    public Task<BusinessConsoleConvertErpPurchaseRequisitionsResponse> ConvertPurchaseRequisitionsToPurchaseOrderAsync(
        string internalBearerToken,
        BusinessConsoleConvertErpPurchaseRequisitionsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConvertErpPurchaseRequisitionsResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/purchase-requisitions/convert-to-purchase-order",
            request,
            cancellationToken);

    public Task<BusinessConsoleReceiveErpSupplierQuotationResponse> ReceiveSupplierQuotationAsync(
        string internalBearerToken,
        BusinessConsoleReceiveErpSupplierQuotationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReceiveErpSupplierQuotationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/supplier-quotations",
            request,
            cancellationToken);

    public Task<BusinessConsoleErpRequestForQuotationListResponse> ListRequestsForQuotationAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpRequestForQuotationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/rfqs?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpSupplierQuotationListResponse> ListSupplierQuotationsAsync(
        string internalBearerToken,
        BusinessConsoleErpSupplierQuotationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpSupplierQuotationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/supplier-quotations?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("rfqNo", request.RfqNo),
                ("supplierCode", request.SupplierCode),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpPurchaseRequisitionListResponse> ListPurchaseRequisitionsAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpPurchaseRequisitionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/purchase-requisitions?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpPurchaseOrderListResponse> ListPurchaseOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        ListPurchaseOrdersCoreAsync(internalBearerToken, request, cancellationToken);

    public Task<BusinessConsoleCreateErpPurchaseOrderResponse> CreatePurchaseOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpPurchaseOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpPurchaseOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/purchase-orders",
            request,
            cancellationToken);

    public Task<BusinessConsoleRecordErpPurchaseReceiptResponse> RecordPurchaseReceiptAsync(
        string internalBearerToken,
        BusinessConsoleRecordErpPurchaseReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordErpPurchaseReceiptResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/purchase-receipts",
            request,
            cancellationToken);

    public Task<BusinessConsoleErpSalesOrderListResponse> ListSalesOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpSalesOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/sales-orders?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpOpportunityListResponse> ListOpportunitiesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpOpportunityListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/opportunities?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpQuotationListResponse> ListQuotationsAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpQuotationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/quotations?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpDeliveryOrderListResponse> ListDeliveryOrdersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpDeliveryOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/delivery-orders?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpPayableListResponse> ListPayablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpPayableListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/payables?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpReceivableListResponse> ListReceivablesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpReceivableListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/receivables?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpCostCandidateListResponse> ListCostCandidatesAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpCostCandidateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/cost-candidates?" + ErpListQuery(request),
            null,
            cancellationToken);

    public async Task<BusinessConsoleConfigureErpWorkCenterCostRateResponse> ConfigureWorkCenterCostRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterCostRateRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamConfigureWorkCenterCostRateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/work-center-cost-rates",
            request,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

        if (!Guid.TryParse(response.WorkCenterCostRateId, out var workCenterCostRateId)
            || workCenterCostRateId == Guid.Empty)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleConfigureErpWorkCenterCostRateResponse(
            workCenterCostRateId.ToString());
    }

    public Task<BusinessConsoleErpWorkCenterCostRateListResponse> ListWorkCenterCostRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterCostRatesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpWorkCenterCostRateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-center-cost-rates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("workCenterId", request.WorkCenterId),
                ("atUtc", request.AtUtc)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleErpWorkOrderCostVarianceResponse> GetWorkOrderCostVarianceAsync(
        string internalBearerToken,
        BusinessConsoleGetErpWorkOrderCostVarianceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleErpWorkOrderCostVarianceResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/erp/finance/work-order-costs/{Uri.EscapeDataString(request.WorkOrderId)}?" + Query(
                ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken,
            configureRequest: message => AddMachineOverheadScopeHeaders(message, request.OrganizationId, request.EnvironmentId));

        ValidateMachineOverheadState(
            response.MachineCostStatus,
            response.MachineCostUnavailableReason,
            response.ActualMachineHours,
            response.AppliedFixedMachineOverhead,
            response.AppliedVariableMachineOverhead,
            response.AppliedMachineOverheadTotal);
        foreach (var operation in response.MachineOverheadOperations)
        {
            ValidateMachineOverheadState(
                operation.Status,
                operation.UnavailableReason,
                operation.ActualMachineHours,
                operation.AppliedFixedMachineOverhead,
                operation.AppliedVariableMachineOverhead,
                operation.AppliedMachineOverheadTotal);
        }

        return response;
    }

    public async Task<BusinessConsoleErpMachineOverheadReconciliationListResponse> ListMachineOverheadReconciliationsAsync(
        string internalBearerToken,
        BusinessConsoleListErpMachineOverheadReconciliationsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleErpMachineOverheadReconciliationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations?" + Query(
                ("accountingPeriodCode", request.AccountingPeriodCode),
                ("workCenterId", request.WorkCenterId),
                ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken,
            configureRequest: message => AddMachineOverheadScopeHeaders(message, request.OrganizationId, request.EnvironmentId));

        ValidateMachineOverheadStatus(response.ReconciliationStatus, response.ReconciliationUnavailableReason);
        foreach (var item in response.Items)
            ValidateMachineOverheadStatus(item.ReconciliationStatus, item.UnavailableReason);

        return response;
    }

    public Task<BusinessConsoleErpJournalVoucherListResponse> ListJournalVouchersAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpJournalVoucherListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/vouchers?" + ErpListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpTrialBalanceResponse> GetTrialBalanceAsync(
        string internalBearerToken,
        BusinessConsoleErpPeriodRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpTrialBalanceResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/trial-balance?" + PeriodQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpMonthEndChecklistResponse> GetMonthEndChecklistAsync(
        string internalBearerToken,
        BusinessConsoleErpPeriodRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpMonthEndChecklistResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/month-end-checklist?" + PeriodQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleOpenErpOpportunityResponse> OpenOpportunityAsync(
        string internalBearerToken,
        BusinessConsoleOpenErpOpportunityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleOpenErpOpportunityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/opportunities",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpQuotationResponse> CreateQuotationAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpQuotationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpQuotationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/quotations",
            request,
            cancellationToken);

    public Task<string> ApproveQuotationAsync(
        string internalBearerToken,
        BusinessConsoleApproveErpQuotationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/erp/quotations/{Uri.EscapeDataString(request.QuotationNo)}/approve",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpSalesOrderResponse> CreateSalesOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpSalesOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpSalesOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/sales-orders",
            request,
            cancellationToken);

    public Task<string> ReleaseSalesOrderCreditHoldAsync(
        string internalBearerToken,
        BusinessConsoleReleaseErpSalesOrderCreditHoldRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/erp/sales-orders/{Uri.EscapeDataString(request.SalesOrderNo)}/release-credit-hold",
            request,
            cancellationToken);

    public Task<BusinessConsoleReleaseErpDeliveryOrderResponse> ReleaseDeliveryOrderAsync(
        string internalBearerToken,
        BusinessConsoleReleaseErpDeliveryOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReleaseErpDeliveryOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/delivery-orders",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpAccountPayableResponse> CreateAccountPayableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountPayableRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpAccountPayableResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/payables",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpAccountReceivableResponse> CreateAccountReceivableAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpAccountReceivableRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpAccountReceivableResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/receivables",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateErpCostCandidateResponse> CreateCostCandidateAsync(
        string internalBearerToken,
        BusinessConsoleCreateErpCostCandidateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateErpCostCandidateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/cost-candidates",
            request,
            cancellationToken);

    public Task<BusinessConsolePostErpJournalVoucherResponse> PostJournalVoucherAsync(
        string internalBearerToken,
        BusinessConsolePostErpJournalVoucherRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsolePostErpJournalVoucherResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/vouchers",
            request,
            cancellationToken);

    public Task<string> ApprovePaymentExecutionAsync(
        string internalBearerToken,
        BusinessConsoleApproveErpPaymentExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/payment-executions",
            request,
            cancellationToken);

    public Task<string> ExecutePaymentExecutionAsync(
        string internalBearerToken,
        BusinessConsoleExecuteErpPaymentExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/erp/finance/payment-executions/{Uri.EscapeDataString(request.PaymentExecutionNo)}/execute",
            request,
            cancellationToken);

    public Task<string> RegisterCashReceiptAsync(
        string internalBearerToken,
        BusinessConsoleRegisterErpCashReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/cash-receipts",
            request,
            cancellationToken);

    public Task<string> MatchCashReceiptAsync(
        string internalBearerToken,
        BusinessConsoleMatchErpCashReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/erp/finance/cash-receipts/{Uri.EscapeDataString(request.CashReceiptNo)}/match",
            request,
            cancellationToken);

    public Task<BusinessConsoleOpenErpAccountingPeriodResponse> OpenAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleOpenErpAccountingPeriodRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleOpenErpAccountingPeriodResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/accounting-periods",
            request,
            cancellationToken);

    public Task<string> CloseAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleCloseErpAccountingPeriodRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/accounting-periods/close",
            request,
            cancellationToken);

    public Task<string> ReopenAccountingPeriodAsync(
        string internalBearerToken,
        BusinessConsoleReopenErpAccountingPeriodRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/accounting-periods/reopen",
            request,
            cancellationToken);

    public Task<BusinessConsoleErpFinanceSummaryResponse> GetFinanceSummaryAsync(
        string internalBearerToken,
        BusinessConsoleErpContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpFinanceSummaryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/summary?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpPayableSourceDocumentResponse> GetPayableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpPayableSourceDocumentResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/payables/by-source?" + SourceDocumentQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpReceivableSourceDocumentResponse> GetReceivableBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpReceivableSourceDocumentResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/receivables/by-source?" + SourceDocumentQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpCostCandidateSourceDocumentResponse> GetCostCandidateBySourceDocumentAsync(
        string internalBearerToken,
        BusinessConsoleErpSourceDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpCostCandidateSourceDocumentResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/cost-candidates/by-source?" + SourceDocumentQuery(request),
            null,
            cancellationToken);

    private static string PeriodQuery(BusinessConsoleErpPeriodRequest request)
    {
        return Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("periodStartDate", request.PeriodStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("periodEndDate", request.PeriodEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    private async Task<BusinessConsoleErpPurchaseOrderListResponse> ListPurchaseOrdersCoreAsync(
        string internalBearerToken,
        BusinessConsoleErpListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamPurchaseOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/purchase-orders?" + ErpListQuery(request),
            null,
            cancellationToken);

        return new BusinessConsoleErpPurchaseOrderListResponse(response.Items.Select(x =>
            new BusinessConsoleErpPurchaseOrderItem(
                x.PurchaseOrderNo,
                x.SupplierCode,
                x.SiteCode,
                x.Status,
                ReceiptReadiness(x),
                x.TotalAmount,
                x.Lines.Select(line => new BusinessConsoleErpPurchaseOrderLineItem(
                    line.LineNo,
                    line.SkuCode,
                    line.UomCode,
                    line.OrderedQuantity,
                    line.ReceivedQuantity,
                    line.OpenQuantity,
                    line.FinalDelivery,
                    line.UnitPrice,
                    line.PromisedDate)).ToArray())).ToArray(),
            response.Total);
    }

    private static string ReceiptReadiness(DownstreamPurchaseOrderItem order)
    {
        if (order.Lines.Count == 0)
        {
            return "no-lines";
        }

        if (order.Lines.All(line => line.OpenQuantity == 0m))
        {
            return "received";
        }

        if (order.Lines.Any(line => line.ReceivedQuantity > 0))
        {
            return "partially-received";
        }

        return "awaiting-arrival";
    }

    private static string SourceDocumentQuery(BusinessConsoleErpSourceDocumentRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("sourceDocumentNo", request.SourceDocumentNo),
            ("sourceType", request.SourceType));

    private static void AddMachineOverheadScopeHeaders(
        HttpRequestMessage request,
        string organizationId,
        string environmentId)
    {
        request.Headers.TryAddWithoutValidation("X-Organization-Id", organizationId);
        request.Headers.TryAddWithoutValidation("X-Environment-Id", environmentId);
    }

    private static void ValidateMachineOverheadState(
        BusinessConsoleMachineOverheadReadStatus status,
        string? unavailableReason,
        params decimal?[] amounts)
    {
        ValidateMachineOverheadStatus(status, unavailableReason);
        var valid = status switch
        {
            BusinessConsoleMachineOverheadReadStatus.Available =>
                amounts.All(amount => amount.HasValue),
            BusinessConsoleMachineOverheadReadStatus.NotApplicable or
                BusinessConsoleMachineOverheadReadStatus.Unavailable =>
                amounts.All(amount => !amount.HasValue),
            _ => false,
        };

        if (!valid)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static void ValidateMachineOverheadStatus(
        BusinessConsoleMachineOverheadReadStatus status,
        string? unavailableReason)
    {
        var valid = status switch
        {
            BusinessConsoleMachineOverheadReadStatus.Available => unavailableReason is null,
            BusinessConsoleMachineOverheadReadStatus.NotApplicable or
                BusinessConsoleMachineOverheadReadStatus.Unavailable => !string.IsNullOrWhiteSpace(unavailableReason),
            _ => false,
        };

        if (!valid)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static string ErpListQuery(BusinessConsoleErpListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("skip", request.Skip),
            ("take", request.Take));

    private sealed record DownstreamPurchaseOrderListResponse(IReadOnlyCollection<DownstreamPurchaseOrderItem> Items, int Total);

    private sealed record DownstreamConfigureWorkCenterCostRateResponse(
        string? WorkCenterCostRateId);

    private sealed record DownstreamPurchaseOrderItem(
        string PurchaseOrderNo,
        string SupplierCode,
        string SiteCode,
        string Status,
        decimal TotalAmount,
        IReadOnlyCollection<DownstreamPurchaseOrderLineItem> Lines);

    private sealed record DownstreamPurchaseOrderLineItem(
        string LineNo,
        string SkuCode,
        string UomCode,
        decimal OrderedQuantity,
        decimal ReceivedQuantity,
        decimal OpenQuantity,
        bool FinalDelivery,
        decimal UnitPrice,
        DateOnly PromisedDate);
}
