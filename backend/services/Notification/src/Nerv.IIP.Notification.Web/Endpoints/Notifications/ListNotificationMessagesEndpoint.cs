using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Web.Application.Queries.Notifications;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpGet("/api/notifications/v1/messages")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListNotificationMessagesEndpoint(IMediator mediator)
    : EndpointWithoutRequest<ResponseData<NotificationMessageListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var response = await mediator.Send(new ListNotificationMessagesQuery(
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id"),
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id"),
            Query<string?>("recipientRef", isRequired: false),
            Query<string?>("status", isRequired: false)), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
