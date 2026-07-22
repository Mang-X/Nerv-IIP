using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

public sealed class BusinessConsoleQualityReasonListRequestValidator : Validator<BusinessConsoleQualityReasonListRequest>
{
    public BusinessConsoleQualityReasonListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.GroupName).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleQualityReasonRequestValidator : Validator<BusinessConsoleQualityReasonRequest>
{
    public BusinessConsoleQualityReasonRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateQualityReasonRequestValidator : Validator<BusinessConsoleCreateQualityReasonRequest>
{
    public BusinessConsoleCreateQualityReasonRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
        RuleFor(x => x.ReasonName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity)
            .NotEmpty()
            .MaximumLength(50)
            .Must(QualityReasonCatalogValidation.IsSupportedSeverity)
            .WithMessage(QualityReasonCatalogValidation.SeverityMessage);
        RuleFor(x => x.DefaultDisposition)
            .MaximumLength(100)
            .Must(QualityReasonCatalogValidation.IsSupportedDefaultDisposition)
            .WithMessage(QualityReasonCatalogValidation.DefaultDispositionMessage);
    }
}

public sealed class BusinessConsoleUpdateQualityReasonRequestValidator : Validator<BusinessConsoleUpdateQualityReasonRequest>
{
    public BusinessConsoleUpdateQualityReasonRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity)
            .NotEmpty()
            .MaximumLength(50)
            .Must(QualityReasonCatalogValidation.IsSupportedSeverity)
            .WithMessage(QualityReasonCatalogValidation.SeverityMessage);
        RuleFor(x => x.DefaultDisposition)
            .MaximumLength(100)
            .Must(QualityReasonCatalogValidation.IsSupportedDefaultDisposition)
            .WithMessage(QualityReasonCatalogValidation.DefaultDispositionMessage);
    }
}

internal static class QualityReasonCatalogValidation
{
    private static readonly HashSet<string> Severities =
    [
        "minor",
        "major",
        "critical",
    ];

    private static readonly HashSet<string> DefaultDispositions =
    [
        "rework",
        "scrap",
        "return-to-supplier",
        "conditional-release",
    ];

    public const string SeverityMessage = "Severity must be one of: minor, major, critical.";
    public const string DefaultDispositionMessage = "DefaultDisposition must be one of: rework, scrap, return-to-supplier, conditional-release, or omitted.";

    public static bool IsSupportedSeverity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Severities.Contains(value.Trim().ToLowerInvariant());
    }

    public static bool IsSupportedDefaultDisposition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return DefaultDispositions.Contains(value.Trim().ToLowerInvariant());
    }
}

public sealed class BusinessConsoleArchiveQualityReasonRequestValidator : Validator<BusinessConsoleArchiveQualityReasonRequest>
{
    public BusinessConsoleArchiveQualityReasonRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleOpenNcrFromInspectionRequestValidator
    : Validator<BusinessConsoleOpenNcrFromInspectionRequest>
{
    public BusinessConsoleOpenNcrFromInspectionRequestValidator()
    {
        RuleFor(x => x.InspectionRecordId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefectReason).NotEmpty().MaximumLength(200);
    }
}

public sealed class BusinessConsoleQualitySpcRequestValidator : Validator<BusinessConsoleQualitySpcRequest>
{
    public BusinessConsoleQualitySpcRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SubgroupSize).InclusiveBetween(2, 10);
        RuleFor(x => x.Take).InclusiveBetween(2, 500);
    }
}

public sealed class BusinessConsoleQualityProcessCapabilityRequestValidator
    : Validator<BusinessConsoleQualityProcessCapabilityRequest>
{
    public BusinessConsoleQualityProcessCapabilityRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SubgroupSize).InclusiveBetween(2, 10);
        RuleFor(x => x.Take).InclusiveBetween(2, 500);
    }
}

public sealed class BusinessConsoleQualityInspectionTaskListRequestValidator
    : Validator<BusinessConsoleQualityInspectionTaskListRequest>
{
    public BusinessConsoleQualityInspectionTaskListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

public sealed class BusinessConsoleQualityNcrDetailRequestValidator
    : Validator<BusinessConsoleQualityNcrDetailRequest>
{
    public BusinessConsoleQualityNcrDetailRequestValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleQualityInspectionRecordDetailRequestValidator
    : Validator<BusinessConsoleQualityInspectionRecordDetailRequest>
{
    public BusinessConsoleQualityInspectionRecordDetailRequestValidator()
    {
        RuleFor(x => x.InspectionRecordId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateInspectionRecordFromTaskRequestValidator
    : Validator<BusinessConsoleCreateInspectionRecordFromTaskRequest>
{
    public BusinessConsoleCreateInspectionRecordFromTaskRequestValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InspectorUserId).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleQualityInspectionPlanCharacteristicsRequestValidator
    : Validator<BusinessConsoleQualityInspectionPlanCharacteristicsRequest>
{
    public BusinessConsoleQualityInspectionPlanCharacteristicsRequestValidator()
    {
        RuleFor(x => x.InspectionPlanId)
            .NotEmpty()
            .Must(value => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
            .WithMessage("InspectionPlanId must be a non-empty GUID.");
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateInspectionPlanRequestValidator
    : Validator<BusinessConsoleCreateInspectionPlanRequest>
{
    public BusinessConsoleCreateInspectionPlanRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.PartnerId).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(100);
        RuleFor(x => x.DocumentType).MaximumLength(100);
        RuleFor(x => x.Characteristics).NotEmpty();
        RuleForEach(x => x.Characteristics).ChildRules(characteristic =>
        {
            characteristic.RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
            characteristic.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            characteristic.RuleFor(x => x.Method).NotEmpty().MaximumLength(100);
            characteristic.RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
            characteristic.RuleFor(x => x.SamplingRule).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class BusinessConsoleActivateInspectionPlanRequestValidator
    : Validator<BusinessConsoleActivateInspectionPlanRequest>
{
    public BusinessConsoleActivateInspectionPlanRequestValidator()
    {
        RuleFor(x => x.InspectionPlanId)
            .NotEmpty()
            .Must(value => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
            .WithMessage("InspectionPlanId must be a non-empty GUID.");
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-plans")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionPlan")]
public sealed class CreateBusinessConsoleQualityInspectionPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateInspectionPlanRequest, BusinessConsoleCreateInspectionPlanResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionPlansManage)
{
    protected override string OrganizationId(BusinessConsoleCreateInspectionPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateInspectionPlanRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateInspectionPlanResponse> ForwardAsync(
        BusinessConsoleCreateInspectionPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.CreateInspectionPlanAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-plans/{inspectionPlanId}/activate")]
[BusinessGatewayOperationId("activateBusinessConsoleQualityInspectionPlan")]
public sealed class ActivateBusinessConsoleQualityInspectionPlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleActivateInspectionPlanRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionPlansManage)
{
    protected override string OrganizationId(BusinessConsoleActivateInspectionPlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleActivateInspectionPlanRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleActivateInspectionPlanRequest request) => "inspection-plan";

    protected override string? ResourceId(BusinessConsoleActivateInspectionPlanRequest request) =>
        Route<string>("inspectionPlanId") ?? request.InspectionPlanId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleActivateInspectionPlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inspectionPlanId = Route<string>("inspectionPlanId") ?? request.InspectionPlanId;
        return quality.ActivateInspectionPlanAsync(
            tokenProvider.BearerToken,
            inspectionPlanId,
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-plans")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionPlans")]
public sealed class ListBusinessConsoleQualityInspectionPlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityListRequest, BusinessConsoleQualityListResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityListResponse> ForwardAsync(
        BusinessConsoleQualityListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListInspectionPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-records")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionRecord")]
public sealed class CreateBusinessConsoleQualityInspectionRecordEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateInspectionRecordRequest, BusinessConsoleCreateInspectionRecordResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsCreate)
{
    protected override string OrganizationId(BusinessConsoleCreateInspectionRecordRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateInspectionRecordRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleCreateInspectionRecordResponse> ForwardAsync(
        BusinessConsoleCreateInspectionRecordRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.CreateInspectionRecordAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-records")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionRecords")]
public sealed class ListBusinessConsoleQualityInspectionRecordsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityListRequest, BusinessConsoleQualityListResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityListResponse> ForwardAsync(
        BusinessConsoleQualityListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListInspectionRecordsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr")]
[BusinessGatewayOperationId("openBusinessConsoleQualityNcrFromInspection")]
public sealed class OpenBusinessConsoleQualityNcrFromInspectionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleOpenNcrFromInspectionRequest, BusinessConsoleOpenNcrFromInspectionResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleOpenNcrFromInspectionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleOpenNcrFromInspectionRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleOpenNcrFromInspectionRequest request) => "inspection-record";

    protected override string? ResourceId(BusinessConsoleOpenNcrFromInspectionRequest request) =>
        Route<string>("inspectionRecordId") ?? request.InspectionRecordId;

    protected override Task<BusinessConsoleOpenNcrFromInspectionResponse> ForwardAsync(
        BusinessConsoleOpenNcrFromInspectionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inspectionRecordId = Route<string>("inspectionRecordId") ?? request.InspectionRecordId;
        return quality.OpenNcrFromInspectionAsync(
            tokenProvider.BearerToken,
            inspectionRecordId,
            request with { InspectionRecordId = inspectionRecordId },
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-tasks")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionTasks")]
public sealed class ListBusinessConsoleQualityInspectionTasksEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityInspectionTaskListRequest, BusinessConsoleQualityInspectionTaskListResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityInspectionTaskListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityInspectionTaskListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityInspectionTaskListResponse> ForwardAsync(
        BusinessConsoleQualityInspectionTaskListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListInspectionTasksAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record")]
[BusinessGatewayOperationId("createBusinessConsoleQualityInspectionRecordFromTask")]
public sealed class CreateBusinessConsoleQualityInspectionRecordFromTaskEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateInspectionRecordFromTaskRequest, BusinessConsoleCreateInspectionRecordFromTaskResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsCreate)
{
    protected override string OrganizationId(BusinessConsoleCreateInspectionRecordFromTaskRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateInspectionRecordFromTaskRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateInspectionRecordFromTaskRequest request) => "inspection-task";

    protected override string? ResourceId(BusinessConsoleCreateInspectionRecordFromTaskRequest request) =>
        Route<string>("inspectionTaskId") ?? request.InspectionTaskId;

    protected override Task<BusinessConsoleCreateInspectionRecordFromTaskResponse> ForwardAsync(
        BusinessConsoleCreateInspectionRecordFromTaskRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inspectionTaskId = Route<string>("inspectionTaskId") ?? request.InspectionTaskId;
        return quality.CreateInspectionRecordFromTaskAsync(
            tokenProvider.BearerToken,
            inspectionTaskId,
            request with { InspectionTaskId = inspectionTaskId },
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-plans/{inspectionPlanId}/characteristics")]
[BusinessGatewayOperationId("listBusinessConsoleQualityInspectionPlanCharacteristics")]
public sealed class ListBusinessConsoleQualityInspectionPlanCharacteristicsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityInspectionPlanCharacteristicsRequest, BusinessConsoleQualityInspectionPlanCharacteristicListResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityInspectionPlanCharacteristicsRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityInspectionPlanCharacteristicsRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleQualityInspectionPlanCharacteristicsRequest request) => "inspection-plan";

    protected override string? ResourceId(BusinessConsoleQualityInspectionPlanCharacteristicsRequest request) =>
        Route<string>("inspectionPlanId") ?? request.InspectionPlanId;

    protected override Task<BusinessConsoleQualityInspectionPlanCharacteristicListResponse> ForwardAsync(
        BusinessConsoleQualityInspectionPlanCharacteristicsRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inspectionPlanId = Route<string>("inspectionPlanId") ?? request.InspectionPlanId;
        return quality.GetInspectionPlanCharacteristicsAsync(
            tokenProvider.BearerToken,
            request with { InspectionPlanId = inspectionPlanId },
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/ncrs")]
[BusinessGatewayOperationId("listBusinessConsoleQualityNcrs")]
public sealed class ListBusinessConsoleQualityNcrsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityListRequest, BusinessConsoleQualityListResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrRead)
{
    protected override string OrganizationId(BusinessConsoleQualityListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityListResponse> ForwardAsync(
        BusinessConsoleQualityListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListNcrsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

// PDA 检验结果页「已触发 NCR」→ 打开 NCR 详情的互链读端点。按 inspection-records.read 门控（检验员
// 可读由其检验触发的 NCR，与 reason-codes/SPC 同权限口径）；代理真实详情端点并随查询下传 org/env，
// 由 Quality 服务端做租户过滤（越权 id 与不存在同为 not found）。
[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/ncrs/{ncrId}")]
[BusinessGatewayOperationId("getBusinessConsoleQualityNcr")]
public sealed class GetBusinessConsoleQualityNcrEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityNcrDetailRequest, BusinessConsoleQualityNcrDetailResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityNcrDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityNcrDetailRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityNcrDetailResponse> ForwardAsync(
        BusinessConsoleQualityNcrDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.GetNcrAsync(tokenProvider.BearerToken, request, cancellationToken);
}

// PDA NCR 详情「来源检验记录」→ 打开检验记录详情的互链读端点（与 NCR 详情同权限口径）；代理真实
// 详情端点并随查询下传 org/env，由 Quality 服务端做租户过滤。
[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/inspection-records/{inspectionRecordId}")]
[BusinessGatewayOperationId("getBusinessConsoleQualityInspectionRecord")]
public sealed class GetBusinessConsoleQualityInspectionRecordEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityInspectionRecordDetailRequest, BusinessConsoleInspectionRecordDetailResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityInspectionRecordDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityInspectionRecordDetailRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleQualityInspectionRecordDetailRequest request) => "inspection-record";

    protected override string? ResourceId(BusinessConsoleQualityInspectionRecordDetailRequest request) =>
        request.InspectionRecordId;

    protected override Task<BusinessConsoleInspectionRecordDetailResponse> ForwardAsync(
        BusinessConsoleQualityInspectionRecordDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.GetInspectionRecordAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/spc/control-chart")]
[BusinessGatewayOperationId("queryBusinessConsoleQualitySpcControlChart")]
public sealed class QueryBusinessConsoleQualitySpcControlChartEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualitySpcRequest, BusinessConsoleQualitySpcControlChartResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualitySpcRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualitySpcRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualitySpcControlChartResponse> ForwardAsync(
        BusinessConsoleQualitySpcRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.QuerySpcControlChartAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/spc/process-capability")]
[BusinessGatewayOperationId("queryBusinessConsoleQualityProcessCapability")]
public sealed class QueryBusinessConsoleQualityProcessCapabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityProcessCapabilityRequest, BusinessConsoleQualityProcessCapabilityResponse>(
        auth,
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityProcessCapabilityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityProcessCapabilityRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityProcessCapabilityResponse> ForwardAsync(
        BusinessConsoleQualityProcessCapabilityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.QueryProcessCapabilityAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/reason-codes")]
[BusinessGatewayOperationId("listBusinessConsoleQualityReasonCodes")]
public sealed class ListBusinessConsoleQualityReasonCodesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityReasonListRequest, BusinessConsoleQualityReasonListResponse>(
        auth,
        // 原因码是检验执行（不合格特性录原因码）所需的参考目录，故按检验记录读权限放行，
        // 而非 NCR 读权限——否则只有 inspection-records.read/create 的 PDA 质检角色会 403。
        BusinessGatewayPermissions.QualityInspectionRecordsRead)
{
    protected override string OrganizationId(BusinessConsoleQualityReasonListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityReasonListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityReasonListResponse> ForwardAsync(
        BusinessConsoleQualityReasonListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.ListQualityReasonsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/reason-codes/{reasonCode}")]
[BusinessGatewayOperationId("getBusinessConsoleQualityReasonCode")]
public sealed class GetBusinessConsoleQualityReasonCodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityReasonRequest, BusinessConsoleQualityReasonItem>(
        auth,
        BusinessGatewayPermissions.QualityNcrRead)
{
    protected override string OrganizationId(BusinessConsoleQualityReasonRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleQualityReasonRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleQualityReasonRequest request) => "quality-reason";

    protected override string? ResourceId(BusinessConsoleQualityReasonRequest request) => Route<string>("reasonCode") ?? request.ReasonCode;

    protected override Task<BusinessConsoleQualityReasonItem> ForwardAsync(
        BusinessConsoleQualityReasonRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var reasonCode = Route<string>("reasonCode") ?? request.ReasonCode;
        return quality.GetQualityReasonAsync(tokenProvider.BearerToken, reasonCode, request with { ReasonCode = reasonCode }, cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/reason-codes")]
[BusinessGatewayOperationId("createBusinessConsoleQualityReasonCode")]
public sealed class CreateBusinessConsoleQualityReasonCodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateQualityReasonRequest, BusinessConsoleQualityReasonItem>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleCreateQualityReasonRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateQualityReasonRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleQualityReasonItem> ForwardAsync(
        BusinessConsoleCreateQualityReasonRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        quality.CreateQualityReasonAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Quality")]
[HttpPut("/api/business-console/v1/quality/reason-codes/{reasonCode}")]
[BusinessGatewayOperationId("updateBusinessConsoleQualityReasonCode")]
public sealed class UpdateBusinessConsoleQualityReasonCodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleUpdateQualityReasonRequest, BusinessConsoleQualityReasonItem>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleUpdateQualityReasonRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleUpdateQualityReasonRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleUpdateQualityReasonRequest request) => "quality-reason";

    protected override string? ResourceId(BusinessConsoleUpdateQualityReasonRequest request) => Route<string>("reasonCode") ?? request.ReasonCode;

    protected override Task<BusinessConsoleQualityReasonItem> ForwardAsync(
        BusinessConsoleUpdateQualityReasonRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var reasonCode = Route<string>("reasonCode") ?? request.ReasonCode;
        return quality.UpdateQualityReasonAsync(tokenProvider.BearerToken, reasonCode, request with { ReasonCode = reasonCode }, cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/reason-codes/{reasonCode}/archive")]
[BusinessGatewayOperationId("archiveBusinessConsoleQualityReasonCode")]
public sealed class ArchiveBusinessConsoleQualityReasonCodeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleArchiveQualityReasonRequest, BusinessConsoleQualityReasonItem>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleArchiveQualityReasonRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleArchiveQualityReasonRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleArchiveQualityReasonRequest request) => "quality-reason";

    protected override string? ResourceId(BusinessConsoleArchiveQualityReasonRequest request) => Route<string>("reasonCode") ?? request.ReasonCode;

    protected override Task<BusinessConsoleQualityReasonItem> ForwardAsync(
        BusinessConsoleArchiveQualityReasonRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var reasonCode = Route<string>("reasonCode") ?? request.ReasonCode;
        return quality.ArchiveQualityReasonAsync(tokenProvider.BearerToken, reasonCode, request with { ReasonCode = reasonCode }, cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/disposition")]
[BusinessGatewayOperationId("submitBusinessConsoleQualityNcrDisposition")]
public sealed class SubmitBusinessConsoleQualityNcrDispositionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNcrDispositionRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleNcrDispositionRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleNcrDispositionRequest request) =>
        request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleNcrDispositionRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleNcrDispositionRequest request) =>
        Route<string>("ncrId") ?? request.NcrId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleNcrDispositionRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncrId = Route<string>("ncrId") ?? request.NcrId;
        var downstreamRequest = request with { NcrId = ncrId };
        return quality.SubmitNcrDispositionAsync(
            tokenProvider.BearerToken,
            ncrId,
            downstreamRequest,
            cancellationToken);
    }
}

[Tags("Business Console Quality")]
[HttpPost("/api/business-console/v1/quality/ncrs/{ncrId}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleQualityNcr")]
public sealed class CloseBusinessConsoleQualityNcrEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleNcrCloseRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrManage)
{
    protected override string OrganizationId(BusinessConsoleNcrCloseRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleNcrCloseRequest request) =>
        request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleNcrCloseRequest request) => "ncr";

    protected override string? ResourceId(BusinessConsoleNcrCloseRequest request) =>
        Route<string>("ncrId") ?? request.NcrId;

    protected override Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleNcrCloseRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var ncrId = Route<string>("ncrId") ?? request.NcrId;
        var downstreamRequest = request with { NcrId = ncrId };
        return quality.CloseNcrAsync(
            tokenProvider.BearerToken,
            ncrId,
            downstreamRequest,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
    }
}
