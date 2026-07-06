using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IOptions<MesEngineeringChangeOptions> options,
    IMediator? mediator = null)
    : IIntegrationEventHandler<EngineeringChangeReleasedIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = EngineeringChangeReleasedIntegrationEventTopic.TopicName;
    public const string ConsumerName = "business-mes.product-engineering-change-released";

    private readonly IntegrationEventConsumerGuard<EngineeringChangeReleasedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1));

    public EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        MesEngineeringChangeOptions options)
        : this(dbContext, deadLetterStore, Options.Create(options))
    {
    }

    public EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        MesEngineeringChangeOptions options,
        IMediator mediator)
        : this(dbContext, deadLetterStore, Options.Create(options), mediator)
    {
    }

    public async Task HandleAsync(
        EngineeringChangeReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(
        EngineeringChangeReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(
        EngineeringChangeReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var affectedProductionVersions = integrationEvent.Payload.AffectedVersions
            .Where(x => IsProductionVersionKind(x.VersionKind))
            .GroupBy(x => x.VersionId, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        if (affectedProductionVersions.Length == 0)
        {
            return;
        }

        foreach (var affected in affectedProductionVersions)
        {
            await AddArchivedVersionMarkerIfMissingAsync(integrationEvent, affected, cancellationToken);
        }

        var archivedVersionIds = affectedProductionVersions
            .Select(x => x.VersionId)
            .ToHashSet(StringComparer.Ordinal);
        var affectedByVersion = affectedProductionVersions.ToDictionary(x => x.VersionId, StringComparer.Ordinal);
        var workOrders = await dbContext.WorkOrders
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.ProductionVersionId != null &&
                archivedVersionIds.Contains(x.ProductionVersionId) &&
                x.Status != WorkOrder.CompletedStatus &&
                x.Status != WorkOrder.ClosedStatus &&
                x.Status != WorkOrder.CancelledStatus &&
                x.Status != WorkOrder.ScrappedStatus)
            .ToListAsync(cancellationToken);

        foreach (var workOrder in workOrders)
        {
            var affected = affectedByVersion[workOrder.ProductionVersionId!];
            if (await HasImpactAsync(integrationEvent, workOrder.WorkOrderId, cancellationToken))
            {
                continue;
            }

            var workOrderStatusAtDetection = workOrder.Status;
            if (workOrder.Status is WorkOrder.CreatedStatus or WorkOrder.ReleasedStatus)
            {
                if (options.Value.NotStartedPolicy == MesEngineeringChangeNotStartedPolicy.AutoRebind &&
                    !string.IsNullOrWhiteSpace(affected.SupersededByVersionId))
                {
                    workOrder.RebindProductionVersionForEngineeringChange(affected.SupersededByVersionId);
                    await AddImpactAsync(MesEngineeringChangeWorkOrderImpact.AutoRebound(
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        workOrder.WorkOrderId,
                        workOrder.SkuId,
                        workOrderStatusAtDetection,
                        integrationEvent.Payload.ChangeNumber,
                        affected.VersionId,
                        affected.SupersededByVersionId,
                        integrationEvent.Payload.EffectiveDate,
                        integrationEvent.OccurredAtUtc), cancellationToken);
                }
                else
                {
                    workOrder.Hold($"Engineering change {integrationEvent.Payload.ChangeNumber} requires production version confirmation.");
                    await AddImpactAsync(MesEngineeringChangeWorkOrderImpact.BlockedForManualConfirmation(
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        workOrder.WorkOrderId,
                        workOrder.SkuId,
                        workOrderStatusAtDetection,
                        integrationEvent.Payload.ChangeNumber,
                        affected.VersionId,
                        affected.SupersededByVersionId,
                        integrationEvent.Payload.EffectiveDate,
                        integrationEvent.OccurredAtUtc), cancellationToken);
                }

                continue;
            }

            await AddImpactAsync(MesEngineeringChangeWorkOrderImpact.PendingDecision(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                workOrder.WorkOrderId,
                workOrder.SkuId,
                workOrderStatusAtDetection,
                integrationEvent.Payload.ChangeNumber,
                affected.VersionId,
                affected.SupersededByVersionId,
                integrationEvent.Payload.EffectiveDate,
                integrationEvent.OccurredAtUtc), cancellationToken);
        }
    }

    private async Task AddArchivedVersionMarkerIfMissingAsync(
        EngineeringChangeReleasedIntegrationEvent integrationEvent,
        EngineeringChangeAffectedVersionPayload affected,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.EngineeringChangeWorkOrderImpacts.AnyAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.ArchivedProductionVersionId == affected.VersionId &&
            x.Status == MesEngineeringChangeImpactStatuses.ArchivedProductionVersion,
            cancellationToken);
        if (exists)
        {
            return;
        }

        await AddImpactAsync(MesEngineeringChangeWorkOrderImpact.ArchivedProductionVersion(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.ChangeNumber,
            affected.VersionId,
            affected.SupersededByVersionId,
            integrationEvent.Payload.EffectiveDate,
            integrationEvent.OccurredAtUtc), cancellationToken);
    }

    private async Task AddImpactAsync(
        MesEngineeringChangeWorkOrderImpact impact,
        CancellationToken cancellationToken)
    {
        dbContext.EngineeringChangeWorkOrderImpacts.Add(impact);
        var impactDetected = impact.GetDomainEvents()
            .OfType<MesEngineeringChangeWorkOrderImpactDetectedDomainEvent>()
            .SingleOrDefault();
        if (impactDetected is null)
        {
            return;
        }

        if (mediator is not null &&
            impact.Status != MesEngineeringChangeImpactStatuses.ArchivedProductionVersion)
        {
            await mediator.Publish(impactDetected, cancellationToken);
        }

        impact.ClearDomainEvents();
    }

    private async Task<bool> HasImpactAsync(
        EngineeringChangeReleasedIntegrationEvent integrationEvent,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EngineeringChangeWorkOrderImpacts.AnyAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.WorkOrderId == workOrderId &&
            x.ChangeNumber == integrationEvent.Payload.ChangeNumber,
            cancellationToken);
    }

    private static bool IsProductionVersionKind(string value)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.Equals(normalized, "productionversion", StringComparison.OrdinalIgnoreCase);
    }
}

public static class EngineeringChangeReleasedIntegrationEventTopic
{
    public const string TopicName = "Nerv.IIP.Contracts.ProductEngineering.EngineeringChangeReleasedIntegrationEvent";
}
