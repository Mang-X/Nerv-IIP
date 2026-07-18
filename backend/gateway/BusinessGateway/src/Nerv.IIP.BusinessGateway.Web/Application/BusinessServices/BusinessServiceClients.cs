using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMasterDataClient
{
    Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMasterDataResourceDetail> GetResourceDetailAsync(
        string internalBearerToken,
        BusinessConsoleMasterDataResourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMasterDataResourceDetail> UpdateResourceAsync(
        string internalBearerToken,
        BusinessConsoleUpdateMasterDataResourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMasterDataResourceDetail> SetResourceEnabledAsync(
        string internalBearerToken,
        BusinessConsoleSetMasterDataResourceEnabledRequest request,
        bool enabled,
        string actor,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductCategoryListResponse> ListProductCategoriesAsync(
        string internalBearerToken,
        BusinessConsoleListProductCategoriesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductCategoryItem> GetProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateProductCategoryAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductCategoryItem> UpdateProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleUpdateProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductCategoryItem> ArchiveProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleArchiveProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSkillListResponse> ListSkillsAsync(
        string internalBearerToken,
        BusinessConsoleListSkillsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSkillItem> GetSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleSkillRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSkillAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkillRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSkillItem> UpdateSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleUpdateSkillRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSkillItem> ArchiveSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleArchiveSkillRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateBusinessPartnerAsync(
        string internalBearerToken,
        BusinessConsoleCreateBusinessPartnerRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateUnitOfMeasureAsync(
        string internalBearerToken,
        BusinessConsoleCreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateUomConversionAsync(
        string internalBearerToken,
        BusinessConsoleCreateUomConversionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateWorkshopAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkshopRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSiteAsync(
        string internalBearerToken,
        BusinessConsoleCreateSiteRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateProductionLineAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionLineRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateWorkCenterAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCenterRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> RegisterDeviceAssetAsync(
        string internalBearerToken,
        BusinessConsoleRegisterDeviceAssetRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateShiftAsync(
        string internalBearerToken,
        BusinessConsoleCreateShiftRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateWorkCalendarAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCalendarRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateTeamAsync(
        string internalBearerToken,
        BusinessConsoleCreateTeamRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> AddTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleAddTeamMemberRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTeamMemberListResponse> ListTeamMembersAsync(
        string internalBearerToken,
        BusinessConsoleListTeamMembersRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> RemoveTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleRemoveTeamMemberRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateDepartmentAsync(
        string internalBearerToken,
        BusinessConsoleCreateDepartmentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> AssignPersonnelSkillAsync(
        string internalBearerToken,
        BusinessConsoleAssignPersonnelSkillRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePersonnelSkillMatrixResponse> ListPersonnelSkillMatrixAsync(
        string internalBearerToken,
        BusinessConsolePersonnelSkillMatrixRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateReferenceDataCodeAsync(
        string internalBearerToken,
        BusinessConsoleCreateReferenceDataCodeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCodeRuleListResponse> ListCodeRulesAsync(
        string internalBearerToken,
        BusinessConsoleCodeRuleContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCodeRuleDetailResponse> GetCodeRuleAsync(
        string internalBearerToken,
        BusinessConsoleCodeRuleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCodeRuleVersionResponse> CreateCodeRuleVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateCodeRuleVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCodeRulePreviewResponse> PreviewCodeRuleAsync(
        string internalBearerToken,
        BusinessConsolePreviewCodeRuleRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessIamDirectoryClient
{
    Task<BusinessConsoleWorkerDirectoryResponse> ListWorkersAsync(
        string internalBearerToken,
        BusinessConsoleWorkerDirectoryRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessInventoryClient
{
    Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInventoryExpiryAlertsResponse> ListExpiryAlertsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryExpiryAlertsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? forwardedPermissions = null);

    Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessQualityClient
{
    Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListInspectionRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionTaskListResponse> ListInspectionTasksAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordFromTaskResponse> CreateInspectionRecordFromTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessConsoleCreateInspectionRecordFromTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionPlanCharacteristicListResponse> GetInspectionPlanCharacteristicsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanCharacteristicsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleOpenNcrFromInspectionResponse> OpenNcrFromInspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleOpenNcrFromInspectionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityNcrDetailResponse> GetNcrAsync(
        string internalBearerToken,
        BusinessConsoleQualityNcrDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInspectionRecordDetailResponse> GetInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualitySpcControlChartResponse> QuerySpcControlChartAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityProcessCapabilityResponse> QueryProcessCapabilityAsync(
        string internalBearerToken,
        BusinessConsoleQualityProcessCapabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonListResponse> ListQualityReasonsAsync(
        string internalBearerToken,
        BusinessConsoleQualityReasonListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> GetQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> CreateQualityReasonAsync(
        string internalBearerToken,
        BusinessConsoleCreateQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> UpdateQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleUpdateQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> ArchiveQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleArchiveQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        string actor,
        CancellationToken cancellationToken);
}

public interface IBusinessFileStorageClient
{
    Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken);
}

public interface IBusinessProductEngineeringClient
{
    Task<BusinessConsoleEngineeringEntityResponse> RegisterEngineeringDocumentAsync(
        string internalBearerToken,
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> PublishSopDocumentAsync(
        string internalBearerToken,
        BusinessConsolePublishSopDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCurrentSopDocumentsResponse> GetCurrentSopDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleCurrentSopDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringDocumentListResponse> ListEngineeringDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringDocumentItem> GetEngineeringDocumentAsync(
        string internalBearerToken,
        string documentNumber,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> CreateEngineeringItemRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringItemListResponse> ListEngineeringItemsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringItemsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringItemRevisionItem> GetEngineeringItemAsync(
        string internalBearerToken,
        string itemCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringBomRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringBomItem> GetEngineeringBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomExplosionResponse> GetEngineeringBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleBomExplosionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomWhereUsedResponse> GetEngineeringBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomDiffResponse> GetBomDiffAsync(
        string internalBearerToken,
        BusinessConsoleBomDiffRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleManufacturingBomListResponse> ListManufacturingBomsAsync(
        string internalBearerToken,
        BusinessConsoleListManufacturingBomsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleManufacturingBomItem> GetManufacturingBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomExplosionResponse> GetManufacturingBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleManufacturingBomExplosionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomWhereUsedResponse> GetManufacturingBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseManufacturingBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseManufacturingBomRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRoutingItem> GetRoutingAsync(
        string internalBearerToken,
        string routingCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseRoutingAsync(
        string internalBearerToken,
        BusinessConsoleReleaseRoutingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationListResponse> ListStandardOperationsAsync(
        string internalBearerToken,
        BusinessConsoleListStandardOperationsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationItem> GetStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationResponse> CreateStandardOperationAsync(
        string internalBearerToken,
        BusinessConsoleCreateStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationResponse> UpdateStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleUpdateStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ArchiveStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleArchiveStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> CancelScheduledEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleCancelScheduledEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> RescheduleEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleRescheduleEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeImpactPreviewResponse> PreviewEngineeringChangeImpactAsync(
        string internalBearerToken,
        BusinessConsoleEngineeringChangeImpactPreviewRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeListResponse> ListEngineeringChangesAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringChangesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeItem> GetEngineeringChangeAsync(
        string internalBearerToken,
        string changeNumber,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateProductionVersionResponse> CreateProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateProductionVersionResponse> UpdateProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleUpdateProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ArchiveProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleArchiveProductionVersionRequest request,
        CancellationToken cancellationToken);
}

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
}

public interface IBusinessSchedulingClient
{
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

public interface IBusinessApprovalClient
{
    Task<BusinessConsoleApprovalTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTemplateListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStartApprovalChainResponse> StartChainAsync(
        string internalBearerToken,
        BusinessConsoleStartApprovalChainRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalChainListResponse> ListChainsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalChainResponse> GetChainAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalTaskListResponse> ListPendingTasksAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalDecisionListResponse> ListDecisionsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDecisionListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResolveApprovalStepResponse> ResolveStepAsync(
        string internalBearerToken,
        BusinessConsoleResolveApprovalStepRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleApprovalDelegationListResponse> ListDelegationsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDelegationListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateApprovalDelegationResponse> CreateDelegationAsync(
        string internalBearerToken,
        BusinessConsoleCreateApprovalDelegationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RevokeDelegationAsync(
        string internalBearerToken,
        string delegationId,
        BusinessConsoleRevokeApprovalDelegationRequest request,
        CancellationToken cancellationToken);
}

public interface IBusinessNotificationClient
{
    Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken);

    Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken);

    Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        string internalBearerToken,
        BusinessConsoleMarkNotificationMessageReadRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();
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
        BusinessConsoleMesWorkOrderListRequest request,
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
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
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
        BusinessConsoleMesRecordDefectRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesRelatedQualityItemListResponse> ListRelatedQualityItemsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMesReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken);

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

    Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
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

public sealed class BusinessServiceProxyException : Exception
{
    public const string DownstreamRequestFailedMessage = "downstream-request-failed";

    public BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : base(DownstreamRequestFailedMessage, innerException)
    {
        _ = message;
        StatusCode = statusCode;
    }

    private BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string safeMessage,
        Exception? innerException,
        bool messageIsSafe)
        : base(messageIsSafe ? safeMessage : DownstreamRequestFailedMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public static BusinessServiceProxyException FromSafeDownstreamMessage(
        HttpStatusCode statusCode,
        string? downstreamMessage,
        Exception? innerException = null) =>
        new(
            statusCode,
            IsStrictSafeDownstreamMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage,
            innerException,
            messageIsSafe: true);

    public static BusinessServiceProxyException FromDownstreamBusinessMessage(
        string? downstreamMessage,
        Exception? innerException = null) =>
        new(
            HttpStatusCode.BadRequest,
            IsSafeDownstreamBusinessMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage,
            innerException,
            messageIsSafe: true);

    private static bool IsStrictSafeDownstreamMessage(string? downstreamMessage)
    {
        if (string.IsNullOrWhiteSpace(downstreamMessage) || downstreamMessage.Length > 128)
        {
            return false;
        }

        var first = downstreamMessage[0];
        if (!IsAsciiLetter(first) && !char.IsAsciiDigit(first))
        {
            return false;
        }

        return downstreamMessage.All(static value =>
            IsAsciiLetter(value) ||
            char.IsAsciiDigit(value) ||
            value is '-' or '_' or '.');
    }

    private static bool IsSafeDownstreamBusinessMessage(string? downstreamMessage)
    {
        if (string.IsNullOrWhiteSpace(downstreamMessage) || downstreamMessage.Length > 500)
        {
            return false;
        }

        var first = downstreamMessage[0];
        if (char.IsWhiteSpace(first))
        {
            return false;
        }

        return downstreamMessage.All(static value =>
            !char.IsControl(value) &&
            value is not '<' and not '>' and not '{' and not '}' and not '/' and not '\\');
    }

    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

public abstract class BusinessServiceHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected async Task<TResponse> SendAsync<TResponse>(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        configureRequest?.Invoke(request);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: jsonOptions ?? JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                response.StatusCode,
                await ReadDownstreamEnvelopeMessageAsync(response, cancellationToken));
        }

        try
        {
            return await ReadResponseDataAsync<TResponse>(response, jsonOptions ?? JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response",
                ex);
        }
    }

    private static async Task<TResponse> ReadResponseDataAsync<TResponse>(
        HttpResponseMessage response,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        var content = response.Content
            ?? throw new InvalidOperationException("Platform API returned an empty response.");
        var json = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Platform API returned an empty response.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Business services use the platform response envelope. A 2xx response
        // with success=false is a business validation failure, not a parse error.
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            throw BusinessServiceProxyException.FromDownstreamBusinessMessage(DownstreamEnvelopeMessage(root));
        }

        var payload = root.TryGetProperty("data", out var data)
            ? data
            : root;

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Platform API returned an empty response data payload.");
        }

        return payload.Deserialize<TResponse>(jsonOptions)
            ?? throw new InvalidOperationException("Platform API returned an empty response data payload.");
    }

    private static string? DownstreamEnvelopeMessage(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;

    private static async Task<string?> ReadDownstreamEnvelopeMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    protected static bool? TrueFlag(bool value) => value ? true : null;

    private static string FormatValue(object value) => value switch
    {
        bool boolValue => boolValue.ToString().ToLowerInvariant(),
        DateOnly date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

public sealed class HttpBusinessApprovalClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessApprovalClient
{
    public Task<BusinessConsoleApprovalTemplateListResponse> ListTemplatesAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTemplateListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalTemplateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/templates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("documentType", request.DocumentType),
                ("isActive", request.IsActive),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateOrUpdateApprovalTemplateResponse> CreateOrUpdateTemplateAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateApprovalTemplateRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateOrUpdateApprovalTemplateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/templates",
            request,
            cancellationToken);

    public Task<BusinessConsoleStartApprovalChainResponse> StartChainAsync(
        string internalBearerToken,
        BusinessConsoleStartApprovalChainRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStartApprovalChainResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/chains",
            request,
            cancellationToken);

    public Task<BusinessConsoleApprovalChainListResponse> ListChainsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalChainListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/chains?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("startedBy", request.StartedBy),
                ("sourceService", request.SourceService),
                ("documentType", request.DocumentType),
                ("documentId", request.DocumentId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalChainResponse> GetChainAsync(
        string internalBearerToken,
        BusinessConsoleApprovalChainRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalChainResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(request.ChainId)}",
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalTaskListResponse> ListPendingTasksAsync(
        string internalBearerToken,
        BusinessConsoleApprovalTaskListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("actorType", request.ActorType),
                ("actorRef", request.ActorRef),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleApprovalDecisionListResponse> ListDecisionsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDecisionListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalDecisionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/decisions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("chainId", request.ChainId),
                ("actorType", request.ActorType),
                ("actorRef", request.ActorRef),
                ("decision", request.Decision),
                ("documentType", request.DocumentType),
                ("documentId", request.DocumentId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResolveApprovalStepResponse> ResolveStepAsync(
        string internalBearerToken,
        BusinessConsoleResolveApprovalStepRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResolveApprovalStepResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(request.ChainId)}/steps/{request.StepNo.ToString(CultureInfo.InvariantCulture)}/resolve",
            request,
            cancellationToken);

    public Task<BusinessConsoleApprovalDelegationListResponse> ListDelegationsAsync(
        string internalBearerToken,
        BusinessConsoleApprovalDelegationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleApprovalDelegationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/approvals/delegations?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("delegatorActorRef", request.DelegatorActorRef),
                ("delegateActorRef", request.DelegateActorRef),
                ("documentType", request.DocumentType),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateApprovalDelegationResponse> CreateDelegationAsync(
        string internalBearerToken,
        BusinessConsoleCreateApprovalDelegationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateApprovalDelegationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/approvals/delegations",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RevokeDelegationAsync(
        string internalBearerToken,
        string delegationId,
        BusinessConsoleRevokeApprovalDelegationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/approvals/delegations/{Uri.EscapeDataString(delegationId)}/revoke?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            request,
            cancellationToken);
}

public sealed class HttpBusinessNotificationClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessNotificationClient
{
    public Task<NotificationMessageListResponse> ListMessagesAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<NotificationMessageListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/notifications/v1/messages?" + NotificationQuery(request),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(notificationRequest, request));

    public Task<NotificationTaskListResponse> ListTasksAsync(
        string internalBearerToken,
        BusinessConsoleNotificationListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<NotificationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/notifications/v1/tasks?" + NotificationQuery(request),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(notificationRequest, request));

    public Task<MarkNotificationMessageReadResponse> MarkMessageReadAsync(
        string internalBearerToken,
        BusinessConsoleMarkNotificationMessageReadRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<MarkNotificationMessageReadResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/notifications/v1/messages/{Uri.EscapeDataString(request.MessageId)}/read?" + Query(("recipientRef", request.RecipientRef)),
            null,
            cancellationToken,
            configureRequest: notificationRequest => AddNotificationScopeHeaders(
                notificationRequest,
                request.OrganizationId,
                request.EnvironmentId));

    private static string NotificationQuery(BusinessConsoleNotificationListRequest request) =>
        Query(
            ("recipientRef", request.RecipientRef),
            ("status", request.Status));

    private static void AddNotificationScopeHeaders(HttpRequestMessage httpRequest, BusinessConsoleNotificationListRequest request)
        => AddNotificationScopeHeaders(httpRequest, request.OrganizationId, request.EnvironmentId);

    private static void AddNotificationScopeHeaders(
        HttpRequestMessage httpRequest,
        string organizationId,
        string environmentId)
    {
        httpRequest.Headers.TryAddWithoutValidation("X-Organization-Id", organizationId);
        httpRequest.Headers.TryAddWithoutValidation("X-Environment-Id", environmentId);
    }
}

public sealed class HttpBusinessMasterDataClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMasterDataClient
{
    public async Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleResourceListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/resources?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("resourceType", request.ResourceType),
                ("includeDisabled", TrueFlag(request.IncludeDisabled)),
                ("skip", request.Skip),
                ("take", request.Take),
                ("codeSet", request.CodeSet),
                ("parentCode", request.ParentCode),
                ("siteCode", request.SiteCode),
                ("lineCode", request.LineCode),
                ("workCenterCode", request.WorkCenterCode),
                ("category", request.Category),
                ("partnerType", request.PartnerType),
                ("keyword", request.Keyword),
                ("all", TrueFlag(request.All)),
                ("departmentCode", request.DepartmentCode),
                ("shiftCode", request.ShiftCode),
                ("userId", request.UserId),
                ("skillCode", request.SkillCode)),
            null,
            cancellationToken);
        return response.Total > 0 ? response : response with { Total = response.Resources.Count };
    }

    public Task<BusinessConsoleMasterDataResourceDetail> GetResourceDetailAsync(
        string internalBearerToken,
        BusinessConsoleMasterDataResourceRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMasterDataResourceDetail>(
            internalBearerToken,
            HttpMethod.Get,
            ResourcePath(request.ResourceType, request.Code) + "?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("codeSet", request.CodeSet),
                ("effectiveFrom", request.EffectiveFrom)),
            null,
            cancellationToken);

    public Task<BusinessConsoleMasterDataResourceDetail> UpdateResourceAsync(
        string internalBearerToken,
        BusinessConsoleUpdateMasterDataResourceRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMasterDataResourceDetail>(
            internalBearerToken,
            HttpMethod.Patch,
            ResourcePath(request.ResourceType, request.Code),
            request,
            cancellationToken);

    public Task<BusinessConsoleMasterDataResourceDetail> SetResourceEnabledAsync(
        string internalBearerToken,
        BusinessConsoleSetMasterDataResourceEnabledRequest request,
        bool enabled,
        string actor,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMasterDataResourceDetail>(
            internalBearerToken,
            HttpMethod.Post,
            ResourcePath(request.ResourceType, request.Code) + (enabled ? "/enable" : "/disable"),
            request,
            cancellationToken,
            configureRequest: message =>
            {
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor);
                message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                message.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);
            });

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/skus",
            request,
            cancellationToken);

    public Task<BusinessConsoleProductCategoryListResponse> ListProductCategoriesAsync(
        string internalBearerToken,
        BusinessConsoleListProductCategoriesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductCategoryListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/product-categories?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("parentCode", request.ParentCode),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleProductCategoryItem> GetProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleProductCategoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductCategoryItem>(
            internalBearerToken,
            HttpMethod.Get,
            ProductCategoryPath(categoryCode) + "?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateProductCategoryAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductCategoryRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/product-categories", request, cancellationToken);

    public Task<BusinessConsoleProductCategoryItem> UpdateProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleUpdateProductCategoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductCategoryItem>(
            internalBearerToken,
            HttpMethod.Put,
            ProductCategoryPath(categoryCode),
            request with { CategoryCode = categoryCode },
            cancellationToken);

    public Task<BusinessConsoleProductCategoryItem> ArchiveProductCategoryAsync(
        string internalBearerToken,
        string categoryCode,
        BusinessConsoleArchiveProductCategoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductCategoryItem>(
            internalBearerToken,
            HttpMethod.Post,
            ProductCategoryPath(categoryCode) + "/archive",
            request with { CategoryCode = categoryCode },
            cancellationToken);

    public Task<BusinessConsoleSkillListResponse> ListSkillsAsync(
        string internalBearerToken,
        BusinessConsoleListSkillsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleSkillListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/skills?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("groupName", request.GroupName),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleSkillItem> GetSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleSkillRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleSkillItem>(
            internalBearerToken,
            HttpMethod.Get,
            SkillPath(skillCode) + "?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateSkillAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkillRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/skills", request, cancellationToken);

    public Task<BusinessConsoleSkillItem> UpdateSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleUpdateSkillRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleSkillItem>(
            internalBearerToken,
            HttpMethod.Put,
            SkillPath(skillCode),
            request with { SkillCode = skillCode },
            cancellationToken);

    public Task<BusinessConsoleSkillItem> ArchiveSkillAsync(
        string internalBearerToken,
        string skillCode,
        BusinessConsoleArchiveSkillRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleSkillItem>(
            internalBearerToken,
            HttpMethod.Post,
            SkillPath(skillCode) + "/archive",
            request with { SkillCode = skillCode },
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateBusinessPartnerAsync(
        string internalBearerToken,
        BusinessConsoleCreateBusinessPartnerRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/partners", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateUnitOfMeasureAsync(
        string internalBearerToken,
        BusinessConsoleCreateUnitOfMeasureRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/units-of-measure", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateUomConversionAsync(
        string internalBearerToken,
        BusinessConsoleCreateUomConversionRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/uom-conversions", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateWorkshopAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkshopRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/workshops", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateSiteAsync(
        string internalBearerToken,
        BusinessConsoleCreateSiteRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/sites", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateProductionLineAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionLineRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/production-lines", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateWorkCenterAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCenterRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/work-centers", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> RegisterDeviceAssetAsync(
        string internalBearerToken,
        BusinessConsoleRegisterDeviceAssetRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/device-assets", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateShiftAsync(
        string internalBearerToken,
        BusinessConsoleCreateShiftRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/shifts", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateWorkCalendarAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCalendarRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/work-calendars", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateTeamAsync(
        string internalBearerToken,
        BusinessConsoleCreateTeamRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/teams", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> AddTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleAddTeamMemberRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, $"/api/business/v1/master-data/teams/{Uri.EscapeDataString(request.TeamCode)}/members", request, cancellationToken);

    public Task<BusinessConsoleTeamMemberListResponse> ListTeamMembersAsync(
        string internalBearerToken,
        BusinessConsoleListTeamMembersRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTeamMemberListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/master-data/teams/{Uri.EscapeDataString(request.TeamCode)}/members?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("teamCode", request.TeamCode),
                ("includeDisabled", TrueFlag(request.IncludeDisabled))),
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> RemoveTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleRemoveTeamMemberRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceItem>(
            internalBearerToken,
            HttpMethod.Delete,
            $"/api/business/v1/master-data/teams/{Uri.EscapeDataString(request.TeamCode)}/members/{Uri.EscapeDataString(request.UserId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("teamCode", request.TeamCode),
                ("userId", request.UserId),
                ("reason", request.Reason)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateDepartmentAsync(
        string internalBearerToken,
        BusinessConsoleCreateDepartmentRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/departments", request, cancellationToken);

    public Task<BusinessConsoleResourceItem> AssignPersonnelSkillAsync(
        string internalBearerToken,
        BusinessConsoleAssignPersonnelSkillRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/personnel-skills", request, cancellationToken);

    public Task<BusinessConsolePersonnelSkillMatrixResponse> ListPersonnelSkillMatrixAsync(
        string internalBearerToken,
        BusinessConsolePersonnelSkillMatrixRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsolePersonnelSkillMatrixResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/personnel-skills/matrix?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("userId", request.UserId),
                ("skillCode", request.SkillCode),
                ("includeDisabled", TrueFlag(request.IncludeDisabled))),
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateReferenceDataCodeAsync(
        string internalBearerToken,
        BusinessConsoleCreateReferenceDataCodeRequest request,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/reference-data", request, cancellationToken);

    public Task<BusinessConsoleCodeRuleListResponse> ListCodeRulesAsync(
        string internalBearerToken,
        BusinessConsoleCodeRuleContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCodeRuleListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/code-rules?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleCodeRuleDetailResponse> GetCodeRuleAsync(
        string internalBearerToken,
        BusinessConsoleCodeRuleRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCodeRuleDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            CodeRulePath(request.RuleKey) + "?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleCodeRuleVersionResponse> CreateCodeRuleVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateCodeRuleVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCodeRuleVersionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            CodeRulePath(request.RuleKey) + "/versions",
            request,
            cancellationToken);

    public Task<BusinessConsoleCodeRulePreviewResponse> PreviewCodeRuleAsync(
        string internalBearerToken,
        BusinessConsolePreviewCodeRuleRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCodeRulePreviewResponse>(
            internalBearerToken,
            HttpMethod.Post,
            CodeRulePath(request.RuleKey) + "/preview",
            request,
            cancellationToken);

    private Task<BusinessConsoleResourceItem> CreateResourceAsync(
        string internalBearerToken,
        string path,
        object request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceItem>(
            internalBearerToken,
            HttpMethod.Post,
            path,
            request,
            cancellationToken);

    private static string ResourcePath(string resourceType, string code) =>
        $"/api/business/v1/master-data/resources/{Uri.EscapeDataString(resourceType)}/{Uri.EscapeDataString(code)}";

    private static string ProductCategoryPath(string categoryCode) =>
        $"/api/business/v1/master-data/product-categories/{Uri.EscapeDataString(categoryCode)}";

    private static string SkillPath(string skillCode) =>
        $"/api/business/v1/master-data/skills/{Uri.EscapeDataString(skillCode)}";

    private static string CodeRulePath(string ruleKey) =>
        $"/api/business/v1/master-data/code-rules/{Uri.EscapeDataString(ruleKey)}";

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));
}

public sealed class HttpBusinessIamDirectoryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessIamDirectoryClient
{
    public Task<BusinessConsoleWorkerDirectoryResponse> ListWorkersAsync(
        string internalBearerToken,
        BusinessConsoleWorkerDirectoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleWorkerDirectoryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/internal/iam/v1/workers?" + Query(
                ("filterSearch", request.Keyword),
                ("pageIndex", request.PageIndex),
                ("pageSize", request.PageSize),
                ("filterEnabled", request.IncludeDisabled ? null : true)),
            null,
            cancellationToken);
}

public sealed class BusinessGatewayInventoryForwardedPermissionOptions
{
    public string Issuer { get; set; } = "business-gateway";

    public string? SigningKey { get; set; }
}

public sealed class HttpBusinessInventoryClient(
    HttpClient httpClient,
    IOptions<BusinessGatewayInventoryForwardedPermissionOptions> forwardedPermissionOptions)
    : BusinessServiceHttpClient(httpClient), IBusinessInventoryClient
{
    public Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("uomCode", request.UomCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("lotNo", request.LotNo),
                ("serialNo", request.SerialNo),
                ("qualityStatus", request.QualityStatus),
                ("ownerType", request.OwnerType),
                ("ownerId", request.OwnerId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleInventoryExpiryAlertsResponse> ListExpiryAlertsAsync(
        string internalBearerToken,
        BusinessConsoleInventoryExpiryAlertsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleInventoryExpiryAlertsResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/inventory/v1/expiry-alerts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("siteCode", request.SiteCode),
                ("skuCode", request.SkuCode),
                ("locationCode", request.LocationCode),
                ("asOfDate", request.AsOfDate),
                ("nearExpiryThresholdDays", request.NearExpiryThresholdDays),
                ("includeZeroAvailable", TrueFlag(request.IncludeZeroAvailable))),
            null,
            cancellationToken);

    public Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? forwardedPermissions = null) =>
        SendAsync<BusinessConsolePostStockMovementResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/movements",
            request,
            cancellationToken,
            configureRequest: httpRequest => AddForwardedPermissions(
                httpRequest,
                forwardedPermissions,
                request.OrganizationId,
                request.EnvironmentId,
                request.IdempotencyKey));

    public Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateStockCountTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/inventory/v1/count-tasks",
            request,
            cancellationToken);

    public Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConfirmStockCountAdjustmentResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/inventory/v1/count-tasks/{Uri.EscapeDataString(countTaskId)}/adjustments",
            new DownstreamConfirmStockCountAdjustmentRequest(
                countTaskId,
                request.CountedQuantity,
                request.IdempotencyKey),
            cancellationToken);

    private sealed record DownstreamConfirmStockCountAdjustmentRequest(
        string CountTaskId,
        decimal CountedQuantity,
        string IdempotencyKey);

    private void AddForwardedPermissions(
        HttpRequestMessage request,
        IReadOnlyCollection<string>? forwardedPermissions,
        string organizationId,
        string environmentId,
        string requestKey)
    {
        if (forwardedPermissions is null || forwardedPermissions.Count == 0)
        {
            return;
        }

        var options = forwardedPermissionOptions.Value;
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return;
        }

        var permissions = string.Join(' ', forwardedPermissions.Order(StringComparer.Ordinal));
        var issuedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = InventoryForwardedPermissionHeaders.CreateSignature(
            options.SigningKey,
            options.Issuer,
            permissions,
            organizationId,
            environmentId,
            requestKey,
            issuedAtUnixSeconds);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.PermissionsHeaderName, permissions);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.IssuerHeaderName, options.Issuer);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.OrganizationHeaderName, organizationId);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.EnvironmentHeaderName, environmentId);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.RequestKeyHeaderName, requestKey);
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.IssuedAtHeaderName, issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(InventoryForwardedPermissionHeaders.SignatureHeaderName, signature);
    }
}

public sealed class HttpBusinessQualityClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessQualityClient
{
    public async Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray(),
            response.Total);
    }

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateInspectionRecordResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/inspection-records",
            ToDownstreamRequest(request),
            cancellationToken);

    public async Task<BusinessConsoleQualityInspectionTaskListResponse> ListInspectionTasksAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionTaskListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skuCode", request.SkuCode),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityInspectionTaskListResponse(
            response.Items.Select(ToInspectionTaskItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleCreateInspectionRecordFromTaskResponse> CreateInspectionRecordFromTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessConsoleCreateInspectionRecordFromTaskRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateInspectionRecordFromTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-tasks/{Uri.EscapeDataString(inspectionTaskId)}/inspection-record",
            new DownstreamCreateInspectionRecordFromTaskRequest(
                inspectionTaskId,
                request.InspectorUserId,
                request.ResultLines?.Select(ToDownstreamLine).ToArray(),
                request.DispositionReason,
                request.DispositionAttachmentFileIds),
            cancellationToken);
        return new BusinessConsoleCreateInspectionRecordFromTaskResponse(
            response.InspectionRecordId,
            response.Result,
            response.NonconformanceReportId,
            response.NonconformanceReportCode);
    }

    public async Task<BusinessConsoleQualityInspectionPlanCharacteristicListResponse> GetInspectionPlanCharacteristicsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanCharacteristicsRequest request,
        CancellationToken cancellationToken)
    {
        // The Quality inspection-plans list already resolves a single plan (with characteristics)
        // by id via its keyword filter; no dedicated detail endpoint is needed.
        var response = await SendAsync<DownstreamInspectionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.InspectionPlanId),
                ("skip", 0),
                ("take", 1)),
            null,
            cancellationToken);
        var plan = response.Items.FirstOrDefault(x =>
                string.Equals(x.InspectionPlanId, request.InspectionPlanId, StringComparison.OrdinalIgnoreCase))
            ?? response.Items.FirstOrDefault();
        var items = (plan?.Characteristics ?? [])
            .Select(c => new BusinessConsoleInspectionPlanCharacteristicItem(
                c.CharacteristicCode,
                c.Name,
                c.CharacteristicType,
                c.Required,
                c.NominalValue,
                c.LowerSpecLimit,
                c.UpperSpecLimit,
                c.UnitCode))
            .ToArray();
        return new BusinessConsoleQualityInspectionPlanCharacteristicListResponse(
            request.InspectionPlanId,
            plan?.PlanCode,
            plan?.Category,
            plan?.SkuCode,
            items);
    }

    public async Task<BusinessConsoleQualityListResponse> ListInspectionRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionRecordListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-records?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("result", request.Status),
                ("skuCode", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleOpenNcrFromInspectionResponse> OpenNcrFromInspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleOpenNcrFromInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamOpenNcrFromInspectionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-records/{Uri.EscapeDataString(inspectionRecordId)}/failures/ncr",
            new DownstreamOpenNcrFromInspectionRequest(
                inspectionRecordId,
                request.OrganizationId,
                request.EnvironmentId,
                request.DefectReason,
                request.AttachmentFileIds),
            cancellationToken);
        return new BusinessConsoleOpenNcrFromInspectionResponse(FormatJsonScalar(response.NcrId));
    }

    public async Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamNcrListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/ncrs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleQualityNcrDetailResponse> GetNcrAsync(
        string internalBearerToken,
        BusinessConsoleQualityNcrDetailRequest request,
        CancellationToken cancellationToken)
    {
        // 代理真实详情端点，org/env 随查询下传由 Quality 服务端做租户过滤：越权 id 与不存在同为
        // not found（下游业务错误透传），不泄露跨租户数据。
        var response = await SendAsync<DownstreamNcrItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(request.NcrId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityNcrDetailResponse(
            response.NcrId,
            response.NcrCode,
            response.Status,
            response.SkuCode,
            response.SourceType,
            response.SourceDocumentId,
            response.DefectQuantity,
            response.DefectReason,
            response.BatchNo,
            response.SerialNo,
            response.SourceInspectionRecordId);
    }

    public async Task<BusinessConsoleInspectionRecordDetailResponse> GetInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordDetailRequest request,
        CancellationToken cancellationToken)
    {
        // 代理真实详情端点；org/env 随查询下传由 Quality 服务端做租户过滤（越权与不存在同为 not found）。
        var record = await SendAsync<DownstreamInspectionRecordDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/quality/inspection-records/{Uri.EscapeDataString(request.InspectionRecordId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);
        return new BusinessConsoleInspectionRecordDetailResponse(
            record.InspectionRecordId,
            record.SourceType,
            record.SourceService,
            record.SourceDocumentId,
            record.SkuCode,
            record.InspectedQuantity,
            record.BatchNo,
            record.SerialNo,
            record.UomCode,
            record.Result,
            record.DispositionReason,
            record.NonconformanceReportId,
            (record.ResultLines ?? []).Select(line => new BusinessConsoleInspectionRecordResultLine(
                line.CharacteristicCode,
                line.ObservedValue,
                line.MeasuredValue,
                line.UnitCode,
                line.Result,
                line.DefectReason,
                line.DefectQuantity)).ToArray(),
            record.CreatedAtUtc);
    }

    public Task<BusinessConsoleQualitySpcControlChartResponse> QuerySpcControlChartAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualitySpcControlChartResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/spc/control-chart?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("characteristicCode", request.CharacteristicCode),
                ("workCenterId", request.WorkCenterId),
                ("subgroupSize", request.SubgroupSize),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityProcessCapabilityResponse> QueryProcessCapabilityAsync(
        string internalBearerToken,
        BusinessConsoleQualityProcessCapabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityProcessCapabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/spc/process-capability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("characteristicCode", request.CharacteristicCode),
                ("workCenterId", request.WorkCenterId),
                ("subgroupSize", request.SubgroupSize),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonListResponse> ListQualityReasonsAsync(
        string internalBearerToken,
        BusinessConsoleQualityReasonListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/reason-codes?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("groupName", request.GroupName),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> GetQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Get,
            QualityReasonPath(reasonCode) + "?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> CreateQualityReasonAsync(
        string internalBearerToken,
        BusinessConsoleCreateQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/reason-codes",
            request,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> UpdateQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleUpdateQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Put,
            QualityReasonPath(reasonCode),
            request with { ReasonCode = reasonCode },
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> ArchiveQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleArchiveQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Post,
            QualityReasonPath(reasonCode) + "/archive",
            request with { ReasonCode = reasonCode },
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/disposition",
            new DownstreamSubmitNcrDispositionRequest(
                ncrId,
                request.DispositionType,
                request.DispositionApprovalChainId,
                request.AttachmentFileIds,
                request.MrbReviews?.Select(ToDownstreamMrbReview).ToArray()),
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/close",
            new DownstreamCloseNcrRequest(
                ncrId,
                request.ReworkWorkOrderId,
                request.ScrapMovementId,
                request.ReturnDocumentId,
                request.Reason),
            cancellationToken,
            configureRequest: message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamInspectionPlanItem item) =>
        new(
            item.InspectionPlanId,
            item.PlanCode,
            item.Status,
            item.Category,
            item.SkuCode,
            item.PartnerId,
            item.WorkCenterId,
            item.DeviceAssetId,
            item.DocumentType,
            null,
            null,
            null,
            null,
            null,
            null);

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamNcrItem item) =>
        new(
            item.NcrId,
            item.NcrCode,
            item.Status,
            null,
            item.SkuCode,
            null,
            null,
            null,
            null,
            item.SourceType,
            item.SourceDocumentId,
            item.DefectQuantity,
            item.DefectReason,
            item.BatchNo,
            item.SerialNo);

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamInspectionRecordItem item) =>
        new(
            item.InspectionRecordId,
            item.InspectionRecordId,
            item.Result,
            null,
            item.SkuCode,
            null,
            null,
            null,
            null,
            item.SourceType,
            item.SourceDocumentId,
            null,
            item.DispositionReason,
            item.BatchNo,
            item.SerialNo);

    private static string QualityReasonPath(string reasonCode) =>
        $"/api/business/v1/quality/reason-codes/{Uri.EscapeDataString(reasonCode)}";

    private sealed record DownstreamInspectionPlanListResponse(
        IReadOnlyCollection<DownstreamInspectionPlanItem> Items,
        int Total);

    private sealed record DownstreamInspectionRecordListResponse(
        IReadOnlyCollection<DownstreamInspectionRecordItem> Items,
        int Total);

    private sealed record DownstreamInspectionTaskListResponse(
        IReadOnlyCollection<DownstreamInspectionTaskItem> Items,
        int Total);

    private sealed record DownstreamCreateInspectionRecordFromTaskResponse(
        string InspectionRecordId,
        string Result,
        string? NonconformanceReportId,
        string? NonconformanceReportCode);

    private sealed record DownstreamInspectionTaskItem(
        string InspectionTaskId,
        string InspectionPlanId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string? SourceDocumentLineId,
        string SkuCode,
        decimal Quantity,
        string UomCode,
        string? BatchNo,
        string? SerialNo,
        string Status,
        DateTimeOffset DueAtUtc,
        DateTimeOffset CreatedAtUtc,
        string? InspectionRecordId);

    private sealed record DownstreamCreateInspectionRecordFromTaskRequest(
        string InspectionTaskId,
        string InspectorUserId,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds);

    private static DownstreamCreateInspectionRecordRequest ToDownstreamRequest(
        BusinessConsoleCreateInspectionRecordRequest request) =>
        new(
            request.OrganizationId,
            request.EnvironmentId,
            request.InspectionPlanId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            request.BatchNo,
            request.SerialNo,
            request.ResultLines?.Select(ToDownstreamLine).ToArray(),
            request.DispositionReason,
            request.DispositionAttachmentFileIds,
            request.StockRelease);

    private static DownstreamInspectionResultLine ToDownstreamLine(
        BusinessConsoleInspectionCharacteristicResult line) =>
        new(
            line.CharacteristicCode,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity,
            line.AttachmentFileIds ?? [],
            line.MeasuredValue);

    private static BusinessConsoleQualityInspectionTaskItem ToInspectionTaskItem(
        DownstreamInspectionTaskItem item) =>
        new(
            item.InspectionTaskId,
            item.InspectionPlanId,
            item.SourceType,
            item.SourceService,
            item.SourceDocumentId,
            item.SourceDocumentLineId,
            item.SkuCode,
            item.Quantity,
            item.UomCode,
            item.BatchNo,
            item.SerialNo,
            item.Status,
            item.DueAtUtc,
            item.CreatedAtUtc,
            item.InspectionRecordId);

    private static DownstreamMrbReview ToDownstreamMrbReview(BusinessConsoleMrbReview review) =>
        new(
            review.ReviewerId,
            review.Decision,
            review.Comment,
            review.ReviewedAtUtc);

    private sealed record DownstreamCreateInspectionRecordRequest(
        string OrganizationId,
        string EnvironmentId,
        string? InspectionPlanId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal InspectedQuantity,
        string? BatchNo,
        string? SerialNo,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BusinessConsoleInspectionStockRelease? StockRelease);

    private sealed record DownstreamInspectionResultLine(
        string CharacteristicCode,
        string ObservedValue,
        string? UnitCode,
        string Result,
        string? DefectReason,
        decimal? DefectQuantity,
        IReadOnlyCollection<string> AttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MeasuredValue);

    private sealed record DownstreamInspectionPlanItem(
        string InspectionPlanId,
        string PlanCode,
        string Category,
        string? SkuCode,
        string? PartnerId,
        string? WorkCenterId,
        string? DeviceAssetId,
        string? DocumentType,
        int Version,
        string Status,
        IReadOnlyCollection<DownstreamInspectionPlanCharacteristic>? Characteristics);

    private sealed record DownstreamInspectionPlanCharacteristic(
        string CharacteristicCode,
        string Name,
        string CharacteristicType,
        bool Required,
        decimal? NominalValue,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit,
        string? UnitCode);

    private sealed record DownstreamInspectionRecordItem(
        string InspectionRecordId,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        string Result,
        string? BatchNo,
        string? SerialNo,
        string? DispositionReason);

    private sealed record DownstreamInspectionRecordDetail(
        string InspectionRecordId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal InspectedQuantity,
        string? BatchNo,
        string? SerialNo,
        string? UomCode,
        string Result,
        string? DispositionReason,
        string? NonconformanceReportId,
        IReadOnlyCollection<DownstreamInspectionRecordDetailLine>? ResultLines,
        DateTime CreatedAtUtc);

    private sealed record DownstreamInspectionRecordDetailLine(
        string CharacteristicCode,
        string ObservedValue,
        decimal? MeasuredValue,
        string? UnitCode,
        string Result,
        string? DefectReason,
        decimal? DefectQuantity);

    private sealed record DownstreamNcrListResponse(
        IReadOnlyCollection<DownstreamNcrItem> Items,
        int Total);

    private sealed record DownstreamNcrItem(
        string NcrId,
        string NcrCode,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        decimal DefectQuantity,
        string DefectReason,
        string? BatchNo,
        string? SerialNo,
        string Status,
        string? SourceInspectionRecordId = null);

    private sealed record DownstreamSubmitNcrDispositionRequest(
        string NcrId,
        string DispositionType,
        string? DispositionApprovalChainId,
        IReadOnlyCollection<string>? AttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<DownstreamMrbReview>? MrbReviews);

    private sealed record DownstreamMrbReview(
        string ReviewerId,
        string Decision,
        string? Comment,
        DateTimeOffset ReviewedAtUtc);

    private sealed record DownstreamCloseNcrRequest(
        string NcrId,
        string? ReworkWorkOrderId,
        string? ScrapMovementId,
        string? ReturnDocumentId,
        string Reason);

    private sealed record DownstreamOpenNcrFromInspectionRequest(
        string InspectionRecordId,
        string OrganizationId,
        string EnvironmentId,
        string DefectReason,
        IReadOnlyCollection<string>? AttachmentFileIds);

    private sealed record DownstreamOpenNcrFromInspectionResponse(JsonElement NcrId);

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };
}

public sealed class HttpBusinessFileStorageClient(HttpClient httpClient) : IBusinessFileStorageClient
{
    public async Task<BusinessConsoleSopFileDownloadGrantResponse> CreateSopFileDownloadGrantAsync(
        string internalBearerToken,
        string fileId,
        BusinessConsoleCreateSopFileDownloadGrantRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/files/v1/files/{Uri.EscapeDataString(fileId)}/download-grants");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        message.Content = JsonContent.Create(new CreateDownloadGrantRequest(request.OrganizationId, request.EnvironmentId));
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-grant-failed");
        }

        var grant = await response.Content.ReadFromJsonAsync<DownloadGrantResponse>(cancellationToken: cancellationToken)
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "filestorage-empty-response");
        return new BusinessConsoleSopFileDownloadGrantResponse(
            grant.FileId,
            grant.ExpiresAtUtc,
            RewriteDownloadGrantContentUrl(grant.Download.Url),
            grant.Download.Headers);
    }

    public async Task<BusinessConsoleSopFileContentResponse> DownloadSopFileContentAsync(
        string internalBearerToken,
        string downloadGrantId,
        IReadOnlyDictionary<string, string> downloadHeaders,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/v1/download-grants/{Uri.EscapeDataString(downloadGrantId)}/content");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        foreach (var (key, value) in downloadHeaders)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                message.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(response.StatusCode, "filestorage-download-content-failed");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new BusinessConsoleSopFileContentResponse(
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            response.Content.Headers.ContentLength,
            bytes);
    }

    private static string RewriteDownloadGrantContentUrl(string downloadUrl)
    {
        const string fileStoragePrefix = "/api/files/v1/download-grants/";
        const string businessConsolePrefix = "/api/business-console/v1/files/download-grants/";
        return downloadUrl.StartsWith(fileStoragePrefix, StringComparison.Ordinal)
            ? businessConsolePrefix + downloadUrl[fileStoragePrefix.Length..]
            : downloadUrl;
    }
}
public sealed class HttpBusinessProductEngineeringClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessProductEngineeringClient
{
    public Task<BusinessConsoleEngineeringEntityResponse> RegisterEngineeringDocumentAsync(
        string internalBearerToken,
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/documents",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> PublishSopDocumentAsync(
        string internalBearerToken,
        BusinessConsolePublishSopDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/sops/publish",
            request,
            cancellationToken);

    public Task<BusinessConsoleCurrentSopDocumentsResponse> GetCurrentSopDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleCurrentSopDocumentsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCurrentSopDocumentsResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/sops/current?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("operationCode", request.OperationCode),
                ("workCenterCode", request.WorkCenterCode),
                ("routingCode", request.RoutingCode),
                ("routingRevision", request.RoutingRevision),
                ("asOfDate", request.AsOfDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringDocumentListResponse> ListEngineeringDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringDocumentsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringDocumentListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/documents?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("documentType", request.DocumentType),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringDocumentItem> GetEngineeringDocumentAsync(
        string internalBearerToken,
        string documentNumber,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringDocumentItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/documents/{Uri.EscapeDataString(documentNumber)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> CreateEngineeringItemRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/items",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringItemListResponse> ListEngineeringItemsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringItemsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringItemListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/items?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringItemRevisionItem> GetEngineeringItemAsync(
        string internalBearerToken,
        string itemCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringItemRevisionItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/items/{Uri.EscapeDataString(itemCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringBomRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-boms/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringBomListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("parentItemCode", request.ParentItemCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringBomItem> GetEngineeringBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringBomItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/engineering-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomExplosionResponse> GetEngineeringBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleBomExplosionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomExplosionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms/explosion?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize),
                ("bomCode", request.BomCode),
                ("revision", request.Revision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomWhereUsedResponse> GetEngineeringBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomWhereUsedResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms/where-used?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("componentCode", request.ComponentCode),
                ("effectiveDate", request.EffectiveDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomDiffResponse> GetBomDiffAsync(
        string internalBearerToken,
        BusinessConsoleBomDiffRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomDiffResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/boms/diff?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("bomKind", request.BomKind),
                ("fromBomCode", request.FromBomCode),
                ("fromRevision", request.FromRevision),
                ("toBomCode", request.ToBomCode),
                ("toRevision", request.ToRevision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleManufacturingBomListResponse> ListManufacturingBomsAsync(
        string internalBearerToken,
        BusinessConsoleListManufacturingBomsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleManufacturingBomListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleManufacturingBomItem> GetManufacturingBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleManufacturingBomItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/manufacturing-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomExplosionResponse> GetManufacturingBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleManufacturingBomExplosionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomExplosionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms/explosion?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize),
                ("bomCode", request.BomCode),
                ("revision", request.Revision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomWhereUsedResponse> GetManufacturingBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomWhereUsedResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms/where-used?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("componentCode", request.ComponentCode),
                ("effectiveDate", request.EffectiveDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseManufacturingBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseManufacturingBomRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/manufacturing-boms/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRoutingListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/routings?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleRoutingItem> GetRoutingAsync(
        string internalBearerToken,
        string routingCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRoutingItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/routings/{Uri.EscapeDataString(routingCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseRoutingAsync(
        string internalBearerToken,
        BusinessConsoleReleaseRoutingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/routings/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationListResponse> ListStandardOperationsAsync(
        string internalBearerToken,
        BusinessConsoleListStandardOperationsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/standard-operations?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationItem> GetStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationResponse> CreateStandardOperationAsync(
        string internalBearerToken,
        BusinessConsoleCreateStandardOperationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/standard-operations",
            request,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationResponse> UpdateStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleUpdateStandardOperationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}",
            request with { OperationCode = operationCode },
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> ArchiveStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleArchiveStandardOperationRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}/archive",
            request with { OperationCode = operationCode },
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> CancelScheduledEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleCancelScheduledEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/cancel-scheduled",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> RescheduleEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleRescheduleEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/reschedule",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeImpactPreviewResponse> PreviewEngineeringChangeImpactAsync(
        string internalBearerToken,
        BusinessConsoleEngineeringChangeImpactPreviewRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeImpactPreviewResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/impact-preview",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeListResponse> ListEngineeringChangesAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringChangesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-changes?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeItem> GetEngineeringChangeAsync(
        string internalBearerToken,
        string changeNumber,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/engineering-changes/{Uri.EscapeDataString(changeNumber)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductionVersionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResolveProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions/resolve?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateProductionVersionResponse> CreateProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/production-versions",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateProductionVersionResponse> UpdateProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleUpdateProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/engineering/production-versions/{Uri.EscapeDataString(productionVersionId)}",
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> ArchiveProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleArchiveProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/engineering/production-versions/{Uri.EscapeDataString(productionVersionId)}/archive",
            new DownstreamArchiveProductionVersionRequest(request.OrganizationId, request.EnvironmentId, productionVersionId, request.Reason),
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private sealed record DownstreamArchiveProductionVersionRequest(
        string OrganizationId,
        string EnvironmentId,
        string ProductionVersionId,
        string Reason);
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
            request.ForecastReference,
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
        var inputDegradationSources = response.InputDegradationSources ?? [];
        var inputSources = response.InputSources ?? [];
        return new BusinessConsoleRunMrpResponse(
            response.RunId,
            response.SuggestionCount,
            response.HasInputDegradation,
            inputDegradationSources,
            inputSources,
            response.InputCoverageStart,
            response.InputCoverageEnd);
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
            x.InputCoverageEnd)).ToArray());
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

    private sealed record DownstreamCreateOrUpdateForecastInputResponse(string ForecastInputId);

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
        int SuggestionCount,
        bool HasInputDegradation,
        IReadOnlyCollection<string>? InputDegradationSources,
        IReadOnlyCollection<string>? InputSources,
        DateOnly? InputCoverageStart,
        DateOnly? InputCoverageEnd);

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
        DateOnly? InputCoverageEnd);

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

    private sealed record SchedulingProblemRequest(SchedulingProblemContract Problem);

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));
}

public sealed class HttpBusinessIndustrialTelemetryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessIndustrialTelemetryClient
{
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
                ("take", request.Take)),
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
                request.DeviceAssetIds),
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
        return new BusinessConsoleAlarmLifecycleResponse(FormatJsonScalar(response.AlarmEventId));
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
        return new BusinessConsoleAlarmLifecycleResponse(FormatJsonScalar(response.AlarmEventId));
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
        return new BusinessConsoleAlarmLifecycleResponse(FormatJsonScalar(response.AlarmEventId));
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
        return new BusinessConsoleCreateMaintenanceWorkOrderResponse(FormatJsonScalar(response.WorkOrderId));
    }

    public async Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}/complete",
            new DownstreamCompleteMaintenanceWorkOrderRequest(
                workOrderId,
                request.Result,
                request.DowntimeReasonCode,
                request.DowntimeMinutes,
                request.SpareParts,
                request.ActualLaborMinutes,
                request.SparePartCostAmount,
                request.ExternalServiceCostAmount,
                request.CostCurrencyCode,
                request.ActualTechnicianUserId),
            cancellationToken);
        return new BusinessConsoleCompleteMaintenanceWorkOrderResponse(true);
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var workOrders = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/work-orders?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("deviceAssetIds", request.DeviceAssetIds)),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceWorkOrderListResponse(workOrders.Items.Select(workOrder =>
            new BusinessConsoleMaintenanceWorkOrderItem(
                FormatJsonScalar(workOrder.WorkOrderId),
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
                ActualTechnicianUserId: workOrder.ActualTechnicianUserId)).ToArray(),
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
        var workOrders = await ListWorkOrdersAsync(internalBearerToken, new BusinessConsoleMaintenanceWorkOrderListRequest(request.OrganizationId, request.EnvironmentId), cancellationToken);
        return workOrders.Items.SingleOrDefault(x => string.Equals(x.WorkOrderId, workOrderId, StringComparison.Ordinal))
            ?? throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.NotFound,
                "maintenance-work-order-not-found");
    }

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
                FormatJsonScalar(sparePart.WorkOrderId),
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

    private static string? FormatOptionalJsonScalar(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : FormatJsonScalar(value.Value);

    private sealed record DownstreamMaintenancePagedResponse<T>(IReadOnlyCollection<T> Items, int Skip, int Take, int Total);

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
        string? ActualTechnicianUserId = null);

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

    private sealed record DownstreamCreateMaintenanceWorkOrderResponse(JsonElement WorkOrderId);

    private sealed record DownstreamCompleteMaintenanceWorkOrderRequest(
        string WorkOrderId,
        string Result,
        string DowntimeReasonCode,
        int DowntimeMinutes,
        IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput> SpareParts,
        int? ActualLaborMinutes = null,
        decimal? SparePartCostAmount = null,
        decimal? ExternalServiceCostAmount = null,
        string? CostCurrencyCode = null,
        string? ActualTechnicianUserId = null);

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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/production-plans/{Uri.EscapeDataString(productionPlanId)}/work-orders",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderListRequest request,
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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> HoldWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/hold",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> CancelWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesWorkOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/cancel",
            request,
            cancellationToken);

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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/quality-holds/{Uri.EscapeDataString(sourceDocumentId)}/force-release",
            new DownstreamForceReleaseQualityHoldRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Reason,
                request.SourceService,
                request.ReleasedAtUtc),
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

    public Task<BusinessConsoleMesCreateReceiptResponse> RetryFinishedGoodsReceiptInventoryPostingAsync(
        string internalBearerToken,
        string requestNo,
        BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesCreateReceiptResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/finished-goods-receipt-requests/{Uri.EscapeDataString(requestNo)}/inventory-posting/retry",
            request,
            cancellationToken);

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

    public Task<BusinessConsoleAcceptedResponse> CreateMaterialIssueRequestAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMesCreateMaterialIssueRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/work-orders/{Uri.EscapeDataString(workOrderId)}/material-issue-requests",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesMaterialIssueRequestListResponse> ListMaterialIssueRequestsAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesMaterialIssueRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/material-issue-requests?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmLineSideMaterialReceiptAsync(
        string internalBearerToken,
        string requestId,
        BusinessConsoleMesConfirmLineSideReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/material-issue-requests/{Uri.EscapeDataString(requestId)}/line-side-receipts",
            request,
            cancellationToken);

    public Task<BusinessConsoleMesDispatchTaskListResponse> ListDispatchTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesDispatchTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/dispatch-tasks?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AssignDispatchTaskAsync(
        string internalBearerToken,
        string operationTaskId,
        BusinessConsoleMesAssignDispatchTaskRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/dispatch-tasks/{Uri.EscapeDataString(operationTaskId)}/assign",
            request,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

    public Task<BusinessConsoleMesOperationTaskListResponse> ListOperationTasksAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/operation-tasks?" + ListQuery(request),
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
        SendAsync<BusinessConsoleAcceptedResponse>(internalBearerToken, HttpMethod.Post,
            $"/api/business/v1/mes/telemetry-production-report-candidates/{Uri.EscapeDataString(candidateId)}/dismiss",
            new { request.OrganizationId, request.EnvironmentId, CandidateId = candidateId, request.Reason, Actor = actor }, cancellationToken);

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

    public Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRecordProductionReportResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/production-reports",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> RecordDefectAsync(
        string internalBearerToken,
        BusinessConsoleMesRecordDefectRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/defects",
            request,
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
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesReceiptRequestListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/mes/finished-goods-receipt-requests?" + ListQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMesCreateReceiptResponse> CreateFinishedGoodsReceiptRequestAsync(
        string internalBearerToken,
        BusinessConsoleMesCreateReceiptRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesCreateReceiptResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/finished-goods-receipt-requests",
            request,
            cancellationToken);

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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/downtime-events",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> ConfirmDowntimeRecoveryAsync(
        string internalBearerToken,
        string downtimeEventId,
        BusinessConsoleMesRecoverDowntimeEventRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/downtime-events/{Uri.EscapeDataString(downtimeEventId)}/recover",
            request,
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
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/mes/shift-handovers",
            request,
            cancellationToken);

    public Task<BusinessConsoleAcceptedResponse> AcceptShiftHandoverAsync(
        string internalBearerToken,
        string handoverId,
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/shift-handovers/{Uri.EscapeDataString(handoverId)}/accept",
            request,
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

    private Task<BusinessConsoleMesOperationTaskActionResponse> OperationTaskActionAsync(
        string internalBearerToken,
        string operationTaskId,
        string action,
        BusinessConsoleMesOperationTaskActionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMesOperationTaskActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/mes/operation-tasks/{Uri.EscapeDataString(operationTaskId)}/{action}",
            request,
            cancellationToken);

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

    private static string WorkOrderListQuery(BusinessConsoleMesWorkOrderListRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("status", request.Status),
            ("keyword", request.Keyword),
            ("workCenterId", request.WorkCenterId),
            ("workCenterIds", request.WorkCenterIds),
            ("shiftId", request.ShiftId),
            ("deviceAssetId", request.DeviceAssetId),
            ("deviceAssetIds", request.DeviceAssetIds),
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
