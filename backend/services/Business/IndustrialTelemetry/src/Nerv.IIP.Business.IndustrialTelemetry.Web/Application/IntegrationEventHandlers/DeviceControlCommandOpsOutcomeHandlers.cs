using DotNetCore.CAP;
using MediatR;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;

internal static class DeviceControlOutcomeConsumer
{
    // Matches the Ops operation code the device control dispatch handler creates its tasks with.
    public const string DeviceControlOperationCode = "device.control.command";

    public static bool IsDeviceControlCommand(string? operationCode) =>
        string.Equals(operationCode, DeviceControlOperationCode, StringComparison.Ordinal);

    // The Ops completed/failed events carry only the generic FailureCode; the device-reported receipt
    // (e.g. Good/BadOutOfRange) lives on the connector attempt output. Fetch the Ops task and read the
    // attempt output so the ledger persists the real device receipt. Degrades to (null,null) if Ops is
    // unavailable or the output is absent — the generic FailureCode still lands via the event.
    public static async Task<(string? DeviceReceiptCode, string? DeviceReceiptMessage)> ResolveDeviceReceiptAsync(
        IDeviceControlOpsClient opsClient,
        string operationTaskId,
        string? attemptId,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = await opsClient.GetDeviceControlTaskAsync(operationTaskId, cancellationToken);
            var attempt = task.Attempts.FirstOrDefault(x => string.Equals(x.AttemptId, attemptId, StringComparison.Ordinal))
                ?? task.Attempts.FirstOrDefault(x => string.Equals(x.AttemptId, task.CurrentAttemptId, StringComparison.Ordinal))
                ?? task.Attempts.LastOrDefault();
            var output = attempt?.Output;
            if (output is null)
            {
                return (null, null);
            }

            output.TryGetValue("deviceReceiptCode", out var code);
            output.TryGetValue("deviceReceiptMessage", out var message);
            return (code, message);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", ConsumerName)]
public sealed class DeviceControlCommandCompletedHandler(
    ISender sender,
    IDeviceControlOpsClient opsClient,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-industrial-telemetry.device-control-completed";

    private readonly IntegrationEventConsumerGuard<OperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationTaskCompleted", 1));

    public async Task HandleAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationTaskCompletedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!DeviceControlOutcomeConsumer.IsDeviceControlCommand(integrationEvent.Payload.OperationCode))
        {
            return;
        }

        var (deviceReceiptCode, deviceReceiptMessage) = await DeviceControlOutcomeConsumer.ResolveDeviceReceiptAsync(
            opsClient, integrationEvent.Payload.OperationTaskId, integrationEvent.Payload.AttemptId, cancellationToken);
        await sender.Send(
            new AdvanceDeviceControlCommandStatusCommand(
                integrationEvent.Payload.OperationTaskId,
                "completed",
                integrationEvent.Payload.FinishedAtUtc,
                FailureCode: null,
                deviceReceiptCode,
                deviceReceiptMessage),
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", ConsumerName)]
public sealed class DeviceControlCommandFailedHandler(
    ISender sender,
    IDeviceControlOpsClient opsClient,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationTaskFailedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-industrial-telemetry.device-control-failed";

    private readonly IntegrationEventConsumerGuard<OperationTaskFailedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationTaskFailed", 1));

    public async Task HandleAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!DeviceControlOutcomeConsumer.IsDeviceControlCommand(integrationEvent.Payload.OperationCode))
        {
            return;
        }

        var (deviceReceiptCode, deviceReceiptMessage) = await DeviceControlOutcomeConsumer.ResolveDeviceReceiptAsync(
            opsClient, integrationEvent.Payload.OperationTaskId, integrationEvent.Payload.AttemptId, cancellationToken);
        await sender.Send(
            new AdvanceDeviceControlCommandStatusCommand(
                integrationEvent.Payload.OperationTaskId,
                "failed",
                integrationEvent.Payload.FinishedAtUtc,
                integrationEvent.Payload.FailureCode,
                deviceReceiptCode,
                deviceReceiptMessage),
            cancellationToken);
    }
}

[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationApprovalRejectedIntegrationEvent", ConsumerName)]
public sealed class DeviceControlCommandRejectedHandler(
    ISender sender,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<OperationApprovalRejectedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-industrial-telemetry.device-control-rejected";

    private readonly IntegrationEventConsumerGuard<OperationApprovalRejectedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(ConsumerName, "ops.OperationApprovalRejected", 1));

    public async Task HandleAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Ops.OperationApprovalRejectedIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(OperationApprovalRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!DeviceControlOutcomeConsumer.IsDeviceControlCommand(integrationEvent.Payload.OperationCode))
        {
            return;
        }

        // Approval rejection is a terminal outcome for the command; advance the ledger to rejected so the
        // history read-face stops showing the dispatch-time approval-pending snapshot.
        await sender.Send(
            new AdvanceDeviceControlCommandStatusCommand(
                integrationEvent.Payload.OperationTaskId,
                "rejected",
                integrationEvent.Payload.DecidedAtUtc,
                FailureCode: null),
            cancellationToken);
    }
}
