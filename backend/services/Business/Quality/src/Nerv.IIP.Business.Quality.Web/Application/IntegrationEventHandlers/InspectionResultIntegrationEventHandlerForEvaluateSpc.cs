using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class InspectionResultIntegrationEventHandlerForEvaluateSpc(
    ApplicationDbContext dbContext,
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.spc-inspection-result";

    private static readonly string[] SupportedEventTypes =
    [
        QualityIntegrationEventTypes.InspectionPassed,
        QualityIntegrationEventTypes.InspectionConditionalReleased,
        QualityIntegrationEventTypes.InspectionRejected,
    ];

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SupportedEventTypes,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.InspectionPlanId)
            || payload.ResultLines is null
            || payload.ResultLines.Count == 0)
        {
            return;
        }

        if (!Guid.TryParse(payload.InspectionPlanId, out var planGuid))
        {
            return;
        }

        var plan = await dbContext.InspectionPlans
            .AsNoTracking()
            .Include(x => x.Characteristics)
            .Where(x => x.Id == new InspectionPlanId(planGuid)
                && x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(plan?.WorkCenterId))
        {
            return;
        }

        var measuredCharacteristics = payload.ResultLines
            .Where(x => x.MeasuredValue.HasValue)
            .Select(x => x.CharacteristicCode.Trim().ToLowerInvariant())
            .Where(characteristicCode => characteristicCode.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(characteristicCode => new
            {
                CharacteristicCode = characteristicCode,
                SubgroupSize = ResolveSubgroupSize(plan.Characteristics.SingleOrDefault(x => x.CharacteristicCode == characteristicCode)?.SamplingRule),
            })
            .ToArray();
        foreach (var characteristic in measuredCharacteristics)
        {
            await sender.Send(
                new EvaluateSpcControlChartCommand(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    payload.SkuCode,
                    characteristic.CharacteristicCode,
                    plan.WorkCenterId,
                    characteristic.SubgroupSize,
                    Take: 125),
                cancellationToken);
        }
    }

    private static int ResolveSubgroupSize(string? samplingRule)
    {
        if (string.IsNullOrWhiteSpace(samplingRule))
        {
            return 5;
        }

        var digits = new string(samplingRule.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var size) && size is >= 2 and <= 10 ? size : 5;
    }
}
