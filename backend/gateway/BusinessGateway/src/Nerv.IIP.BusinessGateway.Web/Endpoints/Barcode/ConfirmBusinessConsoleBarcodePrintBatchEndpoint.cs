using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Validation;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;

[Tags("Business Console Barcode")]
[HttpPost("/api/business-console/v1/barcode/print-batches/{printBatchId}/confirm")]
[BusinessGatewayOperationId("confirmBusinessConsoleBarcodePrintBatch")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status502BadGateway)]
public sealed class ConfirmBusinessConsoleBarcodePrintBatchEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessBarcodeLabelClient barcode,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleConfirmBarcodePrintBatchRequest, BusinessConsoleBarcodePrintLifecycleResponse>(
        auth, BusinessGatewayPermissions.BarcodePrint)
{
    protected override string OrganizationId(BusinessConsoleConfirmBarcodePrintBatchRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleConfirmBarcodePrintBatchRequest request) => request.EnvironmentId;
    protected override string ResourceType(BusinessConsoleConfirmBarcodePrintBatchRequest request) => "barcode-print-batch";
    protected override string? ResourceId(BusinessConsoleConfirmBarcodePrintBatchRequest request) => request.PrintBatchId;

    protected override Task<BusinessConsoleBarcodePrintLifecycleResponse> ForwardAsync(
        BusinessConsoleConfirmBarcodePrintBatchRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        barcode.ConfirmPrintBatchAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleConfirmBarcodePrintBatchRequestValidator : Validator<BusinessConsoleConfirmBarcodePrintBatchRequest>
{
    public BusinessConsoleConfirmBarcodePrintBatchRequestValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty().MaximumLength(150);
        this.Tenant(x => x.OrganizationId, x => x.EnvironmentId);
    }
}
