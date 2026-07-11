using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(TopicName, ConsumerName)]
public sealed class TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ISender sender)
    : IIntegrationEventHandler<TelemetryProductionCountDeltaIntegrationEvent>, ICapSubscribe
{
    public const string TopicName = "Nerv.IIP.Contracts.IndustrialTelemetry.TelemetryProductionCountDeltaIntegrationEvent";
    public const string ConsumerName = "business-mes.industrial-telemetry-production-count";

    private readonly IntegrationEventConsumerGuard<TelemetryProductionCountDeltaIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            IndustrialTelemetryIntegrationEventTypes.ProductionCountDeltaRecorded,
            IndustrialTelemetryIntegrationEventVersions.V1));

    public Task HandleAsync(TelemetryProductionCountDeltaIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(TopicName, Group = ConsumerName)]
    public Task HandleCapAsync(TelemetryProductionCountDeltaIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(TelemetryProductionCountDeltaIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var payloadValidation = ValidatePayload(integrationEvent.Payload);
        if (!payloadValidation.IsValid)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    payloadValidation.FailureCode,
                    payloadValidation.Message),
                cancellationToken);
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var payload = integrationEvent.Payload;
        var workCenterId = await ResolveWorkCenterIdAsync(integrationEvent, cancellationToken);
        var operation = workCenterId is null
            ? null
            : await ResolveCurrentOperationAsync(integrationEvent, workCenterId, cancellationToken);

        if (payload.HasActiveAlarm)
        {
            await AddPendingCandidateAsync(
                integrationEvent,
                workCenterId,
                operation,
                TelemetryProductionReportCandidate.ActiveAlarmSuspensionReason,
                cancellationToken);
            return;
        }

        if (operation is null)
        {
            await AddPendingCandidateAsync(
                integrationEvent,
                workCenterId,
                null,
                workCenterId is null
                    ? TelemetryProductionReportCandidate.NoWorkCenterMappingSuspensionReason
                    : TelemetryProductionReportCandidate.NoCurrentWorkOrderSuspensionReason,
                cancellationToken);
            return;
        }

        if (string.Equals(payload.ReportingMode, TelemetryProductionReportCandidate.DraftReportingMode, StringComparison.OrdinalIgnoreCase))
        {
            dbContext.TelemetryProductionReportCandidates.Add(TelemetryProductionReportCandidate.CreateDraft(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                integrationEvent.IdempotencyKey,
                payload.DeviceAssetId,
                payload.TagKey,
                payload.DeltaQuantity,
                payload.BucketStartUtc,
                payload.BucketEndUtc,
                workCenterId!,
                operation.WorkOrderId,
                operation.OperationTaskIdValue));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await sender.Send(new Commands.Production.RecordProductionReportCommand(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            operation.WorkOrderId,
            operation.OperationTaskIdValue,
            payload.DeltaQuantity,
            0m,
            CompletesOperation: false,
            payload.BucketEndUtc,
            IdempotencyKey: $"telemetry:{integrationEvent.IdempotencyKey}",
            Source: ProductionReport.TelemetrySource), cancellationToken);
    }

    private static PayloadValidationResult ValidatePayload(TelemetryProductionCountDeltaPayload? payload)
    {
        if (payload is null)
        {
            return PayloadValidationResult.Invalid("missing-payload", "Telemetry production count event payload is required.");
        }

        if (string.IsNullOrWhiteSpace(payload.DeviceAssetId) ||
            string.IsNullOrWhiteSpace(payload.TagKey) ||
            string.IsNullOrWhiteSpace(payload.SourceSequence))
        {
            return PayloadValidationResult.Invalid(
                "missing-payload-field",
                "Telemetry production count payload must include device, tag, and source sequence.");
        }

        if (!string.Equals(payload.ReportingMode, TelemetryProductionReportCandidate.DraftReportingMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payload.ReportingMode, TelemetryProductionReportCandidate.PostedReportingMode, StringComparison.OrdinalIgnoreCase))
        {
            return PayloadValidationResult.Invalid(
                "unsupported-reporting-mode",
                "Telemetry production count reporting mode must be draft or posted.");
        }

        if (payload.DeltaQuantity <= 0m)
        {
            return PayloadValidationResult.Invalid(
                "non-positive-count-delta",
                "Telemetry production count delta must be positive.");
        }

        if (payload.BucketEndUtc <= payload.BucketStartUtc)
        {
            return PayloadValidationResult.Invalid(
                "invalid-count-bucket",
                "Telemetry production count bucket end must be after its start.");
        }

        return PayloadValidationResult.Valid;
    }

    private async Task<string?> ResolveWorkCenterIdAsync(TelemetryProductionCountDeltaIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return await dbContext.DeviceAssetWorkCenterMappings
            .Where(x => (x.OrganizationId == null || x.OrganizationId == integrationEvent.OrganizationId) &&
                        (x.EnvironmentId == null || x.EnvironmentId == integrationEvent.EnvironmentId) &&
                        x.DeviceAssetId == integrationEvent.Payload.DeviceAssetId)
            .OrderByDescending(x => x.OrganizationId == integrationEvent.OrganizationId && x.EnvironmentId == integrationEvent.EnvironmentId)
            .Select(x => x.WorkCenterId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<OperationTask?> ResolveCurrentOperationAsync(
        TelemetryProductionCountDeltaIntegrationEvent integrationEvent,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        var operations = await dbContext.OperationTasks
            .Where(x => x.OrganizationId == integrationEvent.OrganizationId)
            .Where(x => x.EnvironmentId == integrationEvent.EnvironmentId)
            .Where(x => x.Status == OperationTaskLifecycleStatus.InProgress)
            .Where(x => x.WorkCenterId == workCenterId)
            .Where(x => x.DeviceAssetId == integrationEvent.Payload.DeviceAssetId)
            .OrderBy(x => x.WorkOrderId)
            .ThenBy(x => x.OperationSequence)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (operations.Length != 1)
        {
            return null;
        }

        var operation = operations[0];
        var isWorkOrderStarted = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.WorkOrderIdValue == operation.WorkOrderId &&
                x.Status == WorkOrder.StartedStatus,
            cancellationToken);
        return isWorkOrderStarted ? operation : null;
    }

    private async Task AddPendingCandidateAsync(
        TelemetryProductionCountDeltaIntegrationEvent integrationEvent,
        string? workCenterId,
        OperationTask? operation,
        string reason,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        dbContext.TelemetryProductionReportCandidates.Add(TelemetryProductionReportCandidate.CreatePendingConfirmation(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.IdempotencyKey,
            payload.DeviceAssetId,
            payload.TagKey,
            payload.ReportingMode,
            payload.DeltaQuantity,
            payload.BucketStartUtc,
            payload.BucketEndUtc,
            workCenterId,
            operation?.WorkOrderId,
            operation?.OperationTaskIdValue,
            reason));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private readonly record struct PayloadValidationResult(bool IsValid, string FailureCode, string Message)
    {
        public static PayloadValidationResult Valid => new(true, string.Empty, string.Empty);

        public static PayloadValidationResult Invalid(string failureCode, string message) =>
            new(false, failureCode, message);
    }
}
