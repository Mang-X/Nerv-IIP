using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.MasterData.SkuDisabledIntegrationEvent", ConsumerName)]
public sealed class SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IMesSkuAvailabilityScopeCoordinator scopeCoordinator)
    : IIntegrationEventHandler<SkuDisabledIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.sku-availability";

    private readonly IntegrationEventConsumerGuard<SkuDisabledIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MasterDataIntegrationEventTypes.SkuDisabled,
            MasterDataIntegrationEventVersions.V1));

    public SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
        : this(
            dbContext,
            deadLetterStore,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext))
    {
    }

    public Task HandleAsync(SkuDisabledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(SkuDisabledIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(SkuDisabledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        SkuDisabledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                integrationEvent.SourceService,
                MasterDataIntegrationEventSources.BusinessMasterData,
                StringComparison.OrdinalIgnoreCase))
        {
            await AddDeadLetterAsync(
                integrationEvent,
                "unexpected-source-service",
                $"Integration event source service '{integrationEvent.SourceService}' is not supported by consumer '{ConsumerName}'.",
                cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.ResourceType, "sku", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.Code)
            || !string.Equals(payload.Status, MesSkuAvailabilityStatuses.Disabled, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.DisabledReason)
            || payload.ChangedAtUtc == default)
        {
            await AddDeadLetterAsync(
                integrationEvent,
                "invalid-sku-disabled-payload",
                "SKU disabled payload must identify a disabled SKU, reason, and change time.",
                cancellationToken);
            return;
        }

        var skuCode = payload.Code.Trim();
        await scopeCoordinator.ExecuteAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            skuCode,
            token => ProjectDisabledSkuAsync(integrationEvent, skuCode, token),
            cancellationToken);
    }

    private async Task ProjectDisabledSkuAsync(
        SkuDisabledIntegrationEvent integrationEvent,
        string skuCode,
        CancellationToken cancellationToken)
    {
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                ConsumerName,
                integrationEvent,
                cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var availability = await dbContext.MesSkuAvailabilities.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId
            && x.EnvironmentId == integrationEvent.EnvironmentId
            && x.SkuCode == skuCode,
            cancellationToken);
        if (availability is null)
        {
            dbContext.MesSkuAvailabilities.Add(MesSkuAvailability.CreateDisabled(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                skuCode,
                payload.ChangedAtUtc,
                payload.DisabledReason.Trim(),
                integrationEvent.EventId));
            return;
        }

        availability.ApplyDisabled(
            payload.ChangedAtUtc,
            payload.DisabledReason.Trim(),
            integrationEvent.EventId);
    }

    private Task AddDeadLetterAsync(
        SkuDisabledIntegrationEvent integrationEvent,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken) =>
        deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(ConsumerName, integrationEvent, failureCode, failureReason),
            cancellationToken);
}
