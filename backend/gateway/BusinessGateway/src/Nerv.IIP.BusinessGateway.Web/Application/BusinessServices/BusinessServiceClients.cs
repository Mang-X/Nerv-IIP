using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;



public interface IBusinessPlanningClient
{
    Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> CreateMpsBucketAsync(
        string internalBearerToken,
        BusinessConsoleCreateMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> UpdateMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleUpdateMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> ReviewMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReviewMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMpsBucketItem> ReleaseMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReleaseMpsBucketRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CancelDemandSourceAsync(
        string internalBearerToken,
        string demandSourceId,
        BusinessConsolePlanningDemandCancelRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleForecastInputListResponse> ListForecastInputsAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleForecastInputItem> CreateOrUpdateForecastInputAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateForecastInputRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRunMrpResponse> RunMrpAsync(
        string internalBearerToken,
        BusinessConsoleRunMrpRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMrpRunListResponse> ListMrpRunsAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken);

    Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AcceptSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePlanningSuggestionRejectedResponse> RejectSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        string rejectedBy,
        BusinessConsoleRejectPlanningSuggestionRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessSchedulingClient
{
    Task<SchedulePlanContract> CreateWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SchedulePlanContract>(new NotSupportedException());

    Task<SchedulePlanRevisionContract> CreatePlanRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulePlanRevisionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SchedulePlanRevisionContract>(new NotSupportedException());

    Task<SchedulePlanContract> PreviewPlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> CreatePlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingContextRequest request,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<GanttScheduleItemContract>> GetPlanGanttAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReleaseSchedulePlanResponse> ReleasePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRevokeSchedulePlanResponse> RevokePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleScheduleOperationOverrideResponse> UpsertOperationOverrideAsync(
        string internalBearerToken,
        BusinessConsoleScheduleOperationOverrideRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<OrderUrgencyContract>> ListOrderUrgenciesAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyListRequest request,
        CancellationToken cancellationToken);

    Task<OrderUrgencyDetailContract> GetOrderUrgencyAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyRequest request,
        CancellationToken cancellationToken);

    Task<OrderUrgencyDetailContract> SetOrderUrgencyBusinessPriorityAsync(
        string internalBearerToken,
        BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request,
        string actor,
        CancellationToken cancellationToken);
}

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

public interface IBusinessIndustrialTelemetryClient
{
    Task<BusinessConsoleConnectorTagCoverageResponse> GetConnectorTagCoverageAsync(
        string internalBearerToken,
        BusinessConsoleConnectorTagCoverageRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryTagCurrentValueResponse> GetTagCurrentValueAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagCurrentValueRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordTelemetrySampleResponse> RecordSampleAsync(
        string internalBearerToken,
        BusinessConsoleRecordTelemetrySampleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostTelemetryAlarmResponse> PostAlarmAsync(
        string internalBearerToken,
        BusinessConsolePostTelemetryAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryRuntimeHoursResponse> QueryRuntimeHoursAsync(string internalBearerToken, BusinessConsoleTelemetryRuntimeHoursRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEquipmentHealthResponse> GetEquipmentHealthAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> AcknowledgeAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleAcknowledgeAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> ShelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleShelveAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> UnshelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleUnshelveAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandResponse> CreateDeviceControlCommandAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string requestedBy,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandDetail> GetDeviceControlCommandAsync(
        string internalBearerToken,
        string commandId,
        BusinessConsoleTelemetryDeviceControlCommandContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandListResponse> ListDeviceControlCommandsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlBindingListResponse> ListDeviceControlBindingsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlBindingListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse> CreateOrUpdateDeviceControlBindingAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDisableTelemetryDeviceControlBindingResponse> DisableDeviceControlBindingAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleDisableTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessMaintenanceClient
{
    Task<BusinessConsoleMaintenanceReasonDirectoryResponse> ListDowntimeReasonsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceReasonDirectoryRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Maintenance reason directory client is not configured.");

    Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse> AssignWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse?> ProbeAssignmentReplayAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse> TransitionWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleTransitionMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenancePlanListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleUpdateMaintenancePlanResponse> UpdatePlanAsync(
        string internalBearerToken,
        string planId,
        BusinessConsoleUpdateMaintenancePlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse> GenerateDueWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse> QueryInspectionMeasurementTrendAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAssetReliabilityResponse> QueryAssetReliabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleQueryMaintenanceAssetReliabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceReliabilitySummaryResponse> QueryReliabilitySummaryAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessMesClient
{
    Task<BusinessConsoleMesReadinessArea> GetFoundationReadinessAreaAsync(
        string internalBearerToken,
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOverviewResponse> GetOverviewAsync(
        string internalBearerToken,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionPlanListResponse> ListProductionPlansAsync(
        string internalBearerToken,
        BusinessConsoleMesProductionPlanListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesFoundationReadinessResponse> GetProductionPlanReadinessAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConvertPlanToWorkOrderAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessMesWorkOrderListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWorkOrderDetailResponse> GetWorkOrderDetailAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ReleaseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesReleaseWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> HoldWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CancelWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCloseWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordEngineeringChangeDecisionAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesEngineeringChangeDecisionRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        string correlationId,
        CancellationToken cancellationToken) =>
        ForceReleaseQualityHoldAsync(internalBearerToken, sourceDocumentId, request, actor, cancellationToken);

    Task<BusinessConsoleMesQualityHoldTimelineResponse> GetQualityHoldTimelineAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesQualityHoldTimelineRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<BusinessConsoleMesReverseProductionReportResponse> ReverseProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesReverseProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesCreateReceiptResponse> RetryFinishedGoodsReceiptInventoryPostingAsync(
        string internalBearerToken,
        string requestNo,
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceivableProducedLotListResponse> ListReceivableProducedLotsAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ReturnLineSideMaterialAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesDispatchTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskForwardRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListReportableOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> StartOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> PauseOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> ResumeOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskActionResponse> CompleteOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesWipSummaryResponse> GetWipSummaryAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListWithoutStatusRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesProductionReportDetailResponse> GetProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTelemetryCandidateListResponse> ListTelemetryCandidatesAsync(string internalBearerToken, BusinessConsoleMesTelemetryCandidateListRequest request, CancellationToken cancellationToken);
    Task<BusinessConsoleMesTelemetryCandidateRow> GetTelemetryCandidateAsync(string internalBearerToken, string candidateId, string organizationId, string environmentId, CancellationToken cancellationToken);
    Task<BusinessConsoleRecordProductionReportResponse> PromoteTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidatePromoteRequest request, string actor, CancellationToken cancellationToken);
    Task<BusinessConsoleAcceptedResponse> DismissTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidateDismissRequest request, string actor, CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessMesRecordDefectRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken,
        string? exactRequestNo = null);

    Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesScheduleResultListResponse> ListScheduleResultsAsync(
        string internalBearerToken,
        BusinessConsoleMesScheduleResultListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesShiftHandoverListResponse> ListShiftHandoversAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CreateShiftHandoverAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateShiftHandoverRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetBatchTraceabilityAsync(
        string internalBearerToken,
        string batchOrSerial,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesTraceabilityResponse> GetMaterialLotTraceabilityAsync(
        string internalBearerToken,
        string materialLotId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);
}


public sealed class HttpBusinessPlanningClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessPlanningClient
{
    public Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken) =>
        ListMpsBucketsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleMpsBucketListResponse> ListMpsBucketsCoreAsync(
        string internalBearerToken,
        BusinessConsoleMpsListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamMpsBucketItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/mps?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("fromDate", request.FromDate),
                ("toDate", request.ToDate),
                ("status", request.Status)),
            null,
            cancellationToken);
        return new BusinessConsoleMpsBucketListResponse(items.Select(ToBusinessConsoleMpsBucket).ToArray());
    }

    public async Task<BusinessConsoleMpsBucketItem> CreateMpsBucketAsync(
        string internalBearerToken,
        BusinessConsoleCreateMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/mps",
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> UpdateMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleUpdateMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}",
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> ReviewMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReviewMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}/review?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public async Task<BusinessConsoleMpsBucketItem> ReleaseMpsBucketAsync(
        string internalBearerToken,
        string mpsId,
        BusinessConsoleReleaseMpsBucketRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMpsBucketItem>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/mps/{Uri.EscapeDataString(mpsId)}/release?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            request,
            cancellationToken);
        return ToBusinessConsoleMpsBucket(response);
    }

    public Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken) =>
        ListDemandSourcesCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleDemandSourceListResponse> ListDemandSourcesCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleDemandSourceResponse>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/demands?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleDemandSourceListResponse(items);
    }

    public async Task<BusinessConsoleDemandSourceResponse> CreateOrUpdateDemandSourceAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateDemandSourceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDemandSourceResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/demands",
            request,
            cancellationToken);
        return new BusinessConsoleDemandSourceResponse(
            response.DemandSourceId,
            request.SourceReference ?? response.DemandSourceId,
            request.DemandType,
            string.Empty,
            string.Empty,
            0,
            "active",
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.DueDate);
    }

    public async Task<BusinessConsoleAcceptedResponse> CancelDemandSourceAsync(
        string internalBearerToken,
        string demandSourceId,
        BusinessConsolePlanningDemandCancelRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<string>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/demands/{Uri.EscapeDataString(demandSourceId)}/cancel?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    public Task<BusinessConsoleForecastInputListResponse> ListForecastInputsAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken) =>
        ListForecastInputsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleForecastInputListResponse> ListForecastInputsCoreAsync(
        string internalBearerToken,
        BusinessConsoleForecastInputListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleForecastInputItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/forecasts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("siteCode", request.SiteCode),
                ("fromDate", request.FromDate),
                ("toDate", request.ToDate)),
            null,
            cancellationToken);
        return new BusinessConsoleForecastInputListResponse(items);
    }

    public async Task<BusinessConsoleForecastInputItem> CreateOrUpdateForecastInputAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateForecastInputRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateForecastInputResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/forecasts",
            request,
            cancellationToken);
        return new BusinessConsoleForecastInputItem(
            response.ForecastInputId,
            response.ForecastReference,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.PeriodStartDate,
            request.PeriodEndDate,
            request.Quantity,
            request.BackwardConsumptionDays,
            request.ForwardConsumptionDays);
    }

    public async Task<BusinessConsoleRunMrpResponse> RunMrpAsync(
        string internalBearerToken,
        BusinessConsoleRunMrpRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRunMrpResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/planning/mrp-runs",
            request,
            cancellationToken);
        return new BusinessConsoleRunMrpResponse(
            response.RunId,
            MrpRunStatusName(response.Status));
    }

    public Task<BusinessConsoleMrpRunListResponse> ListMrpRunsAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken) =>
        ListMrpRunsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsoleMrpRunListResponse> ListMrpRunsCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningContextRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamMrpRunItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/mrp-runs?" + PlanningContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);
        return new BusinessConsoleMrpRunListResponse(items.Select(x => new BusinessConsoleMrpRunItem(
            x.RunId,
            x.HorizonStart,
            x.HorizonEnd,
            MrpRunStatusName(x.Status),
            x.DemandCount,
            x.AvailabilityCount,
            x.SuggestionCount,
            x.ProductionEngineeringSnapshotSource,
            x.InventorySnapshotSource,
            x.HasInputDegradation,
            x.InputDegradationSources ?? [],
            x.InputSources ?? [],
            x.InputCoverageStart,
            x.InputCoverageEnd,
            x.FailureReason)).ToArray());
    }

    public Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken) =>
        ListMrpPeggingCoreAsync(internalBearerToken, runId, cancellationToken);

    private async Task<BusinessConsoleMrpPeggingListResponse> ListMrpPeggingCoreAsync(
        string internalBearerToken,
        string runId,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleMrpPeggingItem>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/planning/mrp-runs/{Uri.EscapeDataString(runId)}/pegging",
            null,
            cancellationToken);
        return new BusinessConsoleMrpPeggingListResponse(items);
    }

    public Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken) =>
        ListSuggestionsCoreAsync(internalBearerToken, request, cancellationToken);

    private async Task<BusinessConsolePlanningSuggestionListResponse> ListSuggestionsCoreAsync(
        string internalBearerToken,
        BusinessConsolePlanningSuggestionListRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<DownstreamPlanningSuggestionItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/planning/suggestions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status)),
            null,
            cancellationToken);
        return new BusinessConsolePlanningSuggestionListResponse(items.Select(x => new BusinessConsolePlanningSuggestionItem(
            x.SuggestionId,
            x.MrpRunId,
            x.SuggestionType,
            x.SkuCode,
            x.UomCode,
            x.SiteCode,
            x.Quantity,
            x.RequiredDate,
            PlanningSuggestionStatusName(x.Status),
            x.ReasonCode,
            x.NetRequirementExplanation is null
                ? null
                : new BusinessConsoleNetRequirementExplanation(
                    x.NetRequirementExplanation.GrossDemandQuantity,
                    x.NetRequirementExplanation.OnHandQuantity,
                    x.NetRequirementExplanation.ReservedQuantity,
                    x.NetRequirementExplanation.AvailableToNetQuantity,
                    x.NetRequirementExplanation.ScheduledReceiptQuantity,
                    x.NetRequirementExplanation.SafetyStockQuantity,
                    x.NetRequirementExplanation.NetRequirementQuantity,
                    x.NetRequirementExplanation.PlannedQuantity,
                    x.NetRequirementExplanation.ScrapRate,
                    x.NetRequirementExplanation.YieldRate,
                    x.NetRequirementExplanation.PrimarySourceType,
                    x.NetRequirementExplanation.Formula,
                    x.NetRequirementExplanation.UomConversions ?? [],
                    x.NetRequirementExplanation.DegradationSources ?? []),
            x.AcceptedDownstreamService,
            x.AcceptedDownstreamDocumentType,
            x.AcceptedDownstreamDocumentId)).ToArray());
    }

    public Task<BusinessConsoleAcceptedResponse> AcceptSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken) =>
        AcceptSuggestionCoreAsync(internalBearerToken, suggestionId, request, cancellationToken);

    private async Task<BusinessConsoleAcceptedResponse> AcceptSuggestionCoreAsync(
        string internalBearerToken,
        string suggestionId,
        BusinessConsoleAcceptPlanningSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        return await SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/suggestions/{Uri.EscapeDataString(suggestionId)}/accept",
            request,
            cancellationToken);
    }

    public Task<BusinessConsolePlanningSuggestionRejectedResponse> RejectSuggestionAsync(
        string internalBearerToken,
        string suggestionId,
        string rejectedBy,
        BusinessConsoleRejectPlanningSuggestionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsolePlanningSuggestionRejectedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/planning/suggestions/{Uri.EscapeDataString(suggestionId)}/reject",
            new DownstreamRejectPlanningSuggestionRequest(suggestionId, rejectedBy, request.Reason),
            cancellationToken);

    private sealed record DownstreamRejectPlanningSuggestionRequest(
        string SuggestionId,
        string RejectedBy,
        string Reason);

    private static string PlanningContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static BusinessConsoleMpsBucketItem ToBusinessConsoleMpsBucket(DownstreamMpsBucketItem item) =>
        new(
            item.MpsId,
            item.SkuCode,
            item.UomCode,
            item.SiteCode,
            item.BucketDate,
            item.Quantity,
            MpsStatusName(item.Status),
            item.ReviewedBy,
            item.ReviewedAtUtc,
            item.ReleasedBy,
            item.ReleasedAtUtc);

    private static string MpsStatusName(JsonElement status) => status.ValueKind switch
    {
        JsonValueKind.Number => status.GetInt32() switch
        {
            0 => "Draft",
            1 => "Reviewed",
            2 => "Released",
            var value => value.ToString(CultureInfo.InvariantCulture),
        },
        JsonValueKind.String => status.GetString() ?? string.Empty,
        _ => status.ToString(),
    };

    private static string MrpRunStatusName(int status) =>
        status switch
        {
            0 => "Created",
            1 => "Running",
            2 => "Completed",
            3 => "Failed",
            _ => status.ToString(CultureInfo.InvariantCulture),
        };

    private static string PlanningSuggestionStatusName(int status) =>
        status switch
        {
            0 => "Open",
            1 => "Accepted",
            2 => "Rejected",
            3 => "Closed",
            _ => status.ToString(CultureInfo.InvariantCulture),
        };

    private sealed record DownstreamCreateOrUpdateDemandSourceResponse(string DemandSourceId);

    private sealed record DownstreamCreateOrUpdateForecastInputResponse(
        string ForecastInputId,
        string ForecastReference);

    private sealed record DownstreamMpsBucketItem(
        string MpsId,
        string SkuCode,
        string UomCode,
        string SiteCode,
        DateOnly BucketDate,
        decimal Quantity,
        JsonElement Status,
        string? ReviewedBy,
        DateTimeOffset? ReviewedAtUtc,
        string? ReleasedBy,
        DateTimeOffset? ReleasedAtUtc);

    private sealed record DownstreamRunMrpResponse(
        string RunId,
        int Status);

    private sealed record DownstreamMrpRunItem(
        string RunId,
        DateOnly HorizonStart,
        DateOnly HorizonEnd,
        int Status,
        int DemandCount,
        int AvailabilityCount,
        int SuggestionCount,
        string ProductionEngineeringSnapshotSource,
        string InventorySnapshotSource,
        bool HasInputDegradation,
        IReadOnlyCollection<string>? InputDegradationSources,
        IReadOnlyCollection<string>? InputSources,
        DateOnly? InputCoverageStart,
        DateOnly? InputCoverageEnd,
        string? FailureReason);

    private sealed record DownstreamPlanningSuggestionItem(
        string SuggestionId,
        string MrpRunId,
        string SuggestionType,
        string SkuCode,
        string UomCode,
        string SiteCode,
        decimal Quantity,
        DateOnly RequiredDate,
        int Status,
        string ReasonCode,
        string? AcceptedDownstreamService,
        string? AcceptedDownstreamDocumentType,
        string? AcceptedDownstreamDocumentId,
        DownstreamNetRequirementExplanation? NetRequirementExplanation);

    private sealed record DownstreamNetRequirementExplanation(
        decimal GrossDemandQuantity,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal AvailableToNetQuantity,
        decimal ScheduledReceiptQuantity,
        decimal SafetyStockQuantity,
        decimal NetRequirementQuantity,
        decimal PlannedQuantity,
        decimal ScrapRate,
        decimal YieldRate,
        string PrimarySourceType,
        string Formula,
        IReadOnlyCollection<string>? UomConversions,
        IReadOnlyCollection<string>? DegradationSources);
}

public sealed class HttpBusinessSchedulingClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessSchedulingClient
{
    public Task<SchedulePlanContract> CreateWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/workbench/plans",
            request,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanRevisionContract> CreatePlanRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulePlanRevisionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanRevisionContract>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/revisions",
            request,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> PreviewPlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/plans/preview",
            new SchedulingProblemRequest(problem),
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> CreatePlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/plans",
            new SchedulingProblemRequest(problem),
            cancellationToken,
            SchedulingJson.Options);

    public Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/scheduling/plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("pageIndex", request.PageIndex?.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", request.PageSize?.ToString(CultureInfo.InvariantCulture))),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<IReadOnlyCollection<GanttScheduleItemContract>> GetPlanGanttAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<GanttScheduleItemContract>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/gantt?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleReleaseSchedulePlanResponse> ReleasePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReleaseSchedulePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/release?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleRevokeSchedulePlanResponse> RevokePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRevokeSchedulePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/revoke?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleScheduleOperationOverrideResponse> UpsertOperationOverrideAsync(
        string internalBearerToken,
        BusinessConsoleScheduleOperationOverrideRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleScheduleOperationOverrideResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/operations/{Uri.EscapeDataString(request.OperationId)}/override",
            request,
            cancellationToken,
            SchedulingJson.Options,
            message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    public Task<IReadOnlyCollection<OrderUrgencyContract>> ListOrderUrgenciesAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<OrderUrgencyContract>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/scheduling/order-urgencies?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("orderReferences", request.OrderReferences)),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<OrderUrgencyDetailContract> GetOrderUrgencyAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<OrderUrgencyDetailContract>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/order-urgencies/{Uri.EscapeDataString(request.OrderReference)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<OrderUrgencyDetailContract> SetOrderUrgencyBusinessPriorityAsync(
        string internalBearerToken,
        BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<OrderUrgencyDetailContract>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/scheduling/order-urgencies/{Uri.EscapeDataString(request.OrderReference)}/business-priority",
            new SetOrderUrgencyBusinessPriorityForwardRequest(
                request.OrderReference, request.OrganizationId, request.EnvironmentId,
                request.Level, request.Reason, request.ExpiresAtUtc),
            cancellationToken,
            SchedulingJson.Options,
            message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    private sealed record SchedulingProblemRequest(SchedulingProblemContract Problem);
    private sealed record SetOrderUrgencyBusinessPriorityForwardRequest(
        string OrderReference,
        string OrganizationId,
        string EnvironmentId,
        string Level,
        string Reason,
        DateTimeOffset? ExpiresAtUtc);

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));
}

public sealed class HttpBusinessIndustrialTelemetryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessIndustrialTelemetryClient
{
    private static readonly HashSet<string> EquipmentHealthRuleCodes = new(StringComparer.Ordinal)
    {
        "threshold-proximity",
        "runtime-hours-24h",
        "alarm-frequency-24h",
        "sustained-exceedance",
        "trend-growth",
    };
    private static readonly JsonSerializerOptions EquipmentHealthJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    public Task<BusinessConsoleConnectorTagCoverageResponse> GetConnectorTagCoverageAsync(
        string internalBearerToken,
        BusinessConsoleConnectorTagCoverageRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConnectorTagCoverageResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/connectors/{Uri.EscapeDataString(request.ConnectorId)}/tag-coverage?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamTelemetryTagListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/tags?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryTagListResponse(page.Items.Select(tag =>
            new BusinessConsoleTelemetryTagItem(
                FormatJsonScalar(tag.TelemetryTagId),
                tag.OrganizationId,
                tag.EnvironmentId,
                tag.DeviceAssetId,
                tag.TagKey,
                tag.ValueType,
                tag.UnitCode,
                tag.SamplingPolicy,
                tag.IsWritable,
                tag.ControlMinValue,
                tag.ControlMaxValue,
                tag.ControlAllowedValues ?? [])).ToArray(), page.Total);
    }

    public Task<BusinessConsoleTelemetryTagCurrentValueResponse> GetTagCurrentValueAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagCurrentValueRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryTagCurrentValueResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/tags/current-value?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("tagKey", request.TagKey)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamAlarmRuleListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/alarm-rules?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("isEnabled", request.IsEnabled),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryAlarmRuleListResponse(page.Items.Select(rule =>
            new BusinessConsoleTelemetryAlarmRuleItem(
                FormatJsonScalar(rule.AlarmRuleId),
                rule.OrganizationId,
                rule.EnvironmentId,
                rule.DeviceAssetId,
                rule.RuleCode,
                rule.AlarmCode,
                rule.Severity,
                rule.TagKey,
                rule.ComparisonOperator,
                rule.ThresholdValue,
                rule.UnitCode,
                rule.IsEnabled,
                rule.UpdatedAtUtc)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateAlarmRuleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/alarm-rules",
            request,
            cancellationToken);
        return new BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse(FormatJsonScalar(response.AlarmRuleId));
    }

    public async Task<BusinessConsoleTelemetryDeviceControlCommandResponse> CreateDeviceControlCommandAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateDeviceControlCommandResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/device-control-commands",
            new DownstreamCreateDeviceControlCommandRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.CommandType,
                request.TagKey,
                request.Value,
                request.Parameters,
                requestedBy,
                request.Reason,
                request.IdempotencyKey,
                request.CorrelationId),
            cancellationToken);
        return new BusinessConsoleTelemetryDeviceControlCommandResponse(
            response.OperationTaskId,
            response.Status,
            response.Approval is null
                ? null
                : new BusinessConsoleTelemetryOperationApprovalSummary(
                    response.Approval.Status,
                    response.Approval.RequestedBy,
                    response.Approval.RequestedAtUtc,
                    response.Approval.DecidedBy,
                    response.Approval.DecidedAtUtc,
                    response.Approval.DecisionReason));
    }

    public Task<BusinessConsoleTelemetryDeviceControlCommandDetail> GetDeviceControlCommandAsync(
        string internalBearerToken,
        string commandId,
        BusinessConsoleTelemetryDeviceControlCommandContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryDeviceControlCommandDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/device-control-commands/{Uri.EscapeDataString(commandId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleTelemetryDeviceControlCommandListResponse> ListDeviceControlCommandsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryDeviceControlCommandListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/device-control-commands?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("status", request.Status),
                ("fromUtc", request.FromUtc),
                ("toUtc", request.ToUtc),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryDeviceControlBindingListResponse> ListDeviceControlBindingsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlBindingListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamDeviceControlBindingListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/device-control-bindings?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("isActive", request.IsActive),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryDeviceControlBindingListResponse(page.Items.Select(binding =>
            new BusinessConsoleTelemetryDeviceControlBindingItem(
                FormatJsonScalar(binding.DeviceControlChannelBindingId),
                binding.OrganizationId,
                binding.EnvironmentId,
                binding.DeviceAssetId,
                binding.ConnectorHostId,
                binding.InstanceKey,
                binding.IsActive,
                binding.DisabledReason,
                binding.UpdatedAtUtc)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse> CreateOrUpdateDeviceControlBindingAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDeviceControlBindingResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/device-control-bindings",
            request,
            cancellationToken);
        return new BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse(FormatJsonScalar(response.DeviceControlChannelBindingId));
    }

    public async Task<BusinessConsoleDisableTelemetryDeviceControlBindingResponse> DisableDeviceControlBindingAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleDisableTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDeviceControlBindingResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/device-control-bindings/{Uri.EscapeDataString(deviceAssetId)}/disable",
            request,
            cancellationToken);
        return new BusinessConsoleDisableTelemetryDeviceControlBindingResponse(FormatJsonScalar(response.DeviceControlChannelBindingId));
    }

    public async Task<BusinessConsoleRecordTelemetrySampleResponse> RecordSampleAsync(
        string internalBearerToken,
        BusinessConsoleRecordTelemetrySampleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordTelemetrySampleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/samples",
            request,
            cancellationToken);
        return new BusinessConsoleRecordTelemetrySampleResponse(
            FormatOptionalJsonScalar(response.TelemetrySummaryId),
            FormatOptionalJsonScalar(response.DeviceStateSnapshotId));
    }

    public async Task<BusinessConsolePostTelemetryAlarmResponse> PostAlarmAsync(
        string internalBearerToken,
        BusinessConsolePostTelemetryAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamPostTelemetryAlarmResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/alarms",
            request,
            cancellationToken);
        return new BusinessConsolePostTelemetryAlarmResponse(FormatJsonScalar(response.AlarmEventId));
    }

    public async Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamAlarmEventListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/alarms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("deviceAssetIds", request.DeviceAssetIds),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take),
                ("alarmEventId", request.AlarmEventId)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryAlarmEventListResponse(page.Items.Select(alarm =>
            new BusinessConsoleTelemetryAlarmEventItem(
                FormatJsonScalar(alarm.AlarmEventId),
                alarm.OrganizationId,
                alarm.EnvironmentId,
                alarm.DeviceAssetId,
                alarm.AlarmCode,
                alarm.Severity,
                alarm.Status,
                alarm.RaisedAtUtc,
                alarm.ClearedAtUtc,
                alarm.ExternalAlarmId,
                alarm.AcknowledgedAtUtc,
                alarm.AcknowledgedBy,
                alarm.ShelvedAtUtc,
                alarm.ShelvedUntilUtc,
                alarm.ShelvedBy,
                alarm.ShelveReason,
                alarm.EscalatedAtUtc,
                alarm.EscalationReason,
                alarm.EscalationRecipientRefs)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleTelemetryHistoryItem>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/timeline?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("fromUtc", request.FromUtc),
                ("toUtc", request.ToUtc)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryHistoryResponse(items);
    }

    public Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryOeeResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/oee?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("windowStartUtc", request.WindowStartUtc),
                ("windowEndUtc", request.WindowEndUtc)),
            null,
            cancellationToken);

    public Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/runtime-availability?" + AvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<BusinessConsoleTelemetryRuntimeHoursResponse> QueryRuntimeHoursAsync(string internalBearerToken, BusinessConsoleTelemetryRuntimeHoursRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryRuntimeHoursResponse>(internalBearerToken, HttpMethod.Get,
            "/api/business/v1/iiot/runtime-hours?" + Query(("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId), ("deviceAssetId", request.DeviceAssetId), ("windowStartUtc", request.WindowStartUtc), ("windowEndUtc", request.WindowEndUtc)),
            null, cancellationToken);

    public Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/runtime-availability?" + DeviceAvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeCurrentStateResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/current-state?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public async Task<BusinessConsoleEquipmentHealthResponse> GetEquipmentHealthAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleEquipmentHealthResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/health?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            EquipmentHealthJsonOptions);

        ValidateEquipmentHealthResponse(response, deviceAssetId, request);
        return response;
    }

    private static void ValidateEquipmentHealthResponse(
        BusinessConsoleEquipmentHealthResponse response,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request)
    {
        if (!string.Equals(response.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(response.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(response.DeviceAssetId, deviceAssetId, StringComparison.Ordinal)
            || response.HealthScore is < 0 or > 100
            || !Enum.IsDefined(response.Level)
            || response.CalculatedAtUtc == default
            || response.CalculatedAtUtc.Offset != TimeSpan.Zero
            || response.DataFreshness is null
            || response.RiskFactors is null
            || response.RuleEvaluations is null)
        {
            throw InvalidEquipmentHealthResponse();
        }

        if (response.RuleEvaluations.Count != EquipmentHealthRuleCodes.Count
            || response.RuleEvaluations.Any(evaluation => evaluation is null)
            || !EquipmentHealthRuleCodes.SetEquals(response.RuleEvaluations.Select(evaluation => evaluation.RuleCode)))
        {
            throw InvalidEquipmentHealthResponse();
        }

        foreach (var evaluation in response.RuleEvaluations)
        {
            if (!HasRequiredRuleFields(
                    evaluation.RuleCode,
                    evaluation.RuleName,
                    evaluation.CurrentValue,
                    evaluation.Threshold,
                    evaluation.Unit,
                    evaluation.Evidence)
                || !Enum.IsDefined(evaluation.Status)
                || !HasCanonicalPenalty(evaluation.RuleCode, evaluation.Status, evaluation.Penalty)
                || !HasCoherentEvidenceSource(
                    evaluation.SourceFactType,
                    evaluation.SourceFactLabel,
                    evaluation.SourceFactOccurredAtUtc))
            {
                throw InvalidEquipmentHealthResponse();
            }
        }

        if (response.RiskFactors.Any(factor => factor is null))
        {
            throw InvalidEquipmentHealthResponse();
        }

        var riskEvaluations = response.RuleEvaluations
            .Where(evaluation => evaluation.Status == BusinessConsoleEquipmentHealthRuleStatus.Risk)
            .ToDictionary(evaluation => evaluation.RuleCode, StringComparer.Ordinal);
        var riskFactorCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factor in response.RiskFactors)
        {
            if (!HasRequiredRuleFields(
                    factor.RuleCode,
                    factor.RuleName,
                    factor.CurrentValue,
                    factor.Threshold,
                    factor.Unit,
                    factor.Evidence)
                || !Enum.IsDefined(factor.Status)
                || factor.Status != BusinessConsoleEquipmentHealthRuleStatus.Risk
                || factor.Penalty <= 0
                || !HasCoherentEvidenceSource(
                    factor.SourceFactType,
                    factor.SourceFactLabel,
                    factor.SourceFactOccurredAtUtc)
                || !riskFactorCodes.Add(factor.RuleCode)
                || !riskEvaluations.TryGetValue(factor.RuleCode, out var evaluation)
                || !RiskFactorMatchesEvaluation(factor, evaluation))
            {
                throw InvalidEquipmentHealthResponse();
            }
        }

        var expectedScore = Math.Clamp(
            100 - riskEvaluations.Values.Sum(evaluation => evaluation.Penalty),
            0,
            100);
        if (!riskFactorCodes.SetEquals(riskEvaluations.Keys)
            || response.HealthScore != expectedScore
            || response.Level != ClassifyEquipmentHealth(expectedScore)
            || !HasCoherentFreshness(
                response.DataFreshness,
                response.CalculatedAtUtc,
                response.RuleEvaluations))
        {
            throw InvalidEquipmentHealthResponse();
        }
    }

    private static bool HasRequiredRuleFields(
        string ruleCode,
        string ruleName,
        string currentValue,
        string threshold,
        string unit,
        string evidence) =>
        !string.IsNullOrWhiteSpace(ruleCode)
        && !string.IsNullOrWhiteSpace(ruleName)
        && !string.IsNullOrWhiteSpace(currentValue)
        && !string.IsNullOrWhiteSpace(threshold)
        && !string.IsNullOrWhiteSpace(unit)
        && !string.IsNullOrWhiteSpace(evidence);

    private static bool HasCanonicalPenalty(
        string ruleCode,
        BusinessConsoleEquipmentHealthRuleStatus status,
        int penalty)
    {
        if (status != BusinessConsoleEquipmentHealthRuleStatus.Risk)
        {
            return penalty == 0;
        }

        return ruleCode switch
        {
            "threshold-proximity" => penalty == 15,
            "runtime-hours-24h" => penalty == 10,
            "alarm-frequency-24h" => penalty is 20 or 45 or 65,
            "sustained-exceedance" => penalty == 20,
            "trend-growth" => penalty == 15,
            _ => false,
        };
    }

    private static BusinessConsoleEquipmentHealthLevel ClassifyEquipmentHealth(int score) =>
        score switch
        {
            >= 90 => BusinessConsoleEquipmentHealthLevel.Healthy,
            >= 70 => BusinessConsoleEquipmentHealthLevel.Watch,
            >= 40 => BusinessConsoleEquipmentHealthLevel.Warning,
            _ => BusinessConsoleEquipmentHealthLevel.Critical,
        };

    private static bool HasCoherentEvidenceSource(
        string? sourceFactType,
        string? sourceFactLabel,
        DateTimeOffset? sourceFactOccurredAtUtc)
    {
        if (sourceFactOccurredAtUtc is null)
        {
            return sourceFactType is null && sourceFactLabel is null;
        }

        return sourceFactOccurredAtUtc.Value != default
            && sourceFactOccurredAtUtc.Value.Offset == TimeSpan.Zero
            && !string.IsNullOrWhiteSpace(sourceFactType)
            && !string.IsNullOrWhiteSpace(sourceFactLabel);
    }

    private static bool RiskFactorMatchesEvaluation(
        BusinessConsoleEquipmentHealthRiskFactor factor,
        BusinessConsoleEquipmentHealthRuleEvaluation evaluation) =>
        factor.RuleName == evaluation.RuleName
        && factor.Status == evaluation.Status
        && factor.Penalty == evaluation.Penalty
        && factor.CurrentValue == evaluation.CurrentValue
        && factor.Threshold == evaluation.Threshold
        && factor.Unit == evaluation.Unit
        && factor.Evidence == evaluation.Evidence
        && factor.SourceFactType == evaluation.SourceFactType
        && factor.SourceFactLabel == evaluation.SourceFactLabel
        && factor.SourceFactOccurredAtUtc == evaluation.SourceFactOccurredAtUtc;

    private static bool HasCoherentFreshness(
        BusinessConsoleEquipmentHealthDataFreshness freshness,
        DateTimeOffset calculatedAtUtc,
        IReadOnlyCollection<BusinessConsoleEquipmentHealthRuleEvaluation> evaluations)
    {
        if (!Enum.IsDefined(freshness.Status))
        {
            return false;
        }

        if (freshness.Status == BusinessConsoleEquipmentHealthFreshness.Unavailable)
        {
            return freshness.AgeSeconds is null
                && freshness.LatestFactAtUtc is null
                && freshness.SourceFactType is null
                && freshness.SourceFactLabel is null
                && evaluations.All(evaluation => evaluation.SourceFactOccurredAtUtc is null);
        }

        if (freshness.AgeSeconds is null or < 0
            || freshness.LatestFactAtUtc is null
            || freshness.LatestFactAtUtc.Value == default
            || freshness.LatestFactAtUtc.Value.Offset != TimeSpan.Zero
            || freshness.LatestFactAtUtc > calculatedAtUtc
            || string.IsNullOrWhiteSpace(freshness.SourceFactType)
            || string.IsNullOrWhiteSpace(freshness.SourceFactLabel))
        {
            return false;
        }

        var exactAge = calculatedAtUtc - freshness.LatestFactAtUtc.Value;
        var ageSeconds = (long)exactAge.TotalSeconds;
        var expectedStatus = exactAge <= TimeSpan.FromMinutes(2)
            ? BusinessConsoleEquipmentHealthFreshness.Fresh
            : exactAge <= TimeSpan.FromMinutes(10)
                ? BusinessConsoleEquipmentHealthFreshness.Delayed
                : BusinessConsoleEquipmentHealthFreshness.Stale;
        return ageSeconds == freshness.AgeSeconds.Value
            && freshness.Status == expectedStatus;
    }

    private static BusinessServiceProxyException InvalidEquipmentHealthResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadGateway,
            "downstream-invalid-response");

    public async Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        var alarms = await ListAlarmsAsync(
            internalBearerToken,
            new BusinessConsoleTelemetryAlarmListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.Status ?? "active",
                request.Skip,
                request.Take,
                request.DeviceAssetIds,
                request.AlarmEventId),
            cancellationToken);
        return new BusinessConsoleEquipmentAlarmListPageResponse(
            alarms.Items,
            alarms.Total);
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> AcknowledgeAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleAcknowledgeAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/acknowledge",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.acknowledge",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> ShelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleShelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/shelve",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.shelve",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> UnshelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleUnshelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/unshelve",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.unshelve",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    private static string AlarmReadbackPath(string organizationId, string environmentId, string alarmEventId) =>
        $"/api/business-console/v1/equipment/alarms?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&alarmEventId={Uri.EscapeDataString(alarmEventId)}";

    private static void EnsureRouteResource(string downstreamResourceId, string routeResourceId)
    {
        if (string.IsNullOrWhiteSpace(downstreamResourceId)
            || !string.Equals(downstreamResourceId, routeResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static string AvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("workCenterIds", request.WorkCenterIds));

    private static string DeviceAvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };

    private static string? FormatOptionalJsonScalar(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : FormatJsonScalar(value.Value);

    private sealed record DownstreamListResponse<T>(IReadOnlyCollection<T> Items, int Total);

    private sealed record DownstreamAlarmEventListItem(
        JsonElement AlarmEventId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string AlarmCode,
        string Severity,
        string Status,
        DateTimeOffset RaisedAtUtc,
        DateTimeOffset? ClearedAtUtc,
        string ExternalAlarmId,
        DateTimeOffset? AcknowledgedAtUtc = null,
        string? AcknowledgedBy = null,
        DateTimeOffset? ShelvedAtUtc = null,
        DateTimeOffset? ShelvedUntilUtc = null,
        string? ShelvedBy = null,
        string? ShelveReason = null,
        DateTimeOffset? EscalatedAtUtc = null,
        string? EscalationReason = null,
        IReadOnlyCollection<string>? EscalationRecipientRefs = null);

    private sealed record DownstreamTelemetryTagListItem(
        JsonElement TelemetryTagId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string TagKey,
        string ValueType,
        string UnitCode,
        string SamplingPolicy,
        bool IsWritable = false,
        decimal? ControlMinValue = null,
        decimal? ControlMaxValue = null,
        IReadOnlyCollection<string>? ControlAllowedValues = null);

    private sealed record DownstreamAlarmRuleListItem(
        JsonElement AlarmRuleId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string RuleCode,
        string AlarmCode,
        string Severity,
        string TagKey,
        string ComparisonOperator,
        decimal ThresholdValue,
        string UnitCode,
        bool IsEnabled,
        DateTimeOffset UpdatedAtUtc);

    private sealed record DownstreamCreateOrUpdateAlarmRuleResponse(JsonElement AlarmRuleId);

    private sealed record DownstreamRecordTelemetrySampleResponse(
        JsonElement? TelemetrySummaryId,
        JsonElement? DeviceStateSnapshotId);

    private sealed record DownstreamPostTelemetryAlarmResponse(JsonElement AlarmEventId);

    private sealed record DownstreamAlarmLifecycleResponse(JsonElement AlarmEventId);

    private sealed record DownstreamCreateDeviceControlCommandRequest(
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string CommandType,
        string? TagKey,
        string? Value,
        IReadOnlyDictionary<string, string>? Parameters,
        string RequestedBy,
        string Reason,
        string IdempotencyKey,
        string CorrelationId);

    private sealed record DownstreamCreateDeviceControlCommandResponse(
        string OperationTaskId,
        string Status,
        DownstreamOperationApprovalSummary? Approval);

    private sealed record DownstreamOperationApprovalSummary(
        string Status,
        string RequestedBy,
        DateTimeOffset RequestedAtUtc,
        string? DecidedBy,
        DateTimeOffset? DecidedAtUtc,
        string? DecisionReason);

    private sealed record DownstreamDeviceControlBindingListItem(
        JsonElement DeviceControlChannelBindingId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string ConnectorHostId,
        string InstanceKey,
        bool IsActive,
        string? DisabledReason,
        DateTimeOffset UpdatedAtUtc);

    private sealed record DownstreamCreateOrUpdateDeviceControlBindingResponse(JsonElement DeviceControlChannelBindingId);
}

public sealed class HttpBusinessMaintenanceClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMaintenanceClient
{
    private static readonly JsonSerializerOptions LifecycleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public async Task<BusinessConsoleMaintenanceReasonDirectoryResponse> ListDowntimeReasonsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceReasonDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamDowntimeReasonDirectoryItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/downtime-reasons?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);

        if (response.Items is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMaintenanceReasonDirectoryResponse(
            response.Items.Select(item => new BusinessConsoleMaintenanceReasonDirectoryItem(
                item.ReasonCode,
                item.ReasonCode,
                item.Description,
                item.ReasonCategory,
                item.LossCategory)).ToArray(),
            response.Skip,
            response.Take,
            response.Total);
    }

    public async Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenanceWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/work-orders",
            request,
            cancellationToken);
        var resourceId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(resourceId, out var parsedWorkOrderId)
            || parsedWorkOrderId == Guid.Empty
            || !string.Equals(response.Status, "Open", StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleCreateMaintenanceWorkOrderResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    "maintenance.work-order.create",
                    "maintenance",
                    "maintenance-work-order",
                    resourceId,
                    response.ChangedAtUtc,
                    response.Status,
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCompleteMaintenanceWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}/complete",
            new DownstreamCompleteMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                request.Result,
                request.DowntimeReasonCode,
                request.DowntimeMinutes,
                request.SpareParts,
                request.ActualLaborMinutes,
                request.SparePartCostAmount,
                request.ExternalServiceCostAmount,
                request.CostCurrencyCode,
                request.ActualTechnicianUserId,
                request.IdempotencyKey),
            cancellationToken);
        var responseWorkOrderId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(responseWorkOrderId, out var parsedWorkOrderId)
            || parsedWorkOrderId == Guid.Empty
            || !Guid.TryParse(workOrderId, out var requestedWorkOrderId)
            || requestedWorkOrderId != parsedWorkOrderId
            || !string.Equals(response.Status, "Completed", StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleCompleteMaintenanceWorkOrderResponse(
            true,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    "maintenance.work-order.complete",
                    "maintenance",
                    "maintenance-work-order",
                    responseWorkOrderId,
                    response.ChangedAtUtc,
                    response.Status,
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken)
    {
        DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem> workOrders;
        if (request.DeviceAssetReferences is { Length: > 0 })
        {
            workOrders = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem>>(
                internalBearerToken,
                HttpMethod.Post,
                "/api/business/internal/v1/maintenance/work-orders/query",
                new DownstreamListMaintenanceWorkOrdersRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.Skip,
                    request.Take,
                    request.DeviceAssetIds,
                    request.Status,
                    request.DeviceAssetId,
                    request.Keyword,
                    request.AssignedTechnicianUserIds,
                    request.AssignedTeamIds,
                    request.WorkOrderId,
                    request.DeviceAssetReferences),
                cancellationToken);
        }
        else
        {
            var scalarQuery = Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("deviceAssetIds", request.DeviceAssetIds),
                ("status", request.Status),
                ("deviceAssetId", request.DeviceAssetId),
                ("keyword", request.Keyword),
                ("assignedTechnicianUserIds", request.AssignedTechnicianUserIds),
                ("assignedTeamIds", request.AssignedTeamIds),
                ("workOrderId", request.WorkOrderId));
            workOrders = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem>>(
                internalBearerToken,
                HttpMethod.Get,
                "/api/business/v1/maintenance/work-orders?" + scalarQuery,
                null,
                cancellationToken);
        }
        return new BusinessConsoleMaintenanceWorkOrderListResponse(workOrders.Items.Select(workOrder =>
            new BusinessConsoleMaintenanceWorkOrderItem(
                FormatMaintenanceWorkOrderId(workOrder.WorkOrderId),
                workOrder.DeviceAssetId,
                workOrder.Priority,
                workOrder.Status,
                workOrder.SourceAlarmId,
                null,
                workOrder.OpenedAtUtc,
                workOrder.AssignedTechnicianUserId,
                workOrder.EstimatedLaborMinutes,
                workOrder.ActualLaborMinutes,
                workOrder.SparePartCostAmount,
                workOrder.ExternalServiceCostAmount,
                workOrder.CostCurrencyCode,
                ActualTechnicianUserId: workOrder.ActualTechnicianUserId,
                SourceReferenceId: workOrder.SourceReferenceId,
                AssignedTeamId: workOrder.AssignedTeamId,
                Version: workOrder.Version)).ToArray(),
            workOrders.Skip,
            workOrders.Take,
            workOrders.Total);
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        var detail = await SendAsync<DownstreamMaintenanceWorkOrderDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        EnsureMaintenanceWorkOrderDetailMatchesRoute(workOrderId, detail.WorkOrder.WorkOrderId);
        return MapWorkOrder(detail.WorkOrder) with
        {
            AllowedActions = detail.AllowedActions,
            BlockReasons = detail.BlockReasons ?? [],
            Lifecycle = detail.Lifecycle.Select(x => new BusinessConsoleMaintenanceWorkOrderLifecycleEventItem(
                x.Action, x.FromStatus, x.ToStatus, x.ActorPrincipalId, x.TechnicianUserId, x.TeamId,
                x.Reason, x.ResultingVersion, x.OccurredAtUtc)).ToArray(),
        };
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderActionResponse> AssignWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken)
    {
        var authoritativeBefore = await GetWorkOrderAsync(
            internalBearerToken,
            workOrderId,
            new BusinessConsoleMaintenanceContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        EnsureAuthoritativePreRead(workOrderId, authoritativeBefore);
        return await SendLifecycleActionAsync(
            internalBearerToken,
            workOrderId,
            "/assignment",
            new DownstreamAssignMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                actorPrincipalId,
                request.TechnicianUserId,
                request.TeamId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion),
            "maintenance.work-order.assign",
            request.IdempotencyKey,
            "Open",
            request.ExpectedVersion,
            cancellationToken);
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderActionResponse?> ProbeAssignmentReplayAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken)
    {
        var probe = await SendAsync<DownstreamMaintenanceAssignmentReplayProbeResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/internal/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}/assignment-replay-probe",
            new DownstreamProbeMaintenanceWorkOrderAssignmentReplayRequest(
                request.OrganizationId,
                request.EnvironmentId,
                actorPrincipalId,
                request.TechnicianUserId,
                request.TeamId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken,
            LifecycleJsonOptions);
        if (!probe.Found)
        {
            if (probe.Receipt is not null)
            {
                throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                    HttpStatusCode.BadGateway,
                    "downstream-invalid-response");
            }
            return null;
        }
        if (probe.Receipt is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return MapLifecycleActionResponse(
            workOrderId,
            probe.Receipt,
            "maintenance.work-order.assign",
            request.IdempotencyKey,
            "Open",
            request.ExpectedVersion);
    }

    public Task<BusinessConsoleMaintenanceWorkOrderActionResponse> TransitionWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleTransitionMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken) =>
        SendLifecycleActionAsync(
            internalBearerToken,
            workOrderId,
            "/actions",
            new DownstreamTransitionMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                request.Action,
                actorPrincipalId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.Result,
                request.DowntimeReasonCode,
                request.DowntimeMinutes,
                request.SpareParts,
                request.ActualLaborMinutes,
                request.SparePartCostAmount,
                request.ExternalServiceCostAmount,
                request.CostCurrencyCode),
            $"maintenance.work-order.{request.Action.ToString().ToLowerInvariant()}",
            request.IdempotencyKey,
            ExpectedStatus(request.Action),
            request.ExpectedVersion,
            cancellationToken);

    private async Task<BusinessConsoleMaintenanceWorkOrderActionResponse> SendLifecycleActionAsync<TRequest>(
        string internalBearerToken,
        string workOrderId,
        string routeSuffix,
        TRequest request,
        string operationType,
        string idempotencyKey,
        string expectedStatus,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMaintenanceWorkOrderActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}{routeSuffix}",
            request,
            cancellationToken,
            LifecycleJsonOptions);
        return MapLifecycleActionResponse(
            workOrderId,
            response,
            operationType,
            idempotencyKey,
            expectedStatus,
            expectedVersion);
    }

    private static BusinessConsoleMaintenanceWorkOrderActionResponse MapLifecycleActionResponse(
        string workOrderId,
        DownstreamMaintenanceWorkOrderActionResponse response,
        string operationType,
        string idempotencyKey,
        string expectedStatus,
        int expectedVersion)
    {
        var responseId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(responseId, out var parsedResponseId)
            || parsedResponseId == Guid.Empty
            || !Guid.TryParse(workOrderId, out var parsedRequestId)
            || parsedRequestId != parsedResponseId
            || !string.Equals(response.Status, expectedStatus, StringComparison.Ordinal)
            || expectedVersion < 0
            || expectedVersion == int.MaxValue
            || response.Version != expectedVersion + 1
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return new BusinessConsoleMaintenanceWorkOrderActionResponse(
            responseId,
            response.Status,
            response.Version,
            response.ChangedAtUtc,
            BusinessConsoleOperationReceipts.Confirmed(
                operationType,
                "maintenance",
                "maintenance-work-order",
                responseId,
                response.ChangedAtUtc,
                response.Status,
                idempotencyKey));
    }

    private static void EnsureAuthoritativePreRead(
        string workOrderId,
        BusinessConsoleMaintenanceWorkOrderItem authoritativeBefore)
    {
        if (!Guid.TryParse(workOrderId, out var requestedId)
            || !Guid.TryParse(authoritativeBefore.WorkOrderId, out var responseId)
            || requestedId != responseId
            || authoritativeBefore.Version < 0
            || !KnownMaintenanceStatuses.Contains(authoritativeBefore.Status))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static void EnsureMaintenanceWorkOrderDetailMatchesRoute(
        string workOrderId,
        JsonElement downstreamWorkOrderId)
    {
        if (!Guid.TryParse(workOrderId, out var requestedId)
            || requestedId == Guid.Empty
            || downstreamWorkOrderId.ValueKind != JsonValueKind.Object
            || !downstreamWorkOrderId.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || !Guid.TryParse(id.GetString(), out var responseId)
            || responseId == Guid.Empty
            || responseId != requestedId)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static string ExpectedStatus(BusinessConsoleMaintenanceWorkOrderAction action) => action switch
    {
        BusinessConsoleMaintenanceWorkOrderAction.Accept => "Accepted",
        BusinessConsoleMaintenanceWorkOrderAction.Start or BusinessConsoleMaintenanceWorkOrderAction.Resume => "InProgress",
        BusinessConsoleMaintenanceWorkOrderAction.Pause => "Paused",
        BusinessConsoleMaintenanceWorkOrderAction.WaitForParts => "WaitingForParts",
        BusinessConsoleMaintenanceWorkOrderAction.Complete => "Completed",
        BusinessConsoleMaintenanceWorkOrderAction.Verify => "Verified",
        BusinessConsoleMaintenanceWorkOrderAction.Close => "Closed",
        BusinessConsoleMaintenanceWorkOrderAction.Cancel => "Cancelled",
        _ => throw BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadGateway,
            "downstream-invalid-response"),
    };

    private static readonly HashSet<string> KnownMaintenanceStatuses =
    [
        "Open", "Accepted", "InProgress", "Paused", "WaitingForParts", "Completed", "Verified", "Closed", "Cancelled",
    ];

    private static BusinessConsoleMaintenanceWorkOrderItem MapWorkOrder(DownstreamMaintenanceWorkOrderListItem workOrder) =>
        new(
            FormatMaintenanceWorkOrderId(workOrder.WorkOrderId),
            workOrder.DeviceAssetId,
            workOrder.Priority,
            workOrder.Status,
            workOrder.SourceAlarmId,
            null,
            workOrder.OpenedAtUtc,
            workOrder.AssignedTechnicianUserId,
            workOrder.EstimatedLaborMinutes,
            workOrder.ActualLaborMinutes,
            workOrder.SparePartCostAmount,
            workOrder.ExternalServiceCostAmount,
            workOrder.CostCurrencyCode,
            ActualTechnicianUserId: workOrder.ActualTechnicianUserId,
            SourceReferenceId: workOrder.SourceReferenceId,
            AssignedTeamId: workOrder.AssignedTeamId,
            Version: workOrder.Version);

    public async Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenancePlanListRequest request,
        CancellationToken cancellationToken)
    {
        var plans = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenancePlanListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("deviceAssetId", request.DeviceAssetId)),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenancePlanListResponse(plans.Items.Select(plan =>
            new BusinessConsoleMaintenancePlanItem(
                FormatJsonScalar(plan.PlanId),
                plan.DeviceAssetId,
                plan.PlanCode,
                plan.Interval,
                plan.StartsOn,
                plan.NextDueOn,
                plan.RuntimeHourInterval,
                plan.NextDueRuntimeHours,
                plan.LastGeneratedRuntimeHours)).ToArray(),
            plans.Skip,
            plans.Take,
            plans.Total);
    }

    public async Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenancePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/plans",
            request,
            cancellationToken);
        return new BusinessConsoleCreateMaintenancePlanResponse(FormatJsonScalar(response.PlanId));
    }

    public async Task<BusinessConsoleUpdateMaintenancePlanResponse> UpdatePlanAsync(
        string internalBearerToken,
        string planId,
        BusinessConsoleUpdateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamUpdateMaintenancePlanResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/maintenance/plans/{Uri.EscapeDataString(planId)}",
            request,
            cancellationToken);
        return new BusinessConsoleUpdateMaintenancePlanResponse(FormatJsonScalar(response.PlanId));
    }

    public async Task<BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse> GenerateDueWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        // Downstream Maintenance requires an OpenedBy for the work orders it raises; the
        // console exposes a single RequestedBy actor, so forward it as both fields.
        var downstreamRequest = new
        {
            request.OrganizationId,
            request.EnvironmentId,
            request.BusinessDate,
            request.RequestedBy,
            OpenedBy = request.RequestedBy,
        };
        var response = await SendAsync<DownstreamGenerateDueMaintenanceWorkOrdersResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/plans/generate-due",
            downstreamRequest,
            cancellationToken);
        return new BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse(
            response.GeneratedCount,
            response.WorkOrderIds.Select(FormatJsonScalar).ToArray());
    }

    public async Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordMaintenanceInspectionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/inspections",
            request,
            cancellationToken);
        return new BusinessConsoleRecordMaintenanceInspectionResponse(FormatJsonScalar(response.InspectionId));
    }

    public async Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        var inspections = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceInspectionListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/inspections?" + ListQuery(request.OrganizationId, request.EnvironmentId, request.Skip, request.Take),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceInspectionListResponse(inspections.Items.Select(inspection =>
            new BusinessConsoleMaintenanceInspectionItem(
                FormatJsonScalar(inspection.InspectionId),
                FormatOptionalJsonScalar(inspection.PlanId),
                FormatOptionalJsonScalar(inspection.WorkOrderId),
                inspection.Inspector,
                inspection.Result,
                inspection.InspectedAtUtc,
                inspection.Measurements ?? [])).ToArray(),
            inspections.Skip,
            inspections.Take,
            inspections.Total);
    }

    public async Task<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse> QueryInspectionMeasurementTrendAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request,
        CancellationToken cancellationToken)
    {
        var trend = await SendAsync<DownstreamMaintenanceInspectionMeasurementTrendResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/inspection-measurements/trends?" + InspectionMeasurementTrendQuery(request),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceInspectionMeasurementTrendResponse(
            trend.OrganizationId,
            trend.EnvironmentId,
            trend.DeviceAssetId,
            trend.CharacteristicCode,
            trend.WindowStartUtc,
            trend.WindowEndUtc,
            trend.Items.Select(item => new BusinessConsoleMaintenanceInspectionMeasurementTrendItem(
                FormatJsonScalar(item.InspectionId),
                FormatOptionalJsonScalar(item.PlanId),
                FormatOptionalJsonScalar(item.WorkOrderId),
                item.InspectedAtUtc,
                item.MeasuredValue,
                item.UomCode,
                item.LowerSpecLimit,
                item.UpperSpecLimit,
                item.IsWithinSpec)).ToArray());
    }

    public async Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        var spareParts = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceSparePartListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/spare-parts?" + ListQuery(request.OrganizationId, request.EnvironmentId, request.Skip, request.Take),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceSparePartListResponse(spareParts.Items.Select(sparePart =>
            new BusinessConsoleMaintenanceSparePartItem(
                FormatJsonScalar(sparePart.SparePartLineId),
                FormatMaintenanceWorkOrderId(sparePart.WorkOrderId),
                sparePart.DeviceAssetId,
                sparePart.SkuCode,
                sparePart.Quantity,
                sparePart.UomCode)).ToArray(),
            spareParts.Skip,
            spareParts.Take,
            spareParts.Total);
    }

    public async Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenanceSparePartResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/spare-parts",
            request,
            cancellationToken);
        return new BusinessConsoleCreateMaintenanceSparePartResponse(FormatJsonScalar(response.SparePartLineId));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/availability-windows?" + AvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/assets/{Uri.EscapeDataString(deviceAssetId)}/availability-windows?" + DeviceAvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<BusinessConsoleAssetReliabilityResponse> QueryAssetReliabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleQueryMaintenanceAssetReliabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAssetReliabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/assets/{Uri.EscapeDataString(deviceAssetId)}/reliability?" + ReliabilityQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMaintenanceReliabilitySummaryResponse> QueryReliabilitySummaryAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMaintenanceReliabilitySummaryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/reliability/summary?" + ReliabilitySummaryQuery(request),
            null,
            cancellationToken);

    private static string AvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("workCenterIds", request.WorkCenterIds));

    private static string DeviceAvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ReliabilityQuery(BusinessConsoleQueryMaintenanceAssetReliabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ReliabilitySummaryQuery(BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetId", request.DeviceAssetId),
            ("technicianUserId", request.TechnicianUserId));

    private static string InspectionMeasurementTrendQuery(BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("deviceAssetId", request.DeviceAssetId),
            ("characteristicCode", request.CharacteristicCode),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string ListQuery(string organizationId, string environmentId, int skip, int take) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId), ("skip", skip), ("take", take));

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };

    private static string FormatMaintenanceWorkOrderId(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? FormatOptionalJsonScalar(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : FormatJsonScalar(value.Value);

    private sealed record DownstreamMaintenancePagedResponse<T>(IReadOnlyCollection<T> Items, int Skip, int Take, int Total);

    private sealed record DownstreamListMaintenanceWorkOrdersRequest(
        string OrganizationId,
        string EnvironmentId,
        int Skip,
        int Take,
        string? DeviceAssetIds,
        string? Status,
        string? DeviceAssetId,
        string? Keyword,
        string? AssignedTechnicianUserIds,
        string? AssignedTeamIds,
        string? WorkOrderId,
        string[] DeviceAssetReferences);

    private sealed record DownstreamDowntimeReasonDirectoryItem(
        JsonElement DowntimeReasonId,
        string OrganizationId,
        string EnvironmentId,
        string ReasonCode,
        string Description,
        string ReasonCategory,
        string LossCategory);

    private sealed record DownstreamMaintenanceWorkOrderListItem(
        JsonElement WorkOrderId,
        string DeviceAssetId,
        string Priority,
        string Status,
        string? SourceAlarmId,
        DateTimeOffset OpenedAtUtc,
        string? AssignedTechnicianUserId = null,
        int? EstimatedLaborMinutes = null,
        int? ActualLaborMinutes = null,
        decimal? SparePartCostAmount = null,
        decimal? ExternalServiceCostAmount = null,
        string? CostCurrencyCode = null,
        string? ActualTechnicianUserId = null,
        string? SourceReferenceId = null,
        string? AssignedTeamId = null,
        int Version = 0);

    private sealed record DownstreamMaintenanceWorkOrderDetail(
        DownstreamMaintenanceWorkOrderListItem WorkOrder,
        IReadOnlyCollection<DownstreamMaintenanceWorkOrderLifecycleEvent> Lifecycle,
        IReadOnlyCollection<string> AllowedActions,
        IReadOnlyCollection<string>? BlockReasons = null);

    private sealed record DownstreamMaintenanceWorkOrderLifecycleEvent(
        string Action,
        string FromStatus,
        string ToStatus,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        int ResultingVersion,
        DateTimeOffset OccurredAtUtc);

    private sealed record DownstreamAssignMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion);

    private sealed record DownstreamProbeMaintenanceWorkOrderAssignmentReplayRequest(
        string OrganizationId,
        string EnvironmentId,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion);

    private sealed record DownstreamMaintenanceAssignmentReplayProbeResponse(
        bool Found,
        DownstreamMaintenanceWorkOrderActionResponse? Receipt);

    private sealed record DownstreamTransitionMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        BusinessConsoleMaintenanceWorkOrderAction Action,
        string ActorPrincipalId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion,
        string? Result,
        string? DowntimeReasonCode,
        int? DowntimeMinutes,
        IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput>? SpareParts,
        int? ActualLaborMinutes,
        decimal? SparePartCostAmount,
        decimal? ExternalServiceCostAmount,
        string? CostCurrencyCode);

    private sealed record DownstreamMaintenanceWorkOrderActionResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc,
        int Version);

    private sealed record DownstreamMaintenancePlanListItem(
        JsonElement PlanId,
        string DeviceAssetId,
        string PlanCode,
        string? Interval,
        DateOnly StartsOn,
        DateOnly? NextDueOn,
        decimal? RuntimeHourInterval,
        decimal? NextDueRuntimeHours,
        decimal LastGeneratedRuntimeHours);

    private sealed record DownstreamMaintenanceInspectionListItem(
        JsonElement InspectionId,
        JsonElement? PlanId,
        JsonElement? WorkOrderId,
        string Inspector,
        string Result,
        DateTimeOffset InspectedAtUtc,
        IReadOnlyCollection<BusinessConsoleMaintenanceInspectionMeasurementItem>? Measurements = null);

    private sealed record DownstreamMaintenanceSparePartListItem(
        JsonElement SparePartLineId,
        JsonElement WorkOrderId,
        string DeviceAssetId,
        string SkuCode,
        decimal Quantity,
        string? UomCode);

    private sealed record DownstreamCreateMaintenanceWorkOrderResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc);

    private sealed record DownstreamCompleteMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        string Result,
        string DowntimeReasonCode,
        int DowntimeMinutes,
        IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput> SpareParts,
        int? ActualLaborMinutes = null,
        decimal? SparePartCostAmount = null,
        decimal? ExternalServiceCostAmount = null,
        string? CostCurrencyCode = null,
        string? ActualTechnicianUserId = null,
        string? IdempotencyKey = null);

    private sealed record DownstreamMaintenanceWorkOrderId(string Id);

    private sealed record DownstreamCompleteMaintenanceWorkOrderResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc);

    private sealed record DownstreamCreateMaintenancePlanResponse(JsonElement PlanId);

    private sealed record DownstreamUpdateMaintenancePlanResponse(JsonElement PlanId);

    private sealed record DownstreamGenerateDueMaintenanceWorkOrdersResponse(
        int GeneratedCount,
        IReadOnlyCollection<JsonElement> WorkOrderIds);

    private sealed record DownstreamRecordMaintenanceInspectionResponse(JsonElement InspectionId);

    private sealed record DownstreamCreateMaintenanceSparePartResponse(JsonElement SparePartLineId);

    private sealed record DownstreamMaintenanceInspectionMeasurementTrendResponse(
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string CharacteristicCode,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        IReadOnlyCollection<DownstreamMaintenanceInspectionMeasurementTrendItem> Items);

    private sealed record DownstreamMaintenanceInspectionMeasurementTrendItem(
        JsonElement InspectionId,
        JsonElement? PlanId,
        JsonElement? WorkOrderId,
        DateTimeOffset InspectedAtUtc,
        decimal MeasuredValue,
        string UomCode,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit,
        bool IsWithinSpec);
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
}

public sealed class HttpBusinessMesClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesClient
{
    // #1341: downstreamService / downstreamDocumentType 是全仓共用的一张词表（PascalCase），
    // 由 DemandPlanningDownstreamReferences（BusinessErp/PurchaseRequisition/BusinessMes/WorkOrder）
    // 定下口径，网关既有读面（BusinessConsoleMesEndpoints 的 "BusinessMes"/"ProductionPlan"）与前端
    // 精确等值判断（PlanningWorkbench 的 service === 'BusinessMes' && type === 'WorkOrder'）都按它匹配。
    // MES 受理回执没有理由另起一套编码，因此这里沿用同一词表，新单据类型按同样构词法扩展。
    private const string MesDownstreamService = "BusinessMes";
    private const string MesMaterialIssueRequestDocumentType = "MaterialIssueRequest";
    private const string MesWorkOrderDocumentType = "WorkOrder";
    private const string MesQualityHoldDocumentType = "QualityHold";
    private const string MesDispatchTaskDocumentType = "DispatchTask";
    private const string MesTelemetryCandidateDocumentType = "TelemetryProductionReportCandidate";
    private const string MesDefectDocumentType = "Defect";
    private const string MesDowntimeEventDocumentType = "DowntimeEvent";
    private const string MesShiftHandoverDocumentType = "ShiftHandover";

    /// <summary>MES accepted-receipt body as returned by the service endpoints.</summary>
    private sealed record MesServiceAcceptedResponse(string? Status, string? ReferenceId, DateTimeOffset? AcceptedAtUtc);

    private static BusinessConsoleAcceptedResponse ToAcceptedResponse(
        MesServiceAcceptedResponse? accepted,
        string downstreamDocumentType) =>
        new(
            // MES only answers 2xx when it accepted the intent, so a parsed body is the acceptance.
            accepted is not null,
            MesDownstreamService,
            downstreamDocumentType,
            string.IsNullOrWhiteSpace(accepted?.ReferenceId) ? null : accepted.ReferenceId);

    /// <summary>
    /// #1341: MES 写面统一回 <c>{ status, referenceId, acceptedAtUtc }</c>；直接反序列化成控制台契约
    /// 会让 <c>accepted</c> 恒 false 并丢掉下游单号，因此所有受理型写面都必须走显式映射。
    /// </summary>
    private async Task<BusinessConsoleAcceptedResponse> SendAcceptedAsync(
        string internalBearerToken,
        string requestUri,
        object? body,
        string downstreamDocumentType,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var accepted = await SendAsync<MesServiceAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            requestUri,
            body,
            cancellationToken,
            configureRequest: configureRequest);
        return ToAcceptedResponse(accepted, downstreamDocumentType);
    }

    public Task<BusinessConsoleMesReadinessArea> GetFoundationReadinessAreaAsync(
        string internalBearerToken,
        string areaCode,
        BusinessConsoleMesFoundationReadinessRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReadinessArea>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/foundation-readiness/{Uri.EscapeDataString(areaCode)}?" + FoundationQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOverviewResponse> GetOverviewAsync(
        string internalBearerToken,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOverviewResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/overview?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionPlanListResponse> ListProductionPlansAsync(
        string internalBearerToken,
        BusinessConsoleMesProductionPlanListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-plans?" + ProductionPlanListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesFoundationReadinessResponse> GetProductionPlanReadinessAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesFoundationReadinessResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/readiness?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConvertPlanToWorkOrderAsync(
        string internalBearerToken,
        string productionPlanId,
        BusinessConsoleMesConvertPlanToWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/work-orders",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessMesWorkOrderListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWorkOrderListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/work-orders?" + WorkOrderListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderDetailResponse> GetWorkOrderDetailAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWorkOrderDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ReleaseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesReleaseWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/release",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> HoldWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/hold",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CancelWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/cancel",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CloseWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCloseWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/close",
            request,
            MesWorkOrderDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordEngineeringChangeDecisionAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesEngineeringChangeDecisionRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/engineering-change-decisions",
            new MesEngineeringChangeDecisionRequest(
                request.OrganizationId,
                request.EnvironmentId,
                workOrderId,
                request.ChangeNumber,
                request.Decision,
                actor,
                request.Reason),
            MesWorkOrderDocumentType,
            cancellationToken);

    private sealed record MesEngineeringChangeDecisionRequest(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string ChangeNumber,
        string Decision,
        string DecidedBy,
        string Reason);

    public Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        ForceReleaseQualityHoldAsync(internalBearerToken, sourceDocumentId, request, actor, Guid.CreateVersion7().ToString("N"), cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ForceReleaseQualityHoldAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesForceReleaseQualityHoldRequest request,
        string actor,
        string correlationId,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/quality-holds/{Uri.EscapeDataString(sourceDocumentId)}/force-release",
            new DownstreamForceReleaseQualityHoldRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason,
                request.SourceService,
                request.ReleasedAtUtc),
            MesQualityHoldDocumentType,
            cancellationToken,
            configureRequest: message =>
            {
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor);
                message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                message.Headers.TryAddWithoutValidation("X-Idempotency-Key", request.IdempotencyKey);
            });

    public Task<BusinessConsoleMesQualityHoldTimelineResponse> GetQualityHoldTimelineAsync(
        string internalBearerToken,
        string sourceDocumentId,
        BusinessConsoleMesQualityHoldTimelineRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesQualityHoldTimelineResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/quality-holds/{Uri.EscapeDataString(sourceDocumentId)}/timeline?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&sourceService={Uri.EscapeDataString(request.SourceService)}",
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReverseProductionReportResponse> ReverseProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesReverseProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReverseProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/production-reports/{Uri.EscapeDataString(reportNo)}/reverse",
            new DownstreamReverseProductionReportRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason,
                actor,
                request.ReversedAtUtc,
                request.IdempotencyKey),
            cancellationToken);

    public async Task<BusinessConsoleMesCreateReceiptResponse> RetryFinishedGoodsReceiptInventoryPostingAsync(
        string internalBearerToken,
        string requestNo,
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateFinishedGoodsReceiptRequestResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/finished-goods-receipt-requests/{Uri.EscapeDataString(requestNo)}/inventory-posting/retry",
            request,
            cancellationToken);

        if (response.FinishedGoodsReceiptRequestId is null ||
            response.FinishedGoodsReceiptRequestId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.RequestNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMesCreateReceiptResponse(
            response.FinishedGoodsReceiptRequestId.Id.ToString(),
            response.RequestNo);
    }

    public Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        CreateRushWorkOrderCoreAsync(internalBearerToken, request, cancellationToken);

    public Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialReadinessResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-readiness?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReceivableProducedLotListResponse> ListReceivableProducedLotsAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReceivableProducedLotListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/produced-lots?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    // MES answers with { status, referenceId, acceptedAtUtc }; deserializing that straight into the
    // console contract left `accepted` false and dropped the allocated 领料单号. Map it explicitly.
    public Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-issue-requests",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesMaterialIssueRequestListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialIssueRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/material-issue-requests?" + MaterialIssueRequestListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-receipts",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ReturnLineSideMaterialAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesReturnLineSideMaterialRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-returns",
            request,
            MesMaterialIssueRequestDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesDispatchTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDispatchTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/dispatch-tasks?" + DispatchTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskForwardRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/dispatch-tasks/{Uri.EscapeDataString(operationTaskId)}/assign",
            request,
            MesDispatchTaskDocumentType,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

    public Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/operation-tasks?" + OperationTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOperationTaskListResponse> ListReportableOperationTasksAsync(
        string internalBearerToken,
        BusinessMesOperationTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/reportable-operation-tasks?" + OperationTaskListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> StartOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "start", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> PauseOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "pause", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> ResumeOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "resume", request, cancellationToken);

    public Task<BusinessConsoleMesOperationTaskActionResponse> CompleteOperationTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        OperationTaskActionAsync(internalBearerToken, operationTaskId, "complete", request, cancellationToken);

    public Task<BusinessConsoleMesWipSummaryResponse> GetWipSummaryAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesWipSummaryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/wip?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
        string internalBearerToken,
        BusinessConsoleMesListWithoutStatusRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionReportListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/production-reports?" + ListQueryWithoutStatus(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesProductionReportDetailResponse> GetProductionReportAsync(
        string internalBearerToken,
        string reportNo,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesProductionReportDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/production-reports/{Uri.EscapeDataString(reportNo)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTelemetryCandidateListResponse> ListTelemetryCandidatesAsync(string internalBearerToken, BusinessConsoleMesTelemetryCandidateListRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTelemetryCandidateListResponse>(internalBearerToken, HttpMethod.Get,
            "/api/business/v1/mes/telemetry-production-report-candidates?" + Query(
                ("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId), ("status", request.Status),
                ("workCenterId", request.WorkCenterId), ("deviceAssetId", request.DeviceAssetId),
                ("fromUtc", request.FromUtc), ("toUtc", request.ToUtc), ("skip", request.Skip), ("take", request.Take)), null, cancellationToken);

    public Task<BusinessConsoleMesTelemetryCandidateRow> GetTelemetryCandidateAsync(string internalBearerToken, string candidateId, string organizationId, string environmentId, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTelemetryCandidateRow>(internalBearerToken, HttpMethod.Get,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}", null, cancellationToken);

    public Task<BusinessConsoleRecordProductionReportResponse> PromoteTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidatePromoteRequest request, string actor, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordProductionReportResponse>(internalBearerToken, HttpMethod.Post,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}/promote",
            new { request.OrganizationId, request.EnvironmentId, CandidateId = candidateId, request.WorkOrderId, request.OperationTaskId, Actor = actor }, cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> DismissTelemetryCandidateAsync(string internalBearerToken, string candidateId, BusinessConsoleMesTelemetryCandidateDismissRequest request, string actor, CancellationToken cancellationToken) =>
        SendAcceptedAsync(internalBearerToken,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}/dismiss",
            new { request.OrganizationId, request.EnvironmentId, CandidateId = candidateId, request.Reason, Actor = actor },
            MesTelemetryCandidateDocumentType, cancellationToken);

    public async Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<DownstreamMesScheduleResult>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/schedules/run",
            request,
            cancellationToken);
        return result.ToBusinessConsoleResult();
    }

    public async Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/production-reports",
            request,
            cancellationToken);

        if (response.ProductionReportId is null ||
            response.ProductionReportId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.ReportNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleRecordProductionReportResponse(
            response.ProductionReportId.Id.ToString(),
            response.ReportNo,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "mes.production-report.record",
                    "mes",
                    "production-report",
                    response.ProductionReportId.Id.ToString(),
                    $"/api/business-console/v1/mes/production-reports/{Uri.EscapeDataString(response.ReportNo)}?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}",
                    request.IdempotencyKey));
    }

    public Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessMesRecordDefectRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/defects",
            new
            {
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.DefectCode,
                request.Quantity,
                request.RecordedAtUtc,
                request.IdempotencyKey,
            },
            MesDefectDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesRelatedQualityItemListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/related-quality-items?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken,
        string? exactRequestNo = null) =>
        SendAsync<BusinessConsoleMesReceiptRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/finished-goods-receipt-requests?" + ReceiptListQuery(request, exactRequestNo),
            null,
            cancellationToken);

    public async Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateFinishedGoodsReceiptRequestResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/finished-goods-receipt-requests",
            request,
            cancellationToken);

        if (response.FinishedGoodsReceiptRequestId is null ||
            response.FinishedGoodsReceiptRequestId.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(response.RequestNo))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMesCreateReceiptResponse(
            response.FinishedGoodsReceiptRequestId.Id.ToString(),
            response.RequestNo);
    }

    public Task<BusinessConsoleMesDowntimeEventListResponse> ListDowntimeEventsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDowntimeEventListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/downtime-events?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/downtime-events",
            request,
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDowntimeEventAsync(
        string internalBearerToken,
        BusinessMesRecordDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/downtime-events",
            new
            {
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                request.WorkCenterId,
                request.DeviceAssetId,
                request.ReasonCode,
                request.StartedAtUtc,
                request.IdempotencyKey,
                request.ToUtc,
            },
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/downtime-events/{Uri.EscapeDataString(downtimeEventId)}/recover",
            request,
            MesDowntimeEventDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesScheduleResultListResponse> ListScheduleResultsAsync(
        string internalBearerToken,
        BusinessConsoleMesScheduleResultListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesScheduleResultListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/schedules?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("trigger", request.Trigger),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesShiftHandoverListResponse> ListShiftHandoversAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesShiftHandoverListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/shift-handovers?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CreateShiftHandoverAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            "/api/business/v1/mes/shift-handovers",
            request,
            MesShiftHandoverDocumentType,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAcceptedAsync(
            internalBearerToken,
            $"/api/business/v1/mes/shift-handovers/{Uri.EscapeDataString(handoverId)}/accept",
            request,
            MesShiftHandoverDocumentType,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetBatchTraceabilityAsync(
        string internalBearerToken,
        string batchOrSerial,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/batches/{Uri.EscapeDataString(batchOrSerial)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesTraceabilityResponse> GetMaterialLotTraceabilityAsync(
        string internalBearerToken,
        string materialLotId,
        BusinessConsoleMesContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesTraceabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/mes/traceability/material-lots/{Uri.EscapeDataString(materialLotId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesCapacityImpactListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/capacity-impacts?" + ListQuery(request),
            null,
            cancellationToken);

    private async Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderCoreAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateRushWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/work-orders/rush",
            request,
            cancellationToken);
        return new BusinessConsoleCreateRushWorkOrderResponse(
            response.WorkOrderId,
            response.Schedule.ToBusinessConsoleResult(),
            response.AffectedWorkOrderIds);
    }

    private async Task<BusinessConsoleMesOperationTaskActionResponse> OperationTaskActionAsync(
        string internalBearerToken,
        string operationTaskId,
        string action,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleMesOperationTaskActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/{action}",
            request,
            cancellationToken);
        var expectedStatus = action switch
        {
            "start" or "resume" => "InProgress",
            "pause" => "Paused",
            "complete" => "Completed",
            _ => null,
        };
        if (!string.Equals(response.OperationTaskId, operationTaskId, StringComparison.Ordinal)
            || expectedStatus is null
            || !string.Equals(response.Status, expectedStatus, StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return response with
        {
            OperationReceipt = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    $"mes.operation-task.{action}",
                    "mes",
                    "operation-task",
                    response.OperationTaskId,
                    response.ChangedAtUtc,
                    response.Status,
                    request.IdempotencyKey),
        };
    }

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string ListQuery(BusinessConsoleMesListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string MaterialIssueRequestListQuery(BusinessConsoleMesMaterialIssueRequestListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take),
            ("operationTaskId", request.OperationTaskId));

    private static string DispatchTaskListQuery(BusinessConsoleMesDispatchTaskListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("assignedUserId", request.AssignedUserId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ReceiptListQuery(BusinessConsoleMesListRequest request, string? exactRequestNo) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("requestNo", exactRequestNo),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string OperationTaskListQuery(BusinessMesOperationTaskListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("workOrderId", request.WorkOrderId),
            ("assignedUserIds", request.AssignedUserIds),
            ("teamIds", request.TeamIds),
            ("workCenterIds", request.WorkCenterIds),
            ("operationTaskId", request.OperationTaskId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string WorkOrderListQuery(BusinessMesWorkOrderListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("assignedUserIds", request.AssignedUserIds),
            ("teamIds", request.TeamIds),
            ("workCenterIds", request.WorkCenterIds),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("statuses", request.Statuses),
            ("workOrderId", request.WorkOrderId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ListQueryWithoutStatus(BusinessConsoleMesListWithoutStatusRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string ProductionPlanListQuery(BusinessConsoleMesProductionPlanListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("source", request.Source),
            ("readinessStatus", request.ReadinessStatus),
            ("skip", request.Skip),
            ("take", request.Take));

    private static string FoundationQuery(BusinessConsoleMesFoundationReadinessRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("siteCode", request.SiteCode),
            ("lineCode", request.LineCode),
            ("workCenterCode", request.WorkCenterCode),
            ("skuId", request.SkuId),
            ("productionVersionId", request.ProductionVersionId),
            ("plannedStartUtc", request.PlannedStartUtc),
            ("plannedEndUtc", request.PlannedEndUtc));

    private sealed record DownstreamCreateRushWorkOrderResponse(
        string WorkOrderId,
        DownstreamMesScheduleResult Schedule,
        IReadOnlyCollection<string> AffectedWorkOrderIds);

    private sealed record DownstreamMesScheduleResult(
        int ScheduleVersion,
        JsonElement Trigger,
        DateTimeOffset ScheduledAtUtc,
        IReadOnlyCollection<BusinessConsoleScheduledOperation> Assignments,
        IReadOnlyCollection<string> AffectedWorkOrderIds)
    {
        public BusinessConsoleMesScheduleResult ToBusinessConsoleResult() =>
            new(
                ScheduleVersion,
                FormatTrigger(Trigger),
                ScheduledAtUtc,
                Assignments,
                AffectedWorkOrderIds);
    }

    private sealed record DownstreamRecordProductionReportResponse(
        DownstreamProductionReportId? ProductionReportId,
        string? ReportNo);

    private sealed record DownstreamProductionReportId(Guid Id);

    private sealed record DownstreamCreateFinishedGoodsReceiptRequestResponse(
        DownstreamFinishedGoodsReceiptRequestId? FinishedGoodsReceiptRequestId,
        string? RequestNo);

    private sealed record DownstreamFinishedGoodsReceiptRequestId(Guid Id);

    // Downstream force-release body carries the actor injected by the gateway from the
    // authenticated principal; the request DTO no longer exposes a caller-supplied actor.
    private sealed record DownstreamForceReleaseQualityHoldRequest(
        string OrganizationId,
        string EnvironmentId,
        string Reason,
        string? SourceService,
        DateTimeOffset? ReleasedAtUtc);

    private sealed record DownstreamReverseProductionReportRequest(
        string OrganizationId,
        string EnvironmentId,
        string Reason,
        string ActorRef,
        DateTimeOffset? ReversedAtUtc,
        string? IdempotencyKey);

    private static string FormatTrigger(JsonElement trigger) => trigger.ValueKind switch
    {
        JsonValueKind.String => trigger.GetString() ?? string.Empty,
        JsonValueKind.Number => trigger.GetRawText(),
        _ => trigger.ToString(),
    };
}
