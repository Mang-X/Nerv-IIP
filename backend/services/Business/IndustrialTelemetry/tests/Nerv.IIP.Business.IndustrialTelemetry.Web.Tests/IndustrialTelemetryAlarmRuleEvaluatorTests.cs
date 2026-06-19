using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryAlarmRuleEvaluatorTests
{
    [Fact]
    public async Task RecordTelemetrySample_evaluates_enabled_alarm_rules_and_raises_alarm()
    {
        await using var dbContext = CreateDbContext(nameof(RecordTelemetrySample_evaluates_enabled_alarm_rules_and_raises_alarm));
        dbContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-01", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", ">=", 90m, "celsius", true));
        dbContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-01", "PRESSURE_RULE", "PRESSURE_HIGH", "warning", "pressure", ">=", 10m, "bar", true));
        await dbContext.SaveChangesAsync();

        var bucketEndUtc = new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero);
        await new RecordTelemetrySampleCommandHandler(dbContext).Handle(
            new RecordTelemetrySampleCommand(
                "org-001",
                "env-dev",
                "DEV-PUMP-01",
                "Temperature",
                bucketEndUtc.AddMinutes(-5),
                bucketEndUtc,
                10,
                80m,
                95m,
                84m,
                "sample-001"),
            CancellationToken.None);

        var alarm = Assert.Single(dbContext.AlarmEvents.Local);
        Assert.Equal("TEMP_HIGH", alarm.AlarmCode);
        Assert.Equal("warning", alarm.Severity);
        Assert.Equal(bucketEndUtc, alarm.RaisedAtUtc);
        Assert.Equal("TEMP_RULE:2026-06-01T08:05:00.0000000+00:00", alarm.ExternalAlarmId);
        Assert.IsType<AlarmRaisedDomainEvent>(alarm.GetDomainEvents().Single());
    }

    [Fact]
    public async Task RecordTelemetrySample_does_not_raise_alarm_when_threshold_misses_or_rule_is_disabled()
    {
        await using var dbContext = CreateDbContext(nameof(RecordTelemetrySample_does_not_raise_alarm_when_threshold_misses_or_rule_is_disabled));
        dbContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-02", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", ">=", 90m, "celsius", true));
        dbContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-02", "TEMP_DISABLED", "TEMP_DISABLED_HIGH", "warning", "temperature", ">=", 50m, "celsius", false));
        await dbContext.SaveChangesAsync();

        var bucketEndUtc = new DateTimeOffset(2026, 6, 1, 8, 10, 0, TimeSpan.Zero);
        await new RecordTelemetrySampleCommandHandler(dbContext).Handle(
            new RecordTelemetrySampleCommand(
                "org-001",
                "env-dev",
                "DEV-PUMP-02",
                "temperature",
                bucketEndUtc.AddMinutes(-5),
                bucketEndUtc,
                10,
                70m,
                89m,
                80m,
                "sample-002"),
            CancellationToken.None);

        Assert.Empty(dbContext.AlarmEvents);
    }

    [Fact]
    public async Task RecordTelemetrySample_uses_rule_and_bucket_external_alarm_id_for_idempotency()
    {
        await using var dbContext = CreateDbContext(nameof(RecordTelemetrySample_uses_rule_and_bucket_external_alarm_id_for_idempotency));
        dbContext.AlarmRules.Add(AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-03", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", ">=", 90m, "celsius", true));
        await dbContext.SaveChangesAsync();

        var bucketEndUtc = new DateTimeOffset(2026, 6, 1, 8, 15, 0, TimeSpan.Zero);
        var command = new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-PUMP-03",
            "temperature",
            bucketEndUtc.AddMinutes(-5),
            bucketEndUtc,
            10,
            70m,
            95m,
            81m,
            "sample-003");
        var handler = new RecordTelemetrySampleCommandHandler(dbContext);

        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var alarm = Assert.Single(dbContext.AlarmEvents);
        Assert.Equal("TEMP_RULE:2026-06-01T08:15:00.0000000+00:00", alarm.ExternalAlarmId);
    }

    [Fact]
    public async Task RecordTelemetrySample_keeps_rule_bucket_idempotency_when_rule_definition_changes_after_alarm_was_raised()
    {
        await using var dbContext = CreateDbContext(nameof(RecordTelemetrySample_keeps_rule_bucket_idempotency_when_rule_definition_changes_after_alarm_was_raised));
        var rule = AlarmRule.Configure("org-001", "env-dev", "DEV-PUMP-04", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", ">=", 90m, "celsius", true);
        dbContext.AlarmRules.Add(rule);
        await dbContext.SaveChangesAsync();

        var bucketEndUtc = new DateTimeOffset(2026, 6, 1, 8, 20, 0, TimeSpan.Zero);
        var command = new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-PUMP-04",
            "temperature",
            bucketEndUtc.AddMinutes(-5),
            bucketEndUtc,
            10,
            70m,
            95m,
            81m,
            "sample-004");
        var handler = new RecordTelemetrySampleCommandHandler(dbContext);

        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        rule.UpdateDefinition("TEMP_CRITICAL", "critical", "temperature", ">=", 90m, "celsius", true);
        await dbContext.SaveChangesAsync();
        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var alarm = Assert.Single(dbContext.AlarmEvents);
        Assert.Equal("TEMP_HIGH", alarm.AlarmCode);
        Assert.Equal("warning", alarm.Severity);
        Assert.Equal("TEMP_RULE:2026-06-01T08:20:00.0000000+00:00", alarm.ExternalAlarmId);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
