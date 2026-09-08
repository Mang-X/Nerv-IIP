using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMasterDataClient
{
    Task<BusinessMasterDataPrincipalWorkContextResponse> GetPrincipalWorkContextAsync(
        string internalBearerToken,
        BusinessMasterDataPrincipalWorkContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMasterDataResourceDetail> GetResourceDetailAsync(
        string internalBearerToken,
        BusinessConsoleMasterDataResourceRequest request,
        CancellationToken cancellationToken);

    Task<BusinessMasterDataResolveReferencesResponse> ResolveReferencesAsync(
        string internalBearerToken,
        BusinessMasterDataResolveReferencesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMasterDataResourceDetail> UpdateResourceAsync(
        string internalBearerToken,
        BusinessConsoleUpdateMasterDataResourceRequest request,
        BusinessServiceAuditContext auditContext,
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
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateWorkerAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkerRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleWorkerDirectoryResponse> ListWorkersAsync(
        string internalBearerToken,
        BusinessConsoleWorkerDirectoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSiteAsync(
        string internalBearerToken,
        BusinessConsoleCreateSiteRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateProductionLineAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionLineRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateWorkCenterAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCenterRequest request,
        BusinessServiceAuditContext auditContext,
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
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> AddTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleAddTeamMemberRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTeamMemberListResponse> ListTeamMembersAsync(
        string internalBearerToken,
        BusinessConsoleListTeamMembersRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> RemoveTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleRemoveTeamMemberRequest request,
        BusinessServiceAuditContext auditContext,
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

    Task<BusinessConsoleToolingAssetListResponse> ListToolingAssetsAsync(
        string internalBearerToken,
        BusinessConsoleListToolingAssetsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleToolingRegistrationResponse> RegisterToolingAssetAsync(
        string internalBearerToken,
        BusinessConsoleRegisterToolingAssetRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ChangeToolingStatusAsync(
        string internalBearerToken,
        BusinessConsoleChangeToolingStatusRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> RecordToolingUsageAsync(
        string internalBearerToken,
        BusinessConsoleRecordToolingUsageRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMasterDataClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMasterDataClient
{
    private static readonly JsonSerializerOptions ToolingJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public Task<BusinessMasterDataPrincipalWorkContextResponse> GetPrincipalWorkContextAsync(
        string internalBearerToken,
        BusinessMasterDataPrincipalWorkContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessMasterDataPrincipalWorkContextResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/master-data/principals/{Uri.EscapeDataString(request.UserId)}/work-context?"
                + Query(
                    ("organizationId", request.OrganizationId),
                    ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

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
                ("skillCode", request.SkillCode),
                ("workshopCode", request.WorkshopCode),
                ("deviceAssetId", request.DeviceAssetId)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        if (response.Resources is null || response.Total < response.Resources.Count)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return response;
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

    public Task<BusinessMasterDataResolveReferencesResponse> ResolveReferencesAsync(
        string internalBearerToken,
        BusinessMasterDataResolveReferencesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessMasterDataResolveReferencesResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/references/resolve",
            request,
            cancellationToken,
            failClosedOnFailureEnvelope: true);

    public Task<BusinessConsoleMasterDataResourceDetail> UpdateResourceAsync(
        string internalBearerToken,
        BusinessConsoleUpdateMasterDataResourceRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMasterDataResourceDetail>(
            internalBearerToken,
            HttpMethod.Patch,
            ResourcePath(request.ResourceType, request.Code),
            request,
            cancellationToken,
            configureRequest: message => ConfigureAuditHeaders(message, auditContext));

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
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/workshops", request, cancellationToken, auditContext);

    public Task<BusinessConsoleResourceItem> CreateWorkerAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkerRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/workers", request, cancellationToken, auditContext);

    /// <summary>
    /// 下游员工目录行的 wire 形状：MasterData 返回的是 <c>name</c>，facade 契约是
    /// <c>displayName</c>——必须经本记录显式映射；直接用 facade 记录反序列化会让姓名
    /// 静默变 null（员工页姓名列、派工姓名快照曾因此全空）。
    /// </summary>
    private sealed record MasterDataWorkerDirectoryWireItem(
        string UserId,
        string EmployeeNo,
        string Name,
        string? DepartmentCode,
        string? DepartmentName,
        string? JobTitle,
        string EmploymentStatus,
        string? Phone,
        bool Active,
        IReadOnlyCollection<BusinessConsoleWorkerTeamItem> Teams,
        IReadOnlyCollection<BusinessConsoleWorkerSkillItem> Skills,
        string SnapshotVersion);

    private sealed record MasterDataWorkerDirectoryWireResponse(
        IReadOnlyCollection<MasterDataWorkerDirectoryWireItem> Items,
        int TotalCount,
        int PageIndex,
        int PageSize);

    public async Task<BusinessConsoleWorkerDirectoryResponse> ListWorkersAsync(
        string internalBearerToken,
        BusinessConsoleWorkerDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var wire = await SendAsync<MasterDataWorkerDirectoryWireResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/workers?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("userId", request.UserId),
                ("departmentCode", request.DepartmentCode),
                ("teamCode", request.TeamCode),
                ("workshopCode", request.WorkshopCode),
                ("workCenterCode", request.WorkCenterCode),
                ("skillCode", request.SkillCode),
                ("employmentStatus", request.EmploymentStatus),
                ("includeDisabled", request.IncludeDisabled),
                ("pageIndex", request.PageIndex),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        if (wire.Items is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return new BusinessConsoleWorkerDirectoryResponse(
            wire.PageIndex,
            wire.PageSize,
            wire.TotalCount,
            wire.Items
                .Select(x => new BusinessConsoleWorkerDirectoryItem(
                    x.UserId,
                    x.EmployeeNo,
                    x.Name,
                    x.DepartmentCode,
                    x.DepartmentName,
                    x.JobTitle,
                    x.EmploymentStatus,
                    x.Phone,
                    x.Active,
                    x.Teams,
                    x.Skills,
                    x.SnapshotVersion))
                .ToArray());
    }

    public Task<BusinessConsoleResourceItem> CreateSiteAsync(
        string internalBearerToken,
        BusinessConsoleCreateSiteRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/sites", request, cancellationToken, auditContext);

    public Task<BusinessConsoleResourceItem> CreateProductionLineAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionLineRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/production-lines", request, cancellationToken, auditContext);

    public Task<BusinessConsoleResourceItem> CreateWorkCenterAsync(
        string internalBearerToken,
        BusinessConsoleCreateWorkCenterRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/work-centers", request, cancellationToken, auditContext);

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
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(internalBearerToken, "/api/business/v1/master-data/teams", request, cancellationToken, auditContext);

    public Task<BusinessConsoleResourceItem> AddTeamMemberAsync(
        string internalBearerToken,
        BusinessConsoleAddTeamMemberRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken) =>
        CreateResourceAsync(
            internalBearerToken,
            $"/api/business/v1/master-data/teams/{Uri.EscapeDataString(request.TeamCode)}/members",
            request,
            cancellationToken,
            auditContext);

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
        BusinessServiceAuditContext auditContext,
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
            cancellationToken,
            configureRequest: message => ConfigureAuditHeaders(message, auditContext));

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

    public async Task<BusinessConsoleToolingAssetListResponse> ListToolingAssetsAsync(
        string internalBearerToken,
        BusinessConsoleListToolingAssetsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleToolingAssetListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/master-data/tooling-assets?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("status", request.Status?.ToString().ToLowerInvariant()),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            jsonOptions: ToolingJsonOptions,
            configureRequest: message => ConfigureCorrelationHeader(message, correlationId),
            failClosedOnFailureEnvelope: true);
        if (response.Items is null ||
            response.Total < 0 ||
            response.Items.Count > request.Take ||
            (response.Items.Count > 0 &&
                (long)response.Total < (long)request.Skip + response.Items.Count) ||
            response.Items.Any(item =>
                item is null ||
                string.IsNullOrWhiteSpace(item.Code) ||
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.ToolingType) ||
                item.UsageCount < 0 ||
                item.MaintenanceLifeCount is <= 0 ||
                item.WorkCenterCodes is null ||
                item.WorkCenterCodes.Any(string.IsNullOrWhiteSpace) ||
                item.SkuCodes is null ||
                item.SkuCodes.Any(string.IsNullOrWhiteSpace)))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return response;
    }

    public async Task<BusinessConsoleToolingRegistrationResponse> RegisterToolingAssetAsync(
        string internalBearerToken,
        BusinessConsoleRegisterToolingAssetRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleToolingRegistrationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/tooling-assets",
            request,
            cancellationToken,
            jsonOptions: ToolingJsonOptions,
            configureRequest: message => ConfigureAuditHeaders(message, auditContext),
            failClosedOnFailureEnvelope: true);
        if (string.IsNullOrWhiteSpace(response.ResourceType) ||
            string.IsNullOrWhiteSpace(response.Code) ||
            string.IsNullOrWhiteSpace(response.DisplayName))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return response;
    }

    public async Task<BusinessConsoleAcceptedResponse> ChangeToolingStatusAsync(
        string internalBearerToken,
        BusinessConsoleChangeToolingStatusRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        await SendNoContentAsync(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/tooling-assets/status",
            request,
            cancellationToken,
            jsonOptions: ToolingJsonOptions,
            configureRequest: message => ConfigureAuditHeaders(message, auditContext));
        return new BusinessConsoleAcceptedResponse(true);
    }

    public async Task<BusinessConsoleAcceptedResponse> RecordToolingUsageAsync(
        string internalBearerToken,
        BusinessConsoleRecordToolingUsageRequest request,
        BusinessServiceAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        await SendNoContentAsync(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/master-data/tooling-assets/usage",
            request,
            cancellationToken,
            jsonOptions: ToolingJsonOptions,
            configureRequest: message => ConfigureAuditHeaders(message, auditContext));
        return new BusinessConsoleAcceptedResponse(true);
    }

    private static void ConfigureCorrelationHeader(HttpRequestMessage message, string correlationId) =>
        message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

    private Task<BusinessConsoleResourceItem> CreateResourceAsync(
        string internalBearerToken,
        string path,
        object request,
        CancellationToken cancellationToken,
        BusinessServiceAuditContext? auditContext = null) =>
        SendAsync<BusinessConsoleResourceItem>(
            internalBearerToken,
            HttpMethod.Post,
            path,
            request,
            cancellationToken,
            configureRequest: auditContext is null
                ? null
                : message => ConfigureAuditHeaders(message, auditContext));

    private static void ConfigureAuditHeaders(
        HttpRequestMessage message,
        BusinessServiceAuditContext auditContext)
    {
        message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", auditContext.Actor);
        message.Headers.TryAddWithoutValidation("X-Correlation-Id", auditContext.CorrelationId);
        message.Headers.TryAddWithoutValidation("X-Causation-Id", auditContext.CausationId);
        if (!string.IsNullOrWhiteSpace(auditContext.IdempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("X-Idempotency-Key", auditContext.IdempotencyKey);
        }
    }

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
