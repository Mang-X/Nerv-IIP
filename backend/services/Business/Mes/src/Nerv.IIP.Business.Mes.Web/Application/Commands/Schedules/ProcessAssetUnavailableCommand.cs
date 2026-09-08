using FluentValidation;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;

/// <summary>
/// Maintenance AssetUnavailable（v1/v2 汇入同一 canonical 事实）在 MES 侧的唯一业务入口。
/// #2964 冻结的边界：只有在同一事务里同时赢得 <c>(ConsumerName, EventId)</c> 与
/// <c>(ConsumerName, IdempotencyKey)</c> 两项身份的事务才能继续登记停机与重排；claim 与副作用同属
/// 这条 command 的 UoW，任一环节失败整体回滚，不会留下"已 claim 但没有停机事实"的半成品。
/// </summary>
public sealed record ProcessAssetUnavailableCommand(
    IIntegrationEventEnvelope Envelope,
    string DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc) : ICommand<ProcessAssetUnavailableResult>;

/// <param name="Claimed">true = 本次投递赢得双身份并执行了副作用；false = 事件实例或业务事实已被处理，本次无副作用。</param>
public sealed record ProcessAssetUnavailableResult(bool Claimed);

public sealed class ProcessAssetUnavailableCommandValidator : AbstractValidator<ProcessAssetUnavailableCommand>
{
    public ProcessAssetUnavailableCommandValidator()
    {
        RuleFor(x => x.Envelope).NotNull();
        RuleFor(x => x.Envelope.EventId).NotEmpty().When(x => x.Envelope is not null);
        RuleFor(x => x.Envelope.IdempotencyKey).NotEmpty().When(x => x.Envelope is not null);
        RuleFor(x => x.Envelope.OrganizationId).NotEmpty().When(x => x.Envelope is not null);
        RuleFor(x => x.Envelope.EnvironmentId).NotEmpty().When(x => x.Envelope is not null);
        RuleFor(x => x.DeviceAssetId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class ProcessAssetUnavailableCommandHandler(
    IMesAssetUnavailableInboxClaimCoordinator claimCoordinator,
    IMesPlanningStore store,
    RuleScheduler scheduler,
    MesRescheduleOptions options)
    : ICommandHandler<ProcessAssetUnavailableCommand, ProcessAssetUnavailableResult>
{
    public async Task<ProcessAssetUnavailableResult> Handle(
        ProcessAssetUnavailableCommand request,
        CancellationToken cancellationToken)
    {
        // 先在 UoW 事务内赢得双身份 claim（Infrastructure 的 coordinator 在 PostgreSQL 上用 advisory 锁把并发竞争者挡在这一行），
        // 再做任何副作用。
        if (!await claimCoordinator.TryClaimAsync(
                AssetUnavailableIntegrationEventHandlerForReschedule.ConsumerName,
                request.Envelope,
                cancellationToken))
        {
            return new ProcessAssetUnavailableResult(false);
        }

        var envelope = request.Envelope;
        var workCenterId = await store.ResolveWorkCenterIdAsync(
            envelope.OrganizationId,
            envelope.EnvironmentId,
            request.DeviceAssetId,
            cancellationToken);
        store.AddUnavailability(new WorkCenterUnavailability(
            workCenterId,
            request.FromUtc,
            null,
            request.Reason,
            request.DeviceAssetId,
            envelope.OrganizationId,
            envelope.EnvironmentId));

        if (options.AutoRescheduleOnAssetUnavailable)
        {
            var plan = scheduler.Schedule(
                await store.GetScheduleOperationsAsync(envelope.OrganizationId, envelope.EnvironmentId, cancellationToken),
                await store.GetUnavailabilitiesAsync(envelope.OrganizationId, envelope.EnvironmentId, cancellationToken));
            await store.AddScheduleResultAsync(
                RescheduleTrigger.AssetUnavailable,
                envelope.OccurredAtUtc,
                plan,
                cancellationToken: cancellationToken);
        }

        return new ProcessAssetUnavailableResult(true);
    }
}
