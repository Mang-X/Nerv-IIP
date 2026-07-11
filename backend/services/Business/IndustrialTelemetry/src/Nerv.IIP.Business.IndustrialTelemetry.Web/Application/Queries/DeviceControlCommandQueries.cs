using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record DeviceControlCommandApproval(
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed record DeviceControlCommandAttempt(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? FailureCode,
    IReadOnlyDictionary<string, string>? Output);

public sealed record DeviceControlCommandResult(
    string CommandId,
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    IReadOnlyDictionary<string, string>? Parameters,
    string RequestedBy,
    string Reason,
    string CorrelationId,
    string IdempotencyKey,
    DateTimeOffset RequestedAtUtc,
    string Status,
    bool StatusFromLiveOps,
    DeviceControlCommandApproval? Approval,
    string? CurrentAttemptId,
    IReadOnlyList<DeviceControlCommandAttempt> Attempts);

public sealed record DeviceControlCommandListItem(
    string CommandId,
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    string RequestedBy,
    string Reason,
    string Status,
    string? ApprovalStatus,
    string? FailureCode,
    string? DeviceReceiptCode,
    string? DeviceReceiptMessage,
    string CorrelationId,
    DateTimeOffset RequestedAtUtc);

// Single device control command result. The ledger holds the IndustrialTelemetry-owned command
// context and its dispatch-time status snapshot; the current status, approval decision and
// execution receipt (operation attempt output) are refreshed live from the authoritative Ops task.
public sealed record GetDeviceControlCommandQuery(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId) : IQuery<DeviceControlCommandResult>;

public sealed class GetDeviceControlCommandQueryHandler(
    ApplicationDbContext dbContext,
    IDeviceControlOpsClient opsClient)
    : IQueryHandler<GetDeviceControlCommandQuery, DeviceControlCommandResult>
{
    public async Task<DeviceControlCommandResult> Handle(GetDeviceControlCommandQuery request, CancellationToken cancellationToken)
    {
        // Device control audit is a device-resource surface: scope the lookup by device asset so a
        // caller authorized for one device cannot read another device's command by guessing a command id.
        var command = await dbContext.DeviceControlCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OperationTaskId == request.OperationTaskId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.DeviceAssetId == request.DeviceAssetId,
                cancellationToken)
            ?? throw new KnownException($"Device control command was not found: {request.OperationTaskId}");

        var task = await TryGetOperationTaskAsync(request.OperationTaskId, cancellationToken);
        return MapResult(command, task);
    }

    private async Task<OperationTaskResponse?> TryGetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        try
        {
            return await opsClient.GetDeviceControlTaskAsync(operationTaskId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Ops is unavailable or no longer holds the task; fall back to the ledger snapshot.
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static DeviceControlCommandResult MapResult(DeviceControlCommand command, OperationTaskResponse? task)
    {
        var approval = task?.Approval is { } liveApproval
            ? new DeviceControlCommandApproval(
                liveApproval.Status,
                liveApproval.RequestedBy,
                liveApproval.RequestedAtUtc,
                liveApproval.DecidedBy,
                liveApproval.DecidedAtUtc,
                liveApproval.DecisionReason)
            : command.ApprovalStatus is null
                ? null
                : new DeviceControlCommandApproval(command.ApprovalStatus, command.RequestedBy, command.RequestedAtUtc, null, null, null);

        var attempts = task?.Attempts
            .Select(attempt => new DeviceControlCommandAttempt(
                attempt.AttemptId,
                attempt.Status,
                attempt.StartedAtUtc,
                attempt.FinishedAtUtc,
                attempt.FailureCode,
                attempt.Output))
            .ToArray() ?? [];

        return new DeviceControlCommandResult(
            command.OperationTaskId,
            command.OperationTaskId,
            command.OrganizationId,
            command.EnvironmentId,
            command.ConnectorHostId,
            command.InstanceKey,
            command.DeviceAssetId,
            command.CommandType,
            command.TagKey,
            command.Value,
            DeserializeParameters(command.ParametersJson),
            command.RequestedBy,
            command.Reason,
            command.CorrelationId,
            command.IdempotencyKey,
            command.RequestedAtUtc,
            task?.Status ?? command.Status,
            task is not null,
            approval,
            task?.CurrentAttemptId,
            attempts);
    }

    private static IReadOnlyDictionary<string, string>? DeserializeParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson);
    }
}

// Device control command history, filtered by device / status / time window and paged. The status
// column is the dispatch-time Ops snapshot; live status progression per row is tracked as follow-up
// (Ops OperationTaskCompleted/Failed consumer), while the single-command read-face already refreshes
// status live from Ops.
public sealed record ListDeviceControlCommandsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip = 0,
    int Take = 100) : IQuery<PagedListResponse<DeviceControlCommandListItem>>;

public sealed class ListDeviceControlCommandsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDeviceControlCommandsQuery, PagedListResponse<DeviceControlCommandListItem>>
{
    public async Task<PagedListResponse<DeviceControlCommandListItem>> Handle(ListDeviceControlCommandsQuery request, CancellationToken cancellationToken)
    {
        var normalizedDevice = IndustrialTelemetryText.Optional(request.DeviceAssetId);
        var normalizedStatus = IndustrialTelemetryText.Optional(request.Status)?.ToLowerInvariant();
        var fromUnixMilliseconds = request.FromUtc?.ToUnixTimeMilliseconds();
        var toUnixMilliseconds = request.ToUtc?.ToUnixTimeMilliseconds();

        var query = dbContext.DeviceControlCommands
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => normalizedDevice == null || x.DeviceAssetId == normalizedDevice)
            .Where(x => normalizedStatus == null || x.Status.ToLower() == normalizedStatus)
            .Where(x => fromUnixMilliseconds == null || x.RequestedAtUnixTimeMilliseconds >= fromUnixMilliseconds)
            .Where(x => toUnixMilliseconds == null || x.RequestedAtUnixTimeMilliseconds <= toUnixMilliseconds);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUnixTimeMilliseconds)
            .ThenByDescending(x => x.OperationTaskId)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new DeviceControlCommandListItem(
                x.OperationTaskId,
                x.OperationTaskId,
                x.OrganizationId,
                x.EnvironmentId,
                x.ConnectorHostId,
                x.DeviceAssetId,
                x.CommandType,
                x.TagKey,
                x.Value,
                x.RequestedBy,
                x.Reason,
                x.Status,
                x.ApprovalStatus,
                x.FailureCode,
                x.DeviceReceiptCode,
                x.DeviceReceiptMessage,
                x.CorrelationId,
                x.RequestedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new PagedListResponse<DeviceControlCommandListItem>(items, total);
    }
}
