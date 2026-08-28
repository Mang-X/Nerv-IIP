using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationActualTimeSettledV2IntegrationEvent", ConsumerName)]
public sealed class MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    OperationMachineOverheadSettlementOrchestrator orchestrator)
    : IIntegrationEventHandler<MesOperationActualTimeSettledV2IntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.operation-machine-overhead";

    public Task HandleAsync(
        MesOperationActualTimeSettledV2IntegrationEvent integrationEvent,
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

    [CapSubscribe(nameof(MesOperationActualTimeSettledV2IntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationActualTimeSettledV2IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Mes.MesOperationActualTimeSettlementVoidedV2IntegrationEvent", ConsumerName)]
public sealed class MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead(
    ApplicationDbContext dbContext,
    ITransactionUnitOfWork unitOfWork,
    IWorkOrderCostMutationLock mutationLock,
    OperationMachineOverheadSettlementOrchestrator orchestrator)
    : IIntegrationEventHandler<MesOperationActualTimeSettlementVoidedV2IntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-erp.operation-machine-overhead-void";

    public Task HandleAsync(
        MesOperationActualTimeSettlementVoidedV2IntegrationEvent integrationEvent,
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

    [CapSubscribe(nameof(MesOperationActualTimeSettlementVoidedV2IntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        MesOperationActualTimeSettlementVoidedV2IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => HandleAsync(integrationEvent, cancellationToken);
}

public sealed class OperationMachineOverheadSettlementOrchestrator(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
{
    public async Task ProcessSettlementAsync(
        MesOperationActualTimeSettledV2IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var payloadHash = ComputeSettlementPayloadHash(integrationEvent);
        var existing = await FindSettlementAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            payload.SettlementRevision,
            cancellationToken);
        if (existing is not null && !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(
                MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
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
            await AddConflictDeadLetterAsync(
                MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        if (existing is not null)
        {
            await TryRecordInboxAsync(
                MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var settlement = await TryCreateSettlementAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            integrationEvent,
            payloadHash,
            cancellationToken);
        if (settlement is null)
            return;

        var cost = await GetOrOpenWorkOrderCostAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            cancellationToken);
        if (!cost.TryFreezeMachineOverheadCurrency(settlement.CurrencyCode))
        {
            await AddCurrencyDeadLetterAsync(
                MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
                integrationEvent,
                settlement.CurrencyCode,
                cancellationToken);
            return;
        }
        if (!await TryRecordInboxAsync(
                MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;

        dbContext.OperationMachineOverheadSettlements.Add(settlement);
        var state = await GetOrCreateStateAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            cancellationToken);
        var transition = state.ApplySettlement(payload.SettlementRevision);
        if (transition.Transition != OperationMachineOverheadSettlementTransition.Activated)
            return;

        var priorTotal = cost.TotalAccumulatedCost;
        if (transition.PreviousActiveRevision is { } previousRevision)
        {
            var previous = await dbContext.OperationMachineOverheadSettlements.SingleAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId
                    && x.EnvironmentId == integrationEvent.EnvironmentId
                    && x.OperationTaskId == payload.OperationTaskId
                    && x.SettlementRevision == previousRevision,
                cancellationToken);
            cost.RecordMachineOverheadSuperseded(previous, payload.SettlementRevision, payload.CompletedAtUtc);
        }
        cost.RecordMachineOverhead(settlement);
        await PostLateAdjustmentIfCapitalizedAsync(
            cost,
            priorTotal,
            $"machine-{payload.OperationTaskId}-r{payload.SettlementRevision}",
            payload.CompletedAtUtc,
            cancellationToken);
    }

    public async Task ProcessVoidAsync(
        MesOperationActualTimeSettlementVoidedV2IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var settlementPayloadHash = ComputeSettlementPayloadHash(integrationEvent);
        var voidPayloadHash = ComputeVoidPayloadHash(settlementPayloadHash, payload.VoidedAtUtc);
        var settlement = await FindSettlementAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            payload.SettlementRevision,
            cancellationToken);
        if (settlement is not null && !string.Equals(settlement.PayloadHash, settlementPayloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
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
            await AddConflictDeadLetterAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var existingVoid = await dbContext.OperationMachineOverheadSettlementVoids.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId
                && x.EnvironmentId == integrationEvent.EnvironmentId
                && x.OperationTaskId == payload.OperationTaskId
                && x.SettlementRevision == payload.SettlementRevision,
            cancellationToken);
        if (existingVoid is not null && !string.Equals(existingVoid.PayloadHash, voidPayloadHash, StringComparison.Ordinal))
        {
            await AddConflictDeadLetterAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }
        if (existingVoid is not null)
        {
            await TryRecordInboxAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var createsSettlement = settlement is null;
        settlement ??= await TryCreateSettlementAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            integrationEvent,
            settlementPayloadHash,
            cancellationToken);
        if (settlement is null)
            return;

        var cost = await GetWorkOrderCostAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.WorkOrderId,
            cancellationToken);
        if (cost is not null && !cost.TryFreezeMachineOverheadCurrency(settlement.CurrencyCode))
        {
            await AddCurrencyDeadLetterAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                settlement.CurrencyCode,
                cancellationToken);
            return;
        }
        if (!await TryRecordInboxAsync(
                MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
                integrationEvent,
                cancellationToken))
            return;

        if (createsSettlement)
            dbContext.OperationMachineOverheadSettlements.Add(settlement);
        var settlementVoid = OperationMachineOverheadSettlementVoid.Create(
            settlement,
            payload.VoidedAtUtc,
            integrationEvent.EventId,
            voidPayloadHash);
        dbContext.OperationMachineOverheadSettlementVoids.Add(settlementVoid);

        var state = await GetOrCreateStateAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            payload.OperationTaskId,
            cancellationToken);
        var transition = state.ApplyVoid(payload.SettlementRevision);
        if (transition.Transition != OperationMachineOverheadSettlementTransition.Voided || cost is null)
            return;

        var priorTotal = cost.TotalAccumulatedCost;
        if (transition.PreviousActiveRevision is { } previousActiveRevision)
        {
            if (previousActiveRevision == payload.SettlementRevision)
                cost.RecordMachineOverheadVoid(settlementVoid);
            else
            {
                var previous = await dbContext.OperationMachineOverheadSettlements.SingleAsync(
                    x => x.OrganizationId == integrationEvent.OrganizationId
                        && x.EnvironmentId == integrationEvent.EnvironmentId
                        && x.OperationTaskId == payload.OperationTaskId
                        && x.SettlementRevision == previousActiveRevision,
                    cancellationToken);
                cost.RecordMachineOverheadSuperseded(previous, payload.SettlementRevision, payload.VoidedAtUtc);
            }
        }
        await PostLateAdjustmentIfCapitalizedAsync(
            cost,
            priorTotal,
            $"machine-{payload.OperationTaskId}-r{payload.SettlementRevision}-void",
            payload.VoidedAtUtc,
            cancellationToken);
    }

    private async Task<OperationMachineOverheadSettlement?> TryCreateSettlementAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent switch
        {
            MesOperationActualTimeSettledV2IntegrationEvent settled => new MachinePayload(
                settled.OrganizationId, settled.EnvironmentId, settled.Payload.WorkOrderId,
                settled.Payload.OperationTaskId, settled.Payload.WorkCenterId, settled.Payload.SettlementRevision,
                settled.Payload.CompletedAtUtc, settled.Payload.DeviceAssetId, settled.Payload.MachineTimeStatus,
                settled.Payload.BillableMachineTicks, settled.Payload.MachineTimeBasisCode, settled.EventId),
            MesOperationActualTimeSettlementVoidedV2IntegrationEvent voided => new MachinePayload(
                voided.OrganizationId, voided.EnvironmentId, voided.Payload.WorkOrderId,
                voided.Payload.OperationTaskId, voided.Payload.WorkCenterId, voided.Payload.SettlementRevision,
                voided.Payload.CompletedAtUtc, voided.Payload.DeviceAssetId, voided.Payload.MachineTimeStatus,
                voided.Payload.BillableMachineTicks, voided.Payload.MachineTimeBasisCode, voided.EventId),
            _ => throw new ArgumentOutOfRangeException(nameof(integrationEvent)),
        };

        if (payload.MachineTimeStatus == MesMachineTimeFactStatus.Unavailable
            || (payload.MachineTimeStatus == MesMachineTimeFactStatus.Available
                && (string.IsNullOrWhiteSpace(payload.DeviceAssetId)
                    || payload.BillableMachineTicks is null or < 0
                    || !string.Equals(payload.MachineTimeBasisCode, MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, StringComparison.Ordinal))))
        {
            await AddUnavailableDeadLetterAsync(consumerName, integrationEvent, cancellationToken);
            return null;
        }

        ResolvedWorkCenterMachineOverheadRate resolved;
        try
        {
            resolved = await new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(dbContext).Handle(
                new(payload.OrganizationId, payload.EnvironmentId, payload.WorkCenterId, payload.CompletedAtUtc),
                cancellationToken);
        }
        catch (KnownException exception)
        {
            await AddRateDeadLetterAsync(consumerName, integrationEvent, exception.Message, cancellationToken);
            return null;
        }

        var rateApplicability = Enum.Parse<MachineOverheadApplicability>(resolved.Applicability, ignoreCase: false);
        var expectedApplicability = payload.MachineTimeStatus == MesMachineTimeFactStatus.NotApplicable
            ? MachineOverheadApplicability.NotApplicable
            : MachineOverheadApplicability.Applicable;
        if (rateApplicability != expectedApplicability)
        {
            await AddApplicabilityDeadLetterAsync(consumerName, integrationEvent, resolved.Applicability, cancellationToken);
            return null;
        }

        var rateId = new WorkCenterMachineOverheadRateId(Guid.Parse(resolved.WorkCenterMachineOverheadRateId));
        return rateApplicability == MachineOverheadApplicability.Applicable
            ? OperationMachineOverheadSettlement.CreateApplied(
                payload.OrganizationId, payload.EnvironmentId, payload.WorkOrderId, payload.OperationTaskId,
                payload.WorkCenterId, payload.SettlementRevision, payload.CompletedAtUtc,
                payload.DeviceAssetId!, payload.BillableMachineTicks!.Value, payload.MachineTimeBasisCode!,
                rateId, resolved.AccountingPeriodCode, resolved.Revision, resolved.CurrencyCode,
                resolved.FixedHourlyRate, resolved.VariableHourlyRate, payload.SourceEventId, payloadHash)
            : OperationMachineOverheadSettlement.CreateNotApplicable(
                payload.OrganizationId, payload.EnvironmentId, payload.WorkOrderId, payload.OperationTaskId,
                payload.WorkCenterId, payload.SettlementRevision, payload.CompletedAtUtc,
                rateId, resolved.AccountingPeriodCode, resolved.Revision, resolved.CurrencyCode,
                payload.SourceEventId, payloadHash);
    }

    private Task<OperationMachineOverheadSettlement?> FindSettlementAsync(
        string organizationId,
        string environmentId,
        string operationTaskId,
        long revision,
        CancellationToken cancellationToken)
        => dbContext.OperationMachineOverheadSettlements.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.OperationTaskId == operationTaskId
                && x.SettlementRevision == revision,
            cancellationToken);

    private Task<bool> HasLifecycleScopeConflictAsync(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workOrderId,
        string workCenterId,
        CancellationToken cancellationToken)
        => dbContext.OperationMachineOverheadSettlements.AnyAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.OperationTaskId == operationTaskId
                && (x.WorkOrderId != workOrderId || x.WorkCenterId != workCenterId),
            cancellationToken);

    private async Task<OperationMachineOverheadSettlementState> GetOrCreateStateAsync(
        string organizationId,
        string environmentId,
        string operationTaskId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.OperationMachineOverheadSettlementStates.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.OperationTaskId == operationTaskId,
            cancellationToken);
        if (state is not null)
            return state;
        state = OperationMachineOverheadSettlementState.Open(organizationId, environmentId, operationTaskId);
        dbContext.OperationMachineOverheadSettlementStates.Add(state);
        return state;
    }

    private async Task<WorkOrderCost> GetOrOpenWorkOrderCostAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var cost = await GetWorkOrderCostAsync(organizationId, environmentId, workOrderId, cancellationToken);
        if (cost is not null)
            return cost;
        cost = WorkOrderCost.Open(organizationId, environmentId, workOrderId, workOrderId);
        dbContext.WorkOrderCosts.Add(cost);
        return cost;
    }

    private Task<WorkOrderCost?> GetWorkOrderCostAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
        => dbContext.WorkOrderCosts.Include(x => x.Details).SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId,
            cancellationToken);

    private Task<bool> TryRecordInboxAsync<TIntegrationEvent>(
        string consumerName,
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
        => ErpProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext, consumerName, integrationEvent, cancellationToken);

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

    private Task AddUnavailableDeadLetterAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "unavailable-machine-time-fact",
                "MES machine-time fact is unavailable or not authoritative; machine overhead remains replayable."),
            cancellationToken);

    private Task AddRateDeadLetterAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string detail,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "missing-machine-overhead-rate",
                detail),
            cancellationToken);

    private Task AddApplicabilityDeadLetterAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string configuredApplicability,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "machine-overhead-applicability-conflict",
                $"MES machine-time status conflicts with ERP configured applicability '{configuredApplicability}'."),
            cancellationToken);

    private Task AddCurrencyDeadLetterAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        string currencyCode,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "incompatible-work-order-machine-overhead-currency",
                $"Work-order cost currency is incompatible with machine-overhead currency '{currencyCode}'."),
            cancellationToken);

    private Task AddConflictDeadLetterAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
        => deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "conflicting-operation-machine-overhead-settlement",
                "Operation machine-overhead settlement conflicts with the frozen business fact."),
            cancellationToken);

    internal static string ComputeSettlementPayloadHash(
        MesOperationActualTimeSettledV2IntegrationEvent integrationEvent)
        => ComputeSettlementPayloadHash(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.WorkOrderId,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.WorkCenterId,
            integrationEvent.Payload.SettlementRevision,
            integrationEvent.Payload.CompletedAtUtc,
            integrationEvent.Payload.DeviceAssetId,
            integrationEvent.Payload.MachineTimeStatus,
            integrationEvent.Payload.BillableMachineTicks,
            integrationEvent.Payload.MachineTimeBasisCode);

    internal static string ComputeSettlementPayloadHash(
        MesOperationActualTimeSettlementVoidedV2IntegrationEvent integrationEvent)
        => ComputeSettlementPayloadHash(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.WorkOrderId,
            integrationEvent.Payload.OperationTaskId,
            integrationEvent.Payload.WorkCenterId,
            integrationEvent.Payload.SettlementRevision,
            integrationEvent.Payload.CompletedAtUtc,
            integrationEvent.Payload.DeviceAssetId,
            integrationEvent.Payload.MachineTimeStatus,
            integrationEvent.Payload.BillableMachineTicks,
            integrationEvent.Payload.MachineTimeBasisCode);

    private static string ComputeSettlementPayloadHash(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        long settlementRevision,
        DateTimeOffset completedAtUtc,
        string? deviceAssetId,
        MesMachineTimeFactStatus machineTimeStatus,
        long? billableMachineTicks,
        string? machineTimeBasisCode)
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
            DeviceAssetId = deviceAssetId,
            MachineTimeStatus = machineTimeStatus,
            BillableMachineTicks = billableMachineTicks,
            MachineTimeBasisCode = machineTimeBasisCode,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    private static string ComputeVoidPayloadHash(string settlementPayloadHash, DateTimeOffset voidedAtUtc)
    {
        var canonicalJson = JsonSerializer.Serialize(new
        {
            SettlementPayloadHash = settlementPayloadHash,
            VoidedAtUtc = voidedAtUtc,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    private sealed record MachinePayload(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string OperationTaskId,
        string WorkCenterId,
        long SettlementRevision,
        DateTimeOffset CompletedAtUtc,
        string? DeviceAssetId,
        MesMachineTimeFactStatus MachineTimeStatus,
        long? BillableMachineTicks,
        string? MachineTimeBasisCode,
        string SourceEventId);
}
