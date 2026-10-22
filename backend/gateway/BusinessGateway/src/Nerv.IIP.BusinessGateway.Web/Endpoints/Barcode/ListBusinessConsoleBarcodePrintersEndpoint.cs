using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Validation;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

[Tags("Business Console Barcode")]
[HttpGet("/api/business-console/v1/barcode/printers")]
[BusinessGatewayOperationId("listBusinessConsoleBarcodePrinters")]
public sealed class ListBusinessConsoleBarcodePrintersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleBarcodePrinterListRequest, BusinessConsoleBarcodePrinterListResponse>(
        auth, BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleBarcodePrinterListRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleBarcodePrinterListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleBarcodePrinterListResponse> ForwardAsync(
        BusinessConsoleBarcodePrinterListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ListPrintersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleBarcodePrinterListRequestValidator : Validator<BusinessConsoleBarcodePrinterListRequest>
{
    public BusinessConsoleBarcodePrinterListRequestValidator() =>
        this.Tenant(x => x.OrganizationId, x => x.EnvironmentId);
}
