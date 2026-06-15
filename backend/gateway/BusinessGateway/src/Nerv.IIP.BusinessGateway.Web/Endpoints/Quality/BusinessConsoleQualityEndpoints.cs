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
        RuleFor(x => x.ReasonCode).MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateQualityReasonRequestValidator : Validator<BusinessConsoleCreateQualityReasonRequest>
{
    public BusinessConsoleCreateQualityReasonRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).MaximumLength(100);
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

[Tags("Business Console Quality")]
[HttpGet("/api/business-console/v1/quality/reason-codes")]
[BusinessGatewayOperationId("listBusinessConsoleQualityReasonCodes")]
public sealed class ListBusinessConsoleQualityReasonCodesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessQualityClient quality,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleQualityReasonListRequest, BusinessConsoleQualityReasonListResponse>(
        auth,
        BusinessGatewayPermissions.QualityNcrRead)
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
            cancellationToken);
    }
}
