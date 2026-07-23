using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.EquipmentHealth;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class EquipmentHealthQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validator_rejects_blank_and_overlong_scope_values()
    {
        var validator = new GetEquipmentHealthQueryValidator();

        var blank = validator.Validate(new GetEquipmentHealthQuery(" ", "", ""));
        var overlong = validator.Validate(new GetEquipmentHealthQuery(
            new string('o', 101),
            new string('e', 101),
            new string('d', 151)));

        Assert.False(blank.IsValid);
        Assert.False(overlong.IsValid);
        Assert.Contains(blank.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.OrganizationId)));
        Assert.Contains(blank.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.EnvironmentId)));
        Assert.Contains(blank.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.DeviceAssetId)));
        Assert.Contains(overlong.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.OrganizationId)));
        Assert.Contains(overlong.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.EnvironmentId)));
        Assert.Contains(overlong.Errors, error => SameProperty(error.PropertyName, nameof(GetEquipmentHealthQuery.DeviceAssetId)));
    }

    [Fact]
    public async Task Handler_loads_only_requested_scope_and_maps_explainable_current_history_runtime_and_alarm_facts()
    {
        await using var dbContext = CreateDbContext(nameof(Handler_loads_only_requested_scope_and_maps_explainable_current_history_runtime_and_alarm_facts));
        var temperatureRule = AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "TEMP_HIGH", "TEMP_ALARM", "critical",
            "temperature", ">=", 100m, "celsius", true);
        var pressureRule = AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "PRESSURE_LOW", "PRESSURE_ALARM", "warning",
            "pressure", "<", 50m, "bar", true);
        var disabledRule = AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "DISABLED_HIGH", "DISABLED_ALARM", "critical",
            "disabled-tag", ">", 1m, "unit", false);
        var equalityRule = AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "EQUALITY", "EQUALITY_ALARM", "critical",
            "temperature", "==", 110m, "celsius", true);
        var otherTenantRule = AlarmRule.Configure(
            "org-b", "env-a", "DEV-A", "OTHER_TENANT", "OTHER_ALARM", "critical",
            "temperature", ">=", 1m, "celsius", true);
        var otherEnvironmentRule = AlarmRule.Configure(
            "org-a", "env-b", "DEV-A", "OTHER_ENV", "OTHER_ALARM", "critical",
            "temperature", ">=", 1m, "celsius", true);
        var otherDeviceRule = AlarmRule.Configure(
            "org-a", "env-a", "DEV-B", "OTHER_DEVICE", "OTHER_ALARM", "critical",
            "temperature", ">=", 1m, "celsius", true);
        dbContext.AlarmRules.AddRange(
            temperatureRule,
            pressureRule,
            disabledRule,
            equalityRule,
            otherTenantRule,
            otherEnvironmentRule,
            otherDeviceRule);

        AddHistory(dbContext, "org-a", "env-a", "DEV-A", "temperature", 101m, 103m, 105m, 107m, 109m, 110m);
        AddHistory(dbContext, "org-a", "env-a", "DEV-A", "pressure", 100m, 100m, 100m, 100m, 100m, 100m);
        AddHistory(dbContext, "org-a", "env-a", "DEV-A", "disabled-tag", 999m, 999m, 999m, 999m, 999m, 999m);
        AddHistory(dbContext, "org-b", "env-a", "DEV-A", "temperature", 999m, 999m, 999m, 999m, 999m, 999m);
        AddHistory(dbContext, "org-a", "env-b", "DEV-A", "temperature", 999m, 999m, 999m, 999m, 999m, 999m);
        AddHistory(dbContext, "org-a", "env-a", "DEV-B", "temperature", 999m, 999m, 999m, 999m, 999m, 999m);
        dbContext.TelemetryRawSamples.Add(RawSample(
            "org-a", "env-a", "DEV-A", "temperature", Now.AddMinutes(1), 999m, "future"));

        dbContext.DeviceStateSnapshots.AddRange(
            DeviceStateSnapshot.Record("org-a", "env-a", "DEV-A", "running", Now.AddHours(-25), "state-1", raiseChangedEvent: false),
            DeviceStateSnapshot.Record("org-a", "env-a", "DEV-A", "stopped", Now.AddHours(-3), "state-2", raiseChangedEvent: false),
            DeviceStateSnapshot.Record("org-a", "env-a", "DEV-A", "running", Now.AddMinutes(1), "state-future", raiseChangedEvent: false),
            DeviceStateSnapshot.Record("org-b", "env-a", "DEV-A", "running", Now.AddHours(-30), "state-other-tenant", raiseChangedEvent: false),
            DeviceStateSnapshot.Record("org-a", "env-a", "DEV-B", "running", Now.AddHours(-30), "state-other-device", raiseChangedEvent: false));

        var activeCritical = AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", "ALM-ACTIVE-CRITICAL", "critical",
            Now.AddHours(-25), "active-critical");
        var clearedWarning = AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", "ALM-CLEARED", "warning",
            Now.AddHours(-1), "cleared-warning");
        clearedWarning.Clear(Now.AddMinutes(-30), "operator");
        dbContext.AlarmEvents.AddRange(
            activeCritical,
            clearedWarning,
            AlarmEvent.Raise("org-b", "env-a", "DEV-A", "ALM-OTHER-TENANT", "critical", Now.AddSeconds(-1), "other-tenant"),
            AlarmEvent.Raise("org-a", "env-a", "DEV-B", "ALM-OTHER-DEVICE", "critical", Now.AddSeconds(-1), "other-device"),
            AlarmEvent.Raise("org-a", "env-a", "DEV-A", "ALM-FUTURE", "critical", Now.AddMinutes(1), "future-alarm"));
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        Assert.Equal("org-a", response.OrganizationId);
        Assert.Equal("env-a", response.EnvironmentId);
        Assert.Equal("DEV-A", response.DeviceAssetId);
        Assert.Equal(Now, response.CalculatedAtUtc);
        Assert.Equal(0, response.HealthScore);
        Assert.Equal("critical", response.Level);
        Assert.Equal(
            [
                EquipmentHealthScoringPolicy.ThresholdProximityRuleCode,
                EquipmentHealthScoringPolicy.RuntimeHoursRuleCode,
                EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode,
                EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode,
                EquipmentHealthScoringPolicy.TrendGrowthRuleCode,
            ],
            response.RuleEvaluations.Select(evaluation => evaluation.RuleCode));
        Assert.All(response.RiskFactors, factor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(factor.CurrentValue));
            Assert.False(string.IsNullOrWhiteSpace(factor.Threshold));
            Assert.False(string.IsNullOrWhiteSpace(factor.Evidence));
        });

        var runtime = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.RuntimeHoursRuleCode);
        Assert.Equal("risk", runtime.Status);
        Assert.Equal("21", runtime.CurrentValue);
        Assert.Equal("设备 DEV-A 运行状态", runtime.SourceFactLabel);
        Assert.Equal(Now.AddHours(-3), runtime.SourceFactOccurredAtUtc);

        var sustained = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode);
        Assert.Equal("risk", sustained.Status);
        Assert.Contains("TEMP_HIGH", sustained.Evidence, StringComparison.Ordinal);
        Assert.Contains("temperature", sustained.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("PRESSURE_LOW", sustained.Evidence, StringComparison.Ordinal);

        var alarm = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);
        Assert.Equal(65, alarm.Penalty);
        Assert.Equal("报警 ALM-ACTIVE-CRITICAL", alarm.SourceFactLabel);
        Assert.Equal(Now.AddHours(-25), alarm.SourceFactOccurredAtUtc);

        Assert.Equal("fresh", response.DataFreshness.Status);
        Assert.Equal(30, response.DataFreshness.AgeSeconds);
        Assert.Equal(Now.AddSeconds(-30), response.DataFreshness.LatestFactAtUtc);
        Assert.Equal("规则 PRESSURE_LOW · 标签 pressure", response.DataFreshness.SourceFactLabel);
        Assert.DoesNotContain(
            response.RuleEvaluations,
            evaluation => evaluation.Evidence.Contains("DISABLED_HIGH", StringComparison.Ordinal)
                || evaluation.Evidence.Contains("EQUALITY", StringComparison.Ordinal)
                || evaluation.Evidence.Contains("OTHER_TENANT", StringComparison.Ordinal)
                || evaluation.Evidence.Contains("OTHER_ENV", StringComparison.Ordinal)
                || evaluation.Evidence.Contains("OTHER_DEVICE", StringComparison.Ordinal));
        Assert.DoesNotContain(
            response.RuleEvaluations.Select(evaluation => evaluation.SourceFactLabel),
            label => label is not null
                && (label.Contains(temperatureRule.Id.ToString(), StringComparison.Ordinal)
                    || label.Contains(activeCritical.Id.ToString(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Handler_excludes_non_directional_disabled_cross_scope_and_future_facts_and_keeps_history_accumulating()
    {
        await using var dbContext = CreateDbContext(nameof(Handler_excludes_non_directional_disabled_cross_scope_and_future_facts_and_keeps_history_accumulating));
        dbContext.AlarmRules.AddRange(
            AlarmRule.Configure("org-a", "env-a", "DEV-A", "EQUAL", "EQ", "critical", "temperature", "==", 10m, "celsius", true),
            AlarmRule.Configure("org-a", "env-a", "DEV-A", "DISABLED", "DISABLED", "critical", "temperature", ">=", 1m, "celsius", false),
            AlarmRule.Configure("org-b", "env-a", "DEV-A", "OTHER", "OTHER", "critical", "temperature", ">=", 1m, "celsius", true),
            AlarmRule.Configure("org-a", "env-a", "DEV-B", "OTHER-DEVICE", "OTHER", "critical", "temperature", ">=", 1m, "celsius", true));
        dbContext.TelemetryRawSamples.AddRange(
            RawSample("org-a", "env-a", "DEV-A", "temperature", Now.AddMinutes(1), 100m, "future"),
            RawSample("org-b", "env-a", "DEV-A", "temperature", Now.AddSeconds(-1), 100m, "other-tenant"),
            RawSample("org-a", "env-a", "DEV-B", "temperature", Now.AddSeconds(-1), 100m, "other-device"));
        dbContext.AlarmEvents.Add(AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", "FUTURE", "critical", Now.AddMinutes(1), "future"));
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        Assert.Equal(100, response.HealthScore);
        Assert.Equal("healthy", response.Level);
        Assert.Empty(response.RiskFactors);
        Assert.Equal("unavailable", response.DataFreshness.Status);
        Assert.Null(response.DataFreshness.AgeSeconds);
        Assert.Null(response.DataFreshness.LatestFactAtUtc);
        Assert.Equal(
            ["accumulating", "accumulating"],
            response.RuleEvaluations
                .Where(evaluation =>
                    evaluation.RuleCode is EquipmentHealthScoringPolicy.SustainedExceedanceRuleCode
                        or EquipmentHealthScoringPolicy.TrendGrowthRuleCode)
                .Select(evaluation => evaluation.Status));
    }

    [Fact]
    public async Task Handler_excludes_a_raw_bucket_ending_exactly_at_the_history_window_start()
    {
        await using var dbContext = CreateDbContext(nameof(Handler_excludes_a_raw_bucket_ending_exactly_at_the_history_window_start));
        dbContext.AlarmRules.Add(AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "BOUNDARY_HIGH", "BOUNDARY-ALARM", "critical",
            "temperature", ">=", 100m, "celsius", true));
        dbContext.TelemetryRawSamples.Add(RawSample(
            "org-a",
            "env-a",
            "DEV-A",
            "temperature",
            Now.AddHours(-24),
            110m,
            "at-window-start"));
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        var thresholdEvaluation = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.ThresholdProximityRuleCode);
        Assert.Equal("accumulating", thresholdEvaluation.Status);
        Assert.Equal("无当前值", thresholdEvaluation.CurrentValue);
        Assert.Equal(100, response.HealthScore);
        Assert.Empty(response.RiskFactors);
        Assert.Equal("unavailable", response.DataFreshness.Status);
        Assert.Null(response.DataFreshness.LatestFactAtUtc);
    }

    [Fact]
    public async Task Handler_keeps_an_old_alarm_active_when_its_clear_fact_is_in_the_future()
    {
        await using var dbContext = CreateDbContext(nameof(Handler_keeps_an_old_alarm_active_when_its_clear_fact_is_in_the_future));
        var alarm = AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", "ALM-FUTURE-CLEAR", "critical",
            Now.AddHours(-25), "future-clear");
        alarm.Clear(Now.AddMinutes(1), "operator");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        var alarmEvaluation = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);
        Assert.Equal("risk", alarmEvaluation.Status);
        Assert.Equal(65, alarmEvaluation.Penalty);
        Assert.Equal(Now.AddHours(-25), alarmEvaluation.SourceFactOccurredAtUtc);
        Assert.Equal("stale", response.DataFreshness.Status);
        Assert.Equal(Now.AddHours(-25), response.DataFreshness.LatestFactAtUtc);
    }

    [Fact]
    public async Task Handler_loads_a_recent_clear_for_an_old_alarm_as_recovery_evidence_not_a_recent_raise()
    {
        await using var dbContext = CreateDbContext(nameof(Handler_loads_a_recent_clear_for_an_old_alarm_as_recovery_evidence_not_a_recent_raise));
        var alarm = AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", "ALM-RECOVERED", "critical",
            Now.AddHours(-25), "recovered");
        alarm.Clear(Now.AddMinutes(-1), "operator");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        var alarmEvaluation = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);
        Assert.Equal("normal", alarmEvaluation.Status);
        Assert.Equal(0, alarmEvaluation.Penalty);
        Assert.Contains("24小时发生 0 次报警", alarmEvaluation.Evidence, StringComparison.Ordinal);
        Assert.Equal("alarm-lifecycle", alarmEvaluation.SourceFactType);
        Assert.Equal("报警 ALM-RECOVERED", alarmEvaluation.SourceFactLabel);
        Assert.Equal(Now.AddMinutes(-1), alarmEvaluation.SourceFactOccurredAtUtc);
        Assert.Equal(100, response.HealthScore);
        Assert.Empty(response.RiskFactors);
        Assert.Equal("fresh", response.DataFreshness.Status);
        Assert.Equal(60, response.DataFreshness.AgeSeconds);
        Assert.Equal(Now.AddMinutes(-1), response.DataFreshness.LatestFactAtUtc);
        Assert.Equal("alarm-lifecycle", response.DataFreshness.SourceFactType);
        Assert.Equal("报警 ALM-RECOVERED", response.DataFreshness.SourceFactLabel);
    }

    [Theory]
    [InlineData(">", 100, 110, "上限")]
    [InlineData(">=", 100, 110, "上限")]
    [InlineData("<", 100, 90, "下限")]
    [InlineData("<=", 100, 90, "下限")]
    public async Task Handler_maps_all_directional_alarm_operators_without_inventing_a_direction(
        string comparisonOperator,
        decimal threshold,
        decimal current,
        string expectedDirection)
    {
        await using var dbContext = CreateDbContext(
            $"{nameof(Handler_maps_all_directional_alarm_operators_without_inventing_a_direction)}-{comparisonOperator}");
        dbContext.AlarmRules.Add(AlarmRule.Configure(
            "org-a", "env-a", "DEV-A", "DIRECTIONAL", "DIRECTIONAL-ALARM", "warning",
            "value", comparisonOperator, threshold, "unit", true));
        dbContext.TelemetryRawSamples.Add(RawSample(
            "org-a", "env-a", "DEV-A", "value", Now.AddMinutes(-1), current, "current"));
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        var thresholdEvaluation = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.ThresholdProximityRuleCode);
        Assert.Equal("risk", thresholdEvaluation.Status);
        Assert.Contains(expectedDirection, thresholdEvaluation.Evidence, StringComparison.Ordinal);
        Assert.Equal("规则 DIRECTIONAL · 标签 value", thresholdEvaluation.SourceFactLabel);
    }

    [Theory]
    [InlineData("warning", false, 45, 55, "warning")]
    [InlineData("critical", false, 65, 35, "critical")]
    [InlineData("critical", true, 0, 100, "healthy")]
    public async Task Handler_maps_alarm_severity_and_lifecycle_into_score_and_recovery(
        string severity,
        bool clearAlarm,
        int expectedPenalty,
        int expectedScore,
        string expectedLevel)
    {
        await using var dbContext = CreateDbContext(
            $"{nameof(Handler_maps_alarm_severity_and_lifecycle_into_score_and_recovery)}-{severity}-{clearAlarm}");
        var alarm = AlarmEvent.Raise(
            "org-a", "env-a", "DEV-A", $"ALM-{severity.ToUpperInvariant()}", severity,
            clearAlarm ? Now.AddHours(-2) : Now.AddHours(-25), $"alarm-{severity}");
        if (clearAlarm)
        {
            alarm.Clear(Now.AddMinutes(-1), "operator");
        }

        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();

        var response = await new GetEquipmentHealthQueryHandler(
            dbContext,
            new FixedTimeProvider(Now)).Handle(
                new GetEquipmentHealthQuery("org-a", "env-a", "DEV-A"),
                CancellationToken.None);

        var alarmEvaluation = Assert.Single(
            response.RuleEvaluations,
            evaluation => evaluation.RuleCode == EquipmentHealthScoringPolicy.AlarmFrequencyRuleCode);
        Assert.Equal(expectedPenalty, alarmEvaluation.Penalty);
        Assert.Equal(expectedScore, response.HealthScore);
        Assert.Equal(expectedLevel, response.Level);
        Assert.Equal(clearAlarm ? "normal" : "risk", alarmEvaluation.Status);
        if (clearAlarm)
        {
            Assert.Equal("alarm-lifecycle", alarmEvaluation.SourceFactType);
            Assert.Equal(Now.AddMinutes(-1), alarmEvaluation.SourceFactOccurredAtUtc);
        }
    }

    private static void AddHistory(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        params decimal[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var occurredAtUtc = Now.AddMinutes(-50 + (index * 10)).AddSeconds(-30);
            dbContext.TelemetryRawSamples.Add(RawSample(
                organizationId,
                environmentId,
                deviceAssetId,
                tagKey,
                occurredAtUtc,
                values[index],
                $"{tagKey}-{index}"));
        }
    }

    private static TelemetryRawSample RawSample(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset bucketEndUtc,
        decimal lastValue,
        string sourceSequence)
    {
        return TelemetryRawSample.Record(
            organizationId,
            environmentId,
            deviceAssetId,
            tagKey,
            bucketEndUtc.AddMinutes(-1),
            bucketEndUtc,
            1,
            lastValue,
            lastValue,
            lastValue,
            lastValue,
            lastValue,
            sourceSequence,
            "SCADA",
            "connector");
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static bool SameProperty(string actual, string expected)
    {
        return string.Equals(
            actual.Replace(" ", string.Empty, StringComparison.Ordinal),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
