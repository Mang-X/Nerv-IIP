using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using FastEndpoints;
using FluentValidation;
using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Planning;

public sealed record BusinessConsoleMaterialDeliveriesRequest(
    [property: RouteParam] string RunId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId,
    [property: QueryParam] string? PlanId = null);

[Tags("Business Console Planning")]
[HttpGet("/api/business-console/v1/planning/mrp-runs/{runId}/material-deliveries")]
[BusinessGatewayOperationId("getBusinessConsolePlanningMaterialDeliveries")]
public sealed class GetBusinessConsolePlanningMaterialDeliveriesEndpoint(
    IBusinessGatewayAuthorizationClient auth, IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMaterialDeliveriesRequest, MaterialDeliveriesResponse>(auth, BusinessGatewayPermissions.PlanningMrpRead)
{
    private static readonly JsonSerializerOptions MaterialDeliveryJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    protected override JsonSerializerOptions ResponseJsonOptions => MaterialDeliveryJson;

    protected override string OrganizationId(BusinessConsoleMaterialDeliveriesRequest req) => req.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleMaterialDeliveriesRequest req) => req.EnvironmentId;
    protected override BusinessGatewayAuthorizationContinuityMode AuthorizationContinuityMode =>
        BusinessGatewayAuthorizationContinuityMode.RealtimeRequired;

    protected override async Task<MaterialDeliveriesResponse> ForwardAsync(
        BusinessConsoleMaterialDeliveriesRequest req, string bearerToken, CancellationToken ct)
    {
        // Owner 聚合查询不能分来源裁剪；先逐一授权，全部通过才允许内部调用。
        var permissions = new List<string>
        {
            BusinessGatewayPermissions.PlanningDemandsRead,
            BusinessGatewayPermissions.ErpProcurementRead,
        };
        if (req.PlanId is not null)
        {
            permissions.AddRange([
                BusinessGatewayPermissions.SchedulingPlansRead,
                BusinessGatewayPermissions.MesWorkOrdersRead,
                BusinessGatewayPermissions.MesReportingRead,
                BusinessGatewayPermissions.EngineeringProductionVersionsRead,
                BusinessGatewayPermissions.EngineeringRoutingsRead,
                BusinessGatewayPermissions.MasterDataResourcesRead,
            ]);
        }
        foreach (var permission in permissions)
        {
            var authorization = await AuthorizationClient.CheckAsync(bearerToken,
                new(permission, req.OrganizationId, req.EnvironmentId, null, null), AuthorizationContinuityMode, ct);
            if (!authorization.IsAllowed)
                throw new BusinessServiceProxyException(HttpStatusCode.Forbidden, "permission-denied");
        }
        return await planning.GetMaterialDeliveriesAsync(tokenProvider.BearerToken, req.RunId,
            req.OrganizationId, req.EnvironmentId, req.PlanId, ct);
    }
}

public sealed class BusinessConsoleMaterialDeliveriesRequestValidator : Validator<BusinessConsoleMaterialDeliveriesRequest>
{
    public BusinessConsoleMaterialDeliveriesRequestValidator()
    {
        RuleFor(x => x.RunId).NotEmpty().Must(x => Guid.TryParse(x, out _));
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128).When(x => x.PlanId is not null);
    }
}
