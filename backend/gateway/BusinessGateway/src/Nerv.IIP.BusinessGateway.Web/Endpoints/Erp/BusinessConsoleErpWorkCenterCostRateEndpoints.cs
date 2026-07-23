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
            .Must(BeAsciiCurrencyCode)
            .WithMessage("CurrencyCode must contain exactly three ASCII letters.");
        RuleFor(x => x.EffectiveFromUtc).NotEmpty().Must(value => value.Offset == TimeSpan.Zero);
        RuleFor(x => x.EffectiveToUtc).Must(value => value is null || value.Value.Offset == TimeSpan.Zero);
        RuleFor(x => x.EffectiveToUtc)
            .Must((request, value) => value is null || value > request.EffectiveFromUtc);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }

    private static bool BeAsciiCurrencyCode(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length == 3
            && value.Trim().All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
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
