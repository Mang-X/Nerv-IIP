using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.Orders;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.MasterData.Web.Endpoints.OrderEndpoints;

public record PayOrderRequest(OrderId Id);

[Tags("Orders")]
[HttpPost("/api/order/pay")]
[AllowAnonymous]
public class PayOrderEndpoint(IMediator mediator) : Endpoint<PayOrderRequest, ResponseData<bool>>
{
    public override async Task HandleAsync(PayOrderRequest req, CancellationToken ct)
    {
        await mediator.Send(new PayOrderCommand(req.Id), ct);
        await Send.OkAsync(true.AsResponseData(), cancellation: ct);
    }
}