using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.EquipmentHealth;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record GetEquipmentHealthQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId) : IQuery<EquipmentHealthResponse>;

public sealed record EquipmentHealthResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    int HealthScore,
    string Level,
    DateTimeOffset CalculatedAtUtc,
    EquipmentHealthDataFreshnessResponse DataFreshness,
    IReadOnlyCollection<EquipmentHealthRiskFactorResponse> RiskFactors,
    IReadOnlyCollection<EquipmentHealthRuleEvaluationResponse> RuleEvaluations);

public sealed record EquipmentHealthDataFreshnessResponse(
    string Status,
    long? AgeSeconds,
    DateTimeOffset? LatestFactAtUtc,
    string? SourceFactType,
    string? SourceFactLabel);

public sealed record EquipmentHealthRiskFactorResponse(
    string RuleCode,
    string RuleName,
    string Status,
    int Penalty,
    string CurrentValue,
    string Threshold,
    string Unit,
    string Evidence,
    string? SourceFactType,
    string? SourceFactLabel,
    DateTimeOffset? SourceFactOccurredAtUtc);

public sealed record EquipmentHealthRuleEvaluationResponse(
    string RuleCode,
    string RuleName,
    string Status,
    int Penalty,
    string CurrentValue,
    string Threshold,
    string Unit,
    string Evidence,
    string? SourceFactType,
    string? SourceFactLabel,
    DateTimeOffset? SourceFactOccurredAtUtc);

public sealed class GetEquipmentHealthQueryValidator : AbstractValidator<GetEquipmentHealthQuery>
{
    public GetEquipmentHealthQueryValidator()
    {
        RuleFor(query => query.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(query => query.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(query => query.DeviceAssetId).NotEmpty().MaximumLength(150);
    }
}

public sealed class GetEquipmentHealthQueryHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<GetEquipmentHealthQuery, EquipmentHealthResponse>
{
    private static readonly TimeSpan FactWindow = TimeSpan.FromHours(24);

    public async Task<EquipmentHealthResponse> Handle(
        GetEquipmentHealthQuery request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var windowStartUtc = now.Subtract(FactWindow);
        var observations = await LoadRuleObservationsAsync(
            request,
            windowStartUtc,
            now,
            cancellationToken);
        var runtime = await LoadRuntimeAsync(request, windowStartUtc, now, cancellationToken);
        var alarms = await LoadAlarmsAsync(request, windowStartUtc, now, cancellationToken);
        var scoringResult = EquipmentHealthScoringPolicy.Evaluate(
            new EquipmentHealthScoringInput(now, observations, runtime, alarms));
        var evaluations = scoringResult.Evaluations
            .Select(MapEvaluation)
            .ToArray();
        var riskFactors = evaluations
            .Where(evaluation => evaluation.Status == "risk")
            .Select(MapRiskFactor)
            .ToArray();
        var newestSourceFact = scoringResult.NewestSourceFact;

        return new EquipmentHealthResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            scoringResult.Score,
            ToContractValue(scoringResult.Level),
            now,
            new EquipmentHealthDataFreshnessResponse(
                ToContractValue(scoringResult.Freshness),
                newestSourceFact is null
                    ? null
                    : Math.Max(0, (long)Math.Floor((now - newestSourceFact.OccurredAtUtc).TotalSeconds)),
                newestSourceFact?.OccurredAtUtc,
                newestSourceFact?.Type,
                newestSourceFact?.Label),
            riskFactors,
            evaluations);
    }

    private async Task<ImmutableArray<EquipmentHealthRuleObservation>> LoadRuleObservationsAsync(
        GetEquipmentHealthQuery request,
        DateTimeOffset windowStartUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rules = await dbContext.AlarmRules
            .AsNoTracking()
            .Where(rule => rule.OrganizationId == request.OrganizationId)
            .Where(rule => rule.EnvironmentId == request.EnvironmentId)
            .Where(rule => rule.DeviceAssetId == request.DeviceAssetId)
            .Where(rule => rule.IsEnabled)
            .Where(rule =>
                rule.ComparisonOperator == ">"
                || rule.ComparisonOperator == ">="
                || rule.ComparisonOperator == "<"
                || rule.ComparisonOperator == "<=")
            .OrderBy(rule => rule.RuleCode)
            .ThenBy(rule => rule.TagKey)
            .Select(rule => new RuleFact(
                rule.RuleCode,
                rule.TagKey,
                rule.ComparisonOperator,
                rule.Severity,
                rule.ThresholdValue,
                rule.UnitCode))
            .ToArrayAsync(cancellationToken);
        if (rules.Length == 0)
        {
            return [];
        }

        var tagKeys = rules
            .Select(rule => rule.TagKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var samples = await dbContext.TelemetryRawSamples
            .AsNoTracking()
            .Where(sample => sample.OrganizationId == request.OrganizationId)
            .Where(sample => sample.EnvironmentId == request.EnvironmentId)
            .Where(sample => sample.DeviceAssetId == request.DeviceAssetId)
            .Where(sample => tagKeys.Contains(sample.TagKey))
            .Where(sample => sample.BucketEndUtc >= windowStartUtc)
            .Where(sample => sample.BucketEndUtc <= now)
            .OrderBy(sample => sample.BucketEndUtc)
            .ThenBy(sample => sample.RecordedAtUtc)
            .ThenBy(sample => sample.SourceSequence)
            .Select(sample => new RawSampleFact(
                sample.TagKey,
                sample.LastValue,
                sample.BucketEndUtc))
            .ToArrayAsync(cancellationToken);

        return rules
            .Select(rule =>
            {
                var ruleSamples = samples
                    .Where(sample => sample.TagKey == rule.TagKey)
                    .Select(sample => new EquipmentHealthHistorySample(
                        decimal.ToDouble(sample.LastValue),
                        RuleSourceFact(rule, sample.BucketEndUtc)))
                    .ToImmutableArray();
                var currentSample = ruleSamples.IsDefaultOrEmpty
                    ? null
                    : new EquipmentHealthValueSample(
                        ruleSamples[^1].Value,
                        ruleSamples[^1].SourceFact);

                return new EquipmentHealthRuleObservation(
                    rule.RuleCode,
                    rule.TagKey,
                    ToRiskDirection(rule.ComparisonOperator),
                    ToAlarmSeverity(rule.Severity),
                    decimal.ToDouble(rule.ThresholdValue),
                    rule.UnitCode,
                    currentSample,
                    ruleSamples);
            })
            .ToImmutableArray();
    }

    private async Task<EquipmentHealthRuntimeFact?> LoadRuntimeAsync(
        GetEquipmentHealthQuery request,
        DateTimeOffset windowStartUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var response = await new QueryRuntimeHoursQueryHandler(dbContext).Handle(
            new QueryRuntimeHoursQuery(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                windowStartUtc,
                now),
            cancellationToken);
        if (!response.HasRuntimeSamples)
        {
            return null;
        }

        var newestStateAtUtc = await dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.OrganizationId == request.OrganizationId)
            .Where(snapshot => snapshot.EnvironmentId == request.EnvironmentId)
            .Where(snapshot => snapshot.DeviceAssetId == request.DeviceAssetId)
            .Where(snapshot => snapshot.OccurredAtUtc <= now)
            .OrderByDescending(snapshot => snapshot.OccurredAtUtc)
            .ThenByDescending(snapshot => snapshot.RecordedAtUtc)
            .ThenByDescending(snapshot => snapshot.SourceSequence)
            .Select(snapshot => (DateTimeOffset?)snapshot.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (newestStateAtUtc is null)
        {
            return null;
        }

        return new EquipmentHealthRuntimeFact(
            decimal.ToDouble(response.TotalRuntimeHours),
            new EquipmentHealthSourceFact(
                "runtime-state",
                $"设备 {request.DeviceAssetId} 运行状态",
                newestStateAtUtc.Value));
    }

    private async Task<ImmutableArray<EquipmentHealthAlarmFact>> LoadAlarmsAsync(
        GetEquipmentHealthQuery request,
        DateTimeOffset windowStartUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alarms = await dbContext.AlarmEvents
            .AsNoTracking()
            .Where(alarm => alarm.OrganizationId == request.OrganizationId)
            .Where(alarm => alarm.EnvironmentId == request.EnvironmentId)
            .Where(alarm => alarm.DeviceAssetId == request.DeviceAssetId)
            .Where(alarm => alarm.RaisedAtUtc <= now)
            .Where(alarm =>
                alarm.RaisedAtUtc >= windowStartUtc
                || alarm.ClearedAtUtc == null
                || alarm.ClearedAtUtc > now)
            .OrderBy(alarm => alarm.RaisedAtUtc)
            .Select(alarm => new AlarmFact(
                alarm.AlarmCode,
                alarm.Severity,
                alarm.RaisedAtUtc,
                alarm.ClearedAtUtc))
            .ToArrayAsync(cancellationToken);

        return alarms
            .Select(alarm =>
            {
                var raisedFact = new EquipmentHealthSourceFact(
                    "alarm-raised",
                    $"报警 {alarm.AlarmCode}",
                    alarm.RaisedAtUtc);
                var wasClearedAtEvaluation =
                    alarm.ClearedAtUtc is not null && alarm.ClearedAtUtc <= now;
                var latestLifecycleFact = wasClearedAtEvaluation
                    ? new EquipmentHealthSourceFact(
                        "alarm-lifecycle",
                        $"报警 {alarm.AlarmCode}",
                        alarm.ClearedAtUtc!.Value)
                    : raisedFact;

                return new EquipmentHealthAlarmFact(
                    ToAlarmSeverity(alarm.Severity),
                    !wasClearedAtEvaluation,
                    raisedFact,
                    latestLifecycleFact);
            })
            .ToImmutableArray();
    }

    private static EquipmentHealthRuleEvaluationResponse MapEvaluation(
        EquipmentHealthRuleEvaluation evaluation)
    {
        return new EquipmentHealthRuleEvaluationResponse(
            evaluation.RuleCode,
            evaluation.Label,
            ToContractValue(evaluation.Status),
            evaluation.Penalty,
            evaluation.Current,
            evaluation.Threshold,
            evaluation.Unit,
            evaluation.Evidence,
            evaluation.SourceFact?.Type,
            evaluation.SourceFact?.Label,
            evaluation.SourceFact?.OccurredAtUtc);
    }

    private static EquipmentHealthRiskFactorResponse MapRiskFactor(
        EquipmentHealthRuleEvaluationResponse evaluation)
    {
        return new EquipmentHealthRiskFactorResponse(
            evaluation.RuleCode,
            evaluation.RuleName,
            evaluation.Status,
            evaluation.Penalty,
            evaluation.CurrentValue,
            evaluation.Threshold,
            evaluation.Unit,
            evaluation.Evidence,
            evaluation.SourceFactType,
            evaluation.SourceFactLabel,
            evaluation.SourceFactOccurredAtUtc);
    }

    private static EquipmentHealthSourceFact RuleSourceFact(
        RuleFact rule,
        DateTimeOffset occurredAtUtc)
    {
        return new EquipmentHealthSourceFact(
            "telemetry-raw-sample",
            $"规则 {rule.RuleCode} · 标签 {rule.TagKey}",
            occurredAtUtc);
    }

    private static EquipmentHealthRiskDirection ToRiskDirection(string comparisonOperator)
    {
        return comparisonOperator is ">" or ">="
            ? EquipmentHealthRiskDirection.High
            : EquipmentHealthRiskDirection.Low;
    }

    private static EquipmentHealthAlarmSeverity ToAlarmSeverity(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" or "fatal" => EquipmentHealthAlarmSeverity.Critical,
            "warning" or "high" => EquipmentHealthAlarmSeverity.Warning,
            _ => EquipmentHealthAlarmSeverity.Other,
        };
    }

    private static string ToContractValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }

    private sealed record RuleFact(
        string RuleCode,
        string TagKey,
        string ComparisonOperator,
        string Severity,
        decimal ThresholdValue,
        string UnitCode);

    private sealed record RawSampleFact(
        string TagKey,
        decimal LastValue,
        DateTimeOffset BucketEndUtc);

    private sealed record AlarmFact(
        string AlarmCode,
        string Severity,
        DateTimeOffset RaisedAtUtc,
        DateTimeOffset? ClearedAtUtc);
}
