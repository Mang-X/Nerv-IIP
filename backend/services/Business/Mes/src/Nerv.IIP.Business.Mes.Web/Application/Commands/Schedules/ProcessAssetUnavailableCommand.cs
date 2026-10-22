using FluentValidation;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;

/// <summary>
/// Maintenance AssetUnavailable（v1/v2 汇入同一 canonical 事实）在 MES 侧的唯一业务入口。
/// #2964 冻结的边界：只有在同一事务里同时赢得 <c>(ConsumerName, EventId)</c> 与
/// <c>(ConsumerName, IdempotencyKey)</c> 两项身份的事务才能继续登记停机；claim 与副作用同属
/// 这条 command 的 UoW，任一环节失败整体回滚，不会留下"已 claim 但没有停机事实"的半成品。
/// </summary>
public sealed record ProcessAssetUnavailableCommand(
    IIntegrationEventEnvelope Envelope,
    string DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc) : ICommand<ProcessAssetUnavailableResult>;

/// <param name="Claimed">true = 本次投递赢得双身份；false = 该投递身份已处理。新预测投递可获 claim，但不重复登记实际停机。</param>
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
    IMesDeviceWorkCenterResolver workCenterResolver,
    ILogger<ProcessAssetUnavailableCommandHandler> logger)
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
                request.DeviceAssetId,
                request.FromUtc,
                cancellationToken))
        {
            return new ProcessAssetUnavailableResult(false);
        }

        var envelope = request.Envelope;
        // 同一实际停机的预测更新/清除仅登记投递；已恢复的事实也不得重新打开。
        // coordinator 持有事实锁直至 UoW 提交，初始事件与新 prediction 身份并发时仍只插入一次。
        if (await store.UnavailabilityExistsAsync(
                envelope.OrganizationId,
                envelope.EnvironmentId,
                request.DeviceAssetId,
                request.FromUtc,
                cancellationToken))
        {
            return new ProcessAssetUnavailableResult(true);
        }

        // 设备归属的工作中心由 MasterData 拥有（#3878）。主数据不可用时 resolver 抛出，
        // 整个 UoW（含 claim）回滚，消息系统重试。
        var workCenterId = await workCenterResolver.ResolveAsync(
            envelope.OrganizationId,
            envelope.EnvironmentId,
            request.DeviceAssetId,
            cancellationToken);
        if (workCenterId is null)
        {
            // 主数据里查不到这台设备的工作中心：不得拿设备编号冒充工作中心，也不得扩大到任意工作中心
            // （equipment-status-event-flow.md）。claim 照常落库，这条停机事实跳过、不重试。
            logger.LogWarning(
                "Skipped asset-unavailable event {EventId}: MasterData has no work center for device {DeviceAssetId} in {OrganizationId}/{EnvironmentId}.",
                envelope.EventId,
                request.DeviceAssetId,
                envelope.OrganizationId,
                envelope.EnvironmentId);
            return new ProcessAssetUnavailableResult(true);
        }

        store.AddUnavailability(new WorkCenterUnavailability(
            workCenterId,
            request.FromUtc,
            null,
            request.Reason,
            request.DeviceAssetId,
            envelope.OrganizationId,
            envelope.EnvironmentId));

        return new ProcessAssetUnavailableResult(true);
    }
}
