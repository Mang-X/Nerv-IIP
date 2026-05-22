using FastEndpoints;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpPost("/api/notifications/v1/messages/read-batch")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class MarkNotificationMessagesReadEndpoint(IMediator mediator)
    : Endpoint<MarkNotificationMessagesReadRequest, ResponseData<IReadOnlyCollection<MarkNotificationMessageReadResponse>>>
{
    public override async Task HandleAsync(MarkNotificationMessagesReadRequest req, CancellationToken ct)
    {
        var response = await mediator.Send(new MarkNotificationMessagesReadCommand(
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id"),
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id"),
            req.MessageIds,
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed class MarkNotificationMessagesReadRequestValidator : Validator<MarkNotificationMessagesReadRequest>
{
    public MarkNotificationMessagesReadRequestValidator()
    {
        RuleFor(x => x.MessageIds).NotEmpty();
    }
}
