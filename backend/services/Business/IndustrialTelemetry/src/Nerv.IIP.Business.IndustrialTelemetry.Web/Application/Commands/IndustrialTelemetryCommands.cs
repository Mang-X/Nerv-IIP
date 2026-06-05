using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

public sealed record CreateTelemetryTagCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    string ValueType,
    string UnitCode,
    string SamplingPolicy) : ICommand<TelemetryTagId>;

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
        RuleFor(x => x.SamplingPolicy).NotEmpty().MaximumLength(100);
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
            return existing.Id;
        }

        var tag = TelemetryTag.Create(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.TagKey, request.ValueType, request.UnitCode, request.SamplingPolicy);
        dbContext.TelemetryTags.Add(tag);
        return tag.Id;
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
    bool IsEnabled) : ICommand<AlarmRuleId>;

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
                request.IsEnabled);
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
            request.IsEnabled);
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

            return new RecordTelemetrySampleResult(existingSummary.Id, stateId);
        }

        dbContext.TelemetrySummaries.Add(incoming);
        return new RecordTelemetrySampleResult(incoming.Id, stateId);
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
            normalizedSourceConnector);
        if (existingState is not null)
        {
            if (!existingState.HasSamePayload(incoming))
            {
                throw new KnownException("Device state source sequence has conflicting payload.");
            }

            return existingState.Id;
        }

        dbContext.DeviceStateSnapshots.Add(incoming);
        return incoming.Id;
    }
}

public sealed record RaiseAlarmCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId) : ICommand<AlarmEventId>;

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
    }
}

public sealed class RaiseAlarmCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RaiseAlarmCommand, AlarmEventId>
{
    public async Task<AlarmEventId> Handle(RaiseAlarmCommand request, CancellationToken cancellationToken)
    {
        var incoming = AlarmEvent.Raise(request.OrganizationId, request.EnvironmentId, request.DeviceAssetId, request.AlarmCode, request.Severity, request.RaisedAtUtc, request.ExternalAlarmId);
        var existing = await dbContext.AlarmEvents.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.AlarmCode == request.AlarmCode
                && x.ExternalAlarmId == request.ExternalAlarmId,
            cancellationToken);
        if (existing is not null)
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

        dbContext.AlarmEvents.Add(incoming);
        return incoming.Id;
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
