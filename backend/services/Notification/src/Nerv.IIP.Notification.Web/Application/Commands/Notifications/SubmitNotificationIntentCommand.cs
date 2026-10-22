using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Messaging.CAP;
using System.Diagnostics;

namespace Nerv.IIP.Notification.Web.Application.Commands.Notifications;

/// <summary>
/// 提交通知意图。
/// <para>
/// 摘要不收裸 <see cref="string"/>：调用方必须先经 <see cref="NotificationSummary"/> 的两个具名工厂之一
/// 产出取值 —— 进程内拼装走 <see cref="NotificationSummary.Render"/>（夹紧），
/// 外部提交走 <see cref="NotificationSummary.FromSubmitted"/>（超界抛）。
/// 后来者在编译期必须显式选一个，选错也是响亮失败而不是静默溢出。
/// </para>
/// <para>
/// 构造时把取值写回 <see cref="Request"/>，让「命令携带的 Request.Summary」与
/// <see cref="Summary"/> 由构造成立地相等：读哪一个都是同一份，不存在「哪个说了算」。
/// </para>
/// <para>
/// ⚠️ <b>「夹紧由命令层保证」成立的前提是调用方闭集，而这个闭集靠的是引用拓扑，不是类型可见性。</b>
/// 本类型是 <c>public</c>，任何引用了 <c>Nerv.IIP.Notification.Web</c> 的工程都能构造它；
/// 今天构造不出来，只是因为除本服务外引用它的工程都不消费本命名空间的类型
/// （测试工程，以及 Aspire AppHost 那条 <c>IsAspireProjectResource</c> 的编排引用）。
/// </para>
/// <para>
/// ⚠️ <b>失效方向：</b>将来任何工程新增一条指向 <c>Nerv.IIP.Notification.Web</c> 的
/// <c>ProjectReference</c>，闭集会<b>静默变宽</b>，而今天<b>没有任何门禁会因此报红</b>。
/// 这是已知边界、如实登记，不是已被守住的性质 —— 要守住它需要一条引用拓扑门禁，不在本票范围。
/// </para>
/// </summary>
public sealed record SubmitNotificationIntentCommand : ICommand<NotificationIntentResponse>
{
    public SubmitNotificationIntentCommand(
        string organizationId,
        string environmentId,
        SubmitNotificationIntentRequest request,
        NotificationSummary summary,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(summary);

        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        Summary = summary;
        Request = request with { Summary = summary.Value };
        Now = now;
    }

    public string OrganizationId { get; }

    public string EnvironmentId { get; }

    /// <summary><see cref="SubmitNotificationIntentRequest.Summary"/> 恒等于 <see cref="Summary"/> 的取值。</summary>
    public SubmitNotificationIntentRequest Request { get; }

    public NotificationSummary Summary { get; }

    public DateTimeOffset Now { get; }
}

public sealed class SubmitNotificationIntentCommandHandler(
    INotificationIntentRepository repository,
    ApplicationDbContext dbContext,
    NotificationDeliveryService deliveryService)
    : ICommandHandler<SubmitNotificationIntentCommand, NotificationIntentResponse>
{
    public async Task<NotificationIntentResponse> Handle(SubmitNotificationIntentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var existing = await repository.GetByDedupeKeyAsync(
            command.OrganizationId,
            command.EnvironmentId,
            request.SourceService,
            request.SourceEventType,
            request.DedupeKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse(duplicate: true);
        }

        var intent = new NotificationIntent(
            command.OrganizationId,
            command.EnvironmentId,
            request.SourceService,
            request.SourceEventType,
            request.SourceEventId,
            request.IntentType,
            request.Severity,
            request.DedupeKey,
            request.Resource?.ResourceType,
            request.Resource?.ResourceId,
            request.Resource?.FileId,
            request.Title,
            request.Summary,
            request.SuggestedRecipientRefs,
            command.Now);

        await repository.AddAsync(intent, cancellationToken);
        await deliveryService.StageSubmittedIntentAsync(intent, command.Now, cancellationToken);

        const string duplicateRecoverySavepoint = "notification_intent_submit_before_save";
        // Use EF's native current transaction here. The CAP unit-of-work wrapper does not expose savepoints,
        // while EF's relational transaction does and can recover from a duplicate unique-conflict inside an outer transaction.
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            Debug.Assert(transaction.SupportsSavepoints, "Notification duplicate recovery requires a transaction that supports savepoints.");
            await transaction.CreateSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (!IsDuplicateIntentConflict(exception))
            {
                throw;
            }

            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            var duplicate = await repository.GetByDedupeKeyAsync(
                command.OrganizationId,
                command.EnvironmentId,
                request.SourceService,
                request.SourceEventType,
                request.DedupeKey,
                cancellationToken)
                ?? RethrowDuplicateConflict(exception);
            return duplicate.ToResponse(duplicate: true);
        }

        return intent.ToResponse(duplicate: false);
    }

    private bool IsDuplicateIntentConflict(DbUpdateException exception)
    {
        return dbContext.ChangeTracker.Entries<NotificationIntent>().Any(x => x.State == EntityState.Added)
            && ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, constraintOrIndexName: null);
    }

    private static NotificationIntent RethrowDuplicateConflict(DbUpdateException exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable duplicate conflict rethrow path.");
    }
}
