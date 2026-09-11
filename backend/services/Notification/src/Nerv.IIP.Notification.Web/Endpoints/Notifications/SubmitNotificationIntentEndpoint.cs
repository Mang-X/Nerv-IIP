using FastEndpoints;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

[HttpPost("/api/notifications/v1/intents")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class SubmitNotificationIntentEndpoint(IMediator mediator, NotificationSummaryBudget summaryBudget)
    : Endpoint<SubmitNotificationIntentRequest, ResponseData<NotificationIntentResponse>>
{
    public override async Task HandleAsync(SubmitNotificationIntentRequest req, CancellationToken ct)
    {
        var response = await mediator.Send(new SubmitNotificationIntentCommand(
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Organization-Id"),
            NotificationEndpointContext.RequiredHeader(HttpContext, "X-Environment-Id"),
            req,
            // 提交路径：超界拒绝而不是截断。校验器排在本 handler 之前，正常不会走到这次抛；
            // 走到了就说明规则没被容器解析到，那也必须响亮失败而不是悄悄改写调用方的文本。
            NotificationSummary.FromSubmitted(req.Summary, summaryBudget),
            DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

/// <summary>
/// 摘要长度规则排在 handler 之前，所以 HTTP 提交的超界摘要在这里就被<b>拒绝</b>，
/// 不会被 handler 里那条面向进程内拼装的夹紧悄悄改写。
/// 上界从 EF 模型派生（<see cref="NotificationSummaryBudget"/>），此处不手抄列宽。
/// </summary>
public sealed class SubmitNotificationIntentRequestValidator : Validator<SubmitNotificationIntentRequest>
{
    public SubmitNotificationIntentRequestValidator(NotificationSummaryBudget summaryBudget)
    {
        RuleFor(x => x.SourceService).NotEmpty();
        RuleFor(x => x.SourceEventType).NotEmpty();
        RuleFor(x => x.SourceEventId).NotEmpty();
        RuleFor(x => x.IntentType).NotEmpty();
        RuleFor(x => x.Severity).NotEmpty();
        RuleFor(x => x.DedupeKey).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(summaryBudget.MaxLength);
        RuleFor(x => x.SuggestedRecipientRefs).NotEmpty();
    }
}
