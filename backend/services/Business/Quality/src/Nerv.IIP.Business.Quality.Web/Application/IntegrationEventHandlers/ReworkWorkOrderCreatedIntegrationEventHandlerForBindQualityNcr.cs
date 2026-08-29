using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

public enum ReworkWorkOrderBindingOutcome
{
    Bound,
    AlreadyBound,
    NcrNotFoundInScope,
    PayloadMismatch,
    BindingConflict,
}

public interface IReworkWorkOrderBindingStore
{
    Task<ReworkWorkOrderBindingOutcome> BindAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        NonconformanceReportId ncrId,
        CancellationToken cancellationToken);
}

public interface IReworkWorkOrderBindingWriter
{
    Task<bool> TryWriteAsync(NonconformanceReport candidate, CancellationToken cancellationToken);
}

public sealed class ReworkWorkOrderBindingStore(
    ApplicationDbContext dbContext,
    IReworkWorkOrderBindingWriter bindingWriter) : IReworkWorkOrderBindingStore
{
    public async Task<ReworkWorkOrderBindingOutcome> BindAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        NonconformanceReportId ncrId,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var ncr = await dbContext.NonconformanceReports.SingleOrDefaultAsync(
            x => x.Id == ncrId
                && x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId,
            cancellationToken);
        if (ncr is null)
        {
            return ReworkWorkOrderBindingOutcome.NcrNotFoundInScope;
        }

        if (ncr.NcrCode != payload.SourceNcrCode
            || ncr.SkuCode != payload.SkuCode
            || ncr.DefectQuantity != payload.Quantity
            || ncr.BatchNo != payload.SourceLotNo
            || ncr.SerialNo != payload.SourceSerialNo)
        {
            return ReworkWorkOrderBindingOutcome.PayloadMismatch;
        }

        var existingWorkOrderId = ncr.ReworkWorkOrderId;
        try
        {
            ncr.BindReworkWorkOrder(payload.ReworkWorkOrderId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ReworkWorkOrderBindingOutcome.BindingConflict;
        }

        if (existingWorkOrderId is not null)
        {
            return ReworkWorkOrderBindingOutcome.AlreadyBound;
        }

        if (await bindingWriter.TryWriteAsync(ncr, cancellationToken))
        {
            return ReworkWorkOrderBindingOutcome.Bound;
        }

        var winningWorkOrderId = await dbContext.NonconformanceReports
            .AsNoTracking()
            .Where(x => x.Id == ncrId
                && x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId)
            .Select(x => x.ReworkWorkOrderId)
            .SingleAsync(cancellationToken);
        return string.Equals(winningWorkOrderId, payload.ReworkWorkOrderId, StringComparison.Ordinal)
            ? ReworkWorkOrderBindingOutcome.AlreadyBound
            : ReworkWorkOrderBindingOutcome.BindingConflict;
    }
}

public sealed class PostgresReworkWorkOrderBindingWriter(ApplicationDbContext dbContext)
    : IReworkWorkOrderBindingWriter
{
    public async Task<bool> TryWriteAsync(
        NonconformanceReport candidate,
        CancellationToken cancellationToken)
    {
        dbContext.Entry(candidate).State = EntityState.Detached;
        var affectedRows = await dbContext.NonconformanceReports
            .Where(x => x.Id == candidate.Id
                && x.OrganizationId == candidate.OrganizationId
                && x.EnvironmentId == candidate.EnvironmentId
                && x.ReworkWorkOrderId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.ReworkWorkOrderId, candidate.ReworkWorkOrderId)
                    .SetProperty(x => x.UpdatedAtUtc, candidate.UpdatedAtUtc),
                cancellationToken);
        return affectedRows == 1;
    }
}

[IntegrationEventConsumer(nameof(ReworkWorkOrderCreatedIntegrationEvent), ConsumerName)]
public sealed class ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr(
    IReworkWorkOrderBindingStore bindingStore,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ReworkWorkOrderCreatedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-rework-work-order-created";

    private readonly IntegrationEventConsumerGuard<ReworkWorkOrderCreatedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(ReworkWorkOrderCreatedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                integrationEvent.SourceService,
                QualityIntegrationEventSources.BusinessMes,
                StringComparison.Ordinal))
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.untrustedSource",
                $"Rework work order receipt source '{integrationEvent.SourceService}' is not MES.",
                cancellationToken);
            return;
        }

        var payload = integrationEvent.Payload;
        if (!Guid.TryParse(payload.SourceNcrId, out var ncrGuid))
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.invalidNcrId",
                $"Rework work order receipt NCR id '{payload.SourceNcrId}' is invalid.",
                cancellationToken);
            return;
        }

        var outcome = await bindingStore.BindAsync(
            integrationEvent,
            new NonconformanceReportId(ncrGuid),
            cancellationToken);
        if (outcome is ReworkWorkOrderBindingOutcome.Bound or ReworkWorkOrderBindingOutcome.AlreadyBound)
        {
            return;
        }

        if (outcome == ReworkWorkOrderBindingOutcome.NcrNotFoundInScope)
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.ncrNotFoundInScope",
                $"NCR '{payload.SourceNcrId}' was not found in the receipt scope.",
                cancellationToken);
            return;
        }

        if (outcome == ReworkWorkOrderBindingOutcome.PayloadMismatch)
        {
            await DeadLetterAsync(
                integrationEvent,
                "quality.reworkWorkOrderCreated.payloadMismatch",
                $"Rework work order receipt does not match NCR '{payload.SourceNcrId}' source facts.",
                cancellationToken);
            return;
        }

        await DeadLetterAsync(
            integrationEvent,
            "quality.reworkWorkOrderCreated.bindingConflict",
            $"NCR '{payload.SourceNcrId}' rejected MES rework work order '{payload.ReworkWorkOrderId}'.",
            cancellationToken);
    }

    private Task DeadLetterAsync(
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken) =>
        deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
}
