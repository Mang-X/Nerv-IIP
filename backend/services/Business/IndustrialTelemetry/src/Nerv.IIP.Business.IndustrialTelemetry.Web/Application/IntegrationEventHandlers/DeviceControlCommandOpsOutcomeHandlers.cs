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
    // output of the EXACT attempt the event names, then advance status + receipt together.
    //
    // Reliability: exceptions are NOT swallowed. Because the resolver runs before the ledger is advanced,
    // a transient Ops fetch failure/timeout/cancellation (or an event whose attempt is not yet present in
    // the task snapshot) propagates and lets CAP retry the whole handler; the ledger stays non-terminal so
    // the receipt is never permanently lost to first-terminal-wins. Only the exact attempt is trusted, so
    // a retry chain cannot mislabel this outcome with another attempt's receipt. An attempt that genuinely
    // carries no device receipt (connector emits none) resolves to (null,null) so status still advances.
    public static async Task<(string? DeviceReceiptCode, string? DeviceReceiptMessage)> ResolveDeviceReceiptAsync(
        IDeviceControlOpsClient opsClient,
        string operationTaskId,
        string attemptId,
        CancellationToken cancellationToken)
    {
        var task = await opsClient.GetDeviceControlTaskAsync(operationTaskId, cancellationToken);
        var attempt = task.Attempts.FirstOrDefault(x => string.Equals(x.AttemptId, attemptId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Ops task '{operationTaskId}' does not yet contain attempt '{attemptId}' for device receipt resolution; retrying.");
        var output = attempt.Output;
        if (output is null)
        {
            return (null, null);
        }

        output.TryGetValue("deviceReceiptCode", out var code);
        output.TryGetValue("deviceReceiptMessage", out var message);
        return (code, message);
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

    [CapSubscribe(nameof(OperationTaskCompletedIntegrationEvent), Group = ConsumerName)]
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

    [CapSubscribe(nameof(OperationTaskFailedIntegrationEvent), Group = ConsumerName)]
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

    [CapSubscribe(nameof(OperationApprovalRejectedIntegrationEvent), Group = ConsumerName)]
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
