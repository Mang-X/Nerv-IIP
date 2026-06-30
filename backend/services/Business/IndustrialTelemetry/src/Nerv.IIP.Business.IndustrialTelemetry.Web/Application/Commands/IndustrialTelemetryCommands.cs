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

            await EvaluateAlarmRulesAsync(request, normalizedTagKey, cancellationToken);

            return new RecordTelemetrySampleResult(existingSummary.Id, stateId);
        }

        dbContext.TelemetrySummaries.Add(incoming);
        await EvaluateAlarmRulesAsync(request, normalizedTagKey, cancellationToken);

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
                && x.Status == "raised")
            .ToListAsync(cancellationToken);
        var activeAlarms = activePersistedAlarms
            .Concat(dbContext.AlarmEvents.Local
                .Where(x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.DeviceAssetId == request.DeviceAssetId
                    && x.Status == "raised"))
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
        return request is RecordTelemetrySampleCommand or RaiseAlarmCommand;
    }

    private static bool IsIdempotentIngestionUniqueConflict(DbUpdateException exception, ApplicationDbContext context)
    {
        // Keep detection entity-scoped so provider-specific index names do not leak into the application layer.
        return exception.Entries.Any(entry => entry.Entity is TelemetrySummary or DeviceStateSnapshot or AlarmEvent) &&
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
