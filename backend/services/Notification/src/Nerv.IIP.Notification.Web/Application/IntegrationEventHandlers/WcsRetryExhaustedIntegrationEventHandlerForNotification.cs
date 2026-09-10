using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.Commands.Notifications;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Wms.WmsIntegrationEvent", ConsumerName)]
public sealed class WcsRetryExhaustedIntegrationEventHandlerForNotification(
    ISender sender, ApplicationDbContext dbContext, IIntegrationEventDeadLetterStore deadLetterStore, TimeProvider timeProvider)
    : IIntegrationEventHandler<WmsIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "notification.wms-wcs-retry-exhausted";
    private readonly IntegrationEventConsumerGuard<WmsIntegrationEvent> consumerGuard = new(new IntegrationEventEnvelopeValidator(), deadLetterStore, new IntegrationEventConsumerOptions(ConsumerName, WmsIntegrationEventTypes.WcsTaskRetryExhausted, WmsIntegrationEventVersions.V1));

    public Task HandleAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken) => consumerGuard.HandleAsync(integrationEvent, HandleValidAsync, cancellationToken);

    [CapSubscribe(nameof(WmsIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);

    /// <summary>
    /// 这条告警的摘要渲染（#3305）。
    /// </summary>
    /// <remarks>
    /// <para><c>DiagnosticMessage</c> 是 WMS 转发的**外部 WCS 原始诊断报文**，长度完全不受本仓控制：
    /// <c>business_wms.wcs_tasks.failure_message</c> 已按 #3305 改为无界 <c>text</c>。
    /// 摘要是**渲染产物**，按本服务自己的承载列宽收口即可；原文一字不少留在 WMS，
    /// operator 顺着 <c>ResourceRef("wcs-task", ExternalTaskId)</c> 下钻可取全文。</para>
    /// <para><b>刻意做成 <c>public static</c> 而不是内联在 <see cref="HandleValidAsync"/> 里</b>：
    /// 跨服务契约用例（<c>WcsFailureMessageCrossServiceSummaryContractTests</c>）要拿两侧 EF 模型算出的
    /// 最坏取值来验它，两边必须调**同一个函数**——各写一份模板就成了「照抄 canonical」，
    /// 模板改一处、断言还对着旧的那份绿。
    /// 传 <see cref="int.MaxValue"/> 即得到未截断的原始渲染，可用来证明断言非同义反复。</para>
    /// </remarks>
    public static string BuildSummary(WmsIntegrationEvent integrationEvent, int summaryMaxLength)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return NotificationSummaryText.Fit(
            $"WCS task {integrationEvent.Payload.PublicReference} exhausted retry attempts: {integrationEvent.Payload.DiagnosticCode} {integrationEvent.Payload.DiagnosticMessage}",
            summaryMaxLength);
    }

    private async Task HandleValidAsync(WmsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(integrationEvent.Payload.PublicReference))
        {
            return;
        }
        if (!await NotificationProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, timeProvider.GetUtcNow(), cancellationToken)) return;
        var summary = BuildSummary(integrationEvent, NotificationSummaryText.ResolveSummaryMaxLength(dbContext.Model));
        var request = new SubmitNotificationIntentRequest(integrationEvent.SourceService, integrationEvent.EventType, integrationEvent.EventId, NotificationContractConstants.IntentTypeTask, NotificationContractConstants.SeverityCritical, integrationEvent.IdempotencyKey, new NotificationResourceRef("wcs-task", integrationEvent.Payload.PublicReference, null), "WCS retry attempts exhausted", summary, ["role:wms-operator"]);
        await sender.Send(new SubmitNotificationIntentCommand(integrationEvent.OrganizationId, integrationEvent.EnvironmentId, request, timeProvider.GetUtcNow()), cancellationToken);
    }
}
