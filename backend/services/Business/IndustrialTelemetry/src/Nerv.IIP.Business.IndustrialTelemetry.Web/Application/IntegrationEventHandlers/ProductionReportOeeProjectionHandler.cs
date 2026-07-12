using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.ProductionReportRecordedIntegrationEvent", ConsumerName)]
public sealed class ProductionReportOeeProjectionHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-industrial-telemetry.production-report-oee-projection";

    private readonly IntegrationEventConsumerGuard<ProductionReportRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, MesIntegrationEventTypes.ProductionReportRecorded, MesIntegrationEventVersions.V1));

    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (string.IsNullOrWhiteSpace(payload.DeviceAssetId) || string.IsNullOrWhiteSpace(payload.WorkCenterId))
        {
            // A device-level OEE fact requires the MES assignment snapshot; skipping incomplete input
            // leaves the read model explicitly degraded instead of turning a malformed event into poison.
            return;
        }

        // MES allocates one immutable report number per production-report fact. The converter's
        // idempotency key deterministically includes this scoped report number; retain the business
        // identity here so the materialized fact remains traceable to the source report.
        var alreadyProjected = await dbContext.OeeProductionFacts.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourceReportNo == payload.ReportNo,
            cancellationToken);
        if (alreadyProjected)
        {
            return;
        }

        dbContext.OeeProductionFacts.Add(OeeProductionFact.Project(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.ReportNo,
            payload.WorkCenterId,
            payload.DeviceAssetId,
            payload.GoodQuantity,
            payload.ScrapQuantity,
            payload.ReworkQuantity,
            payload.UomCode,
            payload.TheoreticalRatePerHour,
            payload.ReportedAtUtc));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is OeeProductionFact) &&
            ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext))
        {
            // CAP can deliver the same report concurrently. The unique projection key is authoritative.
            dbContext.ChangeTracker.Clear();
        }
    }
}
