using MediatR;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ProcessAssetUnavailableCommand(
    IIntegrationEventEnvelope Envelope,
    string DeviceAssetId) : ICommand<RecordSchedulePlanInvalidationsResponse>;

/// <summary>
/// AssetUnavailable 的规范处理：先在本命令的工作单元事务内按事件实例（EventId）与业务事实
/// （IdempotencyKey）双身份 claim inbox，claim 成功才发出失效记录命令。失效命令经 mediator
/// 嵌套发送：<c>CommandUnitOfWorkBehavior</c> 在已有事务时不再开启新事务，只在外层事务内执行并
/// 保存，因此 claim 与失效记录仍是同一次提交；验证器等管道行为也照常作用于内层命令。
/// </summary>
public sealed class ProcessAssetUnavailableCommandHandler(
    ApplicationDbContext dbContext,
    ISender sender)
    : ICommandHandler<ProcessAssetUnavailableCommand, RecordSchedulePlanInvalidationsResponse>
{
    public async Task<RecordSchedulePlanInvalidationsResponse> Handle(
        ProcessAssetUnavailableCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceAssetId);
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAssetUnavailableAsync(
                dbContext,
                AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
                request.Envelope,
                cancellationToken))
            return new RecordSchedulePlanInvalidationsResponse(0, 0);

        return await sender.Send(
            SchedulingPlanInvalidationService.ToCommand(
                request.Envelope,
                SchedulingPlanInvalidationReasons.EquipmentUnavailable,
                SchedulePlanInvalidationScope.Resource,
                request.DeviceAssetId,
                affectedWorkOrderId: null,
                affectedSkuCode: null),
            cancellationToken);
    }
}
