using DotNetCore.CAP;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(PlanningSuggestionAcceptedIntegrationEventTopic.TopicName, ConsumerName)]
public sealed class PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition(
    ApplicationDbContext dbContext,
    CreatePurchaseRequisitionFromSuggestionCommandHandler createHandler,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<PlanningSuggestionAcceptedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.planning-suggestion-accepted-purchase-requisition";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
        DemandPlanningIntegrationEventVersions.V1);

    private readonly IntegrationEventConsumerGuard<PlanningSuggestionAcceptedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        ErpCodingService? codingService = null)
        : this(
            dbContext,
            new CreatePurchaseRequisitionFromSuggestionCommandHandler(dbContext, codingService),
            deadLetterStore)
    {
    }

    public Task HandleAsync(PlanningSuggestionAcceptedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(PlanningSuggestionAcceptedIntegrationEventTopic.TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(PlanningSuggestionAcceptedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(PlanningSuggestionAcceptedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, DemandPlanningIntegrationEventSources.BusinessDemandPlanning, StringComparison.OrdinalIgnoreCase))
        {
            await AddDeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        if (!TargetsErpPurchaseRequisition(integrationEvent.Payload))
        {
            return;
        }

        if (!string.Equals(integrationEvent.Payload.SuggestionType, DemandPlanningSuggestionTypes.PlannedPurchase, StringComparison.OrdinalIgnoreCase))
        {
            await AddDeadLetterAsync(
                integrationEvent,
                "unsupported-suggestion-type",
                $"Planning suggestion type '{integrationEvent.Payload.SuggestionType}' cannot create an ERP purchase requisition.",
                cancellationToken);
            return;
        }

        if (!PayloadHasRequiredPurchaseFacts(integrationEvent.Payload))
        {
            await AddDeadLetterAsync(
                integrationEvent,
                "missing-payload-field",
                "Planning suggestion accepted payload is missing required purchase requisition facts.",
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        await createHandler.Handle(new CreatePurchaseRequisitionFromSuggestionCommand(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            null,
            integrationEvent.Payload.SuggestionId,
            integrationEvent.Payload.SkuCode,
            integrationEvent.Payload.UomCode,
            integrationEvent.Payload.SiteCode,
            integrationEvent.Payload.Quantity,
            integrationEvent.Payload.RequiredDate,
            BuildIdempotencyKey(integrationEvent)),
            cancellationToken);
    }

    private static bool TargetsErpPurchaseRequisition(PlanningSuggestionAcceptedPayload payload)
    {
        return string.Equals(payload.DownstreamService, DemandPlanningDownstreamReferences.BusinessErp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(payload.DownstreamDocumentType, DemandPlanningDownstreamReferences.PurchaseRequisition, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PayloadHasRequiredPurchaseFacts(PlanningSuggestionAcceptedPayload payload)
    {
        return !string.IsNullOrWhiteSpace(payload.SuggestionId)
            && !string.IsNullOrWhiteSpace(payload.SkuCode)
            && !string.IsNullOrWhiteSpace(payload.UomCode)
            && !string.IsNullOrWhiteSpace(payload.SiteCode)
            && payload.Quantity > 0
            && payload.RequiredDate != default;
    }

    private static string BuildIdempotencyKey(PlanningSuggestionAcceptedIntegrationEvent integrationEvent)
    {
        return $"business-erp:purchase-requisition-from-planning-suggestion:{integrationEvent.OrganizationId}:{integrationEvent.EnvironmentId}:{integrationEvent.Payload.SuggestionId}";
    }

    private Task AddDeadLetterAsync(
        PlanningSuggestionAcceptedIntegrationEvent integrationEvent,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureReason),
            cancellationToken);
    }
}
