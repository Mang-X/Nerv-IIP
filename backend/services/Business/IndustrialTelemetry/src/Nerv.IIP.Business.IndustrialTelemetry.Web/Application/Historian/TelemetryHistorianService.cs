using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Historian;

public sealed record TelemetryHistorianRetentionPolicy(TimeSpan RawRetention, TimeSpan HourlyRetention, TimeSpan DailyRetention);

public sealed record TelemetryDownsamplingResult(int HourlyRollupsCreated, int DailyRollupsCreated);

public sealed record TelemetryRetentionCleanupResult(int RawSamplesDeleted, int HourlyRollupsDeleted, int DailyRollupsDeleted);

public sealed class TelemetryHistorianService(ApplicationDbContext dbContext)
{
    public async Task<TelemetryDownsamplingResult> RunDownsamplingAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        var rawSamples = await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);

        var hourlyRollupsCreated = 0;
        var createdHourlyRollups = new List<TelemetryRollup>();
        foreach (var group in rawSamples.GroupBy(x => new
        {
            x.OrganizationId,
            x.EnvironmentId,
            x.DeviceAssetId,
            x.TagKey,
            WindowStartUtc = TruncateToHour(x.BucketStartUtc)
        }))
        {
            var windowEndUtc = group.Key.WindowStartUtc.AddHours(1);
            if (windowEndUtc > asOfUtc)
            {
                continue;
            }

            var exists = await dbContext.TelemetryRollups.AnyAsync(
                x => x.OrganizationId == group.Key.OrganizationId
                    && x.EnvironmentId == group.Key.EnvironmentId
                    && x.DeviceAssetId == group.Key.DeviceAssetId
                    && x.TagKey == group.Key.TagKey
                    && x.Grain == TelemetryRollupGrain.Hourly
                    && x.WindowStartUtc == group.Key.WindowStartUtc,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            var rollup = CreateHourlyRollup(group.Key.OrganizationId, group.Key.EnvironmentId, group.Key.DeviceAssetId, group.Key.TagKey, group.Key.WindowStartUtc, windowEndUtc, group);
            dbContext.TelemetryRollups.Add(rollup);
            createdHourlyRollups.Add(rollup);
            hourlyRollupsCreated++;
        }

        var hourlyRollups = await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowEndUtc <= asOfUtc)
            .OrderBy(x => x.WindowStartUtc)
            .ThenBy(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);
        hourlyRollups = hourlyRollups.Concat(createdHourlyRollups).ToArray();

        var dailyRollupsCreated = 0;
        foreach (var group in hourlyRollups.GroupBy(x => new
        {
            x.OrganizationId,
            x.EnvironmentId,
            x.DeviceAssetId,
            x.TagKey,
            WindowStartUtc = x.WindowStartUtc.Date
        }))
        {
            var windowStartUtc = new DateTimeOffset(group.Key.WindowStartUtc, TimeSpan.Zero);
            var windowEndUtc = windowStartUtc.AddDays(1);
            if (windowEndUtc > asOfUtc)
            {
                continue;
            }

            var exists = await dbContext.TelemetryRollups.AnyAsync(
                x => x.OrganizationId == group.Key.OrganizationId
                    && x.EnvironmentId == group.Key.EnvironmentId
                    && x.DeviceAssetId == group.Key.DeviceAssetId
                    && x.TagKey == group.Key.TagKey
                    && x.Grain == TelemetryRollupGrain.Daily
                    && x.WindowStartUtc == windowStartUtc,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.TelemetryRollups.Add(CreateDailyRollup(group.Key.OrganizationId, group.Key.EnvironmentId, group.Key.DeviceAssetId, group.Key.TagKey, windowStartUtc, windowEndUtc, group));
            dailyRollupsCreated++;
        }

        return new TelemetryDownsamplingResult(hourlyRollupsCreated, dailyRollupsCreated);
    }

    public async Task<TelemetryRetentionCleanupResult> ApplyRetentionAsync(
        TelemetryHistorianRetentionPolicy policy,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        var rawCutoffUtc = asOfUtc.Subtract(policy.RawRetention);
        var hourlyCutoffUtc = asOfUtc.Subtract(policy.HourlyRetention);
        var dailyCutoffUtc = asOfUtc.Subtract(policy.DailyRetention);

        var expiredRawSamples = await dbContext.TelemetryRawSamples
            .Where(x => x.BucketEndUtc < rawCutoffUtc)
            .ToArrayAsync(cancellationToken);
        var expiredHourlyRollups = await dbContext.TelemetryRollups
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowEndUtc < hourlyCutoffUtc)
            .ToArrayAsync(cancellationToken);
        var expiredDailyRollups = await dbContext.TelemetryRollups
            .Where(x => x.Grain == TelemetryRollupGrain.Daily)
            .Where(x => x.WindowEndUtc < dailyCutoffUtc)
            .ToArrayAsync(cancellationToken);

        dbContext.TelemetryRawSamples.RemoveRange(expiredRawSamples);
        dbContext.TelemetryRollups.RemoveRange(expiredHourlyRollups);
        dbContext.TelemetryRollups.RemoveRange(expiredDailyRollups);

        return new TelemetryRetentionCleanupResult(expiredRawSamples.Length, expiredHourlyRollups.Length, expiredDailyRollups.Length);
    }

    private static TelemetryRollup CreateHourlyRollup(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        IEnumerable<TelemetryRawSample> samples)
    {
        var ordered = samples.OrderBy(x => x.BucketStartUtc).ThenBy(x => x.SourceSequence).ToArray();
        var last = ordered.OrderBy(x => x.BucketEndUtc).ThenBy(x => x.SourceSequence).Last();
        return TelemetryRollup.Record(
            organizationId,
            environmentId,
            deviceAssetId,
            tagKey,
            TelemetryRollupGrain.Hourly,
            windowStartUtc,
            windowEndUtc,
            ordered.Sum(x => x.SampleCount),
            ordered.Min(x => x.MinValue),
            ordered.Max(x => x.MaxValue),
            WeightedAverage(ordered.Select(x => (x.AverageValue, x.SampleCount))),
            ordered.First().FirstValue,
            last.LastValue,
            $"historian:hourly:{deviceAssetId}:{tagKey}:{windowStartUtc.ToUnixTimeMilliseconds()}");
    }

    private static TelemetryRollup CreateDailyRollup(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        IEnumerable<TelemetryRollup> rollups)
    {
        var ordered = rollups.OrderBy(x => x.WindowStartUtc).ThenBy(x => x.SourceSequence).ToArray();
        var last = ordered.OrderBy(x => x.WindowEndUtc).ThenBy(x => x.SourceSequence).Last();
        return TelemetryRollup.Record(
            organizationId,
            environmentId,
            deviceAssetId,
            tagKey,
            TelemetryRollupGrain.Daily,
            windowStartUtc,
            windowEndUtc,
            ordered.Sum(x => x.SampleCount),
            ordered.Min(x => x.MinValue),
            ordered.Max(x => x.MaxValue),
            WeightedAverage(ordered.Select(x => (x.AverageValue, x.SampleCount))),
            ordered.First().FirstValue,
            last.LastValue,
            $"historian:daily:{deviceAssetId}:{tagKey}:{windowStartUtc.ToUnixTimeMilliseconds()}");
    }

    private static DateTimeOffset TruncateToHour(DateTimeOffset value)
    {
        return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset).ToUniversalTime();
    }

    private static decimal WeightedAverage(IEnumerable<(decimal AverageValue, int SampleCount)> items)
    {
        var totalCount = 0;
        decimal weightedSum = 0;
        foreach (var item in items)
        {
            totalCount += item.SampleCount;
            weightedSum += item.AverageValue * item.SampleCount;
        }

        return weightedSum / totalCount;
    }
}
