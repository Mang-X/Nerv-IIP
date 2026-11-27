using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

public sealed class BusinessConsoleMesEngineeringChangeImpactsRequestValidator
    : Validator<BusinessConsoleMesEngineeringChangeImpactsRequest>
{
    public BusinessConsoleMesEngineeringChangeImpactsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).MaximumLength(100);
        RuleFor(x => x.WorkOrderId).MaximumLength(100);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.ChangeNumber) || !string.IsNullOrWhiteSpace(x.WorkOrderId))
            .WithMessage("请指定工程变更单号或工单标识。");
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-order-engineering-change-impacts")]
[BusinessGatewayOperationId("listBusinessConsoleMesEngineeringChangeWorkOrderImpacts")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status502BadGateway)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status503ServiceUnavailable)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status504GatewayTimeout)]
public sealed class ListBusinessConsoleMesEngineeringChangeWorkOrderImpactsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    HttpBusinessMesEngineeringChangeImpactClient mes,
    PrincipalWorkScopeResolver workScopeResolver,
    IInternalServiceTokenProvider tokenProvider)
    : Endpoint<BusinessConsoleMesEngineeringChangeImpactsRequest, ResponseData<BusinessConsoleMesEngineeringChangeImpactsResponse>>
{
    public override async Task HandleAsync(BusinessConsoleMesEngineeringChangeImpactsRequest req, CancellationToken ct)
    {
        var changeNumber = Normalize(req.ChangeNumber);
        var workOrderId = Normalize(req.WorkOrderId);
        var engineeringToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext, auth, new(
                BusinessGatewayPermissions.EngineeringChangesRead, req.OrganizationId, req.EnvironmentId,
                changeNumber is null ? null : "engineering-change", changeNumber),
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, ct);
        if (engineeringToken is null)
        {
            return;
        }

        if (CurrentAuthorization?.DataScope?.DenyAll == true)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden, "Forbidden.", ct);
            return;
        }

        var mesToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext, auth, new(
                BusinessGatewayPermissions.MesWorkOrdersRead, req.OrganizationId, req.EnvironmentId,
                null, null, IncludePrincipalContext: true),
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, ct);
        if (mesToken is null)
        {
            return;
        }

        try
        {
            var scope = await workScopeResolver.ResolveWorkOrderListAsync(
                CurrentAuthorization,
                req.OrganizationId, req.EnvironmentId, BusinessGatewayPermissions.MesWorkOrdersRead,
                req.ScopeKind, req.ScopeId, ct);
            var response = await mes.ListAsync(
                tokenProvider.BearerToken, req with { ChangeNumber = changeNumber, WorkOrderId = workOrderId }, scope, ct);
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, ex, ct);
        }
    }

    private BusinessGatewayAuthorizationResult? CurrentAuthorization =>
        HttpContext.Items[BusinessGatewayAuthorization.PrincipalItemKey] as BusinessGatewayAuthorizationResult;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
