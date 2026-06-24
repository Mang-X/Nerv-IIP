using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
    ApplicationDbContext dbContext,
    ConvertPlanToWorkOrderCommandHandler convertHandler,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<PlanningSuggestionAcceptedIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = "Nerv.IIP.Contracts.DemandPlanning.PlanningSuggestionAcceptedIntegrationEvent";
    public const string ConsumerName = "business-mes.demand-planning-suggestion-accepted";

    private readonly IntegrationEventConsumerGuard<PlanningSuggestionAcceptedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            DemandPlanningIntegrationEventVersions.V1));

    public PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
        : this(dbContext, new ConvertPlanToWorkOrderCommandHandler(dbContext), deadLetterStore)
    {
    }

    public async Task HandleAsync(
        PlanningSuggestionAcceptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        PlanningSuggestionAcceptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        PlanningSuggestionAcceptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SuggestionType, "planned-work-order", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.DownstreamService, "BusinessMes", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.DownstreamDocumentType, "WorkOrder", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var existing = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourcePlanReference != null &&
                x.SourcePlanReference.SourceSystem == "DemandPlanning" &&
                x.SourcePlanReference.SourceDocumentType == "PlanningSuggestion" &&
                x.SourcePlanReference.SourceDocumentId == payload.SuggestionId,
            cancellationToken);
        if (existing)
        {
            return;
        }

        var dueUtc = new DateTimeOffset(payload.RequiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await convertHandler.Handle(
            new ConvertPlanToWorkOrderCommand(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.SuggestionId,
                payload.DownstreamDocumentId,
                integrationEvent.OccurredAtUtc,
                payload.SkuCode,
                payload.ProductionVersionReference,
                payload.Quantity,
                payload.UomCode,
                dueUtc,
                null,
                "DemandPlanning",
                "PlanningSuggestion",
                payload.SuggestionId,
                payload.DemandSourceReference,
                integrationEvent.IdempotencyKey),
            cancellationToken);
    }
}
