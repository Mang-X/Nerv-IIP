using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-order-costs")]
[BusinessGatewayOperationId("listBusinessConsoleErpWorkOrderCosts")]
public sealed class ListBusinessConsoleErpWorkOrderCostsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpCostingClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleListErpWorkOrderCostsRequest,
        BusinessConsoleErpWorkOrderCostListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleListErpWorkOrderCostsRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListErpWorkOrderCostsRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleErpWorkOrderCostListResponse> ForwardAsync(
        BusinessConsoleListErpWorkOrderCostsRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListWorkOrderCostsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-order-costs/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleErpWorkOrderCostVariance")]
public sealed class GetBusinessConsoleErpWorkOrderCostVarianceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpCostingClient erp,
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
    IBusinessErpCostingClient erp,
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

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/work-center-machine-overhead-rates")]
[BusinessGatewayOperationId("configureBusinessConsoleErpWorkCenterMachineOverheadRate")]
public sealed class ConfigureBusinessConsoleErpWorkCenterMachineOverheadRateEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpCostingClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest,
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleConfigureErpWorkCenterMachineOverheadRateResponse> ForwardAsync(
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ConfigureWorkCenterMachineOverheadRateAsync(
            tokenProvider.BearerToken,
            request,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-center-machine-overhead-rates")]
[BusinessGatewayOperationId("listBusinessConsoleErpWorkCenterMachineOverheadRates")]
public sealed class ListBusinessConsoleErpWorkCenterMachineOverheadRatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpCostingClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest,
        BusinessConsoleErpWorkCenterMachineOverheadRateListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleErpWorkCenterMachineOverheadRateListResponse> ForwardAsync(
        BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListWorkCenterMachineOverheadRatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleListErpWorkOrderCostsRequestValidator
    : Validator<BusinessConsoleListErpWorkOrderCostsRequest>
{
    public BusinessConsoleListErpWorkOrderCostsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
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

public sealed class BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequestValidator
    : Validator<BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest>
{
    public BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Applicability).IsInEnum();
        RuleFor(x => x.FixedOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.VariableOverheadBudget).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.NormalCapacityMachineHours).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.CurrencyCode)
            .Must(value => !string.IsNullOrWhiteSpace(value)
                && value.Trim().Length == 3
                && value.Trim().All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')))
            .WithMessage("CurrencyCode must contain exactly three ASCII letters.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class BusinessConsoleListErpWorkCenterMachineOverheadRatesRequestValidator
    : Validator<BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest>
{
    public BusinessConsoleListErpWorkCenterMachineOverheadRatesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
