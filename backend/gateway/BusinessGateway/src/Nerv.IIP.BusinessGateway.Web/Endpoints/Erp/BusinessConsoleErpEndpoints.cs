using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/procurement/purchase-orders")]
[BusinessGatewayOperationId("listBusinessConsoleErpPurchaseOrders")]
public sealed class ListBusinessConsoleErpPurchaseOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleErpContextRequest, BusinessConsoleErpPurchaseOrderListResponse>(
        auth,
        BusinessGatewayPermissions.ErpProcurementRead)
{
    protected override string OrganizationId(BusinessConsoleErpContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleErpContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleErpPurchaseOrderListResponse> ForwardAsync(
        BusinessConsoleErpContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListPurchaseOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleErpContextRequestValidator : Validator<BusinessConsoleErpContextRequest>
{
    public BusinessConsoleErpContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}
