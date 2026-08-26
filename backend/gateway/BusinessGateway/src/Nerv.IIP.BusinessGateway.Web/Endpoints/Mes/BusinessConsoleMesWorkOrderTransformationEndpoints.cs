using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/{workOrderId}/split")]
[BusinessGatewayOperationId("splitBusinessConsoleMesWorkOrder")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class SplitBusinessConsoleMesWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesWorkOrderTransformationClient transformationClient,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesSplitWorkOrderRequest, BusinessConsoleMesWorkOrderTransformationMutationResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesSplitWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesSplitWorkOrderRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleMesSplitWorkOrderRequest request) => "mes-work-order";

    protected override string? ResourceId(BusinessConsoleMesSplitWorkOrderRequest request) => request.WorkOrderId;

    protected override async Task<BusinessConsoleMesWorkOrderTransformationMutationResponse> ForwardAsync(
        BusinessConsoleMesSplitWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
            AuthorizationResult,
            request.OrganizationId,
            request.EnvironmentId,
            BusinessGatewayPermissions.MesWorkOrdersManage,
            request.ScopeKind,
            request.ScopeId,
            request.WorkOrderId,
            cancellationToken);
        var result = await transformationClient.SplitAsync(
            tokenProvider.BearerToken,
            request,
            cancellationToken);
        return BusinessConsoleMesWorkOrderTransformationEndpointMapping.ToMutationResponse(
            result,
            "mes.work-order.split",
            request.IdempotencyKey,
            BusinessConsoleMesWorkOrderTransformationEndpointMapping.ReadbackPath(
                result.TransformationId,
                request.OrganizationId,
                request.EnvironmentId,
                request.ScopeKind,
                request.ScopeId));
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/work-orders/merge")]
[BusinessGatewayOperationId("mergeBusinessConsoleMesWorkOrders")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
public sealed class MergeBusinessConsoleMesWorkOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesWorkOrderTransformationClient transformationClient,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesMergeWorkOrdersRequest, BusinessConsoleMesWorkOrderTransformationMutationResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesMergeWorkOrdersRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesMergeWorkOrdersRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleMesMergeWorkOrdersRequest request) => "mes-work-order";

    protected override string? ResourceId(BusinessConsoleMesMergeWorkOrdersRequest request) => request.TargetWorkOrderId;

    protected override async Task<BusinessConsoleMesWorkOrderTransformationMutationResponse> ForwardAsync(
        BusinessConsoleMesMergeWorkOrdersRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        foreach (var sourceWorkOrderId in request.SourceWorkOrderIds.Distinct(StringComparer.Ordinal))
        {
            await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
                AuthorizationResult,
                request.OrganizationId,
                request.EnvironmentId,
                BusinessGatewayPermissions.MesWorkOrdersManage,
                request.ScopeKind,
                request.ScopeId,
                sourceWorkOrderId,
                cancellationToken);
        }

        var result = await transformationClient.MergeAsync(
            tokenProvider.BearerToken,
            request,
            cancellationToken);
        return BusinessConsoleMesWorkOrderTransformationEndpointMapping.ToMutationResponse(
            result,
            "mes.work-order.merge",
            request.IdempotencyKey,
            BusinessConsoleMesWorkOrderTransformationEndpointMapping.ReadbackPath(
                result.TransformationId,
                request.OrganizationId,
                request.EnvironmentId,
                request.ScopeKind,
                request.ScopeId));
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/work-order-transformations/{transformationId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderTransformation")]
public sealed class GetBusinessConsoleMesWorkOrderTransformationEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesWorkOrderTransformationClient transformationClient,
    MesPrincipalWorkScopeAuthorizer workScopeAuthorizer,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesWorkOrderTransformationReadbackRequest, BusinessConsoleMesWorkOrderTransformationReadbackResponse>(
        auth,
        BusinessGatewayPermissions.MesWorkOrdersRead)
{
    protected override bool IncludePrincipalContext => true;

    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override string OrganizationId(BusinessConsoleMesWorkOrderTransformationReadbackRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesWorkOrderTransformationReadbackRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleMesWorkOrderTransformationReadbackRequest request) => "mes-work-order-transformation";

    protected override string? ResourceId(BusinessConsoleMesWorkOrderTransformationReadbackRequest request) => request.TransformationId;

    protected override async Task<BusinessConsoleMesWorkOrderTransformationReadbackResponse> ForwardAsync(
        BusinessConsoleMesWorkOrderTransformationReadbackRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var readback = await transformationClient.GetReadbackAsync(
            tokenProvider.BearerToken,
            request,
            cancellationToken);
        foreach (var sourceWorkOrderId in readback.Lines.Select(x => x.SourceWorkOrderId).Distinct(StringComparer.Ordinal))
        {
            await workScopeAuthorizer.EnsureWorkOrderAccessAsync(
                AuthorizationResult,
                request.OrganizationId,
                request.EnvironmentId,
                BusinessGatewayPermissions.MesWorkOrdersRead,
                request.ScopeKind,
                request.ScopeId,
                sourceWorkOrderId,
                cancellationToken);
        }

        return new(
            readback.TransformationId,
            readback.Type,
            readback.IdempotencyKey,
            readback.Actor,
            readback.Reason,
            readback.OccurredAtUtc,
            readback.Lines.Select(x => new BusinessConsoleMesWorkOrderTransformationLineResponse(
                x.SourceWorkOrderId,
                x.TargetWorkOrderId,
                x.Quantity,
                x.UomCode,
                x.SourceStatus,
                x.TargetStatus,
                x.SourceVersion,
                x.TargetVersion)).ToArray());
    }
}

public sealed class BusinessConsoleMesSplitWorkOrderRequestValidator
    : Validator<BusinessConsoleMesSplitWorkOrderRequest>
{
    public BusinessConsoleMesSplitWorkOrderRequestValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Targets).NotNull().Must(x => x is not null && x.Count >= 2);
        RuleForEach(x => x.Targets).ChildRules(target =>
        {
            target.RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
            target.RuleFor(x => x.Quantity).GreaterThan(0m);
        });
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleMesMergeWorkOrdersRequestValidator
    : Validator<BusinessConsoleMesMergeWorkOrdersRequest>
{
    public BusinessConsoleMesMergeWorkOrdersRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceWorkOrderIds).NotNull().Must(x => x is not null && x.Count >= 2);
        RuleForEach(x => x.SourceWorkOrderIds).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TargetWorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class BusinessConsoleMesWorkOrderTransformationReadbackRequestValidator
    : Validator<BusinessConsoleMesWorkOrderTransformationReadbackRequest>
{
    public BusinessConsoleMesWorkOrderTransformationReadbackRequestValidator()
    {
        RuleFor(x => x.TransformationId)
            .NotEmpty()
            .MaximumLength(100)
            .Must(value => Guid.TryParse(value?.Trim(), out var parsed) && parsed != Guid.Empty)
            .WithMessage("TransformationId must be a non-empty GUID.");
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

internal static class BusinessConsoleMesWorkOrderTransformationEndpointMapping
{
    public static BusinessConsoleMesWorkOrderTransformationMutationResponse ToMutationResponse(
        BusinessMesWorkOrderTransformationResult result,
        string operationType,
        string idempotencyKey,
        string readbackPath) =>
        new(
            true,
            result.TransformationId,
            result.Type,
            result.SourceWorkOrderIds,
            result.TargetWorkOrderIds,
            result.IsIdempotentReplay,
            BusinessConsoleOperationReceipts.Accepted(
                operationType,
                "BusinessMes",
                "WorkOrderTransformation",
                result.TransformationId,
                readbackPath,
                idempotencyKey));

    public static string ReadbackPath(
        string transformationId,
        string organizationId,
        string environmentId,
        string? scopeKind,
        string? scopeId) =>
        "/api/business-console/v1/mes/work-order-transformations/"
        + Uri.EscapeDataString(transformationId)
        + "?"
        + Query(
            ("organizationId", organizationId),
            ("environmentId", environmentId),
            ("scopeKind", scopeKind),
            ("scopeId", scopeId));

    private static string Query(params (string Name, string? Value)[] values) =>
        string.Join(
            '&',
            values
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value!.Trim())}"));
}
