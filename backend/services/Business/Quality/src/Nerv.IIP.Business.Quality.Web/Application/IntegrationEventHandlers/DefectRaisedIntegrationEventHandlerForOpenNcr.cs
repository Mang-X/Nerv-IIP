using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.DefectRaisedIntegrationEvent", ConsumerName)]
public sealed class DefectRaisedIntegrationEventHandlerForOpenNcr(
    ApplicationDbContext dbContext,
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<DefectRaisedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-defect-raised";

    private const string MesDefectSourceType = "in-process";
    private const string UnknownMesSkuCode = "MES-SKU-UNRESOLVED";

    private readonly IntegrationEventConsumerGuard<DefectRaisedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.DefectRaised,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(DefectRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(DefectRaisedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(DefectRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(DefectRaisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        if (await NcrExistsForMesDefectAsync(integrationEvent, payload.DefectNo, cancellationToken))
        {
            return;
        }

        try
        {
            await sender.Send(
                new CreateNonconformanceReportCommand(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    MesDefectSourceType,
                    payload.DefectNo,
                    UnknownMesSkuCode,
                    payload.Quantity,
                    payload.DefectCode,
                    null,
                    null,
                    []),
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsMesDefectSourceUniqueConflict(exception))
        {
            DetachPendingMesDefectNcr(integrationEvent, payload.DefectNo);
        }
    }

    private async Task<bool> NcrExistsForMesDefectAsync(
        DefectRaisedIntegrationEvent integrationEvent,
        string defectNo,
        CancellationToken cancellationToken)
    {
        if (dbContext.NonconformanceReports.Local.Any(x =>
            x.OrganizationId == integrationEvent.OrganizationId &&
            x.EnvironmentId == integrationEvent.EnvironmentId &&
            x.SourceType == MesDefectSourceType &&
            x.SourceDocumentId == defectNo))
        {
            return true;
        }

        return await dbContext.NonconformanceReports.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourceType == MesDefectSourceType &&
                x.SourceDocumentId == defectNo,
            cancellationToken);
    }

    private static bool IsMesDefectSourceUniqueConflict(DbUpdateException exception)
    {
        var text = $"{exception.Message} {exception.InnerException?.Message}";
        if (text.Contains("ux_ncr_mes_defect_source", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var innerException = exception.InnerException;
        var sqlState = innerException?.GetType().GetProperty("SqlState")?.GetValue(innerException) as string;
        var constraintName = innerException?.GetType().GetProperty("ConstraintName")?.GetValue(innerException) as string;
        return string.Equals(sqlState, "23505", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(constraintName, "ux_ncr_mes_defect_source", StringComparison.OrdinalIgnoreCase);
    }

    private void DetachPendingMesDefectNcr(DefectRaisedIntegrationEvent integrationEvent, string defectNo)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<NonconformanceReport>()
                     .Where(entry =>
                         entry.State == EntityState.Added &&
                         entry.Entity.OrganizationId == integrationEvent.OrganizationId &&
                         entry.Entity.EnvironmentId == integrationEvent.EnvironmentId &&
                         entry.Entity.SourceType == MesDefectSourceType &&
                         entry.Entity.SourceDocumentId == defectNo)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
