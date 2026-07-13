using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.MasterData;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.MasterData.BusinessPartnerChangedIntegrationEvent", ConsumerName)]
public sealed class BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<BusinessPartnerChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.business-partner-availability";

    private static readonly IntegrationEventConsumerOptions ConsumerOptions = new(
        ConsumerName,
        MasterDataIntegrationEventTypes.BusinessPartnerChanged,
        MasterDataIntegrationEventVersions.V1);

    private readonly IntegrationEventConsumerGuard<BusinessPartnerChangedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        ConsumerOptions);

    public Task HandleAsync(BusinessPartnerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(BusinessPartnerChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(BusinessPartnerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(BusinessPartnerChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MasterDataIntegrationEventSources.BusinessMasterData, StringComparison.OrdinalIgnoreCase))
        {
            await AddDeadLetterAsync(integrationEvent, "unexpected-source-service", $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.", cancellationToken);
            return;
        }

        if (!string.Equals(integrationEvent.Payload.ResourceType, "business-partner", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(integrationEvent.Payload.Code))
        {
            await AddDeadLetterAsync(integrationEvent, "invalid-partner-payload", "Business partner changed payload must identify a business-partner code.", cancellationToken);
            return;
        }

        var status = integrationEvent.Payload.Status?.Trim().ToLowerInvariant();
        if (status is not (BusinessPartnerAvailabilityStatuses.Active or BusinessPartnerAvailabilityStatuses.Disabled))
        {
            await AddDeadLetterAsync(integrationEvent, "unsupported-partner-status", $"Business partner status '{integrationEvent.Payload.Status ?? "<missing>"}' is not supported.", cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var partnerCode = integrationEvent.Payload.Code.Trim();
        var availability = await dbContext.BusinessPartnerAvailabilities.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.PartnerCode == partnerCode,
            cancellationToken);

        if (availability is null)
        {
            dbContext.BusinessPartnerAvailabilities.Add(BusinessPartnerAvailability.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                partnerCode,
                status,
                integrationEvent.Payload.ChangedAtUtc,
                integrationEvent.EventId));
            return;
        }

        availability.Apply(status, integrationEvent.Payload.ChangedAtUtc, integrationEvent.EventId);
    }

    private Task AddDeadLetterAsync(
        BusinessPartnerChangedIntegrationEvent integrationEvent,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        return deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureReason),
            cancellationToken);
    }
}
