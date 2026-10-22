using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

public abstract class BusinessConsoleMesAndonEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    PrincipalWorkScopeResolver scopes,
    string permission)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permission)
    where TRequest : class, IBusinessConsoleMesAndonScopeRequest
{
    private readonly string _permission = permission;
    protected override System.Text.Json.JsonSerializerOptions ResponseJsonOptions => BusinessConsoleMesAndonJson.Options;
    protected override bool IncludePrincipalContext => true;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;
    protected override string OrganizationId(TRequest request) => request.OrganizationId;
    protected override string EnvironmentId(TRequest request) => request.EnvironmentId;

    protected Task<PrincipalWorkScopeSelection> ResolveScopeAsync(TRequest request, CancellationToken ct) =>
        scopes.ResolveAsync(AuthorizationResult, request.OrganizationId, request.EnvironmentId,
            _permission, request.ScopeKind, request.ScopeId, ct);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/andon-calls")]
[BusinessGatewayOperationId("raiseBusinessConsoleMesAndonCall")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class RaiseBusinessConsoleMesAndonCallEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesAndonClient mes,
    PrincipalWorkScopeResolver scopes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesAndonEndpoint<BusinessConsoleMesRaiseAndonCallRequest, BusinessConsoleMesAndonCallResponse>(auth, scopes, BusinessGatewayPermissions.MesOperationsManage)
{
    protected override async Task<BusinessConsoleMesAndonCallResponse> ForwardAsync(BusinessConsoleMesRaiseAndonCallRequest request, string bearerToken, CancellationToken ct) =>
        await mes.RaiseAsync(tokenProvider.BearerToken, request, await ResolveScopeAsync(request, ct), RequireIdempotentAuditContext(request), ct);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/andon-calls/{id}/claim")]
[BusinessGatewayOperationId("claimBusinessConsoleMesAndonCall")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class ClaimBusinessConsoleMesAndonCallEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesAndonClient mes,
    PrincipalWorkScopeResolver scopes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesAndonEndpoint<BusinessConsoleMesAndonCallActionRequest, BusinessConsoleMesAndonCallResponse>(auth, scopes, BusinessGatewayPermissions.MesOperationsManage)
{
    protected override async Task<BusinessConsoleMesAndonCallResponse> ForwardAsync(BusinessConsoleMesAndonCallActionRequest request, string bearerToken, CancellationToken ct) =>
        await mes.ClaimAsync(tokenProvider.BearerToken, request, await ResolveScopeAsync(request, ct), RequireIdempotentAuditContext(request), ct);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/andon-calls/{id}/close")]
[BusinessGatewayOperationId("closeBusinessConsoleMesAndonCall")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class CloseBusinessConsoleMesAndonCallEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesAndonClient mes,
    PrincipalWorkScopeResolver scopes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesAndonEndpoint<BusinessConsoleMesAndonCallActionRequest, BusinessConsoleMesAndonCallResponse>(auth, scopes, BusinessGatewayPermissions.MesOperationsManage)
{
    protected override async Task<BusinessConsoleMesAndonCallResponse> ForwardAsync(BusinessConsoleMesAndonCallActionRequest request, string bearerToken, CancellationToken ct) =>
        await mes.CloseAsync(tokenProvider.BearerToken, request, await ResolveScopeAsync(request, ct), RequireIdempotentAuditContext(request), ct);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/andon-calls/{id}")]
[BusinessGatewayOperationId("getBusinessConsoleMesAndonCall")]
public sealed class GetBusinessConsoleMesAndonCallEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesAndonClient mes,
    PrincipalWorkScopeResolver scopes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesAndonEndpoint<BusinessConsoleMesAndonCallRequest, BusinessConsoleMesAndonCallResponse>(auth, scopes, BusinessGatewayPermissions.MesOperationsRead)
{
    protected override async Task<BusinessConsoleMesAndonCallResponse> ForwardAsync(BusinessConsoleMesAndonCallRequest request, string bearerToken, CancellationToken ct) =>
        await mes.GetAsync(tokenProvider.BearerToken, request, await ResolveScopeAsync(request, ct), ct);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/andon-calls")]
[BusinessGatewayOperationId("listBusinessConsoleMesAndonCalls")]
public sealed class ListBusinessConsoleMesAndonCallsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesAndonClient mes,
    PrincipalWorkScopeResolver scopes,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesAndonEndpoint<BusinessConsoleMesListAndonCallsRequest, BusinessConsoleMesAndonCallListResponse>(auth, scopes, BusinessGatewayPermissions.MesOperationsRead)
{
    protected override async Task<BusinessConsoleMesAndonCallListResponse> ForwardAsync(BusinessConsoleMesListAndonCallsRequest request, string bearerToken, CancellationToken ct) =>
        await mes.ListAsync(tokenProvider.BearerToken, request, await ResolveScopeAsync(request, ct), ct);
}

public sealed class BusinessConsoleMesRaiseAndonCallValidator : Validator<BusinessConsoleMesRaiseAndonCallRequest>
{
    public BusinessConsoleMesRaiseAndonCallValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleMesAndonCallActionValidator : Validator<BusinessConsoleMesAndonCallActionRequest>
{
    public BusinessConsoleMesAndonCallActionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleMesAndonCallValidator : Validator<BusinessConsoleMesAndonCallRequest>
{
    public BusinessConsoleMesAndonCallValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleMesListAndonCallsValidator : Validator<BusinessConsoleMesListAndonCallsRequest>
{
    public BusinessConsoleMesListAndonCallsValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Queue).IsInEnum();
        RuleFor(x => x.Category).IsInEnum();
    }
}
