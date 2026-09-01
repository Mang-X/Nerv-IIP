using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/line-side-inventory-balances")]
[BusinessGatewayOperationId("listBusinessConsoleMesLineSideInventoryBalances")]
public sealed class ListBusinessConsoleMesLineSideInventoryBalancesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        LineSideInventoryBalancesRequest,
        LineSideInventoryBalancesResponse>(
        auth,
        BusinessGatewayPermissions.MesMaterialsRead)
{
    protected override string OrganizationId(LineSideInventoryBalancesRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(LineSideInventoryBalancesRequest request) =>
        request.EnvironmentId;

    protected override Task<LineSideInventoryBalancesResponse> ForwardAsync(
        LineSideInventoryBalancesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        inventory.ListLineSideBalancesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class LineSideInventoryBalancesRequestValidator
    : Validator<LineSideInventoryBalancesRequest>
{
    public LineSideInventoryBalancesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
