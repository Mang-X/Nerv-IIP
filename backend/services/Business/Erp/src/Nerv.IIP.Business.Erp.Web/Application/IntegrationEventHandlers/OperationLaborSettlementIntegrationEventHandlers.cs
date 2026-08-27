using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationActualTimeSettledIntegrationEvent", ConsumerName)]
public sealed class MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    OperationLaborSettlementOrchestrator orchestrator)
    : IIntegrationEventHandler<MesOperationActualTimeSettledIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.operation-actual-time-labor-cost";

    public Task HandleAsync(
        MesOperationActualTimeSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        return CostingIntegrationEventUnitOfWork.ExecuteAsync(
            dbContext,
            unitOfWork,
            async () =>
            {
                await mutationLock.AcquireAsync(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    integrationEvent.Payload.WorkOrderId,
                    cancellationToken);
                await orchestrator.ProcessSettlementAsync(integrationEvent, cancellationToken);
            },
            cancellationToken);
    }

    [CapSubscribe(nameof(MesOperationActualTimeSettledIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationActualTimeSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);
}

public sealed partial class OperationLaborSettlementOrchestrator
{
    private readonly ApplicationDbContext dbContext;
    private readonly IIntegrationEventDeadLetterStore deadLetterStore;

    public OperationLaborSettlementOrchestrator(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore)
    {
        this.dbContext = dbContext;
        this.deadLetterStore = deadLetterStore;
    }

    public async Task ProcessSettlementAsync(
        MesOperationActualTimeSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var coveredReports = NormalizeCoveredReports(payload.CoveredProductionReportNos);
        var payloadHash = ComputePayloadHash(integrationEvent, coveredReports);
        var existing = await dbContext.OperationLaborSettlements.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId
                && x.SettlementRevision == payload.SettlementRevision,
            cancellationToken);
        if (existing is not null && !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(integrationEvent, cancellationToken);
            return;
        }

        var conflictingCoveredReport = await dbContext.OperationLaborCoveredReports.FirstOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && coveredReports.Contains(x.ReportNo)
                && x.OperationTaskId != payload.OperationTaskId,
            cancellationToken);
        if (conflictingCoveredReport is not null)
        {
            await AddConflictDeadLetterAsync(integrationEvent, cancellationToken);
            return;
        }

        WorkCenterCostRate? rate = existing is null
            ? await dbContext.WorkCenterCostRates
                .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkCenterId == payload.WorkCenterId
                    && x.EffectiveFromUtc <= payload.CompletedAtUtc
                    && (x.EffectiveToUtc == null || payload.CompletedAtUtc < x.EffectiveToUtc))
                .OrderByDescending(x => x.Revision)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        if (existing is null && rate is null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                    integrationEvent,
                    "missing-work-center-cost-rate",
                    $"Work-center cost rate '{payload.WorkCenterId}' has no active revision at '{payload.CompletedAtUtc:O}'."),
                cancellationToken);
            return;
        }
        if (existing is null && await dbContext.OperationLaborSettlements.AnyAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkOrderId == payload.WorkOrderId
                    && x.CurrencyCode != rate!.CurrencyCode,
                cancellationToken))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                    integrationEvent,
                    "incompatible-work-order-labor-currency",
                    $"Work order '{payload.WorkOrderId}' already contains actual labor in a currency incompatible with '{rate!.CurrencyCode}'."),
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;

        if (existing is not null)
        {
            return;
        }

        var settlement = OperationLaborSettlement.Create(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            payload.OperationTaskId,
            payload.WorkCenterId,
            payload.SettlementRevision,
            payload.CompletedAtUtc,
            payload.ActualLaborTicks,
            rate!.Id,
            rate.Revision,
            rate.CurrencyCode,
            rate.HourlyRate,
            integrationEvent.EventId,
            payloadHash);
        dbContext.OperationLaborSettlements.Add(settlement);

        var state = await dbContext.OperationLaborSettlementStates.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId,
            cancellationToken);
        if (state is null)
        {
            state = OperationLaborSettlementState.Open(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.OperationTaskId);
            dbContext.OperationLaborSettlementStates.Add(state);
        }

        var transition = state.ApplySettlement(payload.SettlementRevision);
        if (transition.Transition != OperationLaborSettlementTransition.Activated)
            return;

        foreach (var reportNo in coveredReports)
        {
            if (!await dbContext.OperationLaborCoveredReports.AnyAsync(
                    x => x.OrganizationId == integrationEvent.OrganizationId
                        && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.ReportNo == reportNo,
                    cancellationToken))
                dbContext.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    payload.WorkOrderId,
                    payload.OperationTaskId,
                    payload.SettlementRevision,
                    reportNo));
        }

        {
            var cost = await dbContext.WorkOrderCosts
                .Include(x => x.Details)
                .SingleOrDefaultAsync(
                    x => x.OrganizationId == integrationEvent.OrganizationId
                        && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.WorkOrderId == payload.WorkOrderId,
                    cancellationToken);
            if (cost is null)
            {
                cost = WorkOrderCost.Open(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    payload.WorkOrderId,
                    payload.WorkOrderId);
                dbContext.WorkOrderCosts.Add(cost);
            }

            var priorTotal = cost.TotalAccumulatedCost;
            if (transition.PreviousActiveRevision is { } previousRevision)
            {
                var previous = await dbContext.OperationLaborSettlements.SingleAsync(
                    x => x.OrganizationId == integrationEvent.OrganizationId
                        && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.OperationTaskId == payload.OperationTaskId
                        && x.SettlementRevision == previousRevision,
                    cancellationToken);
                cost.RecordActualLaborSuperseded(previous, payload.SettlementRevision, payload.CompletedAtUtc);
            }

            cost.ReplaceAllTheoreticalLabor(
                $"actual-labor:{payload.OperationTaskId}:r{payload.SettlementRevision}",
                payload.CompletedAtUtc);
            cost.RecordActualLabor(settlement);

            if (cost.CapitalizationPublished)
                await CostVariancePosting.PostLateAdjustmentAsync(
                    dbContext,
                    cost,
                    cost.TotalAccumulatedCost - priorTotal,
                    $"{payload.OperationTaskId}-r{payload.SettlementRevision}",
                    payload.CompletedAtUtc,
                    cancellationToken);
        }
    }

    private Task AddConflictDeadLetterAsync(
        MesOperationActualTimeSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                "conflicting-operation-labor-settlement",
                $"Operation actual-time settlement '{integrationEvent.Payload.OperationTaskId}' revision '{integrationEvent.Payload.SettlementRevision}' conflicts with the frozen business fact."),
            cancellationToken);

    internal static string[] NormalizeCoveredReports(IReadOnlyCollection<string> values)
        => values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    internal static string ComputePayloadHash(
        MesOperationActualTimeSettledIntegrationEvent integrationEvent,
        IReadOnlyCollection<string> coveredReports)
    {
        var payload = integrationEvent.Payload;
        return ComputeSettlementPayloadHash(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            payload.OperationTaskId,
            payload.WorkCenterId,
            payload.SettlementRevision,
            payload.CompletedAtUtc,
            payload.ActualLaborTicks,
            payload.ActualMachineTicks,
            coveredReports);
    }

    internal static string ComputeSettlementPayloadHash(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        long actualLaborTicks,
        long actualMachineTicks,
        IReadOnlyCollection<string> coveredReports)
    {
        var canonicalJson = JsonSerializer.Serialize(new
        {
            OrganizationId = organizationId,
            EnvironmentId = environmentId,
            WorkOrderId = workOrderId,
            OperationTaskId = operationTaskId,
            WorkCenterId = workCenterId,
            SettlementRevision = settlementRevision,
            CompletedAtUtc = completedAtUtc,
            ActualLaborTicks = actualLaborTicks,
            ActualMachineTicks = actualMachineTicks,
            CoveredProductionReportNos = coveredReports,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationActualTimeSettlementVoidedIntegrationEvent", ConsumerName)]
public sealed class MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    OperationLaborSettlementOrchestrator orchestrator)
    : IIntegrationEventHandler<MesOperationActualTimeSettlementVoidedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.operation-actual-time-labor-cost-void";

    public Task HandleAsync(
        MesOperationActualTimeSettlementVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.SourceService, MesIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        return CostingIntegrationEventUnitOfWork.ExecuteAsync(
            dbContext,
            unitOfWork,
            async () =>
            {
                await mutationLock.AcquireAsync(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    integrationEvent.Payload.WorkOrderId,
                    cancellationToken);
                await orchestrator.ProcessVoidAsync(integrationEvent, cancellationToken);
            },
            cancellationToken);
    }

    [CapSubscribe(nameof(MesOperationActualTimeSettlementVoidedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationActualTimeSettlementVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);
}

public sealed partial class OperationLaborSettlementOrchestrator
{
    public async Task ProcessVoidAsync(
        MesOperationActualTimeSettlementVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var coveredReports = NormalizeCoveredReports(payload.CoveredProductionReportNos);
        var settlementPayloadHash = ComputeSettlementPayloadHash(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                payload.OperationTaskId,
                payload.WorkCenterId,
                payload.SettlementRevision,
                payload.CompletedAtUtc,
                payload.ActualLaborTicks,
                payload.ActualMachineTicks,
                coveredReports);
        var voidPayloadHash = ComputeVoidPayloadHash(settlementPayloadHash, payload.VoidedAtUtc);

        var settlement = await dbContext.OperationLaborSettlements.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId
                && x.SettlementRevision == payload.SettlementRevision,
            cancellationToken);
        if (settlement is not null
            && !string.Equals(settlement.PayloadHash, settlementPayloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(integrationEvent, cancellationToken);
            return;
        }

        var existingVoid = await dbContext.OperationLaborSettlementVoids.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId
                && x.SettlementRevision == payload.SettlementRevision,
            cancellationToken);
        if (existingVoid is not null
            && !string.Equals(existingVoid.PayloadHash, voidPayloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(integrationEvent, cancellationToken);
            return;
        }

        WorkCenterCostRate? rate = settlement is null
            ? await dbContext.WorkCenterCostRates
                .Where(x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkCenterId == payload.WorkCenterId
                    && x.EffectiveFromUtc <= payload.CompletedAtUtc
                    && (x.EffectiveToUtc == null || payload.CompletedAtUtc < x.EffectiveToUtc))
                .OrderByDescending(x => x.Revision)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        if (settlement is null && rate is null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                    integrationEvent,
                    "missing-work-center-cost-rate",
                    $"Work-center cost rate '{payload.WorkCenterId}' has no active revision at '{payload.CompletedAtUtc:O}'."),
                cancellationToken);
            return;
        }
        if (settlement is null && await dbContext.OperationLaborSettlements.AnyAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkOrderId == payload.WorkOrderId
                    && x.CurrencyCode != rate!.CurrencyCode,
                cancellationToken))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                    integrationEvent,
                    "incompatible-work-order-labor-currency",
                    $"Work order '{payload.WorkOrderId}' already contains actual labor in a currency incompatible with '{rate!.CurrencyCode}'."),
                cancellationToken);
            return;
        }

        if (!await ErpProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;

        if (existingVoid is not null)
        {
            return;
        }

        if (settlement is null)
        {
            settlement = OperationLaborSettlement.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                payload.OperationTaskId,
                payload.WorkCenterId,
                payload.SettlementRevision,
                payload.CompletedAtUtc,
                payload.ActualLaborTicks,
                rate!.Id,
                rate.Revision,
                rate.CurrencyCode,
                rate.HourlyRate,
                integrationEvent.EventId,
                settlementPayloadHash);
            dbContext.OperationLaborSettlements.Add(settlement);
        }

        var settlementVoid = OperationLaborSettlementVoid.Create(
            settlement,
            payload.VoidedAtUtc,
            integrationEvent.EventId,
            voidPayloadHash);
        dbContext.OperationLaborSettlementVoids.Add(settlementVoid);

        var state = await dbContext.OperationLaborSettlementStates.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId,
            cancellationToken);
        if (state is null)
        {
            state = OperationLaborSettlementState.Open(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.OperationTaskId);
            dbContext.OperationLaborSettlementStates.Add(state);
        }

        var transition = state.ApplyVoid(payload.SettlementRevision);
        if (transition.Transition != OperationLaborSettlementTransition.Voided)
            return;

        foreach (var reportNo in coveredReports)
        {
            if (!await dbContext.OperationLaborCoveredReports.AnyAsync(
                    x => x.OrganizationId == integrationEvent.OrganizationId
                        && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.ReportNo == reportNo,
                    cancellationToken))
                dbContext.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    payload.WorkOrderId,
                    payload.OperationTaskId,
                    payload.SettlementRevision,
                    reportNo));
        }

        var cost = await dbContext.WorkOrderCosts
            .Include(x => x.Details)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.WorkOrderId == payload.WorkOrderId,
                cancellationToken);
        if (cost is not null)
        {
            var priorTotal = cost.TotalAccumulatedCost;
            foreach (var reportNo in coveredReports)
                cost.ReplaceTheoreticalLabor(
                    reportNo,
                    $"actual-labor:{payload.OperationTaskId}:r{payload.SettlementRevision}:void-replace:{reportNo}",
                    payload.VoidedAtUtc);

            if (transition.PreviousActiveRevision is { } previousActiveRevision)
            {
                if (previousActiveRevision == payload.SettlementRevision)
                    cost.RecordActualLaborVoid(settlementVoid);
                else
                {
                    var previous = await dbContext.OperationLaborSettlements.SingleAsync(
                        x => x.OrganizationId == integrationEvent.OrganizationId
                            && x.EnvironmentId == integrationEvent.EnvironmentId
                            && x.OperationTaskId == payload.OperationTaskId
                            && x.SettlementRevision == previousActiveRevision,
                        cancellationToken);
                    cost.RecordActualLaborSuperseded(previous, payload.SettlementRevision, payload.VoidedAtUtc);
                }
            }

            if (cost.CapitalizationPublished)
                await CostVariancePosting.PostLateAdjustmentAsync(
                    dbContext,
                    cost,
                    cost.TotalAccumulatedCost - priorTotal,
                    $"{payload.OperationTaskId}-r{payload.SettlementRevision}-void",
                    payload.VoidedAtUtc,
                    cancellationToken);
        }

    }

    private Task AddConflictDeadLetterAsync(
        MesOperationActualTimeSettlementVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                integrationEvent,
                "conflicting-operation-labor-settlement-void",
                $"Operation actual-time settlement void '{integrationEvent.Payload.OperationTaskId}' revision '{integrationEvent.Payload.SettlementRevision}' conflicts with the frozen business fact."),
            cancellationToken);

    private static string ComputeVoidPayloadHash(string settlementPayloadHash, DateTimeOffset voidedAtUtc)
    {
        var canonicalJson = JsonSerializer.Serialize(new { SettlementPayloadHash = settlementPayloadHash, VoidedAtUtc = voidedAtUtc });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }
}
