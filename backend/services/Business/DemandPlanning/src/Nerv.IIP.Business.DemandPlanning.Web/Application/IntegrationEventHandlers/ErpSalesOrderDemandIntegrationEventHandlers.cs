using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.SalesOrderReleasedIntegrationEvent", ConsumerName)]
public sealed class SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SalesOrderReleasedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-demand-planning.erp-sales-order-demand";
    private readonly SalesOrderDemandEventProcessor processor = new(dbContext, deadLetterStore, ConsumerName);
    private readonly IntegrationEventConsumerGuard<SalesOrderReleasedIntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.SalesOrderReleased, ErpIntegrationEventVersions.V1));

    public Task HandleAsync(SalesOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        guard.HandleAsync(integrationEvent, processor.ProcessAsync, cancellationToken);

    [CapSubscribe(nameof(SalesOrderReleasedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(SalesOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.SalesOrderChangedIntegrationEvent", ConsumerName)]
public sealed class SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SalesOrderChangedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource.ConsumerName;
    private readonly SalesOrderDemandEventProcessor processor = new(dbContext, deadLetterStore, ConsumerName);
    private readonly IntegrationEventConsumerGuard<SalesOrderChangedIntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.SalesOrderChanged, ErpIntegrationEventVersions.V1));

    public Task HandleAsync(SalesOrderChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        guard.HandleAsync(integrationEvent, processor.ProcessAsync, cancellationToken);

    [CapSubscribe(nameof(SalesOrderChangedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(SalesOrderChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Erp.SalesOrderCancelledIntegrationEvent", ConsumerName)]
public sealed class SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<SalesOrderCancelledIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource.ConsumerName;
    private readonly SalesOrderDemandEventProcessor processor = new(dbContext, deadLetterStore, ConsumerName);
    private readonly IntegrationEventConsumerGuard<SalesOrderCancelledIntegrationEvent> guard = new(
        new IntegrationEventEnvelopeValidator(), deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, ErpIntegrationEventTypes.SalesOrderCancelled, ErpIntegrationEventVersions.V1));

    public Task HandleAsync(SalesOrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        guard.HandleAsync(integrationEvent, processor.ProcessAsync, cancellationToken);

    [CapSubscribe(nameof(SalesOrderCancelledIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(SalesOrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) => HandleAsync(integrationEvent, cancellationToken);
}

internal sealed class SalesOrderDemandEventProcessor(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    string consumerName)
{
    public Task ProcessAsync(SalesOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        ProcessCoreAsync(integrationEvent, integrationEvent.Payload, "released", cancellationToken);

    public Task ProcessAsync(SalesOrderChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        ProcessCoreAsync(integrationEvent, integrationEvent.Payload, "released", cancellationToken);

    public Task ProcessAsync(SalesOrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        ProcessCoreAsync(integrationEvent, integrationEvent.Payload, "cancelled", cancellationToken);

    private async Task ProcessCoreAsync(IIntegrationEventEnvelope integrationEvent, SalesOrderLifecyclePayload payload, string expectedStatus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var validationError = Validate(integrationEvent, payload, expectedStatus);
        if (validationError is not null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(consumerName, integrationEvent, "invalid-sales-order-demand-payload", validationError),
                cancellationToken);
            return;
        }

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                await ProcessValidatedAsync(integrationEvent, payload, cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 4)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (
                attempt < 4 &&
                ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, "PK_sales_order_demand_projections"))
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task ProcessValidatedAsync(IIntegrationEventEnvelope integrationEvent, SalesOrderLifecyclePayload payload, CancellationToken cancellationToken)
    {
        if (!await DemandPlanningProcessedIntegrationEventInbox.TryRecordAsync(dbContext, consumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var projection = await dbContext.SalesOrderDemandProjections.SingleOrDefaultAsync(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.SalesOrderId == payload.SalesOrderId,
            cancellationToken);

        if (projection is not null && payload.OrderVersion <= projection.OrderVersion)
        {
            await SaveAsync(cancellationToken);
            return;
        }

        var existingDemands = await dbContext.DemandSources.Where(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.DemandType == "sales-order" &&
            x.SourceDocumentId == payload.SalesOrderId).ToListAsync(cancellationToken);
        var existingByLine = existingDemands.ToDictionary(x => x.SourceLineReference, StringComparer.Ordinal);
        var isOrderCancelled = string.Equals(payload.Status, "cancelled", StringComparison.Ordinal);
        var activeLineReferences = new HashSet<string>(StringComparer.Ordinal);

        if (!isOrderCancelled)
        {
            foreach (var line in payload.Lines.Where(x => !x.Cancelled))
            {
                activeLineReferences.Add(line.SalesOrderLineNo);
                if (existingByLine.TryGetValue(line.SalesOrderLineNo, out var demand))
                {
                    demand.ApplySalesOrderSnapshot(line.Quantity, line.RequiredDate, payload.OrderVersion);
                }
                else
                {
                    dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        payload.SalesOrderId,
                        payload.SalesOrderNo,
                        line.SalesOrderLineNo,
                        payload.CustomerCode,
                        line.SkuCode,
                        line.UomCode,
                        payload.SiteCode,
                        line.Quantity,
                        line.RequiredDate,
                        payload.OrderVersion));
                }
            }
        }

        foreach (var demand in existingDemands.Where(x => isOrderCancelled || !activeLineReferences.Contains(x.SourceLineReference)))
        {
            demand.CancelFromSalesOrder(payload.OrderVersion);
        }

        if (projection is null)
        {
            dbContext.SalesOrderDemandProjections.Add(new SalesOrderDemandProjection(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.SalesOrderId,
                payload.SalesOrderNo,
                payload.CustomerCode,
                payload.SiteCode,
                payload.OrderVersion,
                payload.Status,
                integrationEvent.EventId,
                integrationEvent.OccurredAtUtc));
        }
        else
        {
            projection.Apply(payload.SalesOrderNo, payload.CustomerCode, payload.SiteCode, payload.OrderVersion, payload.Status, integrationEvent.EventId, integrationEvent.OccurredAtUtc);
        }

        await SaveAsync(cancellationToken);
    }

    private Task<int> SaveAsync(CancellationToken cancellationToken) =>
        ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
            dbContext, dbContext.SaveChangesAsync, cancellationToken);

    private static string? Validate(IIntegrationEventEnvelope integrationEvent, SalesOrderLifecyclePayload payload, string expectedStatus)
    {
        if (!string.Equals(integrationEvent.SourceService, ErpIntegrationEventSources.BusinessErp, StringComparison.Ordinal))
        {
            return $"Unexpected source service '{integrationEvent.SourceService}'.";
        }

        if (payload.OrderVersion <= 0 || string.IsNullOrWhiteSpace(payload.SalesOrderId) ||
            string.IsNullOrWhiteSpace(payload.SalesOrderNo) || string.IsNullOrWhiteSpace(payload.CustomerCode) ||
            string.IsNullOrWhiteSpace(payload.SiteCode) || payload.Lines is null)
        {
            return "Sales order identity, customer, site, positive version, and full line snapshot are required.";
        }

        if (!string.Equals(payload.Status, expectedStatus, StringComparison.Ordinal))
        {
            return $"Event fact requires sales order status '{expectedStatus}', but payload supplied '{payload.Status}'.";
        }

        if (payload.Lines.GroupBy(x => x.SalesOrderLineNo, StringComparer.Ordinal).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1) ||
            payload.Lines.Any(line => string.IsNullOrWhiteSpace(line.SkuCode) || string.IsNullOrWhiteSpace(line.UomCode) || line.RequiredDate == default || (!line.Cancelled && line.Quantity <= 0m)))
        {
            return "Sales order lines require unique line numbers, SKU, UOM, due date, and positive active quantity.";
        }

        return null;
    }
}

internal static class DemandPlanningProcessedIntegrationEventInbox
{
    public static Task<bool> TryRecordAsync(ApplicationDbContext dbContext, string consumerName, IIntegrationEventEnvelope integrationEvent, CancellationToken cancellationToken) =>
        ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            consumerName,
            integrationEvent,
            record => new ProcessedIntegrationEvent(record.ConsumerName, record.EventId, record.EventType, record.EventVersion, record.SourceService, record.IdempotencyKey, record.ProcessedAtUtc),
            cancellationToken);
}
