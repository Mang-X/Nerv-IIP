using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Historian;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryHistorianTests
{
    [Fact]
    public async Task Record_sample_persists_raw_historian_detail_and_keeps_summary_compatibility()
    {
        await using var dbContext = CreateDbContext(nameof(Record_sample_persists_raw_historian_detail_and_keeps_summary_compatibility));
        dbContext.TelemetryTags.Add(TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", "number", "rpm", "sample-60s"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordTelemetrySampleCommandHandler(dbContext);
        var result = await handler.Handle(new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "spindle.speed",
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 8, 1, 0, TimeSpan.Zero),
            3,
            1200m,
            1500m,
            1350m,
            "opcua:cell-01:spindle.speed:1782892800000",
            "opcua",
            "host-001/cell-01",
            FirstValue: 1210m,
            LastValue: 1490m), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.NotNull(result.TelemetrySummaryId);
        var raw = Assert.Single(dbContext.TelemetryRawSamples);
        Assert.Equal("spindle.speed", raw.TagKey);
        Assert.Equal(3, raw.SampleCount);
        Assert.Equal(1200m, raw.MinValue);
        Assert.Equal(1500m, raw.MaxValue);
        Assert.Equal(1350m, raw.AverageValue);
        Assert.Equal(1210m, raw.FirstValue);
        Assert.Equal(1490m, raw.LastValue);
        Assert.Equal("opcua:cell-01:spindle.speed:1782892800000", raw.SourceSequence);
        Assert.Single(dbContext.TelemetrySummaries);
    }

    [Fact]
    public async Task Record_sample_rejects_bucket_width_that_violates_tag_sampling_policy()
    {
        await using var dbContext = CreateDbContext(nameof(Record_sample_rejects_bucket_width_that_violates_tag_sampling_policy));
        dbContext.TelemetryTags.Add(TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "temperature", "number", "celsius", "sample-10s"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordTelemetrySampleCommandHandler(dbContext);

        var ex = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(new RecordTelemetrySampleCommand(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "temperature",
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 8, 1, 0, TimeSpan.Zero),
            1,
            80m,
            80m,
            80m,
            "opcua:cell-01:temperature:1782892800000",
            "opcua",
            "host-001/cell-01",
            FirstValue: 80m,
            LastValue: 80m), CancellationToken.None));

        Assert.Contains("sampling policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Downsampling_rolls_raw_samples_to_hourly_and_daily_min_max_weighted_average_first_last()
    {
        await using var dbContext = CreateDbContext(nameof(Downsampling_rolls_raw_samples_to_hourly_and_daily_min_max_weighted_average_first_last));
        dbContext.TelemetryRawSamples.AddRange(
            Raw("DEV-CNC-01", "temperature", "2026-07-01T08:00:00Z", "2026-07-01T08:01:00Z", 2, 10m, 12m, 11m, 10m, 12m, "seq-001"),
            Raw("DEV-CNC-01", "temperature", "2026-07-01T08:30:00Z", "2026-07-01T08:31:00Z", 2, 20m, 22m, 21m, 20m, 22m, "seq-002"),
            Raw("DEV-CNC-01", "temperature", "2026-07-01T09:00:00Z", "2026-07-01T09:01:00Z", 1, 30m, 30m, 30m, 30m, 30m, "seq-003"));
        await dbContext.SaveChangesAsync();

        var service = new TelemetryHistorianService(dbContext);
        var result = await service.RunDownsamplingAsync(
            "org-001",
            "env-dev",
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, result.HourlyRollupsCreated);
        Assert.Equal(1, result.DailyRollupsCreated);
        var hourly = await dbContext.TelemetryRollups
            .SingleAsync(x => x.Grain == TelemetryRollupGrain.Hourly && x.WindowStartUtc == DateTimeOffset.Parse("2026-07-01T08:00:00Z"));
        Assert.Equal(4, hourly.SampleCount);
        Assert.Equal(10m, hourly.MinValue);
        Assert.Equal(22m, hourly.MaxValue);
        Assert.Equal(16m, hourly.AverageValue);
        Assert.Equal(10m, hourly.FirstValue);
        Assert.Equal(22m, hourly.LastValue);

        var daily = await dbContext.TelemetryRollups.SingleAsync(x => x.Grain == TelemetryRollupGrain.Daily);
        Assert.Equal(5, daily.SampleCount);
        Assert.Equal(10m, daily.MinValue);
        Assert.Equal(30m, daily.MaxValue);
        Assert.Equal(18.8m, daily.AverageValue);
        Assert.Equal(10m, daily.FirstValue);
        Assert.Equal(30m, daily.LastValue);
    }

    [Fact]
    public async Task Retention_cleanup_removes_only_expired_layers()
    {
        await using var dbContext = CreateDbContext(nameof(Retention_cleanup_removes_only_expired_layers));
        dbContext.TelemetryRawSamples.AddRange(
            Raw("DEV-CNC-01", "temperature", "2026-06-29T08:00:00Z", "2026-06-29T08:01:00Z", 1, 10m, 10m, 10m, 10m, 10m, "raw-old"),
            Raw("DEV-CNC-01", "temperature", "2026-07-01T08:00:00Z", "2026-07-01T08:01:00Z", 1, 20m, 20m, 20m, 20m, 20m, "raw-new"));
        dbContext.TelemetryRollups.AddRange(
            Rollup(TelemetryRollupGrain.Hourly, "2026-05-01T08:00:00Z", "2026-05-01T09:00:00Z", "hour-old"),
            Rollup(TelemetryRollupGrain.Hourly, "2026-07-01T08:00:00Z", "2026-07-01T09:00:00Z", "hour-new"),
            Rollup(TelemetryRollupGrain.Daily, "2025-01-01T00:00:00Z", "2025-01-02T00:00:00Z", "day-old"),
            Rollup(TelemetryRollupGrain.Daily, "2026-07-01T00:00:00Z", "2026-07-02T00:00:00Z", "day-new"));
        await dbContext.SaveChangesAsync();

        var service = new TelemetryHistorianService(dbContext);
        var result = await service.ApplyRetentionAsync(
            new TelemetryHistorianRetentionPolicy(TimeSpan.FromDays(1), TimeSpan.FromDays(30), TimeSpan.FromDays(365)),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, result.RawSamplesDeleted);
        Assert.Equal(1, result.HourlyRollupsDeleted);
        Assert.Equal(1, result.DailyRollupsDeleted);
        Assert.DoesNotContain(dbContext.TelemetryRawSamples, x => x.SourceSequence == "raw-old");
        Assert.Contains(dbContext.TelemetryRawSamples, x => x.SourceSequence == "raw-new");
        Assert.DoesNotContain(dbContext.TelemetryRollups, x => x.SourceSequence == "hour-old");
        Assert.DoesNotContain(dbContext.TelemetryRollups, x => x.SourceSequence == "day-old");
    }

    private static TelemetryRawSample Raw(
        string deviceAssetId,
        string tagKey,
        string bucketStartUtc,
        string bucketEndUtc,
        int sampleCount,
        decimal minValue,
        decimal maxValue,
        decimal averageValue,
        decimal firstValue,
        decimal lastValue,
        string sourceSequence)
    {
        return TelemetryRawSample.Record(
            "org-001",
            "env-dev",
            deviceAssetId,
            tagKey,
            DateTimeOffset.Parse(bucketStartUtc),
            DateTimeOffset.Parse(bucketEndUtc),
            sampleCount,
            minValue,
            maxValue,
            averageValue,
            firstValue,
            lastValue,
            sourceSequence,
            "opcua",
            "host-001/cell-01");
    }

    private static TelemetryRollup Rollup(TelemetryRollupGrain grain, string windowStartUtc, string windowEndUtc, string sourceSequence)
    {
        return TelemetryRollup.Record(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "temperature",
            grain,
            DateTimeOffset.Parse(windowStartUtc),
            DateTimeOffset.Parse(windowEndUtc),
            1,
            10m,
            10m,
            10m,
            10m,
            10m,
            sourceSequence);
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

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
