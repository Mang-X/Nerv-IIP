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
