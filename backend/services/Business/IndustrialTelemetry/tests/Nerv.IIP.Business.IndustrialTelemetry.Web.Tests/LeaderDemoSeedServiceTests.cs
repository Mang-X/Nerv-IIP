using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class LeaderDemoSeedServiceTests
{
    [Fact]
    public async Task Seed_creates_two_second_temperature_and_vibration_tags_with_vibration_rule_once_without_final_facts()
    {
        await using var db = CreateDbContext();
        var seed = new LeaderDemoSeedService(db);

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var tags = await db.TelemetryTags.OrderBy(x => x.TagKey).ToArrayAsync();
        Assert.Collection(
            tags,
            temperature =>
            {
                Assert.Equal("DEV-CNC-DEMO", temperature.DeviceAssetId);
                Assert.Equal(LeaderDemoSeedService.TemperatureTagKey, temperature.TagKey);
                Assert.Equal("decimal", temperature.ValueType);
                Assert.Equal("degC", temperature.UnitCode);
                Assert.Equal("bucket-2s", temperature.SamplingPolicy);
            },
            vibration =>
            {
                Assert.Equal("DEV-CNC-DEMO", vibration.DeviceAssetId);
                Assert.Equal(LeaderDemoSeedService.VibrationTagKey, vibration.TagKey);
                Assert.Equal("decimal", vibration.ValueType);
                Assert.Equal("mm/s", vibration.UnitCode);
                Assert.Equal("bucket-2s", vibration.SamplingPolicy);
            });
        var rule = Assert.Single(await db.AlarmRules.ToArrayAsync());
        Assert.Equal("ALARM-DEMO-001", rule.RuleCode);
        Assert.Equal("VIBRATION-HIGH", rule.AlarmCode);
        Assert.Equal(LeaderDemoSeedService.VibrationTagKey, rule.TagKey);
        Assert.Equal(8m, rule.ThresholdValue);
        Assert.Equal("mm/s", rule.UnitCode);
        Assert.True(rule.IsEnabled);
        Assert.Empty(await db.TelemetryRawSamples.ToArrayAsync());
        Assert.Empty(await db.TelemetrySummaries.ToArrayAsync());
        Assert.Empty(await db.DeviceStateSnapshots.ToArrayAsync());
        Assert.Empty(await db.AlarmEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_temperature_tag()
    {
        await using var db = CreateDbContext();
        db.TelemetryTags.Add(TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-DEMO", LeaderDemoSeedService.TemperatureTagKey, "string", "text", "on-change"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(LeaderDemoSeedService.TemperatureTagKey, exception.Message, StringComparison.Ordinal);
        Assert.Equal("string", (await db.TelemetryTags.SingleAsync()).ValueType);
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_vibration_tag()
    {
        await using var db = CreateDbContext();
        db.TelemetryTags.Add(TelemetryTag.Create(
            "org-001",
            "env-dev",
            "DEV-CNC-DEMO",
            LeaderDemoSeedService.VibrationTagKey,
            "decimal",
            "g",
            "bucket-10s"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(db).SeedAsync("org-001", "env-dev"));

        Assert.Contains(LeaderDemoSeedService.VibrationTagKey, exception.Message, StringComparison.Ordinal);
        Assert.Equal("g", (await db.TelemetryTags.SingleAsync()).UnitCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"industrial-telemetry-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new IndustrialTelemetrySeedTestMediator());
    }

    private sealed class IndustrialTelemetrySeedTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
