using FastEndpoints;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpPost("/api/notifications/v1/intents")]
[AllowAnonymous]
public sealed class SubmitNotificationIntentEndpoint(IMediator mediator)
    : Endpoint<SubmitNotificationIntentRequest, ResponseData<NotificationIntentResponse>>
{
    public override async Task HandleAsync(SubmitNotificationIntentRequest req, CancellationToken ct)
    {
        var response = await mediator.Send(new SubmitNotificationIntentCommand(
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id"),
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id"),
            req,
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

public sealed class SubmitNotificationIntentRequestValidator : Validator<SubmitNotificationIntentRequest>
{
    public SubmitNotificationIntentRequestValidator()
    {
        RuleFor(x => x.SourceService).NotEmpty();
        RuleFor(x => x.SourceEventType).NotEmpty();
        RuleFor(x => x.SourceEventId).NotEmpty();
        RuleFor(x => x.IntentType).NotEmpty();
        RuleFor(x => x.Severity).NotEmpty();
        RuleFor(x => x.DedupeKey).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.SuggestedRecipientRefs).NotEmpty();
    }
}
