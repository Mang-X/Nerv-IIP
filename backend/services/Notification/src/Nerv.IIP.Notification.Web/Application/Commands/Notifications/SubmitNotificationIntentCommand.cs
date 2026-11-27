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
    NotificationDeliveryService deliveryService,
    IConfiguration? configuration = null)
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
            ExpandRecipientRefs(command),
            command.Now);

        // 聚合新增意图、消息和任务，投递暂存仅新增 DeliveryAttempt；绑定、偏好和订阅只读。
        // 记录调用方已有的待插入实体，只撤销本次新增图和投递记录。
        var existingAdds = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        await repository.AddAsync(intent, cancellationToken);
        await deliveryService.StageSubmittedIntentAsync(intent, command.Now, cancellationToken);

        var submitAdds = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added && !existingAdds.Contains(entry.Entity))
            .ToArray();

        const string duplicateRecoverySavepoint = "notification_intent_submit_before_save";
        // CAP 工作单元未暴露保存点，通过 EF 当前事务恢复唯一键冲突。
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            Debug.Assert(transaction.SupportsSavepoints, "通知去重冲突恢复需要事务支持保存点。");
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

            foreach (var entry in submitAdds)
            {
                entry.State = EntityState.Detached;
            }

            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }

            var duplicate = await repository.GetByDedupeKeyAsync(
                command.OrganizationId,
                command.EnvironmentId,
                request.SourceService,
                request.SourceEventType,
                request.DedupeKey,
                cancellationToken);
            if (duplicate is null)
            {
                throw;
            }

            return duplicate.ToResponse(duplicate: true);
        }

        return intent.ToResponse(duplicate: false);
    }

    private IReadOnlyCollection<string> ExpandRecipientRefs(SubmitNotificationIntentCommand command)
    {
        var recipients = command.Request.SuggestedRecipientRefs;
        var roles = configuration?.GetSection("Notification:RoleRecipients").GetChildren()
            .FirstOrDefault(scope => scope.Key == command.OrganizationId)?.GetChildren()
            .FirstOrDefault(scope => scope.Key == command.EnvironmentId);
        if (roles is null || recipients is null)
        {
            return recipients!;
        }

        const string rolePrefix = "role:";
        var hasExpansion = false;
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipient in recipients)
        {
            var role = recipient;
            var userIds = role?.StartsWith(rolePrefix, StringComparison.Ordinal) == true
                ? roles.GetChildren().FirstOrDefault(section => section.Key == role[rolePrefix.Length..])
                    ?.Get<string[]>()
                : null;
            if (userIds is { Length: > 0 })
            {
                hasExpansion = true;
                expanded.UnionWith(userIds.Select(userId => $"user:{userId.Trim()}"));
            }
            else
            {
                expanded.Add(recipient!);
            }
        }

        return hasExpansion ? expanded.ToArray() : recipients;
    }

    private bool IsDuplicateIntentConflict(DbUpdateException exception)
    {
        return dbContext.ChangeTracker.Entries<NotificationIntent>().Any(x => x.State == EntityState.Added)
            && ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, constraintOrIndexName: null);
    }
}
