using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpPost("/api/notifications/v1/messages/{messageId}/read")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class MarkNotificationMessageReadEndpoint(IMediator mediator)
    : EndpointWithoutRequest<ResponseData<MarkNotificationMessageReadResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var response = await mediator.Send(new MarkNotificationMessageReadCommand(
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id"),
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id"),
            Route<string>("messageId")!,
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
