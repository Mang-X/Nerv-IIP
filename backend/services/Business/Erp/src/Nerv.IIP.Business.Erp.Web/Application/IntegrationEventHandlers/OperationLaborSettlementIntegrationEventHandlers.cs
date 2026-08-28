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

        if (await HasLifecycleScopeConflictAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.OperationTaskId,
                payload.WorkOrderId,
                payload.WorkCenterId,
                cancellationToken))
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

        if (existing is not null)
        {
            await TryRecordInboxAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var rate = await FindApplicableRateAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkCenterId,
            payload.CompletedAtUtc,
            cancellationToken);
        if (rate is null)
        {
            await AddRateDeadLetterAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                payload.WorkCenterId,
                payload.CompletedAtUtc,
                cancellationToken);
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
            rate.Id,
            rate.Revision,
            rate.CurrencyCode,
            rate.HourlyRate,
            integrationEvent.EventId,
            payloadHash);
        var cost = await GetOrOpenWorkOrderCostAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            cancellationToken);
        if (settlement.Amount != 0m && !cost.TryFreezeLaborCurrency(rate.CurrencyCode))
        {
            await AddCurrencyDeadLetterAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                payload.WorkOrderId,
                rate.CurrencyCode,
                cancellationToken);
            return;
        }
        if (!await TryRecordInboxAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;

        dbContext.OperationLaborSettlements.Add(settlement);

        var state = await GetOrCreateStateAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            cancellationToken);
        var transition = state.ApplySettlement(payload.SettlementRevision);
        if (transition.Transition != OperationLaborSettlementTransition.Activated)
            return;

        await RecordCoveredReportsAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            payload.OperationTaskId,
            payload.SettlementRevision,
            coveredReports,
            cancellationToken);
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
        await PostLateAdjustmentIfCapitalizedAsync(
            cost,
            priorTotal,
            $"{payload.OperationTaskId}-r{payload.SettlementRevision}",
            payload.CompletedAtUtc,
            cancellationToken);
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

    private async Task<OperationLaborSettlementState> GetOrCreateStateAsync(string organizationId, string environmentId, string operationTaskId, CancellationToken cancellationToken)
    {
        var state = await dbContext.OperationLaborSettlementStates.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.OperationTaskId == operationTaskId, cancellationToken);
        if (state is not null) return state;
        state = OperationLaborSettlementState.Open(organizationId, environmentId, operationTaskId);
        dbContext.OperationLaborSettlementStates.Add(state);
        return state;
    }

    private async Task RecordCoveredReportsAsync(string organizationId, string environmentId, string workOrderId, string operationTaskId, long revision, IReadOnlyCollection<string> reports, CancellationToken cancellationToken)
    {
        foreach (var reportNo in reports)
            if (!await dbContext.OperationLaborCoveredReports.AnyAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.ReportNo == reportNo, cancellationToken))
                dbContext.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create(organizationId, environmentId, workOrderId, operationTaskId, revision, reportNo));
    }

    private Task<WorkCenterCostRate?> FindApplicableRateAsync(
        string organizationId,
        string environmentId,
        string workCenterId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
        => dbContext.WorkCenterCostRates
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.EffectiveFromUtc <= completedAtUtc
                && (x.EffectiveToUtc == null || completedAtUtc < x.EffectiveToUtc))
            .OrderByDescending(x => x.Revision)
            .FirstOrDefaultAsync(cancellationToken);

    private Task AddRateDeadLetterAsync<TIntegrationEvent>(string consumerName, TIntegrationEvent integrationEvent, string workCenterId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
        => deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(consumerName, integrationEvent, "missing-work-center-cost-rate", $"Work-center cost rate '{workCenterId}' has no active revision at '{completedAtUtc:O}'."), cancellationToken);

    private Task AddCurrencyDeadLetterAsync<TIntegrationEvent>(string consumerName, TIntegrationEvent integrationEvent, string workOrderId, string currencyCode, CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
        => deadLetterStore.AddAsync(IntegrationEventDeadLetterMessage.Create(consumerName, integrationEvent, "incompatible-work-order-labor-currency", $"Work order '{workOrderId}' already contains priced labor in a currency incompatible with '{currencyCode}'."), cancellationToken);

    private Task<bool> TryRecordInboxAsync<TIntegrationEvent>(
        string consumerName,
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
        => ErpProcessedIntegrationEventInbox.TryRecordAsync(dbContext, consumerName, integrationEvent, cancellationToken);

    private Task<bool> HasLifecycleScopeConflictAsync(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        string workCenterId,
        CancellationToken cancellationToken)
        => dbContext.OperationLaborSettlements.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.OperationTaskId == operationTaskId
                && (x.WorkOrderId != workOrderId || x.WorkCenterId != workCenterId),
            cancellationToken);

    private async Task<WorkOrderCost> GetOrOpenWorkOrderCostAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var cost = await dbContext.WorkOrderCosts
            .Include(x => x.Details)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkOrderId == workOrderId,
                cancellationToken);
        if (cost is null)
        {
            cost = WorkOrderCost.Open(organizationId, environmentId, workOrderId, workOrderId);
            dbContext.WorkOrderCosts.Add(cost);
        }
        return cost;
    }

    private Task<WorkOrderCost?> GetWorkOrderCostAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
        => dbContext.WorkOrderCosts
            .Include(x => x.Details)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkOrderId == workOrderId,
                cancellationToken);

    private async Task PostLateAdjustmentIfCapitalizedAsync(
        WorkOrderCost cost,
        decimal priorTotal,
        string postingIdentity,
        DateTimeOffset postedAtUtc,
        CancellationToken cancellationToken)
    {
        if (cost.IsFullyCapitalized)
            await CostVariancePosting.PostLateAdjustmentAsync(
                dbContext,
                cost,
                cost.TotalAccumulatedCost - priorTotal,
                postingIdentity,
                postedAtUtc,
                cancellationToken);
    }

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

        if (await HasLifecycleScopeConflictAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.OperationTaskId,
                payload.WorkOrderId,
                payload.WorkCenterId,
                cancellationToken))
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

        if (existingVoid is not null)
        {
            await TryRecordInboxAsync(
                MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var createsSettlement = settlement is null;
        if (settlement is null)
        {
            var rate = await FindApplicableRateAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkCenterId,
                payload.CompletedAtUtc,
                cancellationToken);
            if (rate is null)
            {
                await AddRateDeadLetterAsync(
                    MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                    integrationEvent,
                    payload.WorkCenterId,
                    payload.CompletedAtUtc,
                    cancellationToken);
                return;
            }
            settlement = OperationLaborSettlement.Create(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                payload.OperationTaskId,
                payload.WorkCenterId,
                payload.SettlementRevision,
                payload.CompletedAtUtc,
                payload.ActualLaborTicks,
                rate.Id,
                rate.Revision,
                rate.CurrencyCode,
                rate.HourlyRate,
                integrationEvent.EventId,
                settlementPayloadHash);
        }

        var cost = await GetWorkOrderCostAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            cancellationToken);
        if (cost is not null && settlement.Amount != 0m && !cost.TryFreezeLaborCurrency(settlement.CurrencyCode))
        {
            await AddCurrencyDeadLetterAsync(
                MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                integrationEvent,
                payload.WorkOrderId,
                settlement.CurrencyCode,
                cancellationToken);
            return;
        }
        if (!await TryRecordInboxAsync(
                MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;
        if (createsSettlement)
            dbContext.OperationLaborSettlements.Add(settlement);

        var settlementVoid = OperationLaborSettlementVoid.Create(
            settlement,
            payload.VoidedAtUtc,
            integrationEvent.EventId,
            voidPayloadHash);
        dbContext.OperationLaborSettlementVoids.Add(settlementVoid);

        var state = await GetOrCreateStateAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            cancellationToken);
        var transition = state.ApplyVoid(payload.SettlementRevision);
        if (transition.Transition != OperationLaborSettlementTransition.Voided)
            return;

        await RecordCoveredReportsAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            payload.OperationTaskId,
            payload.SettlementRevision,
            coveredReports,
            cancellationToken);
        if (cost is null)
            return;

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

        await PostLateAdjustmentIfCapitalizedAsync(
            cost,
            priorTotal,
            $"{payload.OperationTaskId}-r{payload.SettlementRevision}-void",
            payload.VoidedAtUtc,
            cancellationToken);
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
