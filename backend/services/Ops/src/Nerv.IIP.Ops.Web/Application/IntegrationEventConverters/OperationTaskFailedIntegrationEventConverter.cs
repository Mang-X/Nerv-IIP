using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.DomainEvents;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Ops.Web.Application.IntegrationEventConverters;

public sealed class OperationTaskFailedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskFailedDomainEvent, OperationTaskFailedIntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OperationTaskFailedIntegrationEvent Convert(OperationTaskFailedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var attempt = domainEvent.Attempt;
        var result = domainEvent.Result;
        var finishedAtUtc = result.FinishedAtUtc;

        return new OperationTaskFailedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            "ops.OperationTaskFailed",
            1,
            finishedAtUtc,
            "ops",
            result.Context.CorrelationId,
            task.Id.Id,
            task.OrganizationId,
            task.EnvironmentId,
            attempt.ConnectorHostId,
            $"ops:operation-task-failed:{task.Id.Id}:{attempt.Id.Id}",
            new OperationTaskFailedPayload(
                task.Id.Id,
                attempt.Id.Id,
                task.InstanceKey,
                task.OperationCode,
                finishedAtUtc,
                ReadFailureCode(attempt.FailureJson)));
    }

    private static string? ReadFailureCode(string? failureJson)
    {
        return string.IsNullOrWhiteSpace(failureJson)
            ? null
            : JsonSerializer.Deserialize<FailureReason>(failureJson, JsonOptions)?.Code;
    }
}
