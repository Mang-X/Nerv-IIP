using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleResourceItem(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion);

public sealed record BusinessConsoleResourceListResponse(
    IReadOnlyCollection<BusinessConsoleResourceItem> Resources,
    int Total);

public sealed record BusinessConsoleListResourcesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Take = 100);

public sealed record BusinessConsoleListSkusRequest(
    string OrganizationId,
    string EnvironmentId,
    bool IncludeDisabled = false,
    int Take = 100);

public sealed record BusinessConsoleCreateSkuRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string BaseUomCode,
    string Category,
    string MaterialType,
    string BatchTrackingPolicy,
    string SerialTrackingPolicy,
    string ShelfLifePolicyCode,
    string StorageConditionCode,
    string DefaultBarcodeRuleCode,
    bool QualityRequired,
    IReadOnlyCollection<string>? ComplianceTags,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateBusinessPartnerRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string PartnerType,
    string Name);

public sealed record BusinessConsoleCreateUnitOfMeasureRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode);

public sealed record BusinessConsoleCreateUomConversionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode,
    DateOnly EffectiveFrom);

public sealed record BusinessConsoleCreateSiteRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string Timezone);

public sealed record BusinessConsoleCreateProductionLineRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string SiteCode);

public sealed record BusinessConsoleCreateWorkCenterRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity);

public sealed record BusinessConsoleRegisterDeviceAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Model,
    string LineCode,
    string WorkCenterCode,
    string AssetClassCode,
    string Manufacturer,
    string SerialNo,
    decimal? MinimumCapacity,
    decimal? MaximumCapacity,
    string CapacityUomCode,
    string Criticality,
    bool Maintainable,
    bool TelemetryEnabled,
    IReadOnlyDictionary<string, string>? ExternalReferences);

public sealed record BusinessConsoleCreateShiftRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes);

public sealed record BusinessConsoleCreateWorkCalendarRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name);

public sealed record BusinessConsoleCreateTeamRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string DepartmentCode,
    string ShiftCode);

public sealed record BusinessConsoleCreateDepartmentRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string? ParentDepartmentCode);

public sealed record BusinessConsoleAssignPersonnelSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    string UserId,
    string SkillCode,
    string Level,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

public sealed record BusinessConsoleCreateReferenceDataCodeRequest(
    string OrganizationId,
    string EnvironmentId,
    string CodeSet,
    string Code,
    string Name);

public sealed record BusinessConsoleWorkbenchSummaryRequest(
    string OrganizationId,
    string EnvironmentId,
    int Take = 20);

public sealed record BusinessConsoleWorkbenchSummaryResponse(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<BusinessConsoleWorkbenchKpiItem> Kpis,
    BusinessConsoleWorkbenchTodoSummary Todos,
    BusinessConsoleWorkbenchMessageSummary Messages,
    BusinessConsoleWorkbenchAlertSummary Alerts,
    IReadOnlyCollection<BusinessConsoleWorkbenchSourceStatus> SourceStatuses);

public sealed record BusinessConsoleWorkbenchKpiItem(
    string Key,
    string Label,
    int Value,
    string Source,
    string Status);

public sealed record BusinessConsoleWorkbenchTodoSummary(
    string Status,
    int Total,
    IReadOnlyCollection<BusinessConsoleWorkbenchTodoItem> Items);

public sealed record BusinessConsoleWorkbenchTodoItem(
    string Source,
    string ItemId,
    string ItemType,
    string Status,
    string? ReferenceId,
    DateTimeOffset? DueAtUtc);

public sealed record BusinessConsoleWorkbenchMessageSummary(
    string Status,
    int Total,
    int Unread,
    IReadOnlyCollection<BusinessConsoleWorkbenchMessageItem> Items);

public sealed record BusinessConsoleWorkbenchMessageItem(
    string MessageId,
    string Status,
    string Severity,
    string? ResourceType,
    string? ResourceId,
    DateTimeOffset CreatedAtUtc);

public sealed record BusinessConsoleWorkbenchAlertSummary(
    string Status,
    int Total,
    int Critical,
    IReadOnlyCollection<BusinessConsoleWorkbenchAlertItem> Items);

public sealed record BusinessConsoleWorkbenchAlertItem(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc);

public sealed record BusinessConsoleWorkbenchSourceStatus(
    string Source,
    string Status,
    string? PermissionCode,
    string? Reason);

public sealed record BusinessConsoleApprovalTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorType,
    string ActorRef);

public sealed record BusinessConsoleApprovalTaskListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalTaskItem> Items);

public sealed record BusinessConsoleApprovalTaskItem(
    string ChainId,
    int StepNo,
    string StepName,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    DateTimeOffset? DueAtUtc);

public sealed record BusinessConsoleNotificationListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RecipientRef,
    string? Status,
    int Take = 20);

public sealed record BusinessConsoleInventoryAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleInventoryAvailabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    IReadOnlyCollection<BusinessConsoleInventoryAvailabilityLineResponse> Items);

public sealed record BusinessConsoleInventoryAvailabilityLineResponse(
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

public sealed record BusinessConsolePostStockMovementRequest(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity);

public sealed record BusinessConsolePostStockMovementResponse(
    string MovementId,
    decimal OnHandQuantity,
    decimal AvailableQuantity);

public sealed record BusinessConsoleCreateStockCountTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleCreateStockCountTaskResponse(
    string CountTaskId,
    long ExpectedLedgerVersion);

public sealed record BusinessConsoleConfirmStockCountAdjustmentRequest(
    [property: RouteParam] string CountTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    decimal CountedQuantity,
    string IdempotencyKey);

public sealed record BusinessConsoleConfirmStockCountAdjustmentResponse(
    string MovementId,
    decimal VarianceQuantity,
    decimal OnHandQuantity);

public sealed record BusinessConsoleQualityListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    int Take = 100);

public sealed record BusinessConsoleQualityItem(
    string Id,
    string Code,
    string Status,
    string? Category,
    string? SkuCode,
    string? PartnerId,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? DocumentType,
    string? SourceType,
    string? SourceDocumentId,
    decimal? DefectQuantity,
    string? DefectReason,
    string? BatchNo,
    string? SerialNo);

public sealed record BusinessConsoleQualityListResponse(
    IReadOnlyCollection<BusinessConsoleQualityItem> Items);

public sealed record BusinessConsoleCreateInspectionRecordRequest(
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
    IReadOnlyCollection<BusinessConsoleInspectionCharacteristicResult>? ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string>? DispositionAttachmentFileIds);

public sealed record BusinessConsoleInspectionCharacteristicResult(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string>? AttachmentFileIds);

public sealed record BusinessConsoleCreateInspectionRecordResponse(string InspectionRecordId);

public sealed record BusinessConsoleNcrDispositionRequest(
    [property: RouteParam] string NcrId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string DispositionType,
    string? DispositionApprovalChainId,
    IReadOnlyCollection<string>? AttachmentFileIds);

public sealed record BusinessConsoleNcrCloseRequest(
    [property: RouteParam] string NcrId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId);

public sealed record BusinessConsoleAcceptedResponse(bool Accepted);

public sealed record BusinessConsoleListEngineeringBomsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ParentItemCode = null,
    string? Status = null);

public sealed record BusinessConsoleEngineeringBomListResponse(
    IReadOnlyCollection<BusinessConsoleEngineeringBomItem> Items);

public sealed record BusinessConsoleEngineeringBomItem(
    string BomCode,
    string Revision,
    string ParentItemCode,
    string Status,
    DateOnly? EffectiveDate);

public sealed record BusinessConsoleRegisterEngineeringDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleEngineeringEntityResponse(string Id);

public sealed record BusinessConsoleCreateEngineeringItemRevisionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string Revision,
    string Name,
    bool Release,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleReleaseEngineeringBomRequest(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleBomLineRequest> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleBomLineRequest(
    string ComponentCode,
    decimal Quantity,
    string UnitOfMeasureCode);

public sealed record BusinessConsoleListManufacturingBomsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? Status = null);

public sealed record BusinessConsoleManufacturingBomListResponse(
    IReadOnlyCollection<BusinessConsoleManufacturingBomItem> Items);

public sealed record BusinessConsoleManufacturingBomItem(
    string BomCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<BusinessConsoleManufacturingBomMaterialLine> MaterialLines);

public sealed record BusinessConsoleManufacturingBomMaterialLine(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

public sealed record BusinessConsoleReleaseManufacturingBomRequest(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomCode,
    string EngineeringBomRevision,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleManufacturingBomMaterialLineRequest> MaterialLines,
    IReadOnlyCollection<BusinessConsoleRecipeLineRequest> RecipeLines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleManufacturingBomMaterialLineRequest(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

public sealed record BusinessConsoleRecipeLineRequest(
    string ParameterCode,
    string TargetValue,
    string UnitOfMeasureCode);

public sealed record BusinessConsoleListRoutingsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? Status = null);

public sealed record BusinessConsoleRoutingListResponse(
    IReadOnlyCollection<BusinessConsoleRoutingItem> Items);

public sealed record BusinessConsoleRoutingItem(
    string RoutingCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate);

public sealed record BusinessConsoleReleaseRoutingRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RoutingCode,
    string Revision,
    string SkuCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleRoutingOperationRequest> Operations,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleRoutingOperationRequest(
    int Sequence,
    string WorkCenterCode,
    string OperationName,
    int StandardMinutes);

public sealed record BusinessConsoleReleaseEngineeringChangeRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleAffectedVersionRequest> AffectedVersions,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleAffectedVersionRequest(
    string VersionKind,
    string VersionId);

public sealed record BusinessConsoleListProductionVersionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? Status = null);

public sealed record BusinessConsoleProductionVersionListResponse(
    IReadOnlyCollection<BusinessConsoleProductionVersionItem> Items);

public sealed record BusinessConsoleProductionVersionItem(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault,
    string Status);

public sealed record BusinessConsoleResolveProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize);

public sealed record BusinessConsoleResolveProductionVersionResponse(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly EffectiveDate,
    decimal LotSize,
    string Status);

public sealed record BusinessConsoleCreateProductionVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault);

public sealed record BusinessConsoleCreateProductionVersionResponse(string ProductionVersionId);

public sealed record BusinessConsoleUpdateProductionVersionRequest(
    [property: RouteParam] string ProductionVersionId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string MbomVersionId,
    string RoutingVersionId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    int Priority,
    bool IsDefault);

public sealed record BusinessConsoleArchiveProductionVersionRequest(
    [property: RouteParam] string ProductionVersionId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason);

public sealed record BusinessConsolePlanningContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleCreateOrUpdateDemandSourceRequest(
    string OrganizationId,
    string EnvironmentId,
    string DemandType,
    string? SourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleDemandSourceResponse(
    string DemandSourceId,
    string SourceReference,
    string DemandType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate);

public sealed record BusinessConsoleDemandSourceListResponse(IReadOnlyCollection<BusinessConsoleDemandSourceResponse> Items);

public sealed record BusinessConsoleRunMrpRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd);

public sealed record BusinessConsoleRunMrpResponse(string RunId, int SuggestionCount);

public sealed record BusinessConsoleMrpRunItem(
    string RunId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    string Status,
    int DemandCount,
    int AvailabilityCount,
    int SuggestionCount,
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource);

public sealed record BusinessConsoleMrpRunListResponse(IReadOnlyCollection<BusinessConsoleMrpRunItem> Items);

public sealed record BusinessConsoleMrpPeggingItem(
    string SuggestionId,
    string PeggingType,
    string DemandSourceReference,
    string ParentSkuCode,
    string? ComponentSkuCode,
    decimal Quantity,
    string? ProductionVersionReference,
    string? ManufacturingBomReference,
    string? RoutingReference);

public sealed record BusinessConsoleMrpPeggingListResponse(IReadOnlyCollection<BusinessConsoleMrpPeggingItem> Items);

public sealed record BusinessConsolePlanningSuggestionListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null);

public sealed record BusinessConsolePlanningSuggestionItem(
    string SuggestionId,
    string RunId,
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string Status,
    string ReasonCode);

public sealed record BusinessConsolePlanningSuggestionListResponse(IReadOnlyCollection<BusinessConsolePlanningSuggestionItem> Items);

public sealed record BusinessConsoleAcceptPlanningSuggestionRequest(
    [property: RouteParam] string SuggestionId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string DownstreamService,
    string DownstreamDocumentType,
    string DownstreamDocumentId);

public sealed record BusinessConsoleSchedulingContextRequest(
    string OrganizationId,
    string EnvironmentId,
    int? PageIndex = null,
    int? PageSize = null);

public sealed record BusinessConsoleSchedulingPlanRequest(
    [property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleSchedulePlanSummaryResponse(
    string PlanId,
    string ProblemId,
    Nerv.IIP.Contracts.Scheduling.SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    int AssignmentCount,
    int ConflictCount,
    int UnscheduledOperationCount);

public sealed record BusinessConsoleReleaseSchedulePlanResponse(
    string PlanId,
    Nerv.IIP.Contracts.Scheduling.SchedulePlanStatusContract Status,
    DateTimeOffset? ReleasedAtUtc);

public sealed record BusinessConsoleEquipmentContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleEquipmentOverviewRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetIds);

public sealed record BusinessConsoleEquipmentAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetIds,
    string? WorkCenterIds);

public sealed record BusinessConsoleEquipmentOverviewResponse(
    IReadOnlyCollection<BusinessConsoleEquipmentDeviceSummary> Devices,
    IReadOnlyCollection<Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeAvailabilityWindowContract> ActiveBlocks);

public sealed record BusinessConsoleEquipmentDeviceSummary(
    string DeviceAssetId,
    string? CurrentState,
    bool IsSourceFresh,
    int ActiveAlarmCount,
    int ActiveBlockCount);

public sealed record BusinessConsoleEquipmentDeviceDetailResponse(
    Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeCurrentStateResponse CurrentState,
    Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeAvailabilityResponse Availability);

public sealed record BusinessConsoleEquipmentAlarmListResponse(
    IReadOnlyCollection<Nerv.IIP.Contracts.EquipmentRuntime.EquipmentRuntimeAlarmSummary> Items);

public sealed record BusinessConsoleErpContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleErpSourceDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentNo,
    string? SourceType = null);

public sealed record BusinessConsoleCreateErpPurchaseRequisitionRequest(
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

public sealed record BusinessConsoleCreateErpPurchaseRequisitionResponse(string PurchaseRequisitionId);

public sealed record BusinessConsoleCreateErpRequestForQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? RfqNo,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<BusinessConsoleErpRfqLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpRfqLine(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    DateOnly RequiredDate);

public sealed record BusinessConsoleCreateErpRequestForQuotationResponse(string RequestForQuotationId);

public sealed record BusinessConsoleReceiveErpSupplierQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string RfqNo,
    string SupplierCode,
    IReadOnlyCollection<BusinessConsoleErpSupplierQuotationLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpSupplierQuotationLine(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public sealed record BusinessConsoleReceiveErpSupplierQuotationResponse(string SupplierQuotationId);

public sealed record BusinessConsoleCreateErpPurchaseOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyCollection<BusinessConsoleErpPurchaseOrderCommandLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpPurchaseOrderCommandLine(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public sealed record BusinessConsoleCreateErpPurchaseOrderResponse(string PurchaseOrderId);

public sealed record BusinessConsoleRecordErpPurchaseReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<BusinessConsoleErpPurchaseReceiptLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpPurchaseReceiptLine(
    string PurchaseOrderLineNo,
    decimal ReceivedQuantity);

public sealed record BusinessConsoleRecordErpPurchaseReceiptResponse(string PurchaseReceiptId);

public sealed record BusinessConsoleErpPurchaseOrderListResponse(
    IReadOnlyCollection<BusinessConsoleErpPurchaseOrderItem> Items);

public sealed record BusinessConsoleErpPurchaseOrderItem(
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    string Status,
    string ReceiptReadiness,
    decimal TotalAmount,
    IReadOnlyCollection<BusinessConsoleErpPurchaseOrderLineItem> Lines);

public sealed record BusinessConsoleErpPurchaseOrderLineItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public sealed record BusinessConsoleErpSalesOrderListResponse(
    IReadOnlyCollection<BusinessConsoleErpSalesOrderItem> Items);

public sealed record BusinessConsoleErpSalesOrderItem(
    string SalesOrderNo,
    string CustomerCode,
    string Status,
    decimal TotalAmount);

public sealed record BusinessConsoleOpenErpOpportunityRequest(
    string OrganizationId,
    string EnvironmentId,
    string? OpportunityNo,
    string CustomerCode,
    string Topic,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleOpenErpOpportunityResponse(string OpportunityId);

public sealed record BusinessConsoleCreateErpQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string CustomerCode,
    DateOnly ExpiresOn,
    IReadOnlyCollection<BusinessConsoleErpQuotationLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpQuotationLine(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly RequiredDate);

public sealed record BusinessConsoleCreateErpQuotationResponse(string QuotationId);

public sealed record BusinessConsoleApproveErpQuotationRequest(
    string OrganizationId,
    string EnvironmentId,
    string QuotationNo);

public sealed record BusinessConsoleCreateErpSalesOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SalesOrderNo,
    string QuotationNo,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateErpSalesOrderResponse(string SalesOrderId);

public sealed record BusinessConsoleReleaseErpDeliveryOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeliveryOrderNo,
    string SalesOrderNo,
    IReadOnlyCollection<BusinessConsoleErpDeliveryOrderLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpDeliveryOrderLine(
    string SalesOrderLineNo,
    decimal Quantity);

public sealed record BusinessConsoleReleaseErpDeliveryOrderResponse(string DeliveryOrderId);

public sealed record BusinessConsoleCreateErpAccountPayableRequest(
    string OrganizationId,
    string EnvironmentId,
    string? PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    string CurrencyCode,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateErpAccountPayableResponse(string AccountPayableId);

public sealed record BusinessConsoleCreateErpAccountReceivableRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    string CurrencyCode,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateErpAccountReceivableResponse(string AccountReceivableId);

public sealed record BusinessConsoleCreateErpCostCandidateRequest(
    string OrganizationId,
    string EnvironmentId,
    string? CandidateNo,
    string SourceType,
    string SourceDocumentNo,
    decimal Amount,
    string CurrencyCode,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateErpCostCandidateResponse(string CostCandidateId);

public sealed record BusinessConsolePostErpJournalVoucherRequest(
    string OrganizationId,
    string EnvironmentId,
    string? VoucherNo,
    DateOnly PostingDate,
    IReadOnlyCollection<BusinessConsoleErpJournalVoucherLine> Lines,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleErpJournalVoucherLine(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    string Memo);

public sealed record BusinessConsolePostErpJournalVoucherResponse(string JournalVoucherId);

public sealed record BusinessConsoleErpFinanceSummaryResponse(
    decimal OpenPayableAmount,
    decimal OpenReceivableAmount,
    decimal CostCandidateAmount,
    int PostedVoucherCount);

public sealed record BusinessConsoleErpPayableSourceDocumentResponse(
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpReceivableSourceDocumentResponse(
    string ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpCostCandidateSourceDocumentResponse(
    string CandidateNo,
    string SourceType,
    string SourceDocumentNo,
    decimal Amount,
    string CurrencyCode,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleApprovalTemplateListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentType = null,
    bool? IsActive = null);

public sealed record BusinessConsoleApprovalTemplateListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalTemplateItem> Items);

public sealed record BusinessConsoleApprovalTemplateItem(
    string TemplateId,
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<BusinessConsoleApprovalTemplateStepItem> Steps);

public sealed record BusinessConsoleApprovalTemplateStepItem(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours);

public sealed record BusinessConsoleCreateOrUpdateApprovalTemplateRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string DocumentType,
    int Version,
    bool IsActive,
    IReadOnlyCollection<BusinessConsoleApprovalTemplateStepItem> Steps);

public sealed record BusinessConsoleCreateOrUpdateApprovalTemplateResponse(string TemplateId);

public sealed record BusinessConsoleStartApprovalChainRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    string StartedBy);

public sealed record BusinessConsoleStartApprovalChainResponse(string ChainId);

public sealed record BusinessConsoleApprovalChainRequest(
    string OrganizationId,
    string EnvironmentId,
    string ChainId);

public sealed record BusinessConsoleApprovalChainResponse(
    string ChainId,
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    int TemplateVersion,
    string Status,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    IReadOnlyCollection<BusinessConsoleApprovalStepItem> Steps,
    IReadOnlyCollection<BusinessConsoleApprovalDecisionItem> Decisions);

public sealed record BusinessConsoleApprovalStepItem(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    string Status,
    DateTimeOffset? DueAtUtc,
    string? ResolvedDecision);

public sealed record BusinessConsoleApprovalDecisionItem(
    string DecisionId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAtUtc);

public sealed record BusinessConsoleResolveApprovalStepRequest(
    string OrganizationId,
    string EnvironmentId,
    string ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment);

public sealed record BusinessConsoleResolveApprovalStepResponse(string DecisionId);

public sealed record BusinessConsoleCreateOrUpdateBarcodeRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status);

public sealed record BusinessConsoleCreateOrUpdateBarcodeRuleResponse(string BarcodeRuleId);

public sealed record BusinessConsoleBarcodeTemplateListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null);

public sealed record BusinessConsoleBarcodeTemplateListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodeTemplateItem> Templates);

public sealed record BusinessConsoleBarcodeTemplateItem(
    string TemplateId,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string Status);

public sealed record BusinessConsoleCreateOrUpdateBarcodeTemplateRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string TemplateName,
    string TemplateFileId,
    string VariableSchemaJson,
    string Status);

public sealed record BusinessConsoleCreateOrUpdateBarcodeTemplateResponse(string TemplateId);

public sealed record BusinessConsoleCreateBarcodePrintBatchRequest(
    string OrganizationId,
    string EnvironmentId,
    string BarcodeRuleId,
    string LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string LabelValuesJson,
    int RequestedQuantity);

public sealed record BusinessConsoleCreateBarcodePrintBatchResponse(string PrintBatchId);

public sealed record BusinessConsoleBarcodePrintBatchRequest(
    string OrganizationId,
    string EnvironmentId,
    string PrintBatchId);

public sealed record BusinessConsoleBarcodePrintBatchResponse(
    BusinessConsoleBarcodePrintBatchDetail PrintBatch);

public sealed record BusinessConsoleBarcodePrintBatchDetail(
    string PrintBatchId,
    string LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int RequestedQuantity,
    string Status,
    IReadOnlyCollection<BusinessConsoleBarcodePrintItemDetail> Items);

public sealed record BusinessConsoleBarcodePrintItemDetail(
    int SequenceNo,
    string LabelValue,
    string? FileId);

public sealed record BusinessConsoleRecordBarcodeScanRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey,
    string Result,
    string? RejectionReason);

public sealed record BusinessConsoleRecordBarcodeScanResponse(string ScanRecordId);

public sealed record BusinessConsoleBarcodeScanListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceCode = null,
    string? ScannedValue = null,
    string? SourceWorkflow = null,
    string? SourceDocumentId = null);

public sealed record BusinessConsoleBarcodeScanListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodeScanRecordItem> Scans);

public sealed record BusinessConsoleBarcodeScanRecordItem(
    string ScanRecordId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string Result,
    string? RejectionReason,
    DateTimeOffset ScannedAtUtc);

public sealed record BusinessConsoleMesListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    int Take = 100);

public sealed record BusinessConsoleMesWorkOrderListResponse(
    IReadOnlyCollection<BusinessConsoleMesWorkOrderItem> Items);

public sealed record BusinessConsoleMesWorkOrderItem(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    int Priority,
    DateTimeOffset DueUtc,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesOperationTaskItem> OperationTasks);

public sealed record BusinessConsoleMesOperationTaskItem(
    string OperationTaskId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    long DurationTicks,
    DateTimeOffset? ExistingStartUtc,
    DateTimeOffset? ExistingEndUtc);

public sealed record BusinessConsoleCreateRushWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int? OperationSequence,
    int DurationMinutes,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleRunScheduleRequest(
    string OrganizationId,
    string EnvironmentId,
    string Trigger);

public sealed record BusinessConsoleMesScheduleResult(
    int ScheduleVersion,
    string Trigger,
    DateTimeOffset ScheduledAtUtc,
    IReadOnlyCollection<BusinessConsoleScheduledOperation> Assignments,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public sealed record BusinessConsoleScheduledOperation(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Reason);

public sealed record BusinessConsoleCreateRushWorkOrderResponse(
    string WorkOrderId,
    BusinessConsoleMesScheduleResult Schedule,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public sealed record BusinessConsoleRecordProductionReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc,
    string? IdempotencyKey = null,
    IReadOnlyCollection<BusinessConsoleConsumedMaterialLotInput>? ConsumedMaterialLots = null);

public sealed record BusinessConsoleConsumedMaterialLotInput(
    string MaterialId,
    string MaterialLotId,
    decimal ConsumedQuantity,
    string MaterialIssueRequestNo);

public sealed record BusinessConsoleRecordProductionReportResponse(string ProductionReportId, string ReportNo);

public sealed record BusinessConsoleMesContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleMesFoundationReadinessRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesFoundationReadinessResponse(
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessArea> Areas,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> BlockingIssues,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> WarningIssues);

public sealed record BusinessConsoleMesReadinessArea(
    string AreaCode,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> Issues);

public sealed record BusinessConsoleMesReadinessIssue(
    string Code,
    string Severity,
    string Message,
    string? SourceSystem,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceDisplayName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Version,
    string? FixHint);

public sealed record BusinessConsoleMesOverviewResponse(
    IReadOnlyCollection<BusinessConsoleMesCockpitCount> Counts,
    IReadOnlyCollection<BusinessConsoleMesBlockerSummary> Blockers,
    IReadOnlyCollection<BusinessConsoleMesPendingWorkItem> PendingWork);

public sealed record BusinessConsoleMesCockpitCount(string Key, int Count, string Status);

public sealed record BusinessConsoleMesBlockerSummary(string AreaCode, string Code, string Message, int Count);

public sealed record BusinessConsoleMesPendingWorkItem(string RoleCode, string WorkType, int Count, string? RouteHint);

public sealed record BusinessConsoleMesProductionPlanListResponse(IReadOnlyCollection<BusinessConsoleMesProductionPlanRow> Items);

public sealed record BusinessConsoleMesProductionPlanRow(
    string ProductionPlanId,
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference,
    string SkuId,
    decimal PlannedQuantity,
    string UomCode,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesProductionPlanReadinessRequest(
    [property: RouteParam] string ProductionPlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesConvertPlanToWorkOrderRequest(
    [property: RouteParam] string ProductionPlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal PlannedQuantity,
    string UomCode,
    string? WorkCenterId,
    DateTimeOffset? DueUtc,
    string? SourceSystem = null,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? SourceDemandReference = null,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleMesWorkOrderDetailRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesWorkOrderDetailResponse(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<BusinessConsoleMesOperationTaskRow> OperationTasks,
    BusinessConsoleMesSourcePlanReference? SourcePlanReference = null);

public sealed record BusinessConsoleMesSourcePlanReference(
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference);

public sealed record BusinessConsoleMesReleaseWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    bool ConfirmWarnings,
    string IdempotencyKey);

public sealed record BusinessConsoleMesMaterialReadinessRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesMaterialReadinessResponse(
    string WorkOrderId,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<BusinessConsoleMesMaterialReadinessRow> Items);

public sealed record BusinessConsoleMesMaterialReadinessRow(
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal RequestedQuantity,
    decimal StagedQuantity,
    decimal ReceivedQuantity,
    decimal ShortageQuantity,
    string Status);

public sealed record BusinessConsoleMesCreateMaterialIssueRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? OperationTaskId,
    string MaterialId,
    decimal? Quantity,
    IReadOnlyCollection<string>? MaterialIds,
    string IdempotencyKey);

public sealed record BusinessConsoleMesMaterialIssueRequestListResponse(IReadOnlyCollection<BusinessConsoleMesMaterialIssueRequestRow> Items);

public sealed record BusinessConsoleMesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string? MaterialLotId,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    string Status,
    string? WmsRequestId,
    DateTimeOffset RequestedAtUtc);

public sealed record BusinessConsoleMesConfirmLineSideReceiptRequest(
    [property: RouteParam] string RequestId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? MaterialLotId,
    decimal? ReceivedQuantity,
    IReadOnlyCollection<string>? EvidenceFileIds,
    string IdempotencyKey);

public sealed record BusinessConsoleMesDispatchTaskListResponse(IReadOnlyCollection<BusinessConsoleMesDispatchTaskRow> Items);

public sealed record BusinessConsoleMesDispatchTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    DateTimeOffset? PlannedStartUtc,
    IReadOnlyCollection<string> BlockingReasons);

public sealed record BusinessConsoleMesAssignDispatchTaskRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    string IdempotencyKey);

public sealed record BusinessConsoleMesOperationTaskListResponse(IReadOnlyCollection<BusinessConsoleMesOperationTaskRow> Items);

public sealed record BusinessConsoleMesOperationTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? StartedAtUtc,
    string QualityStatus);

public sealed record BusinessConsoleMesOperationTaskActionRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? ReasonCode,
    string IdempotencyKey);

public sealed record BusinessConsoleMesOperationTaskActionResponse(
    string OperationTaskId,
    string Status,
    DateTimeOffset ChangedAtUtc);

public sealed record BusinessConsoleMesWipSummaryResponse(IReadOnlyCollection<BusinessConsoleMesWipSummaryRow> Items);

public sealed record BusinessConsoleMesWipSummaryRow(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string Status,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyCollection<string> BlockingReasons);

public sealed record BusinessConsoleMesProductionReportListResponse(IReadOnlyCollection<BusinessConsoleMesProductionReportRow> Items);

public sealed record BusinessConsoleMesProductionReportRow(
    string ProductionReportId,
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc);

public sealed record BusinessConsoleMesRecordDefectRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    string DefectCode,
    decimal DefectQuantity,
    string? MaterialLotId,
    string? BatchOrSerial,
    string IdempotencyKey);

public sealed record BusinessConsoleMesRelatedQualityItemListResponse(IReadOnlyCollection<BusinessConsoleMesRelatedQualityItemRow> Items);

public sealed record BusinessConsoleMesRelatedQualityItemRow(
    string QualityItemId,
    string SourceType,
    string SourceDocumentId,
    string Status,
    string? DefectCode,
    string? NcrId);

public sealed record BusinessConsoleMesReceiptRequestListResponse(IReadOnlyCollection<BusinessConsoleMesReceiptRequestRow> Items);

public sealed record BusinessConsoleMesReceiptRequestRow(
    string ReceiptRequestId,
    string RequestNo,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string ReceiptStatus,
    DateTimeOffset RequestedAtUtc);

public sealed record BusinessConsoleMesCreateReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesCreateReceiptResponse(string FinishedGoodsReceiptRequestId, string RequestNo);

public sealed record BusinessConsoleMesDowntimeEventListResponse(IReadOnlyCollection<BusinessConsoleMesDowntimeEventRow> Items);

public sealed record BusinessConsoleMesDowntimeEventRow(
    string DowntimeEventId,
    string WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? RecoveredAtUtc);

public sealed record BusinessConsoleMesRecordDowntimeEventRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string ReasonCode,
    DateTimeOffset StartedAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesRecoverDowntimeEventRequest(
    [property: RouteParam] string DowntimeEventId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    DateTimeOffset RecoveredAtUtc,
    string IdempotencyKey);

public sealed record BusinessConsoleMesShiftHandoverListResponse(IReadOnlyCollection<BusinessConsoleMesShiftHandoverRow> Items);

public sealed record BusinessConsoleMesShiftHandoverRow(
    string HandoverId,
    string ShiftId,
    string TeamId,
    string HandoverStatus,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc);

public sealed record BusinessConsoleMesCreateShiftHandoverRequest(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    IReadOnlyCollection<string>? OpenIssueIds,
    string IdempotencyKey);

public sealed record BusinessConsoleMesAcceptShiftHandoverRequest(
    [property: RouteParam] string HandoverId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string IdempotencyKey);

public sealed record BusinessConsoleMesTraceabilityByWorkOrderRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityByBatchRequest(
    [property: RouteParam] string BatchOrSerial,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityByMaterialLotRequest(
    [property: RouteParam] string MaterialLotId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesTraceabilityResponse(
    IReadOnlyCollection<BusinessConsoleMesTraceabilityNode> Nodes,
    IReadOnlyCollection<BusinessConsoleMesTraceabilityEdge> Edges);

public sealed record BusinessConsoleMesTraceabilityNode(string NodeId, string NodeType, string DisplayName, string Status);

public sealed record BusinessConsoleMesTraceabilityEdge(string FromNodeId, string ToNodeId, string RelationType);

public sealed record BusinessConsoleMesCapacityImpactListResponse(IReadOnlyCollection<BusinessConsoleMesCapacityImpactRow> Items);

public sealed record BusinessConsoleMesCapacityImpactRow(
    string ImpactId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ReasonCode);
