using MediatR;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record ProcessMaterialSupplyEtaChangedCommand(
    IIntegrationEventEnvelope Envelope,
    IReadOnlyCollection<string> SkuCodes) : ICommand<RecordSchedulePlanInvalidationsResponse>;

public sealed class ProcessMaterialSupplyEtaChangedCommandHandler(
    ApplicationDbContext dbContext,
    ISender sender)
    : ICommandHandler<ProcessMaterialSupplyEtaChangedCommand, RecordSchedulePlanInvalidationsResponse>
{
    public async Task<RecordSchedulePlanInvalidationsResponse> Handle(
        ProcessMaterialSupplyEtaChangedCommand request,
        CancellationToken cancellationToken)
    {
        if (!await SchedulingProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                MaterialSupplyEtaChangedIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
                request.Envelope,
                cancellationToken))
        {
            return new RecordSchedulePlanInvalidationsResponse(0, 0);
        }

        return await sender.Send(
            SchedulingPlanInvalidationService.ToSkuCommand(
                request.Envelope,
                SchedulingPlanInvalidationReasons.MaterialReadinessChanged,
                request.SkuCodes),
            cancellationToken);
    }
}
