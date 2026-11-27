using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Wms;

[Tags("Business Console WMS")]
[HttpPost("/api/business-console/v1/wms/inbound-orders/{inboundOrderId}/confirm-receipt")]
[BusinessGatewayOperationId("confirmBusinessConsoleWmsInboundReceipt")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status403Forbidden)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status409Conflict)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData), StatusCodes.Status422UnprocessableEntity)]
public sealed class ConfirmBusinessConsoleWmsInboundReceiptEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessWmsClient wms,
    IInternalServiceTokenProvider tokenProvider,
    WmsTrustedRequestContextResolver trustedContextResolver)
    : BusinessConsoleWmsTrustedProxyEndpoint<BusinessConsoleConfirmWmsInboundReceiptRequest, BusinessConsoleWmsReceiptConfirmationResult>(
        auth, trustedContextResolver, BusinessGatewayPermissions.WmsReceiptsManage)
{
    protected override bool IncludePrincipalContext => true;

    protected override string OrganizationId(BusinessConsoleConfirmWmsInboundReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleConfirmWmsInboundReceiptRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleWmsReceiptConfirmationResult> ForwardAsync(
        BusinessConsoleConfirmWmsInboundReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var trusted = await ResolveTrustedContextAsync(request, cancellationToken);
        var scope = trusted.ResolveScope(request.ScopeKind, request.ScopeId);
        return await wms.ConfirmInboundReceiptAsync(
            tokenProvider.BearerToken,
            new BusinessWmsConfirmInboundReceiptRequest(
                Route<string>("inboundOrderId") ?? request.InboundOrderId,
                request.OrganizationId,
                request.EnvironmentId,
                trusted.ActorPrincipalId,
                trusted.AuthorizedSiteCodes,
                scope.ScopeKind,
                scope.ScopeId,
                request.ExpectedVersion,
                request.IdempotencyKey,
                request.Lines,
                request.ExchangeRate),
            cancellationToken);
    }
}

public sealed class BusinessConsoleConfirmWmsInboundReceiptRequestValidator
    : Validator<BusinessConsoleConfirmWmsInboundReceiptRequest>
{
    public BusinessConsoleConfirmWmsInboundReceiptRequestValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExchangeRate).GreaterThan(0m).LessThan(10_000_000_000m)
            .PrecisionScale(18, 8, ignoreTrailingZeros: true);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).NotNull().ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0m);
        });
    }
}
