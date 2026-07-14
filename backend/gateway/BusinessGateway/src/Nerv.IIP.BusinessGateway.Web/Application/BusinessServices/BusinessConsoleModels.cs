using FastEndpoints;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleResourceItem(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion,
    string? PartnerType = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? SiteCode = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? WorkCenterCode = null,
    string? Status = null,
    string? Category = null,
    string? MaterialType = null,
    string? CodeSet = null,
    string? BaseUomCode = null,
    string? TaxId = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    string? UserId = null,
    string? SkillCode = null,
    string? SkillLevel = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? FromUomCode = null,
    string? ToUomCode = null,
    decimal? Factor = null,
    decimal? Offset = null,
    int? Precision = null,
    string? RoundingMode = null,
    string? DeviceAssetId = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null);

public sealed record BusinessConsoleResourceListResponse(
    IReadOnlyCollection<BusinessConsoleResourceItem> Resources,
    int Total,
    bool Truncated = false,
    int? Limit = null);

public sealed record BusinessConsoleListResourcesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100,
    string? CodeSet = null,
    string? ParentCode = null,
    string? SiteCode = null,
    string? LineCode = null,
    string? WorkCenterCode = null,
    string? Category = null,
    string? PartnerType = null,
    string? Keyword = null,
    bool All = false,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    string? UserId = null,
    string? SkillCode = null);

public sealed record BusinessConsoleListDeviceAssetsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100,
    string? LineCode = null,
    string? WorkCenterCode = null,
    string? Keyword = null);

public sealed record BusinessConsoleListSkusRequest(
    string OrganizationId,
    string EnvironmentId,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleListProductCategoriesRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? ParentCode = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleProductCategoryRequest(
    [property: RouteParam] string CategoryCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleCreateProductCategoryRequest(
    string OrganizationId,
    string EnvironmentId,
    string? CategoryCode,
    string CategoryName,
    string? ParentCode,
    string? Description,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleUpdateProductCategoryRequest(
    [property: RouteParam] string CategoryCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string CategoryName,
    string? ParentCode,
    string? Description);

public sealed record BusinessConsoleArchiveProductCategoryRequest(
    [property: RouteParam] string CategoryCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason = "");

public sealed record BusinessConsoleProductCategoryItem(
    string CategoryCode,
    string CategoryName,
    string? ParentCode,
    string Path,
    string? Description,
    bool Enabled,
    string SnapshotVersion);

public sealed record BusinessConsoleProductCategoryListResponse(
    IReadOnlyCollection<BusinessConsoleProductCategoryItem> Items,
    int Total);

public sealed record BusinessConsoleListSkillsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? GroupName = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleSkillRequest(
    [property: RouteParam] string SkillCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleCreateSkillRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleUpdateSkillRequest(
    [property: RouteParam] string SkillCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description);

public sealed record BusinessConsoleArchiveSkillRequest(
    [property: RouteParam] string SkillCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason = "");

public sealed record BusinessConsoleSkillItem(
    string SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description,
    bool Enabled,
    string SnapshotVersion);

public sealed record BusinessConsoleSkillListResponse(
    IReadOnlyCollection<BusinessConsoleSkillItem> Items,
    int Total);

public sealed record BusinessConsoleListWorkshopsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool IncludeDisabled = false,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleWorkerDirectoryRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    int PageIndex = 1,
    int PageSize = 20,
    bool IncludeDisabled = false);

public sealed record BusinessConsoleWorkerDirectoryResponse(
    int PageIndex,
    int PageSize,
    int TotalCount,
    IReadOnlyList<BusinessConsoleWorkerDirectoryItem> Items);

public sealed record BusinessConsoleWorkerDirectoryItem(
    string UserId,
    string DisplayName,
    string? EmployeeNo,
    string? Department,
    string Status,
    string? Email);

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
    string? Code,
    string PartnerType,
    string Name,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateUnitOfMeasureRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DimensionType,
    int Precision,
    string RoundingMode,
    string? IdempotencyKey = null);

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
    string? Code,
    string Name,
    string Timezone,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateProductionLineRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? WorkshopCode = null,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateWorkCenterRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    int CapacityMinutesPerDay,
    string ResourceType,
    string PlantCode,
    string LineCode,
    string DefaultCalendarCode,
    string CapacityUnit,
    bool FiniteCapacity,
    string? WorkshopCode = null,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateWorkshopRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? ManagerUserId,
    string? Description,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleAddTeamMemberRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    bool IsLeader,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record BusinessConsoleListTeamMembersRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    bool IncludeDisabled = false);

public sealed record BusinessConsoleRemoveTeamMemberRequest(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    string Reason = "");

public sealed record BusinessConsoleTeamMemberItem(
    string TeamCode,
    string UserId,
    bool IsLeader,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool Active,
    string SnapshotVersion);

public sealed record BusinessConsoleTeamMemberListResponse(
    IReadOnlyCollection<BusinessConsoleTeamMemberItem> Members,
    int Total);

public sealed record BusinessConsoleDeviceAssetComponent(
    string ComponentCode,
    string ComponentName,
    decimal Quantity,
    bool Critical);

public sealed record BusinessConsoleRegisterDeviceAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
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
    IReadOnlyDictionary<string, string>? ExternalReferences,
    string? IdempotencyKey = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? SiteCode = null,
    string? WorkshopCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    IReadOnlyCollection<BusinessConsoleDeviceAssetComponent>? Components = null);

public sealed record BusinessConsoleCreateShiftRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int PaidMinutes,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateWorkCalendarRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleWorkCalendarWorkingTime(
    DayOfWeek DayOfWeek);

public sealed record BusinessConsoleWorkCalendarHoliday(
    DateOnly Date,
    string Name);

public sealed record BusinessConsoleWorkCalendarException(
    DateOnly Date,
    bool IsWorkingDay,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string? Reason);

public sealed record BusinessConsoleCreateTeamRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string DepartmentCode,
    string ShiftCode,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCreateDepartmentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? ParentDepartmentCode,
    string? IdempotencyKey = null);

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

public sealed record BusinessConsoleCodeRuleContextRequest(string OrganizationId, string EnvironmentId);

public sealed record BusinessConsoleCodeRuleRequest(string OrganizationId, string EnvironmentId, string RuleKey);

public sealed record BusinessConsoleCreateCodeRuleVersionRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    string DisplayName,
    string AppliesTo,
    ScopeDimension Scope,
    IReadOnlyList<CodeRuleSegment> Segments,
    bool IsActive,
    DateTimeOffset? EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason);

public sealed record BusinessConsolePreviewCodeRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    IReadOnlyList<CodeRuleSegment> Segments,
    IReadOnlyDictionary<string, string>? Fields,
    string SiteCode = "");

public sealed record BusinessConsoleCodeRuleItem(
    string RuleKey,
    string DisplayName,
    string AppliesTo,
    ScopeDimension Scope,
    IReadOnlyList<CodeRuleSegment> Segments,
    bool IsActive,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BusinessConsoleCodeRuleVersionItem(
    string RuleKey,
    int Version,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason,
    DateTimeOffset CreatedAtUtc);

public sealed record BusinessConsoleCodeRuleListResponse(IReadOnlyCollection<BusinessConsoleCodeRuleItem> Rules);

public sealed record BusinessConsoleCodeRuleDetailResponse(
    BusinessConsoleCodeRuleItem Rule,
    IReadOnlyCollection<BusinessConsoleCodeRuleVersionItem> Versions);

public sealed record BusinessConsoleCodeRuleVersionResponse(
    string RuleKey,
    int Version,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason);

public sealed record BusinessConsoleCodeRulePreviewResponse(string RuleKey, string SampleCode);

public sealed record BusinessConsoleMasterDataResourceRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    DateOnly? EffectiveFrom = null);

public sealed record BusinessConsoleUpdateMasterDataResourceRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    string? Name = null,
    string? BaseUomCode = null,
    string? Category = null,
    string? MaterialType = null,
    string? BatchTrackingPolicy = null,
    string? SerialTrackingPolicy = null,
    string? ShelfLifePolicyCode = null,
    string? StorageConditionCode = null,
    string? DefaultBarcodeRuleCode = null,
    bool? QualityRequired = null,
    string? PartnerType = null,
    string? Timezone = null,
    string? SiteCode = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    int? PaidMinutes = null,
    string? ManagerUserId = null,
    string? Description = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? ResourceKind = null,
    string? DefaultCalendarCode = null,
    string? CapacityUnit = null,
    bool? FiniteCapacity = null,
    string? WorkCenterCode = null,
    string? AssetClassCode = null,
    string? Model = null,
    string? Manufacturer = null,
    string? SerialNo = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    IReadOnlyCollection<BusinessConsoleDeviceAssetComponent>? Components = null,
    decimal? MinimumCapacity = null,
    decimal? MaximumCapacity = null,
    string? CapacityUomCode = null,
    string? Criticality = null,
    bool? Maintainable = null,
    bool? TelemetryEnabled = null,
    string? DimensionType = null,
    int? Precision = null,
    string? RoundingMode = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? TaxId = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarWorkingTime>? WorkingTimes = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarHoliday>? Holidays = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarException>? Exceptions = null,
    decimal? Factor = null,
    decimal? Offset = null,
    DateOnly? EffectiveFrom = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null,
    bool ClearCreditLimit = false);

public sealed record BusinessConsoleSetMasterDataResourceEnabledRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    string Code,
    string? CodeSet = null,
    string Reason = "",
    DateOnly? EffectiveFrom = null);

public sealed record BusinessConsoleMasterDataResourceDetail(
    string ResourceType,
    string Code,
    string DisplayName,
    bool Active,
    string SnapshotVersion,
    string OrganizationId,
    string EnvironmentId,
    string? Name = null,
    string? BaseUomCode = null,
    string? InventoryUomCode = null,
    string? PurchaseUomCode = null,
    string? SalesUomCode = null,
    string? ManufacturingUomCode = null,
    string? Category = null,
    string? MaterialType = null,
    string? BatchTrackingPolicy = null,
    string? SerialTrackingPolicy = null,
    string? ShelfLifePolicyCode = null,
    string? StorageConditionCode = null,
    string? DefaultBarcodeRuleCode = null,
    bool? QualityRequired = null,
    string? PartnerType = null,
    IReadOnlyCollection<string>? PartnerRoles = null,
    string? Timezone = null,
    string? SiteCode = null,
    string? ParentDepartmentCode = null,
    string? DepartmentCode = null,
    string? ShiftCode = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    int? PaidMinutes = null,
    string? PlantCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    int? CapacityMinutesPerDay = null,
    string? ResourceKind = null,
    string? DefaultCalendarCode = null,
    string? CapacityUnit = null,
    bool? FiniteCapacity = null,
    string? WorkCenterCode = null,
    string? AssetClassCode = null,
    string? Model = null,
    string? Manufacturer = null,
    string? SerialNo = null,
    DateOnly? PurchaseDate = null,
    decimal? PurchaseCost = null,
    string? PurchaseCurrencyCode = null,
    DateOnly? WarrantyExpiresOn = null,
    string? SupplierPartnerCode = null,
    string? StationCode = null,
    string? ParentDeviceId = null,
    DateOnly? RetiredOn = null,
    bool? Retired = null,
    IReadOnlyCollection<BusinessConsoleDeviceAssetComponent>? Components = null,
    decimal? MinimumCapacity = null,
    decimal? MaximumCapacity = null,
    string? CapacityUomCode = null,
    string? Criticality = null,
    bool? Maintainable = null,
    bool? TelemetryEnabled = null,
    string? CodeSet = null,
    string? DimensionType = null,
    int? Precision = null,
    string? RoundingMode = null,
    string? TaxId = null,
    string? Status = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarWorkingTime>? WorkingTimes = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarHoliday>? Holidays = null,
    IReadOnlyCollection<BusinessConsoleWorkCalendarException>? Exceptions = null,
    string? FromUomCode = null,
    string? ToUomCode = null,
    decimal? Factor = null,
    decimal? Offset = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? UserId = null,
    string? SkillCode = null,
    string? SkillLevel = null,
    decimal? CreditLimit = null,
    string? CreditCurrencyCode = null);

public sealed record BusinessConsolePersonnelSkillMatrixRequest(
    string OrganizationId,
    string EnvironmentId,
    string? UserId = null,
    string? SkillCode = null,
    bool IncludeDisabled = false);

public sealed record BusinessConsolePersonnelSkillMatrixCell(
    string SkillCode,
    string Level,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

public sealed record BusinessConsolePersonnelSkillMatrixRow(
    string UserId,
    IReadOnlyCollection<BusinessConsolePersonnelSkillMatrixCell> Skills);

public sealed record BusinessConsolePersonnelSkillMatrixResponse(
    IReadOnlyCollection<string> SkillCodes,
    IReadOnlyCollection<BusinessConsolePersonnelSkillMatrixRow> Rows);

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
    string ActorRef,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleApprovalTaskListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalTaskItem> Items,
    int Total);

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

public sealed record BusinessConsoleInventoryExpiryAlertsRequest(
    string OrganizationId,
    string EnvironmentId,
    string SiteCode,
    string? SkuCode,
    string? LocationCode,
    DateOnly? AsOfDate,
    int? NearExpiryThresholdDays,
    bool IncludeZeroAvailable = false);

public sealed record BusinessConsoleInventoryExpiryAlertsResponse(
    IReadOnlyCollection<BusinessConsoleInventoryExpiryAlertLineResponse> Items);

public sealed record BusinessConsoleInventoryExpiryAlertLineResponse(
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly ExpiryDate,
    int DaysUntilExpiry,
    bool IsExpired,
    bool IsNearExpiry,
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
    decimal Quantity,
    bool AllowExpiredStock = false);

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
    string? MovementId,
    decimal VarianceQuantity,
    decimal OnHandQuantity,
    string Status,
    string? ApprovalChainId);

public sealed record BusinessConsoleQualityListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
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
    IReadOnlyCollection<BusinessConsoleQualityItem> Items,
    int Total);

public sealed record BusinessConsoleQualitySpcRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125);

public sealed record BusinessConsoleQualityProcessCapabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int Take = 125,
    int SubgroupSize = 5);

public sealed record BusinessConsoleQualitySpcControlChartResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize,
    IReadOnlyCollection<BusinessConsoleQualitySpcMeasurementPoint> DataPoints,
    IReadOnlyCollection<BusinessConsoleQualitySpcSubgroup> Subgroups,
    BusinessConsoleQualitySpcControlLimits ControlLimits,
    IReadOnlyCollection<BusinessConsoleQualitySpcRuleViolation> RuleViolations);

public sealed record BusinessConsoleQualitySpcMeasurementPoint(
    string InspectionRecordId,
    string SourceDocumentId,
    DateTimeOffset MeasuredAtUtc,
    decimal MeasuredValue,
    string? UnitCode);

public sealed record BusinessConsoleQualitySpcSubgroup(
    int Index,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal Xbar,
    decimal Range);

public sealed record BusinessConsoleQualitySpcControlLimits(
    decimal CenterLine,
    decimal AverageRange,
    decimal XbarUpperControlLimit,
    decimal XbarLowerControlLimit,
    decimal RangeUpperControlLimit,
    decimal RangeLowerControlLimit,
    bool Locked,
    DateTimeOffset CalculatedAtUtc);

public sealed record BusinessConsoleQualitySpcRuleViolation(
    string Rule,
    int StartSubgroupIndex,
    int EndSubgroupIndex,
    string Message);

public sealed record BusinessConsoleQualityProcessCapabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SampleCount,
    decimal Mean,
    decimal StandardDeviation,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    decimal? Cp,
    decimal? Cpk);

public sealed record BusinessConsoleQualityReasonListRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? GroupName = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleQualityReasonRequest(
    [property: RouteParam] string ReasonCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleCreateQualityReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleUpdateQualityReasonRequest(
    [property: RouteParam] string ReasonCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition);

public sealed record BusinessConsoleArchiveQualityReasonRequest(
    [property: RouteParam] string ReasonCode,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleQualityReasonItem(
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition,
    bool Enabled,
    string SnapshotVersion);

public sealed record BusinessConsoleQualityReasonListResponse(
    IReadOnlyCollection<BusinessConsoleQualityReasonItem> Items,
    int Total);

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
    IReadOnlyCollection<string>? DispositionAttachmentFileIds,
    BusinessConsoleInspectionStockRelease? StockRelease = null);

public sealed record BusinessConsoleInspectionCharacteristicResult(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string>? AttachmentFileIds,
    decimal? MeasuredValue = null);

public sealed record BusinessConsoleInspectionStockRelease(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string SourceQualityStatus,
    string? OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleCreateInspectionRecordResponse(string InspectionRecordId);

public sealed record BusinessConsoleQualityInspectionTaskListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? SkuCode = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleQualityInspectionTaskItem(
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

public sealed record BusinessConsoleQualityInspectionTaskListResponse(
    IReadOnlyCollection<BusinessConsoleQualityInspectionTaskItem> Items,
    int Total);

public sealed record BusinessConsoleCreateInspectionRecordFromTaskRequest(
    [property: RouteParam] string InspectionTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string InspectorUserId,
    IReadOnlyCollection<BusinessConsoleInspectionCharacteristicResult>? ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string>? DispositionAttachmentFileIds);

public sealed record BusinessConsoleCreateInspectionRecordFromTaskResponse(
    string InspectionRecordId,
    string Result,
    string? NonconformanceReportId,
    string? NonconformanceReportCode);

public sealed record BusinessConsoleQualityInspectionPlanCharacteristicsRequest(
    [property: RouteParam] string InspectionPlanId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

/// <summary>
/// 按 id 取单条 NCR 详情（PDA 检验结果页「已触发 NCR」→ 打开 NCR 的互链）。代理真实详情端点，
/// org/env 随查询下传由 Quality 服务端做租户过滤（越权 id 与不存在同为 not found）。
/// </summary>
public sealed record BusinessConsoleQualityNcrDetailRequest(
    [property: RouteParam] string NcrId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

/// <summary>
/// NCR 详情（含权威的来源检验记录回链 <see cref="SourceInspectionRecordId"/>——记录 ↔ NCR 双向互查
/// 由服务端业务关系驱动，而非客户端 query 参数）。
/// </summary>
public sealed record BusinessConsoleQualityNcrDetailResponse(
    string Id,
    string Code,
    string Status,
    string? SkuCode,
    string? SourceType,
    string? SourceDocumentId,
    decimal? DefectQuantity,
    string? DefectReason,
    string? BatchNo,
    string? SerialNo,
    string? SourceInspectionRecordId);

/// <summary>
/// 按 id 取单条检验记录详情（PDA NCR 详情「来源检验记录」→ 打开记录的互链）。代理真实详情端点，
/// org/env 随查询下传由 Quality 服务端做租户过滤。
/// </summary>
public sealed record BusinessConsoleQualityInspectionRecordDetailRequest(
    [property: RouteParam] string InspectionRecordId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleInspectionRecordResultLine(
    string CharacteristicCode,
    string ObservedValue,
    decimal? MeasuredValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity);

/// <summary>检验记录详情（含权威结论、处置与回链 NCR id——记录 ↔ NCR 双向互查）。</summary>
public sealed record BusinessConsoleInspectionRecordDetailResponse(
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
    IReadOnlyCollection<BusinessConsoleInspectionRecordResultLine> ResultLines,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleInspectionPlanCharacteristicItem(
    string CharacteristicCode,
    string Name,
    string CharacteristicType,
    bool Required,
    decimal? NominalValue,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    string? UnitCode);

public sealed record BusinessConsoleQualityInspectionPlanCharacteristicListResponse(
    string InspectionPlanId,
    string? PlanCode,
    string? Category,
    string? SkuCode,
    IReadOnlyCollection<BusinessConsoleInspectionPlanCharacteristicItem> Items);

public sealed record BusinessConsoleOpenNcrFromInspectionRequest(
    [property: RouteParam] string InspectionRecordId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string DefectReason,
    IReadOnlyCollection<string>? AttachmentFileIds = null);

public sealed record BusinessConsoleOpenNcrFromInspectionResponse(string NcrId);

public sealed record BusinessConsoleNcrDispositionRequest(
    [property: RouteParam] string NcrId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string DispositionType,
    string? DispositionApprovalChainId,
    IReadOnlyCollection<string>? AttachmentFileIds,
    IReadOnlyCollection<BusinessConsoleMrbReview>? MrbReviews = null);

public sealed record BusinessConsoleMrbReview(
    string ReviewerId,
    string Decision,
    string? Comment,
    DateTimeOffset ReviewedAtUtc);

public sealed record BusinessConsoleNcrCloseRequest(
    [property: RouteParam] string NcrId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId);

public sealed record BusinessConsoleAcceptedResponse(
    bool Accepted,
    string? DownstreamService = null,
    string? DownstreamDocumentType = null,
    string? DownstreamDocumentId = null);

public sealed record BusinessConsoleEngineeringContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleListEngineeringDocumentsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode = null,
    string? DocumentType = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsolePublishSopDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string OperationCode,
    string? WorkCenterCode,
    string? RoutingCode,
    string? RoutingRevision,
    DateOnly EffectiveDate,
    string FileId,
    string FileName,
    string ContentType,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleCurrentSopDocumentsRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string? WorkCenterCode = null,
    string? RoutingCode = null,
    string? RoutingRevision = null,
    DateOnly? AsOfDate = null);

public sealed record BusinessConsoleEngineeringDocumentListResponse(
    IReadOnlyCollection<BusinessConsoleEngineeringDocumentItem> Items,
    int Total);

public sealed record BusinessConsoleEngineeringDocumentItem(
    string DocumentNumber,
    string Revision,
    string? ItemCode,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    DateTime RegisteredAtUtc,
    string? Status = null,
    string? OperationCode = null,
    string? WorkCenterCode = null,
    string? RoutingCode = null,
    string? RoutingRevision = null,
    DateOnly? EffectiveDate = null);

public sealed record BusinessConsoleCurrentSopDocumentsResponse(
    IReadOnlyCollection<BusinessConsoleCurrentSopDocumentItem> Items);

public sealed record BusinessConsoleCurrentSopDocumentItem(
    string DocumentNumber,
    string Revision,
    string OperationCode,
    string? WorkCenterCode,
    string? RoutingCode,
    string? RoutingRevision,
    DateOnly EffectiveDate,
    string FileId,
    string FileName,
    string ContentType,
    string Status);

public sealed record BusinessConsoleCreateSopFileDownloadGrantRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleSopFileDownloadGrantResponse(
    string FileId,
    DateTimeOffset ExpiresAtUtc,
    string DownloadUrl,
    IReadOnlyDictionary<string, string> DownloadHeaders);

public sealed record BusinessConsoleSopFileContentResponse(
    string ContentType,
    long? ContentLength,
    byte[] Content);

public sealed record BusinessConsoleListEngineeringBomsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ParentItemCode = null,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleEngineeringBomListResponse(
    IReadOnlyCollection<BusinessConsoleEngineeringBomItem> Items,
    int Total);

public sealed record BusinessConsoleEngineeringBomItem(
    string BomCode,
    string Revision,
    string ParentItemCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<BusinessConsoleEngineeringBomLine> Lines);

public sealed record BusinessConsoleEngineeringBomLine(
    string ChildItemCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? ReferenceDesignators = null,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed record BusinessConsoleBomExplosionRequest(
    string OrganizationId,
    string EnvironmentId,
    string ItemCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null);

public sealed record BusinessConsoleManufacturingBomExplosionRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null);

public sealed record BusinessConsoleBomWhereUsedRequest(
    string OrganizationId,
    string EnvironmentId,
    string ComponentCode,
    DateOnly EffectiveDate);

public sealed record BusinessConsoleBomExplosionDiagnostic(
    string Code,
    string Severity,
    string ItemCode,
    string Message,
    string Path);

public sealed record BusinessConsoleBomExplosionNode(
    string ItemCode,
    string? ParentItemCode,
    string? BomCode,
    string? Revision,
    DateOnly? EffectiveDate,
    int Level,
    string Path,
    decimal LineQuantity,
    decimal RequiredQuantity,
    string UnitOfMeasureCode,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    bool Backflush = false,
    IReadOnlyCollection<BusinessConsoleBomExplosionNode>? Children = null);

public sealed record BusinessConsoleBomExplosionResponse(
    string BomKind,
    string SelectionMode,
    BusinessConsoleBomExplosionNode Root,
    IReadOnlyCollection<BusinessConsoleBomExplosionDiagnostic> Diagnostics);

public sealed record BusinessConsoleBomWhereUsedItem(
    string BomKind,
    string BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly? EffectiveDate,
    decimal LineQuantity,
    string UnitOfMeasureCode,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    bool Backflush = false);

public sealed record BusinessConsoleBomWhereUsedResponse(
    string ComponentCode,
    IReadOnlyCollection<BusinessConsoleBomWhereUsedItem> Items);

public sealed record BusinessConsoleBomDiffRequest(
    string OrganizationId,
    string EnvironmentId,
    string BomKind,
    string FromBomCode,
    string FromRevision,
    string ToBomCode,
    string ToRevision);

public sealed record BusinessConsoleBomDiffFieldChange(
    string FieldName,
    string? OldValue,
    string? NewValue);

public sealed record BusinessConsoleBomDiffLineItem(
    string ChangeType,
    string? OldItemCode,
    string? NewItemCode,
    decimal? OldQuantity,
    decimal? NewQuantity,
    string? OldUnitOfMeasureCode,
    string? NewUnitOfMeasureCode,
    decimal? OldScrapRate,
    decimal? NewScrapRate,
    decimal? OldYieldRate,
    decimal? NewYieldRate,
    string? OldAlternateGroup,
    string? NewAlternateGroup,
    string? OldSubstituteSkuCodes,
    string? NewSubstituteSkuCodes,
    IReadOnlyCollection<BusinessConsoleBomDiffFieldChange> FieldChanges);

public sealed record BusinessConsoleBomDiffSummary(int Added, int Removed, int Replaced, int Changed);

public sealed record BusinessConsoleBomDiffResponse(
    string BomKind,
    string FromVersionId,
    string ToVersionId,
    string RootItemCode,
    IReadOnlyCollection<BusinessConsoleBomDiffLineItem> Lines,
    BusinessConsoleBomDiffSummary Summary);

public sealed record BusinessConsoleRegisterEngineeringDocumentRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    string? IdempotencyKey = null,
    string? ItemCode = null);

public sealed record BusinessConsoleEngineeringEntityResponse(string Id);

public sealed record BusinessConsoleCreateEngineeringItemRevisionRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string Revision,
    string Name,
    bool Release,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleListEngineeringItemsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode = null,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleEngineeringItemListResponse(
    IReadOnlyCollection<BusinessConsoleEngineeringItemRevisionItem> Items,
    int Total);

public sealed record BusinessConsoleEngineeringItemRevisionItem(
    string ItemCode,
    string Revision,
    string Name,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

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
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleManufacturingBomListResponse(
    IReadOnlyCollection<BusinessConsoleManufacturingBomItem> Items,
    int Total);

public sealed record BusinessConsoleManufacturingBomItem(
    string BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomVersionId,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<BusinessConsoleManufacturingBomMaterialLine> MaterialLines,
    IReadOnlyCollection<BusinessConsoleRecipeLine> RecipeLines);

public sealed record BusinessConsoleManufacturingBomMaterialLine(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed record BusinessConsoleRecipeLine(
    string ParameterCode,
    string TargetValue,
    string UnitOfMeasureCode);

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
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleRoutingListResponse(
    IReadOnlyCollection<BusinessConsoleRoutingItem> Items,
    int Total);

public sealed record BusinessConsoleRoutingItem(
    string RoutingCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<BusinessConsoleRoutingOperationItem> Operations);

public sealed record BusinessConsoleRoutingOperationItem(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int StandardMinutes);

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
    string OperationCode,
    string OperationName,
    int StandardMinutes);

public sealed record BusinessConsoleListStandardOperationsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleStandardOperationListResponse(
    IReadOnlyCollection<BusinessConsoleStandardOperationItem> Items,
    int Total);

public sealed record BusinessConsoleStandardOperationItem(
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    int StandardMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BusinessConsoleCreateStandardOperationRequest(
    string OrganizationId,
    string EnvironmentId,
    string? OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleUpdateStandardOperationRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description);

public sealed record BusinessConsoleArchiveStandardOperationRequest(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string Reason);

public sealed record BusinessConsoleStandardOperationResponse(string OperationCode);

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
    string VersionId,
    string? SupersededByVersionId = null);

public sealed record BusinessConsoleCancelScheduledEngineeringChangeRequest(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    string Reason);

public sealed record BusinessConsoleRescheduleEngineeringChangeRequest(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    DateOnly EffectiveDate,
    string Reason);

public sealed record BusinessConsoleEngineeringChangeImpactPreviewRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleEngineeringChangeImpactAffectedVersionRequest> AffectedVersions);

public sealed record BusinessConsoleEngineeringChangeImpactAffectedVersionRequest(
    string VersionKind,
    string VersionId);

public sealed record BusinessConsoleEngineeringChangeImpactNode(
    string NodeType,
    string VersionId,
    string DisplayName,
    string ImpactLevel,
    string? RelatedVersionId,
    string? SkuCode,
    string? ConsoleRoute);

public sealed record BusinessConsoleEngineeringChangeImpactRisk(
    string Code,
    string Severity,
    string Message,
    string? RelatedVersionId);

public sealed record BusinessConsoleEngineeringChangeImpactPreviewResponse(
    DateOnly EffectiveDate,
    IReadOnlyCollection<BusinessConsoleEngineeringChangeImpactNode> Nodes,
    IReadOnlyCollection<BusinessConsoleEngineeringChangeImpactRisk> Risks);

public sealed record BusinessConsoleListEngineeringChangesRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleEngineeringChangeListResponse(
    IReadOnlyCollection<BusinessConsoleEngineeringChangeItem> Items,
    int Total);

public sealed record BusinessConsoleEngineeringChangeItem(
    string ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    string Status,
    DateOnly? EffectiveDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<BusinessConsoleEngineeringChangeAffectedVersionItem> AffectedVersions);

public sealed record BusinessConsoleEngineeringChangeAffectedVersionItem(
    string VersionKind,
    string VersionId,
    string? SupersededByVersionId);

public sealed record BusinessConsoleListProductionVersionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleProductionVersionListResponse(
    IReadOnlyCollection<BusinessConsoleProductionVersionItem> Items,
    int Total);

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

public sealed record BusinessConsoleMpsListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? SiteCode = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null);

public sealed record BusinessConsoleCreateMpsBucketRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity);

public sealed record BusinessConsoleUpdateMpsBucketRequest(
    [property: RouteParam] string MpsId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity);

public sealed record BusinessConsoleReviewMpsBucketRequest(
    [property: RouteParam] string MpsId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ReviewedBy);

public sealed record BusinessConsoleReleaseMpsBucketRequest(
    [property: RouteParam] string MpsId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string ReleasedBy);

public sealed record BusinessConsoleMpsBucketItem(
    string MpsId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity,
    string Status,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAtUtc = null,
    string? ReleasedBy = null,
    DateTimeOffset? ReleasedAtUtc = null);

public sealed record BusinessConsoleMpsBucketListResponse(IReadOnlyCollection<BusinessConsoleMpsBucketItem> Items);

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

public sealed record BusinessConsolePlanningDemandCancelRequest(
    [property: RouteParam] string DemandSourceId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleForecastInputListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? SiteCode = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null);

public sealed record BusinessConsoleCreateOrUpdateForecastInputRequest(
    string OrganizationId,
    string EnvironmentId,
    string ForecastReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    int BackwardConsumptionDays = 0,
    int ForwardConsumptionDays = 0);

public sealed record BusinessConsoleForecastInputItem(
    string ForecastInputId,
    string ForecastReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    int BackwardConsumptionDays,
    int ForwardConsumptionDays);

public sealed record BusinessConsoleForecastInputListResponse(IReadOnlyCollection<BusinessConsoleForecastInputItem> Items);

public sealed record BusinessConsoleRunMrpRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd);

public sealed record BusinessConsoleRunMrpResponse(
    string RunId,
    int SuggestionCount,
    bool HasInputDegradation,
    IReadOnlyCollection<string> InputDegradationSources,
    IReadOnlyCollection<string> InputSources,
    DateOnly? InputCoverageStart,
    DateOnly? InputCoverageEnd);

public sealed record BusinessConsoleMrpRunItem(
    string RunId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    string Status,
    int DemandCount,
    int AvailabilityCount,
    int SuggestionCount,
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    bool HasInputDegradation,
    IReadOnlyCollection<string> InputDegradationSources,
    IReadOnlyCollection<string> InputSources,
    DateOnly? InputCoverageStart,
    DateOnly? InputCoverageEnd);

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
    string? RoutingReference,
    string SourceType = "unknown",
    decimal GrossDemandQuantity = 0m);

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
    string ReasonCode,
    BusinessConsoleNetRequirementExplanation? NetRequirementExplanation = null,
    string? DownstreamService = null,
    string? DownstreamDocumentType = null,
    string? DownstreamDocumentId = null);

public sealed record BusinessConsoleNetRequirementExplanation(
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
    IReadOnlyCollection<string> UomConversions,
    IReadOnlyCollection<string> DegradationSources);

public sealed record BusinessConsolePlanningSuggestionListResponse(IReadOnlyCollection<BusinessConsolePlanningSuggestionItem> Items);

public sealed record BusinessConsoleAcceptPlanningSuggestionRequest(
    [property: RouteParam] string SuggestionId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId,
    string? IdempotencyKey = null);

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

public sealed record BusinessConsoleRecordTelemetrySampleRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int SampleCount,
    decimal MinValue,
    decimal MaxValue,
    decimal AverageValue,
    string SourceSequence,
    string SourceSystem,
    string SourceConnector,
    string? DeviceState = null,
    DateTimeOffset? StateOccurredAtUtc = null,
    decimal? FirstValue = null,
    decimal? LastValue = null);

public sealed record BusinessConsoleRecordTelemetrySampleResponse(
    string? TelemetrySummaryId,
    string? DeviceStateSnapshotId);

public sealed record BusinessConsolePostTelemetryAlarmRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId,
    DateTimeOffset? ClearedAtUtc = null,
    string? ClearedBy = null,
    string? ClearReason = null);

public sealed record BusinessConsolePostTelemetryAlarmResponse(string AlarmEventId);

public sealed record BusinessConsoleErpContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleErpListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

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

public sealed record BusinessConsoleCreateErpPurchaseRequisitionResponse(
    string PurchaseRequisitionId,
    string? RequisitionNo = null);

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

public sealed record BusinessConsoleConvertErpPurchaseRequisitionsRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> PurchaseRequisitionNos,
    string? PurchaseOrderNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<string>? RfqSupplierCodes = null,
    string? RfqNo = null,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY");

public sealed record BusinessConsoleConvertErpPurchaseRequisitionsResponse(
    string Status,
    string? PurchaseOrderId = null,
    string? PurchaseOrderNo = null,
    string? RfqNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<BusinessConsoleConvertedErpPurchaseOrderLine>? Lines = null);

public sealed record BusinessConsoleConvertedErpPurchaseOrderLine(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate,
    IReadOnlyCollection<BusinessConsoleConvertedErpPurchaseOrderLineSource> Sources);

public sealed record BusinessConsoleConvertedErpPurchaseOrderLineSource(
    string PurchaseRequisitionNo,
    string PurchaseRequisitionLineNo,
    decimal Quantity);

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

public sealed record BusinessConsoleErpRequestForQuotationListResponse(
    IReadOnlyCollection<BusinessConsoleErpRequestForQuotationItem> Items,
    int Total);

public sealed record BusinessConsoleErpRequestForQuotationItem(
    string RfqNo,
    string Status,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<BusinessConsoleErpRequestForQuotationLineItem> Lines,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpRequestForQuotationLineItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string SiteCode,
    DateOnly RequiredDate);

public sealed record BusinessConsoleErpPurchaseRequisitionListResponse(
    IReadOnlyCollection<BusinessConsoleErpPurchaseRequisitionItem> Items,
    int Total);

public sealed record BusinessConsoleErpPurchaseRequisitionItem(
    string PurchaseRequisitionId,
    string RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string Status,
    DateTime CreatedAtUtc,
    string? ConvertedPurchaseOrderNo = null,
    DateTime? ConvertedAtUtc = null);

public sealed record BusinessConsoleErpPurchaseOrderListResponse(
    IReadOnlyCollection<BusinessConsoleErpPurchaseOrderItem> Items,
    int Total);

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
    decimal OpenQuantity,
    bool FinalDelivery,
    decimal UnitPrice,
    DateOnly PromisedDate,
    IReadOnlyCollection<BusinessConsoleErpPurchaseOrderLineSourceItem>? Sources = null);

public sealed record BusinessConsoleErpPurchaseOrderLineSourceItem(
    string PurchaseRequisitionNo,
    string PurchaseRequisitionLineNo,
    decimal Quantity);

public sealed record BusinessConsoleErpSalesOrderListResponse(
    IReadOnlyCollection<BusinessConsoleErpSalesOrderItem> Items,
    int Total);

public sealed record BusinessConsoleErpSalesOrderItem(
    string SalesOrderNo,
    string CustomerCode,
    string Status,
    decimal TotalAmount);

public sealed record BusinessConsoleErpOpportunityListResponse(
    IReadOnlyCollection<BusinessConsoleErpOpportunityItem> Items,
    int Total);

public sealed record BusinessConsoleErpOpportunityItem(
    string OpportunityNo,
    string CustomerCode,
    string Topic,
    string Status,
    DateTime OpenedAtUtc);

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

public sealed record BusinessConsoleErpQuotationListResponse(
    IReadOnlyCollection<BusinessConsoleErpQuotationItem> Items,
    int Total);

public sealed record BusinessConsoleErpQuotationItem(
    string QuotationNo,
    string CustomerCode,
    DateOnly ExpiresOn,
    string Status,
    decimal TotalAmount,
    IReadOnlyCollection<BusinessConsoleErpQuotationLineItem> Lines,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpQuotationLineItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly RequiredDate);

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

public sealed record BusinessConsoleErpDeliveryOrderListResponse(
    IReadOnlyCollection<BusinessConsoleErpDeliveryOrderItem> Items,
    int Total);

public sealed record BusinessConsoleErpDeliveryOrderItem(
    string DeliveryOrderNo,
    string SalesOrderNo,
    string CustomerCode,
    string Status,
    IReadOnlyCollection<BusinessConsoleErpDeliveryOrderLineItem> Lines,
    DateTime ReleasedAtUtc);

public sealed record BusinessConsoleErpDeliveryOrderLineItem(
    string SalesOrderLineNo,
    decimal Quantity);

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

public sealed record BusinessConsoleErpJournalVoucherListResponse(
    IReadOnlyCollection<BusinessConsoleErpJournalVoucherItem> Items,
    int Total);

public sealed record BusinessConsoleErpJournalVoucherItem(
    string VoucherNo,
    DateOnly PostingDate,
    string Status,
    decimal TotalDebitAmount,
    decimal TotalCreditAmount,
    IReadOnlyCollection<BusinessConsoleErpJournalVoucherLineItem> Lines,
    DateTime PostedAtUtc);

public sealed record BusinessConsoleErpJournalVoucherLineItem(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    string Memo);

public sealed record BusinessConsoleErpFinanceSummaryResponse(
    decimal OpenPayableAmount,
    decimal OpenReceivableAmount,
    decimal CostCandidateAmount,
    int PostedVoucherCount);

public sealed record BusinessConsoleErpPeriodRequest(
    string OrganizationId,
    string EnvironmentId,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate);

public sealed record BusinessConsoleOpenErpAccountingPeriodRequest(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record BusinessConsoleOpenErpAccountingPeriodResponse(string AccountingPeriodId);

public sealed record BusinessConsoleCloseErpAccountingPeriodRequest(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    string ClosedBy,
    string Reason);

public sealed record BusinessConsoleReopenErpAccountingPeriodRequest(
    string OrganizationId,
    string EnvironmentId,
    string PeriodCode,
    string ReopenedBy,
    string Reason);

public sealed record BusinessConsoleApproveErpPaymentExecutionRequest(
    string OrganizationId,
    string EnvironmentId,
    string PayableNo,
    decimal Amount,
    DateOnly PaymentDate,
    string CashAccountCode,
    string IdempotencyKey,
    string? PaymentCurrencyCode = null,
    decimal PaymentExchangeRate = 1m,
    IReadOnlyCollection<BusinessConsoleErpPaymentAllocationLine>? Allocations = null);

public sealed record BusinessConsoleErpPaymentAllocationLine(string PayableNo, decimal Amount);

public sealed record BusinessConsoleExecuteErpPaymentExecutionRequest(
    string OrganizationId,
    string EnvironmentId,
    string PaymentExecutionNo,
    string ExecutedBy = "system:business-gateway");

public sealed record BusinessConsoleRegisterErpCashReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReceivableNo,
    decimal Amount,
    DateOnly CollectionDate,
    string CashAccountCode,
    string IdempotencyKey);

public sealed record BusinessConsoleMatchErpCashReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string CashReceiptNo);

public sealed record BusinessConsoleErpTrialBalanceResponse(
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal TotalLocalDebitAmount,
    decimal TotalLocalCreditAmount,
    bool IsBalanced,
    IReadOnlyCollection<BusinessConsoleErpTrialBalanceLine> Lines);

public sealed record BusinessConsoleErpTrialBalanceLine(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal LocalDebitAmount,
    decimal LocalCreditAmount,
    decimal LocalBalanceAmount);

public sealed record BusinessConsoleErpMonthEndChecklistResponse(
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    int UnpostedDocumentCount,
    int UnmatchedSupplierInvoiceCount,
    decimal GrIrLocalBalance,
    int PostedVoucherCount);

public sealed record BusinessConsoleErpPayableListResponse(
    IReadOnlyCollection<BusinessConsoleErpPayableItem> Items,
    int Total);

public sealed record BusinessConsoleErpPayableItem(
    string PayableNo,
    string SourceDocumentNo,
    string SupplierCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpReceivableListResponse(
    IReadOnlyCollection<BusinessConsoleErpReceivableItem> Items,
    int Total);

public sealed record BusinessConsoleErpReceivableItem(
    string ReceivableNo,
    string SourceDocumentNo,
    string CustomerCode,
    decimal Amount,
    decimal OpenAmount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

public sealed record BusinessConsoleErpCostCandidateListResponse(
    IReadOnlyCollection<BusinessConsoleErpCostCandidateItem> Items,
    int Total);

public sealed record BusinessConsoleErpCostCandidateItem(
    string CandidateNo,
    string SourceType,
    string SourceDocumentNo,
    decimal Amount,
    string CurrencyCode,
    string Status,
    DateTime CreatedAtUtc);

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
    bool? IsActive = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleApprovalTemplateListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalTemplateItem> Items,
    int Total);

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
    int? DueInHours,
    string? CompletionPolicy = null,
    string? ConditionExpression = null,
    BusinessConsoleApprovalRoutingCondition? Condition = null);

public sealed record BusinessConsoleApprovalRoutingCondition(
    decimal? MinimumAmount = null,
    decimal? MaximumAmount = null,
    IReadOnlyCollection<string>? DocumentTypes = null,
    IReadOnlyCollection<string>? OrganizationIds = null,
    IReadOnlyCollection<string>? DepartmentIds = null);

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
    string StartedBy,
    decimal? Amount = null,
    string? RoutingOrganizationId = null,
    string? DepartmentId = null);

public sealed record BusinessConsoleStartApprovalChainResponse(string ChainId);

public sealed record BusinessConsoleApprovalChainListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? StartedBy = null,
    string? SourceService = null,
    string? DocumentType = null,
    string? DocumentId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleApprovalChainListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalChainItem> Items,
    int Total);

public sealed record BusinessConsoleApprovalChainItem(
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
    string StartedBy,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

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

public sealed record BusinessConsoleApprovalDecisionListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChainId = null,
    string? ActorType = null,
    string? ActorRef = null,
    string? Decision = null,
    string? DocumentType = null,
    string? DocumentId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleApprovalDecisionListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalDecisionListItem> Items,
    int Total);

public sealed record BusinessConsoleApprovalDecisionListItem(
    string DecisionId,
    string ChainId,
    int StepNo,
    string ActorType,
    string ActorRef,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAtUtc,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId);

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

public sealed record BusinessConsoleApprovalDelegationListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? DelegatorActorRef = null,
    string? DelegateActorRef = null,
    string? DocumentType = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleApprovalDelegationListResponse(
    IReadOnlyCollection<BusinessConsoleApprovalDelegationItem> Items,
    int Total);

public sealed record BusinessConsoleApprovalDelegationItem(
    string DelegationId,
    string OrganizationId,
    string EnvironmentId,
    string DelegatorActorType,
    string DelegatorActorRef,
    string DelegateActorType,
    string DelegateActorRef,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string Status,
    string? Reason,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? RevokedBy,
    DateTimeOffset? RevokedAtUtc);

public sealed record BusinessConsoleCreateApprovalDelegationRequest(
    string OrganizationId,
    string EnvironmentId,
    string DelegatorActorType,
    string DelegatorActorRef,
    string DelegateActorType,
    string DelegateActorRef,
    string? DocumentType,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset EffectiveToUtc,
    string? Reason,
    string CreatedBy);

public sealed record BusinessConsoleCreateApprovalDelegationResponse(string DelegationId);

public sealed record BusinessConsoleRevokeApprovalDelegationRequest(
    [property: RouteParam] string DelegationId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string RevokedBy);

public sealed record BusinessConsoleCreateOrUpdateBarcodeRuleRequest(
    string OrganizationId,
    string EnvironmentId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status,
    int? Gs1CompanyPrefixLength = null);

public sealed record BusinessConsoleCreateOrUpdateBarcodeRuleResponse(string BarcodeRuleId);

public sealed record BusinessConsoleBarcodeRuleListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleBarcodeRuleListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodeRuleItem> Rules,
    int Total);

public sealed record BusinessConsoleBarcodeRuleItem(
    string BarcodeRuleId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    int? Gs1CompanyPrefixLength,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status);

public sealed record BusinessConsoleBarcodeTemplateListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleBarcodeTemplateListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodeTemplateItem> Templates,
    int Total);

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

public sealed record BusinessConsoleBarcodePrintBatchListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleBarcodePrintBatchListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodePrintBatchItem> PrintBatches,
    int Total);

public sealed record BusinessConsoleBarcodePrintBatchItem(
    string PrintBatchId,
    string LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int RequestedQuantity,
    string Status,
    DateTimeOffset CreatedAtUtc);

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
    string? SourceDocumentId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleBarcodeScanListResponse(
    IReadOnlyCollection<BusinessConsoleBarcodeScanRecordItem> Scans,
    int Total);

public sealed record BusinessConsoleBarcodeScanRecordItem(
    string ScanRecordId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string Result,
    string? RejectionReason,
    string DownstreamProcessingStatus,
    DateTimeOffset ScannedAtUtc);

public sealed record BusinessConsoleMesListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesWorkOrderListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    int Skip = 0,
    int Take = 100,
    string? WorkCenterIds = null,
    string? DeviceAssetIds = null);

public sealed record BusinessConsoleMesListWithoutStatusRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesProductionPlanListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Source = null,
    string? ReadinessStatus = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesWorkOrderListResponse(
    IReadOnlyCollection<BusinessConsoleMesWorkOrderItem> Items,
    int Total);

public sealed record BusinessConsoleMesWorkOrderItem(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    int Priority,
    DateTimeOffset DueUtc,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesOperationTaskItem> OperationTasks,
    string? WorkOrderNo = null,
    string? SkuCode = null);

public sealed record BusinessConsoleMesOperationTaskItem(
    string OperationTaskId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    long DurationTicks,
    DateTimeOffset? ExistingStartUtc,
    DateTimeOffset? ExistingEndUtc,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null);

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
    IReadOnlyCollection<BusinessConsoleConsumedMaterialLotInput>? ConsumedMaterialLots = null,
    decimal ReworkQuantity = 0m,
    string? ScrapReasonCode = null,
    string? DefectRecordNo = null,
    string? ProducedLotNo = null,
    string? SerialNo = null);

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

public sealed record BusinessConsoleMesProductionPlanListResponse(
    IReadOnlyCollection<BusinessConsoleMesProductionPlanRow> Items,
    int Total);

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

public sealed record BusinessConsoleMesWorkOrderReasonRequest(
    [property: RouteParam] string WorkOrderId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    DateTimeOffset? ChangedAtUtc);

// Actor is intentionally omitted: the gateway injects the authenticated principal as the
// force-release audit actor so a caller cannot forge the releaser identity via the request body.
public sealed record BusinessConsoleMesForceReleaseQualityHoldRequest(
    [property: RouteParam] string SourceDocumentId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    string? SourceService,
    DateTimeOffset? ReleasedAtUtc);

public sealed record BusinessConsoleMesReverseProductionReportRequest(
    [property: RouteParam] string ReportNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string Reason,
    DateTimeOffset? ReversedAtUtc,
    string? IdempotencyKey);

public sealed record BusinessConsoleMesReverseProductionReportResponse(
    string ProductionReportId,
    string ReportNo,
    string OriginalReportNo);

public sealed record BusinessConsoleMesRetryFinishedGoodsReceiptInventoryPostingRequest(
    [property: RouteParam] string RequestNo,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
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
    string UomCode,
    decimal? Quantity,
    IReadOnlyCollection<string>? MaterialIds,
    string IdempotencyKey);

public sealed record BusinessConsoleMesMaterialIssueRequestListResponse(
    IReadOnlyCollection<BusinessConsoleMesMaterialIssueRequestRow> Items,
    int Total);

public sealed record BusinessConsoleMesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string UomCode,
    string? MaterialLotId,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    decimal ConsumedQuantity,
    string Status,
    string? WmsRequestId,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? MaterialCode = null);

public sealed record BusinessConsoleMesConfirmLineSideReceiptRequest(
    [property: RouteParam] string RequestId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? MaterialLotId,
    decimal? ReceivedQuantity,
    IReadOnlyCollection<string>? EvidenceFileIds,
    string IdempotencyKey);

public sealed record BusinessConsoleMesDispatchTaskListResponse(
    IReadOnlyCollection<BusinessConsoleMesDispatchTaskRow> Items,
    int Total);

public sealed record BusinessConsoleMesDispatchTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    DateTimeOffset? PlannedStartUtc,
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? OperationCode = null);

public sealed record BusinessConsoleMesAssignDispatchTaskRequest(
    [property: RouteParam] string OperationTaskId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    string IdempotencyKey);

public sealed record BusinessConsoleMesOperationTaskListResponse(
    IReadOnlyCollection<BusinessConsoleMesOperationTaskRow> Items,
    int Total);

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
    string QualityStatus,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? OperationCode = null);

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

public sealed record BusinessConsoleMesWipSummaryResponse(
    IReadOnlyCollection<BusinessConsoleMesWipSummaryRow> Items,
    int Total);

public sealed record BusinessConsoleMesWipSummaryRow(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string Status,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null);

public sealed record BusinessConsoleMesProductionReportListResponse(
    IReadOnlyCollection<BusinessConsoleMesProductionReportRow> Items,
    int Total);

public sealed record BusinessConsoleMesTelemetryCandidateListRequest(string OrganizationId, string EnvironmentId, string? Status = null,
    string? WorkCenterId = null, string? DeviceAssetId = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, int Skip = 0, int Take = 50);
public sealed record BusinessConsoleMesTelemetryCandidateDetailRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);
public sealed record BusinessConsoleMesTelemetryCandidatePromoteRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId, string WorkOrderId, string OperationTaskId);
public sealed record BusinessConsoleMesTelemetryCandidateDismissRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId, string Reason);
public sealed record BusinessConsoleMesTelemetryCandidateListResponse(IReadOnlyCollection<BusinessConsoleMesTelemetryCandidateRow> Items, int Total);
public sealed record BusinessConsoleMesTelemetryCandidateTransition(string FromStatus, string ToStatus, string Actor, string? Reason, DateTimeOffset OccurredAtUtc);
public sealed record BusinessConsoleMesTelemetryCandidateRow(string CandidateId, string OrganizationId, string EnvironmentId, string Status,
    string ReportingMode, string DeviceAssetId, string TagKey, decimal GoodQuantity, DateTimeOffset BucketStartUtc, DateTimeOffset BucketEndUtc,
    string? WorkCenterId, string? WorkOrderId, string? OperationTaskId, string? SuspensionReason, string SourceIdempotencyKey,
    string? ResolutionReason, string? ResolvedBy, DateTimeOffset? ResolvedAtUtc, string? ProductionReportId,
    IReadOnlyCollection<BusinessConsoleMesTelemetryCandidateTransition> Transitions);

public sealed record BusinessConsoleMesProductionReportRow(
    string ProductionReportId,
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    // 冲销互链(MAN-444/#798):冲销行携带被冲销的原报工号与冲销原因,供 Console 展示负向记录标记
    // 与「原单 ⇄ 冲销单」双向高亮。MES 事实层 ProductionReportFact 已有这两个字段,facade 直接透传。
    string? ReversedReportNo = null,
    string? ReversalReason = null,
    // 报工所属工单状态,供 Console 冲销按钮分级(已关闭工单前端禁用,与后端拒绝构成双层拦截,MAN-444/#798)。
    string? WorkOrderStatus = null,
    // 冲销本报工的负向记录单号(服务端逐行反查):非空即"已冲销",供 Console 跨分页稳定判定已冲销与
    // 原单→冲销单互链,不再从当前页推断(MAN-444/#798 review)。
    string? ReversalReportNo = null);

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

public sealed record BusinessConsoleMesRelatedQualityItemListResponse(
    IReadOnlyCollection<BusinessConsoleMesRelatedQualityItemRow> Items,
    int Total);

public sealed record BusinessConsoleMesRelatedQualityItemRow(
    string QualityItemId,
    string SourceType,
    string SourceDocumentId,
    string Status,
    string? DefectCode,
    string? NcrId);

public sealed record BusinessConsoleMesReceiptRequestListResponse(
    IReadOnlyCollection<BusinessConsoleMesReceiptRequestRow> Items,
    int Total);

public sealed record BusinessConsoleMesReceiptRequestRow(
    string ReceiptRequestId,
    string RequestNo,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    decimal? UnitCost,
    string ReceiptStatus,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? SkuCode = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string? PostedInventoryMovementId = null,
    DateTimeOffset? PostedAtUtc = null);

public sealed record BusinessConsoleMesCreateReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc,
    decimal? UnitCost,
    string IdempotencyKey,
    string? ProducedLotNo = null,
    string? SerialNo = null);

public sealed record BusinessConsoleMesCreateReceiptResponse(string FinishedGoodsReceiptRequestId, string RequestNo);

public sealed record BusinessConsoleMesDowntimeEventListResponse(
    IReadOnlyCollection<BusinessConsoleMesDowntimeEventRow> Items,
    int Total);

public sealed record BusinessConsoleMesDowntimeEventRow(
    string DowntimeEventId,
    string WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? RecoveredAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null);

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

public sealed record BusinessConsoleMesShiftHandoverListResponse(
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverRow> Items,
    int Total);

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

public sealed record BusinessConsoleMesCapacityImpactListResponse(
    IReadOnlyCollection<BusinessConsoleMesCapacityImpactRow> Items,
    int Total);

public sealed record BusinessConsoleMesCapacityImpactRow(
    string ImpactId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ReasonCode,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null);
