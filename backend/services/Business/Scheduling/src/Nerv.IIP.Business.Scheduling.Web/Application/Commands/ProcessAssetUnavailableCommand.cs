using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ProcessAssetUnavailableCommand(
    IIntegrationEventEnvelope Envelope,
    string DeviceAssetId) : ICommand<RecordSchedulePlanInvalidationsResponse>;

public sealed class ProcessAssetUnavailableCommandHandler(
    ApplicationDbContext dbContext,
    RecordSchedulePlanInvalidationsCommandHandler invalidationHandler)
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

        return await invalidationHandler.Handle(
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
