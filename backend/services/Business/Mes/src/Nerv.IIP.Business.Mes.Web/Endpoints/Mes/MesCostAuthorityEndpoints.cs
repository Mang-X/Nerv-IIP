using FastEndpoints;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public sealed class GetFinishedGoodsReceiptCostAuthorityEndpoint(ISender sender)
    : Endpoint<MesFinishedGoodsReceiptCostAuthorityRequest, MesFinishedGoodsReceiptCostAuthorityResponse>
{
    public override void Configure()
    {
        Post("/internal/business-mes/v1/finished-goods-receipt-cost-authority");
        Policies(InternalServiceAuthorizationPolicy.Name);
        Tags("Business MES Internal");
    }

    public override async Task HandleAsync(
        MesFinishedGoodsReceiptCostAuthorityRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new GetFinishedGoodsReceiptCostAuthorityQuery(request),
            cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
