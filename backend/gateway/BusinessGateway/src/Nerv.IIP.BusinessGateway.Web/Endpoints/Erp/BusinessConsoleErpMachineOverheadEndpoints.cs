using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-order-costs/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleErpWorkOrderCostVariance")]
public sealed class GetBusinessConsoleErpWorkOrderCostVarianceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleGetErpWorkOrderCostVarianceRequest,
        BusinessConsoleErpWorkOrderCostVarianceResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleGetErpWorkOrderCostVarianceRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleGetErpWorkOrderCostVarianceRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleErpWorkOrderCostVarianceResponse> ForwardAsync(
        BusinessConsoleGetErpWorkOrderCostVarianceRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.GetWorkOrderCostVarianceAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-center-machine-overhead-reconciliations")]
[BusinessGatewayOperationId("listBusinessConsoleErpWorkCenterMachineOverheadReconciliations")]
public sealed class ListBusinessConsoleErpMachineOverheadReconciliationsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleListErpMachineOverheadReconciliationsRequest,
        BusinessConsoleErpMachineOverheadReconciliationListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleListErpMachineOverheadReconciliationsRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListErpMachineOverheadReconciliationsRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleErpMachineOverheadReconciliationListResponse> ForwardAsync(
        BusinessConsoleListErpMachineOverheadReconciliationsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListMachineOverheadReconciliationsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleGetErpWorkOrderCostVarianceRequestValidator
    : Validator<BusinessConsoleGetErpWorkOrderCostVarianceRequest>
{
    public BusinessConsoleGetErpWorkOrderCostVarianceRequestValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class BusinessConsoleListErpMachineOverheadReconciliationsRequestValidator
    : Validator<BusinessConsoleListErpMachineOverheadReconciliationsRequest>
{
    public BusinessConsoleListErpMachineOverheadReconciliationsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100).When(x => x.WorkCenterId is not null);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
