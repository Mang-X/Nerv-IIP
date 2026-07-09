using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

public sealed record CreateTelemetryTagCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    string ValueType,
    string UnitCode,
    string SamplingPolicy,
    bool IsWritable = false,
    decimal? ControlMinValue = null,
    decimal? ControlMaxValue = null,
    IReadOnlyCollection<string>? ControlAllowedValues = null) : ICommand<TelemetryTagId>;

public sealed class CreateTelemetryTagCommandValidator : AbstractValidator<CreateTelemetryTagCommand>
{
    public CreateTelemetryTagCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ValueType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.UnitCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SamplingPolicy)
            .NotEmpty()
            .MaximumLength(100)
            .Must(BeValidSamplingPolicy)
            .WithMessage("SamplingPolicy is invalid.");
        RuleFor(x => x.ControlMinValue)
            .LessThanOrEqualTo(x => x.ControlMaxValue)
            .When(x => x.ControlMinValue.HasValue && x.ControlMaxValue.HasValue);
        RuleForEach(x => x.ControlAllowedValues).MaximumLength(100);
    }

    private static bool BeValidSamplingPolicy(string samplingPolicy)
    {
        try
        {
            _ = TelemetrySamplingPolicy.Parse(samplingPolicy);
            return true;
        }
        catch (KnownException)
        {
            return false;
        }
    }
}

public sealed class CreateTelemetryTagCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateTelemetryTagCommand, TelemetryTagId>
{
    public async Task<TelemetryTagId> Handle(CreateTelemetryTagCommand request, CancellationToken cancellationToken)
    {
        var normalizedTagKey = request.TagKey.Trim().ToLowerInvariant();
        var existing = await dbContext.TelemetryTags.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey,
            cancellationToken);
        if (existing is not null)
        {
            existing.UpdateDefinition(request.ValueType, request.UnitCode, request.SamplingPolicy);
            existing.ConfigureControl(request.IsWritable, request.ControlMinValue, request.ControlMaxValue, request.ControlAllowedValues ?? []);
            return existing.Id;
        }

        var tag = TelemetryTag.Create(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.TagKey, request.ValueType, request.UnitCode, request.SamplingPolicy);
        tag.ConfigureControl(request.IsWritable, request.ControlMinValue, request.ControlMaxValue, request.ControlAllowedValues ?? []);
        dbContext.TelemetryTags.Add(tag);
        return tag.Id;
    }
}

public interface IDeviceControlOpsClient
{
    Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken);

    Task<OperationTaskResponse> GetDeviceControlTaskAsync(string operationTaskId, CancellationToken cancellationToken);
}

public sealed class DeviceControlOpsClient(IOpsClient opsClient) : IDeviceControlOpsClient
{
    public Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
    {
        return opsClient.CreateOperationTaskAsync(request, cancellationToken);
    }

    public Task<OperationTaskResponse> GetDeviceControlTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        return opsClient.GetOperationTaskAsync(operationTaskId, cancellationToken);
    }
}

public sealed record CreateDeviceControlCommandCommand(
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
    string IdempotencyKey,
    string CorrelationId) : ICommand<OperationTaskResponse>;

internal static class DeviceControlCommandValidation
{
    public static bool IsSupportedCommandType(string commandType)
    {
        return IsSingleTagCommand(commandType) || IsParameterSetCommand(commandType);
    }

    public static bool IsSingleTagCommand(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
        {
            return false;
        }

        var normalized = commandType.Trim().ToLowerInvariant();
        return normalized is "write-tag" or "start-stop";
    }

    public static bool IsParameterSetCommand(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
        {
            return false;
        }

        return string.Equals(commandType.Trim(), "parameter-set", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CreateDeviceControlCommandCommandValidator : AbstractValidator<CreateDeviceControlCommandCommand>
{
    public CreateDeviceControlCommandCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConnectorHostId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.InstanceKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CommandType)
            .NotEmpty()
            .MaximumLength(50)
            .Must(DeviceControlCommandValidation.IsSupportedCommandType)
            .WithMessage("Device control command type must be write-tag, start-stop or parameter-set.");
        When(x => DeviceControlCommandValidation.IsSingleTagCommand(x.CommandType), () =>
        {
            RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
            RuleFor(x => x.Value).NotEmpty().MaximumLength(256);
        });
        When(x => DeviceControlCommandValidation.IsParameterSetCommand(x.CommandType), () =>
        {
            RuleFor(x => x.Parameters).NotEmpty();
            RuleForEach(x => x.Parameters!.Keys).NotEmpty().MaximumLength(150);
            RuleForEach(x => x.Parameters!.Values).NotEmpty().MaximumLength(256);
        });
        RuleFor(x => x.RequestedBy).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateDeviceControlCommandCommandHandler(
    ApplicationDbContext dbContext,
    IDeviceControlOpsClient opsClient)
    : ICommandHandler<CreateDeviceControlCommandCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(CreateDeviceControlCommandCommand request, CancellationToken cancellationToken)
    {
        var commandType = IndustrialTelemetryText.RequiredLower(request.CommandType, nameof(request.CommandType));
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["connectorHostId"] = request.ConnectorHostId.Trim(),
            ["commandType"] = commandType,
            ["deviceAssetId"] = request.DeviceAssetId.Trim()
        };

        string? ledgerTagKey = null;
        string? ledgerValue = null;
        string? ledgerParametersJson = null;

        switch (commandType)
        {
            case "write-tag":
            case "start-stop":
                var tagKey = IndustrialTelemetryText.RequiredLower(request.TagKey ?? string.Empty, nameof(request.TagKey));
                var value = IndustrialTelemetryText.Required(request.Value ?? string.Empty, nameof(request.Value));
                await ValidateWritableTagAsync(request, tagKey, value, cancellationToken);
                parameters["tagKey"] = tagKey;
                parameters["value"] = value;
                ledgerTagKey = tagKey;
                ledgerValue = value;
                break;
            case "parameter-set":
                var parameterSet = await AddParameterSetAsync(request, parameters, cancellationToken);
                ledgerParametersJson = JsonSerializer.Serialize(parameterSet);
                break;
            default:
                throw new KnownException($"Unsupported device control command type: {request.CommandType}");
        }

        var taskRequest = new CreateOperationTaskRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.InstanceKey,
            "device.control.command",
            request.IdempotencyKey,
            request.RequestedBy,
            request.Reason,
            request.CorrelationId,
            parameters);
        var response = await opsClient.CreateDeviceControlTaskAsync(taskRequest, cancellationToken);
        await RecordCommandLedgerAsync(request, response, commandType, ledgerTagKey, ledgerValue, ledgerParametersJson, cancellationToken);
        return response;
    }

    private async Task RecordCommandLedgerAsync(
        CreateDeviceControlCommandCommand request,
        OperationTaskResponse response,
        string commandType,
        string? tagKey,
        string? value,
        string? parametersJson,
        CancellationToken cancellationToken)
    {
        // Ops task creation is idempotent on the idempotency key: repeat dispatches resolve to the
        // same operation task id, so anchor the ledger on that id to keep the command history clean.
        var alreadyRecorded = await dbContext.DeviceControlCommands
            .AnyAsync(x => x.OperationTaskId == response.OperationTaskId, cancellationToken);
        if (alreadyRecorded)
        {
            return;
        }

        dbContext.DeviceControlCommands.Add(DeviceControlCommand.Record(
            response.OperationTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId,
            request.InstanceKey,
            request.DeviceAssetId,
            commandType,
            tagKey,
            value,
            parametersJson,
            request.RequestedBy,
            request.Reason,
            request.IdempotencyKey,
            request.CorrelationId,
            response.Status,
            response.Approval?.Status,
            response.RequestedAtUtc));
    }

    private async Task<IReadOnlyDictionary<string, string>> AddParameterSetAsync(
        CreateDeviceControlCommandCommand request,
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        if (request.Parameters is null || request.Parameters.Count == 0)
        {
            throw new KnownException("Parameter-set device control command requires parameters.");
        }

        var parameterSet = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in request.Parameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var tagKey = IndustrialTelemetryText.RequiredLower(item.Key, nameof(request.Parameters));
            var value = IndustrialTelemetryText.Required(item.Value, nameof(request.Parameters));
            await ValidateWritableTagAsync(request, tagKey, value, cancellationToken);
            parameters[$"parameter.{tagKey}"] = value;
            parameterSet[tagKey] = value;
        }

        return parameterSet;
    }

    private async Task ValidateWritableTagAsync(
        CreateDeviceControlCommandCommand request,
        string tagKey,
        string value,
        CancellationToken cancellationToken)
    {
        var tag = await dbContext.TelemetryTags.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == tagKey,
            cancellationToken)
            ?? throw new KnownException($"Telemetry tag was not found for device control: {tagKey}");
        if (!tag.IsWritable)
        {
            throw new KnownException($"Telemetry tag is not writable: {tagKey}");
        }

        var allowedValues = tag.ControlAllowedValues;
        if (allowedValues.Count > 0 && !allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new KnownException($"Device control value is not allowed for tag {tagKey}.");
        }

        if (string.Equals(tag.ValueType, "number", StringComparison.OrdinalIgnoreCase))
        {
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                throw new KnownException($"Device control value must be numeric for tag {tagKey}.");
            }

            if (tag.ControlMinValue.HasValue && numericValue < tag.ControlMinValue.Value)
            {
                throw new KnownException($"Device control value is below the allowed minimum for tag {tagKey}.");
            }

            if (tag.ControlMaxValue.HasValue && numericValue > tag.ControlMaxValue.Value)
            {
                throw new KnownException($"Device control value is above the allowed maximum for tag {tagKey}.");
            }
        }
    }

}

// Advances a device control command ledger row to a terminal Ops outcome. Sent by the Ops
// OperationTaskCompleted/Failed consumers so the history read-face reflects the real result
// instead of the dispatch-time snapshot. Idempotent: unknown or already-terminal commands no-op.
public sealed record AdvanceDeviceControlCommandStatusCommand(
    string OperationTaskId,
    string TerminalStatus,
    DateTimeOffset FinishedAtUtc,
    string? FailureCode) : ICommand<bool>;

public sealed class AdvanceDeviceControlCommandStatusCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AdvanceDeviceControlCommandStatusCommand, bool>
{
    public async Task<bool> Handle(AdvanceDeviceControlCommandStatusCommand request, CancellationToken cancellationToken)
    {
        var command = await dbContext.DeviceControlCommands
            .SingleOrDefaultAsync(x => x.OperationTaskId == request.OperationTaskId, cancellationToken);
        if (command is null || command.IsTerminal)
        {
            return false;
        }

        command.ApplyOpsOutcome(request.TerminalStatus, request.FinishedAtUtc, request.FailureCode);
        return true;
    }
}

public sealed record CreateOrUpdateAlarmRuleCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string RuleCode,
    string AlarmCode,
    string Severity,
    string TagKey,
    string ComparisonOperator,
    decimal ThresholdValue,
    string UnitCode,
    bool IsEnabled,
    decimal DeadbandValue = 0m,
    int OnDelaySeconds = 0,
    int OffDelaySeconds = 0,
    int MinDurationSeconds = 0,
    string? Priority = null) : ICommand<AlarmRuleId>;

public sealed class CreateOrUpdateAlarmRuleCommandValidator : AbstractValidator<CreateOrUpdateAlarmRuleCommand>
{
    public CreateOrUpdateAlarmRuleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuleCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AlarmCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ComparisonOperator)
            .NotEmpty()
            .MaximumLength(8)
            .Must(AlarmRule.IsSupportedComparisonOperator)
            .WithMessage("Unsupported alarm rule comparison operator.");
        RuleFor(x => x.UnitCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DeadbandValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OnDelaySeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OffDelaySeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinDurationSeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Priority).MaximumLength(50);
    }
}

public sealed class CreateOrUpdateAlarmRuleCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateAlarmRuleCommand, AlarmRuleId>
{
    public async Task<AlarmRuleId> Handle(CreateOrUpdateAlarmRuleCommand request, CancellationToken cancellationToken)
    {
        var normalizedRuleCode = request.RuleCode.Trim();
        var existing = await dbContext.AlarmRules.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.RuleCode == normalizedRuleCode,
            cancellationToken);
        if (existing is not null)
        {
            existing.UpdateDefinition(
                request.AlarmCode,
                request.Severity,
                request.TagKey,
                request.ComparisonOperator,
                request.ThresholdValue,
                request.UnitCode,
                request.IsEnabled,
                request.DeadbandValue,
                request.OnDelaySeconds,
                request.OffDelaySeconds,
                request.MinDurationSeconds,
                request.Priority);
            return existing.Id;
        }

        var rule = AlarmRule.Configure(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            normalizedRuleCode,
            request.AlarmCode,
            request.Severity,
            request.TagKey,
            request.ComparisonOperator,
            request.ThresholdValue,
            request.UnitCode,
            request.IsEnabled,
            request.DeadbandValue,
            request.OnDelaySeconds,
            request.OffDelaySeconds,
            request.MinDurationSeconds,
            request.Priority);
        dbContext.AlarmRules.Add(rule);
        return rule.Id;
    }
}

public sealed record RecordTelemetrySampleCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int SampleCount,
    decimal MinValue,
    decimal MaxValue,
    decimal AverageValue,
    string SourceSequence,
    string? SourceSystem = null,
    string? SourceConnector = null,
    decimal? FirstValue = null,
    decimal? LastValue = null,
    string? DeviceState = null,
    DateTimeOffset? StateOccurredAtUtc = null) : ICommand<RecordTelemetrySampleResult>;

public sealed record RecordTelemetrySampleResult(TelemetrySummaryId? TelemetrySummaryId, DeviceStateSnapshotId? DeviceStateSnapshotId);

public sealed class RecordTelemetrySampleCommandValidator : AbstractValidator<RecordTelemetrySampleCommand>
{
    public RecordTelemetrySampleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BucketEndUtc).GreaterThan(x => x.BucketStartUtc);
        RuleFor(x => x.SampleCount).GreaterThan(0);
        RuleFor(x => x.SourceSequence).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SourceSystem).MaximumLength(100);
        RuleFor(x => x.SourceConnector).MaximumLength(150);
    }
}

public sealed class RecordTelemetrySampleCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordTelemetrySampleCommand, RecordTelemetrySampleResult>
{
    public async Task<RecordTelemetrySampleResult> Handle(RecordTelemetrySampleCommand request, CancellationToken cancellationToken)
    {
        DeviceStateSnapshotId? stateId = null;
        if (!string.IsNullOrWhiteSpace(request.DeviceState))
        {
            stateId = await RecordDeviceStateAsync(request, cancellationToken);
        }

        var normalizedTagKey = request.TagKey.Trim().ToLowerInvariant();
        var normalizedSourceSequence = IndustrialTelemetryText.Required(request.SourceSequence, nameof(request.SourceSequence));
        var normalizedSourceSystem = IndustrialTelemetryText.Optional(request.SourceSystem);
        var normalizedSourceConnector = IndustrialTelemetryText.Optional(request.SourceConnector);
        await ValidateSamplingPolicyAsync(request, normalizedTagKey, cancellationToken);
        await RecordRawSampleAsync(request, normalizedTagKey, normalizedSourceSequence, normalizedSourceSystem, normalizedSourceConnector, cancellationToken);
        var existingSummary = await dbContext.TelemetrySummaries.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceSystem == normalizedSourceSystem
                && x.SourceConnector == normalizedSourceConnector
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey
                && x.SourceSequence == normalizedSourceSequence,
            cancellationToken);
        var incoming = TelemetrySummary.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.TagKey,
            request.BucketStartUtc,
            request.BucketEndUtc,
            request.SampleCount,
            request.MinValue,
            request.MaxValue,
            request.AverageValue,
            normalizedSourceSequence,
            normalizedSourceSystem,
            normalizedSourceConnector);
        if (existingSummary is not null)
        {
            if (!existingSummary.HasSamePayload(incoming))
            {
                throw new KnownException("Telemetry summary source sequence has conflicting payload.");
            }

            await EvaluateAlarmRulesAsync(request, normalizedTagKey, cancellationToken);

            return new RecordTelemetrySampleResult(existingSummary.Id, stateId);
        }

        dbContext.TelemetrySummaries.Add(incoming);
        await EvaluateAlarmRulesAsync(request, normalizedTagKey, cancellationToken);

        return new RecordTelemetrySampleResult(incoming.Id, stateId);
    }

    private async Task ValidateSamplingPolicyAsync(
        RecordTelemetrySampleCommand request,
        string normalizedTagKey,
        CancellationToken cancellationToken)
    {
        var tagPolicy = await dbContext.TelemetryTags
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey)
            .Select(x => x.SamplingPolicy)
            .SingleOrDefaultAsync(cancellationToken);
        if (tagPolicy is null)
        {
            return;
        }

        var bucketSeconds = TelemetrySamplingPolicy.Parse(tagPolicy).BucketSeconds;
        var actualSeconds = (int)(request.BucketEndUtc - request.BucketStartUtc).TotalSeconds;
        if (actualSeconds != bucketSeconds)
        {
            throw new KnownException($"Telemetry bucket duration does not match sampling policy '{tagPolicy}'.");
        }
    }

    private async Task<TelemetryRawSampleId> RecordRawSampleAsync(
        RecordTelemetrySampleCommand request,
        string normalizedTagKey,
        string normalizedSourceSequence,
        string? normalizedSourceSystem,
        string? normalizedSourceConnector,
        CancellationToken cancellationToken)
    {
        var incoming = TelemetryRawSample.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            normalizedTagKey,
            request.BucketStartUtc,
            request.BucketEndUtc,
            request.SampleCount,
            request.MinValue,
            request.MaxValue,
            request.AverageValue,
            request.FirstValue ?? request.AverageValue,
            request.LastValue ?? request.AverageValue,
            normalizedSourceSequence,
            normalizedSourceSystem,
            normalizedSourceConnector);
        var existing = await dbContext.TelemetryRawSamples.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceSystem == normalizedSourceSystem
                && x.SourceConnector == normalizedSourceConnector
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey
                && x.SourceSequence == normalizedSourceSequence,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.HasSamePayload(incoming))
            {
                throw new KnownException("Telemetry raw sample source sequence has conflicting payload.");
            }

            return existing.Id;
        }

        dbContext.TelemetryRawSamples.Add(incoming);
        return incoming.Id;
    }

    private async Task<DeviceStateSnapshotId> RecordDeviceStateAsync(RecordTelemetrySampleCommand request, CancellationToken cancellationToken)
    {
        var normalizedSourceSequence = IndustrialTelemetryText.Required(request.SourceSequence, nameof(request.SourceSequence));
        var normalizedSourceSystem = IndustrialTelemetryText.Optional(request.SourceSystem);
        var normalizedSourceConnector = IndustrialTelemetryText.Optional(request.SourceConnector);
        var existingState = await dbContext.DeviceStateSnapshots.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceSystem == normalizedSourceSystem
                && x.SourceConnector == normalizedSourceConnector
                && x.DeviceAssetId == request.DeviceAssetId
                && x.SourceSequence == normalizedSourceSequence,
            cancellationToken);
        var incoming = DeviceStateSnapshot.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.DeviceState!,
            request.StateOccurredAtUtc ?? request.BucketEndUtc,
            normalizedSourceSequence,
            normalizedSourceSystem,
            normalizedSourceConnector,
            raiseChangedEvent: false);
        if (existingState is not null)
        {
            if (!existingState.HasSamePayload(incoming))
            {
                throw new KnownException("Device state source sequence has conflicting payload.");
            }

            return existingState.Id;
        }

        var latestState = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId)
            .OrderByDescending(x => x.OccurredAtUnixTimeMilliseconds)
            .ThenByDescending(x => x.RecordedAtUnixTimeMilliseconds)
            .ThenByDescending(x => x.SourceSequence)
            .Select(x => new LatestDeviceStateSnapshot(x.State, x.OccurredAtUnixTimeMilliseconds))
            .FirstOrDefaultAsync(cancellationToken);
        if (ShouldPublishDeviceStateChanged(incoming, latestState))
        {
            incoming.RaiseStateChangedEvent();
        }

        dbContext.DeviceStateSnapshots.Add(incoming);
        return incoming.Id;
    }

    private static bool ShouldPublishDeviceStateChanged(
        DeviceStateSnapshot incoming,
        LatestDeviceStateSnapshot? latestState)
    {
        if (latestState is null)
        {
            return true;
        }

        if (incoming.OccurredAtUnixTimeMilliseconds < latestState.OccurredAtUnixTimeMilliseconds)
        {
            return false;
        }

        return incoming.State != latestState.State;
    }

    private sealed record LatestDeviceStateSnapshot(
        string State,
        long OccurredAtUnixTimeMilliseconds);

    private async Task EvaluateAlarmRulesAsync(
        RecordTelemetrySampleCommand request,
        string normalizedTagKey,
        CancellationToken cancellationToken)
    {
        var rules = await dbContext.AlarmRules
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey
                && x.IsEnabled)
            .OrderBy(x => x.RuleCode)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return;
        }

        if (await HasNewerSummaryAsync(request, normalizedTagKey, cancellationToken))
        {
            return;
        }

        var ruleCodes = rules.Select(x => x.RuleCode).ToArray();
        var activePersistedAlarms = await dbContext.AlarmEvents
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.Status != "cleared")
            .ToListAsync(cancellationToken);
        var activeAlarms = activePersistedAlarms
            .Concat(dbContext.AlarmEvents.Local
                .Where(x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.DeviceAssetId == request.DeviceAssetId
                    && x.Status != "cleared"))
            .DistinctBy(x => x.Id)
            .Where(alarm => ruleCodes.Any(ruleCode => IsAlarmForRule(alarm, ruleCode)))
            .ToArray();
        var previousSummaries = rules.Any(rule => rule.RequiredTriggerSeconds > 0 || rule.OffDelaySeconds > 0)
            ? await LoadPreviousSummariesAsync(request, normalizedTagKey, cancellationToken)
            : [];
        foreach (var rule in rules)
        {
            var ruleActiveAlarms = activeAlarms
                .Where(alarm => IsAlarmForRule(alarm, rule.RuleCode))
                .OrderBy(alarm => alarm.RaisedAtUtc)
                .ToArray();
            var isTriggered = rule.Evaluate(request.AverageValue, request.MaxValue);
            if (isTriggered)
            {
                if (ruleActiveAlarms.Length == 0)
                {
                    if (HasSatisfiedTriggerDelay(rule, request, previousSummaries))
                    {
                        dbContext.AlarmEvents.Add(AlarmEvent.Raise(
                            request.OrganizationId,
                            request.EnvironmentId,
                            request.DeviceAssetId,
                            rule.AlarmCode,
                            rule.Severity,
                            request.BucketEndUtc,
                            CreateRuleExternalAlarmId(rule),
                            rule.Priority,
                            rule.TagKey,
                            rule.SelectObservedValue(request.AverageValue, request.MaxValue),
                            rule.ThresholdValue,
                            rule.UnitCode));
                    }
                }

                foreach (var duplicate in ruleActiveAlarms.Skip(1))
                {
                    TryClearRuleAlarm(duplicate, request.BucketEndUtc, "duplicate-rule-alarm-suppressed");
                }

                continue;
            }

            if (!rule.IsReturnToNormal(request.AverageValue, request.MaxValue))
            {
                continue;
            }

            foreach (var activeAlarm in ruleActiveAlarms)
            {
                if (HasSatisfiedReturnToNormalDelay(rule, request, activeAlarm.RaisedAtUtc, previousSummaries))
                {
                    TryClearRuleAlarm(activeAlarm, request.BucketEndUtc, "return-to-normal");
                }
            }
        }
    }

    private async Task<bool> HasNewerSummaryAsync(
        RecordTelemetrySampleCommand request,
        string normalizedTagKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.TelemetrySummaries
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.TagKey == normalizedTagKey)
            .AnyAsync(x => x.BucketEndUnixTimeMilliseconds > request.BucketEndUtc.ToUnixTimeMilliseconds(), cancellationToken)
            || HasLocalNewerSummary(request, normalizedTagKey);
    }

    private bool HasLocalNewerSummary(RecordTelemetrySampleCommand request, string normalizedTagKey)
    {
        return dbContext.TelemetrySummaries.Local.Any(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.DeviceAssetId == request.DeviceAssetId
            && x.TagKey == normalizedTagKey
            && x.BucketEndUtc > request.BucketEndUtc);
    }

    private async Task<IReadOnlyCollection<TelemetrySummary>> LoadPreviousSummariesAsync(
        RecordTelemetrySampleCommand request,
        string normalizedTagKey,
        CancellationToken cancellationToken)
    {
        var previousPersistedSummaries = await dbContext.TelemetrySummaries
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.TagKey == normalizedTagKey)
            .Where(x => x.BucketEndUnixTimeMilliseconds <= request.BucketStartUtc.ToUnixTimeMilliseconds())
            .OrderByDescending(x => x.BucketEndUnixTimeMilliseconds)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        return previousPersistedSummaries
            .Concat(dbContext.TelemetrySummaries.Local
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Where(x => x.EnvironmentId == request.EnvironmentId)
                .Where(x => x.DeviceAssetId == request.DeviceAssetId)
                .Where(x => x.TagKey == normalizedTagKey)
                .Where(x => x.BucketEndUtc <= request.BucketStartUtc))
            .DistinctBy(x => x.Id)
            .OrderByDescending(x => x.BucketEndUtc)
            .Take(100)
            .ToArray();
    }

    private static bool HasSatisfiedTriggerDelay(
        AlarmRule rule,
        RecordTelemetrySampleCommand request,
        IReadOnlyCollection<TelemetrySummary> previousSummaries)
    {
        var requiredSeconds = rule.RequiredTriggerSeconds;
        if (requiredSeconds <= 0)
        {
            return true;
        }

        var conditionStartUtc = CalculateConsecutiveConditionStart(
            rule,
            previousSummaries,
            static (rule, summary) => rule.Evaluate(summary.AverageValue, summary.MaxValue),
            request.BucketStartUtc);
        return (request.BucketEndUtc - conditionStartUtc).TotalSeconds >= requiredSeconds;
    }

    private static bool HasSatisfiedReturnToNormalDelay(
        AlarmRule rule,
        RecordTelemetrySampleCommand request,
        DateTimeOffset raisedAtUtc,
        IReadOnlyCollection<TelemetrySummary> previousSummaries)
    {
        if (request.BucketEndUtc < raisedAtUtc)
        {
            return false;
        }

        if (rule.OffDelaySeconds <= 0)
        {
            return true;
        }

        var conditionStartUtc = CalculateConsecutiveConditionStart(
            rule,
            previousSummaries,
            static (rule, summary) => rule.IsReturnToNormal(summary.AverageValue, summary.MaxValue),
            request.BucketStartUtc);
        return (request.BucketEndUtc - conditionStartUtc).TotalSeconds >= rule.OffDelaySeconds;
    }

    private static DateTimeOffset CalculateConsecutiveConditionStart(
        AlarmRule rule,
        IReadOnlyCollection<TelemetrySummary> previousSummaries,
        Func<AlarmRule, TelemetrySummary, bool> predicate,
        DateTimeOffset defaultStartUtc)
    {
        var conditionStartUtc = defaultStartUtc;
        var expectedPreviousBucketEndUtc = defaultStartUtc;
        foreach (var summary in DeduplicatePreviousSummariesByBucketEnd(previousSummaries))
        {
            if (summary.BucketEndUtc != expectedPreviousBucketEndUtc)
            {
                break;
            }

            if (!predicate(rule, summary))
            {
                break;
            }

            conditionStartUtc = summary.BucketStartUtc;
            expectedPreviousBucketEndUtc = summary.BucketStartUtc;
        }

        return conditionStartUtc;
    }

    private static IEnumerable<TelemetrySummary> DeduplicatePreviousSummariesByBucketEnd(IReadOnlyCollection<TelemetrySummary> previousSummaries)
    {
        return previousSummaries
            .GroupBy(summary => summary.BucketEndUtc)
            .Select(group => group.OrderByDescending(summary => summary.RecordedAtUtc).First())
            .OrderByDescending(summary => summary.BucketEndUtc);
    }

    private static string CreateRuleExternalAlarmId(AlarmRule rule)
    {
        return rule.RuleCode;
    }

    private static bool IsAlarmForRule(AlarmEvent alarm, string ruleCode)
    {
        return string.Equals(alarm.ExternalAlarmId, ruleCode, StringComparison.Ordinal)
            || alarm.ExternalAlarmId.StartsWith($"{ruleCode}:", StringComparison.Ordinal);
    }

    private static void TryClearRuleAlarm(AlarmEvent alarm, DateTimeOffset clearedAtUtc, string clearReason)
    {
        if (clearedAtUtc < alarm.RaisedAtUtc)
        {
            return;
        }

        alarm.Clear(clearedAtUtc, "system:industrial-telemetry", clearReason);
    }
}

public sealed class IndustrialTelemetryIdempotentIngestionBehavior<TRequest, TResponse>(ApplicationDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!IsIdempotentIngestionCommand(request))
        {
            return await next(cancellationToken);
        }

        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotentIngestionUniqueConflict(ex, dbContext))
        {
            // These ingestion commands own the scoped DbContext writes; clear failed Added entities before retrying.
            dbContext.ChangeTracker.Clear();
            return await next(cancellationToken);
        }
    }

    private static bool IsIdempotentIngestionCommand(TRequest request)
    {
        return request is RecordTelemetrySampleCommand or RaiseAlarmCommand or CreateDeviceControlCommandCommand;
    }

    private static bool IsIdempotentIngestionUniqueConflict(DbUpdateException exception, ApplicationDbContext context)
    {
        // Keep detection entity-scoped so provider-specific index names do not leak into the application layer.
        return exception.Entries.Any(entry => entry.Entity is TelemetryRawSample or TelemetrySummary or DeviceStateSnapshot or AlarmEvent or DeviceControlCommand) &&
            EnumerateExceptions(exception).Any(inner =>
                IsPostgreSqlUniqueConflict(inner) ||
                IsSqliteUniqueConflict(context, inner) ||
                IsSqlServerUniqueConflict(context, inner) ||
                IsMySqlUniqueConflict(context, inner));
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool IsPostgreSqlUniqueConflict(Exception exception)
    {
        if (!string.Equals(exception.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        return exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string == "23505";
    }

    private static bool IsSqliteUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var errorCode = GetIntProperty(exception, "SqliteErrorCode");
        var extendedErrorCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
        return errorCode == 19 || extendedErrorCode is 1555 or 2067;
    }

    private static bool IsSqlServerUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        return (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("SqlException", StringComparison.OrdinalIgnoreCase)) &&
            GetIntProperty(exception, "Number") is 2601 or 2627;
    }

    private static bool IsMySqlUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        return (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase)) &&
            GetIntProperty(exception, "Number") == 1062;
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        var value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            _ => null
        };
    }
}

public sealed record RaiseAlarmCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId,
    string? Priority = null,
    string? TagKey = null,
    decimal? ObservedValue = null,
    decimal? ThresholdValue = null,
    string? UnitCode = null) : ICommand<AlarmEventId>;

public sealed class RaiseAlarmCommandValidator : AbstractValidator<RaiseAlarmCommand>
{
    public RaiseAlarmCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AlarmCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ExternalAlarmId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Priority).MaximumLength(50);
        RuleFor(x => x.TagKey).MaximumLength(150);
        RuleFor(x => x.UnitCode).MaximumLength(50);
    }
}

public sealed class RaiseAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RaiseAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(RaiseAlarmCommand request, CancellationToken cancellationToken)
    {
        var incoming = AlarmEvent.Raise(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.AlarmCode,
            request.Severity,
            request.RaisedAtUtc,
            request.ExternalAlarmId,
            request.Priority,
            request.TagKey,
            request.ObservedValue,
            request.ThresholdValue,
            request.UnitCode);
        var existing = await dbContext.AlarmEvents.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.AlarmCode == request.AlarmCode
                && x.ExternalAlarmId == request.ExternalAlarmId
                && x.Status != "cleared",
            cancellationToken);
        if (existing is not null)
        {
            return EnsureCompatibleDuplicate(existing, incoming);
        }

        if (!string.IsNullOrWhiteSpace(incoming.TagKey))
        {
            existing = await dbContext.AlarmEvents.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.DeviceAssetId == request.DeviceAssetId
                    && x.TagKey == incoming.TagKey
                    && x.ExternalAlarmId == request.ExternalAlarmId
                    && x.Status != "cleared",
                cancellationToken);
            if (existing is not null)
            {
                return EnsureCompatibleDuplicate(existing, incoming);
            }
        }

        dbContext.AlarmEvents.Add(incoming);
        return incoming.Id;
    }

    private static AlarmEventId EnsureCompatibleDuplicate(AlarmEvent existing, AlarmEvent incoming)
    {
        try
        {
            existing.EnsureCompatibleDuplicate(incoming);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }

        return existing.Id;
    }
}

public sealed record ClearAlarmCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string ExternalAlarmId,
    DateTimeOffset ClearedAtUtc,
    string ClearedBy,
    string? ClearReason) : ICommand<AlarmEventId>;

public sealed class ClearAlarmCommandValidator : AbstractValidator<ClearAlarmCommand>
{
    public ClearAlarmCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AlarmCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalAlarmId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ClearedBy).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ClearReason).MaximumLength(300);
    }
}

public sealed class ClearAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ClearAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(ClearAlarmCommand request, CancellationToken cancellationToken)
    {
        var alarm = await dbContext.AlarmEvents.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.AlarmCode == request.AlarmCode
                && x.ExternalAlarmId == request.ExternalAlarmId,
            cancellationToken)
            ?? throw new KnownException($"Alarm event was not found: {request.ExternalAlarmId}");
        try
        {
            alarm.Clear(request.ClearedAtUtc, request.ClearedBy, request.ClearReason);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }

        return alarm.Id;
    }
}

public sealed record AcknowledgeAlarmCommand(
    AlarmEventId AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset AcknowledgedAtUtc,
    string AcknowledgedBy) : ICommand<AlarmEventId>;

public sealed class AcknowledgeAlarmCommandValidator : AbstractValidator<AcknowledgeAlarmCommand>
{
    public AcknowledgeAlarmCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AcknowledgedBy).NotEmpty().MaximumLength(150);
    }
}

public sealed class AcknowledgeAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AcknowledgeAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(AcknowledgeAlarmCommand request, CancellationToken cancellationToken)
    {
        var alarm = await LoadAlarmAsync(dbContext, request.AlarmEventId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        try
        {
            alarm.Acknowledge(request.AcknowledgedAtUtc, request.AcknowledgedBy);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }

        return alarm.Id;
    }

    internal static async Task<AlarmEvent> LoadAlarmAsync(
        ApplicationDbContext dbContext,
        AlarmEventId alarmEventId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AlarmEvents.SingleOrDefaultAsync(
            x => x.Id == alarmEventId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId,
            cancellationToken)
            ?? throw new KnownException($"Alarm event was not found: {alarmEventId.Id:D}");
    }
}

public sealed record ShelveAlarmCommand(
    AlarmEventId AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset ShelvedAtUtc,
    int DurationMinutes,
    string ShelvedBy,
    string? Reason) : ICommand<AlarmEventId>;

public sealed class ShelveAlarmCommandValidator : AbstractValidator<ShelveAlarmCommand>
{
    public ShelveAlarmCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 24 * 60);
        RuleFor(x => x.ShelvedBy).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public sealed class ShelveAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ShelveAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(ShelveAlarmCommand request, CancellationToken cancellationToken)
    {
        var alarm = await AcknowledgeAlarmCommandHandler.LoadAlarmAsync(dbContext, request.AlarmEventId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        try
        {
            alarm.Shelve(
                request.ShelvedAtUtc,
                request.ShelvedAtUtc.AddMinutes(request.DurationMinutes),
                request.ShelvedBy,
                request.Reason);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }

        return alarm.Id;
    }
}

public sealed record UnshelveAlarmCommand(
    AlarmEventId AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset UnshelvedAtUtc) : ICommand<AlarmEventId>;

public sealed class UnshelveAlarmCommandValidator : AbstractValidator<UnshelveAlarmCommand>
{
    public UnshelveAlarmCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class UnshelveAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UnshelveAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(UnshelveAlarmCommand request, CancellationToken cancellationToken)
    {
        var alarm = await AcknowledgeAlarmCommandHandler.LoadAlarmAsync(dbContext, request.AlarmEventId, request.OrganizationId, request.EnvironmentId, cancellationToken);
        alarm.Unshelve(request.UnshelvedAtUtc);
        return alarm.Id;
    }
}

public sealed record RunAlarmEscalationsCommand(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset AsOfUtc,
    int UnacknowledgedTimeoutMinutes,
    IReadOnlyCollection<string> SeverityLevels,
    IReadOnlyCollection<string> RecipientRefs,
    int MaxAlarms = 500) : ICommand<RunAlarmEscalationsResult>;

public sealed record RunAlarmEscalationsResult(int EscalatedCount, IReadOnlyCollection<AlarmEventId> AlarmEventIds);

public sealed class RunAlarmEscalationsCommandValidator : AbstractValidator<RunAlarmEscalationsCommand>
{
    public RunAlarmEscalationsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UnacknowledgedTimeoutMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RecipientRefs).NotEmpty();
        RuleForEach(x => x.RecipientRefs).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MaxAlarms).InclusiveBetween(1, 5000);
    }
}

public sealed class RunAlarmEscalationsCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RunAlarmEscalationsCommand, RunAlarmEscalationsResult>
{
    public async Task<RunAlarmEscalationsResult> Handle(RunAlarmEscalationsCommand request, CancellationToken cancellationToken)
    {
        var alarms = await dbContext.AlarmEvents
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.Status != "cleared")
            .OrderBy(x => x.RaisedAtUtc)
            .Take(request.MaxAlarms)
            .ToArrayAsync(cancellationToken);
        var escalated = new List<AlarmEventId>();
        var timeout = TimeSpan.FromMinutes(request.UnacknowledgedTimeoutMinutes);
        var severityLevels = Normalize(request.SeverityLevels);
        var recipientRefs = Normalize(request.RecipientRefs);
        foreach (var alarm in alarms)
        {
            alarm.ExpireShelving(request.AsOfUtc);
            if (!alarm.ShouldEscalateAt(request.AsOfUtc, timeout, severityLevels))
            {
                continue;
            }

            var reason = severityLevels.Any(level =>
                string.Equals(level, alarm.Severity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, alarm.Priority, StringComparison.OrdinalIgnoreCase))
                ? "severity"
                : "unacknowledged-timeout";
            alarm.Escalate(request.AsOfUtc, reason, recipientRefs);
            escalated.Add(alarm.Id);
        }

        return new RunAlarmEscalationsResult(escalated.Count, escalated);
    }

    private static IReadOnlyCollection<string> Normalize(IReadOnlyCollection<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
