using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;

[Tags("Business Console ERP")]
[HttpPost("/api/business-console/v1/erp/finance/work-center-cost-rates")]
[BusinessGatewayOperationId("configureBusinessConsoleErpWorkCenterCostRate")]
public sealed class ConfigureBusinessConsoleErpWorkCenterCostRateEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleConfigureErpWorkCenterCostRateRequest,
        BusinessConsoleConfigureErpWorkCenterCostRateResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceManage)
{
    protected override string OrganizationId(BusinessConsoleConfigureErpWorkCenterCostRateRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConfigureErpWorkCenterCostRateRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleConfigureErpWorkCenterCostRateResponse> ForwardAsync(
        BusinessConsoleConfigureErpWorkCenterCostRateRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ConfigureWorkCenterCostRateAsync(
            tokenProvider.BearerToken,
            request,
            RequireAuthorizedPrincipalActorReference(),
            cancellationToken);
}

[Tags("Business Console ERP")]
[HttpGet("/api/business-console/v1/erp/finance/work-center-cost-rates")]
[BusinessGatewayOperationId("listBusinessConsoleErpWorkCenterCostRates")]
public sealed class ListBusinessConsoleErpWorkCenterCostRatesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessErpClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<
        BusinessConsoleListErpWorkCenterCostRatesRequest,
        BusinessConsoleErpWorkCenterCostRateListResponse>(
        auth,
        BusinessGatewayPermissions.ErpFinanceRead)
{
    protected override string OrganizationId(BusinessConsoleListErpWorkCenterCostRatesRequest request) =>
        request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListErpWorkCenterCostRatesRequest request) =>
        request.EnvironmentId;

    protected override Task<BusinessConsoleErpWorkCenterCostRateListResponse> ForwardAsync(
        BusinessConsoleListErpWorkCenterCostRatesRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        erp.ListWorkCenterCostRatesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleConfigureErpWorkCenterCostRateRequestValidator
    : Validator<BusinessConsoleConfigureErpWorkCenterCostRateRequest>
{
    public BusinessConsoleConfigureErpWorkCenterCostRateRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.HourlyRate).GreaterThan(0);
        RuleFor(x => x.CurrencyCode)
            .Must(value => value is not null && Regex.IsMatch(value, "^[A-Z]{3}$", RegexOptions.CultureInvariant))
            .WithMessage("CurrencyCode must be a three-letter uppercase ISO currency code.");
        RuleFor(x => x.EffectiveFromUtc).NotEmpty().Must(value => value.Offset == TimeSpan.Zero);
        RuleFor(x => x.EffectiveToUtc).Must(value => value is null || value.Value.Offset == TimeSpan.Zero);
        RuleFor(x => x.EffectiveToUtc)
            .Must((request, value) => value is null || value > request.EffectiveFromUtc);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class BusinessConsoleListErpWorkCenterCostRatesRequestValidator
    : Validator<BusinessConsoleListErpWorkCenterCostRatesRequest>
{
    public BusinessConsoleListErpWorkCenterCostRatesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AtUtc).Must(value => value is null || value.Value.Offset == TimeSpan.Zero);
    }
}
