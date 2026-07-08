using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
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
        CancellationToken cancellationToken,
        int maxRawSamples = 50000,
        int maxHourlyRollups = 50000)
    {
        var rawSampleBatch = await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.SourceSequence)
            .Take(maxRawSamples + 1)
            .ToArrayAsync(cancellationToken);
        var rawSamples = rawSampleBatch.Take(maxRawSamples).ToArray();
        var lastRawBatchKey = await ResolveRawBatchBoundaryAsync(rawSampleBatch, rawSamples, organizationId, environmentId, maxRawSamples, asOfUtc, cancellationToken);
        if (lastRawBatchKey is null && rawSampleBatch.Length > maxRawSamples)
        {
            rawSamples = await LoadRawWindowAsync(ToRawWindowKey(rawSamples[0]), asOfUtc, cancellationToken);
        }

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
            if (lastRawBatchKey == new RawWindowKey(group.Key.OrganizationId, group.Key.EnvironmentId, group.Key.DeviceAssetId, group.Key.TagKey, group.Key.WindowStartUtc))
            {
                continue;
            }

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

        var hourlyRollupBatch = await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowEndUtc <= asOfUtc)
            .OrderBy(x => x.WindowStartUtc)
            .ThenBy(x => x.SourceSequence)
            .Take(maxHourlyRollups + 1)
            .ToArrayAsync(cancellationToken);
        var persistedHourlyRollups = hourlyRollupBatch.Take(maxHourlyRollups).ToArray();
        var lastHourlyBatchKey = await ResolveHourlyBatchBoundaryAsync(hourlyRollupBatch, persistedHourlyRollups, organizationId, environmentId, maxHourlyRollups, asOfUtc, cancellationToken);
        if (lastHourlyBatchKey is null && hourlyRollupBatch.Length > maxHourlyRollups)
        {
            persistedHourlyRollups = await LoadHourlyDayAsync(ToRollupWindowKey(hourlyRollupBatch[0]), asOfUtc, cancellationToken);
        }

        var hourlyRollups = persistedHourlyRollups.Concat(createdHourlyRollups).ToArray();

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
            if (lastHourlyBatchKey == new RollupWindowKey(group.Key.OrganizationId, group.Key.EnvironmentId, group.Key.DeviceAssetId, group.Key.TagKey, group.Key.WindowStartUtc))
            {
                continue;
            }

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
        return await ApplyRetentionAsync(null, null, null, null, policy, asOfUtc, cancellationToken);
    }

    public async Task<TelemetryRetentionCleanupResult> ApplyRetentionAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        TelemetryHistorianRetentionPolicy defaultPolicy,
        CancellationToken cancellationToken)
    {
        var tags = await dbContext.TelemetryTags
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.TagKey)
            .ToArrayAsync(cancellationToken);
        if (tags.Length == 0)
        {
            return await ApplyRetentionAsync(organizationId, environmentId, null, null, defaultPolicy, asOfUtc, cancellationToken);
        }

        var result = new TelemetryRetentionCleanupResult(0, 0, 0);
        foreach (var tag in tags)
        {
            var tagPolicy = ToRetentionPolicy(tag, defaultPolicy);
            var tagResult = await ApplyRetentionAsync(organizationId, environmentId, tag.DeviceAssetId, tag.TagKey, tagPolicy, asOfUtc, cancellationToken);
            result = new TelemetryRetentionCleanupResult(
                result.RawSamplesDeleted + tagResult.RawSamplesDeleted,
                result.HourlyRollupsDeleted + tagResult.HourlyRollupsDeleted,
                result.DailyRollupsDeleted + tagResult.DailyRollupsDeleted);
        }

        return result;
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

    private async Task<RawWindowKey?> ResolveRawBatchBoundaryAsync(
        TelemetryRawSample[] rawSampleBatch,
        TelemetryRawSample[] rawSamples,
        string organizationId,
        string environmentId,
        int maxRawSamples,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (rawSampleBatch.Length <= maxRawSamples || rawSamples.Length == 0)
        {
            return null;
        }

        var firstKey = ToRawWindowKey(rawSamples[0]);
        var lastKey = ToRawWindowKey(rawSamples[^1]);
        if (firstKey == lastKey)
        {
            return null;
        }

        var firstWindowHasMoreRows = await HasRawWindowRowsAfterBatchAsync(firstKey, organizationId, environmentId, asOfUtc, maxRawSamples, cancellationToken);
        return firstWindowHasMoreRows ? firstKey : lastKey;
    }

    private async Task<bool> HasRawWindowRowsAfterBatchAsync(
        RawWindowKey key,
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        int maxRawSamples,
        CancellationToken cancellationToken)
    {
        var windowEndUtc = key.WindowStartUtc.AddHours(1);
        var count = await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.BucketStartUtc >= key.WindowStartUtc)
            .Where(x => x.BucketStartUtc < windowEndUtc)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .Take(maxRawSamples + 1)
            .CountAsync(cancellationToken);
        return count > maxRawSamples;
    }

    private async Task<TelemetryRawSample[]> LoadRawWindowAsync(RawWindowKey key, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var windowEndUtc = key.WindowStartUtc.AddHours(1);
        return await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == key.OrganizationId)
            .Where(x => x.EnvironmentId == key.EnvironmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.BucketStartUtc >= key.WindowStartUtc)
            .Where(x => x.BucketStartUtc < windowEndUtc)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<RollupWindowKey?> ResolveHourlyBatchBoundaryAsync(
        TelemetryRollup[] hourlyRollupBatch,
        TelemetryRollup[] hourlyRollups,
        string organizationId,
        string environmentId,
        int maxHourlyRollups,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (hourlyRollupBatch.Length <= maxHourlyRollups || hourlyRollups.Length == 0)
        {
            return null;
        }

        var firstKey = ToRollupWindowKey(hourlyRollups[0]);
        var lastKey = ToRollupWindowKey(hourlyRollups[^1]);
        if (firstKey == lastKey)
        {
            return null;
        }

        var firstDayHasMoreRows = await HasHourlyDayRowsAfterBatchAsync(firstKey, organizationId, environmentId, asOfUtc, maxHourlyRollups, cancellationToken);
        return firstDayHasMoreRows ? firstKey : lastKey;
    }

    private async Task<bool> HasHourlyDayRowsAfterBatchAsync(
        RollupWindowKey key,
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        int maxHourlyRollups,
        CancellationToken cancellationToken)
    {
        var windowStartUtc = new DateTimeOffset(key.WindowStartUtc, TimeSpan.Zero);
        var windowEndUtc = windowStartUtc.AddDays(1);
        var count = await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowStartUtc >= windowStartUtc)
            .Where(x => x.WindowStartUtc < windowEndUtc)
            .Where(x => x.WindowEndUtc <= asOfUtc)
            .Take(maxHourlyRollups + 1)
            .CountAsync(cancellationToken);
        return count > maxHourlyRollups;
    }

    private async Task<TelemetryRollup[]> LoadHourlyDayAsync(RollupWindowKey key, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var windowStartUtc = new DateTimeOffset(key.WindowStartUtc, TimeSpan.Zero);
        var windowEndUtc = windowStartUtc.AddDays(1);
        return await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == key.OrganizationId)
            .Where(x => x.EnvironmentId == key.EnvironmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowStartUtc >= windowStartUtc)
            .Where(x => x.WindowStartUtc < windowEndUtc)
            .Where(x => x.WindowEndUtc <= asOfUtc)
            .OrderBy(x => x.WindowStartUtc)
            .ThenBy(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<TelemetryRetentionCleanupResult> ApplyRetentionAsync(
        string? organizationId,
        string? environmentId,
        string? deviceAssetId,
        string? tagKey,
        TelemetryHistorianRetentionPolicy policy,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        var rawCutoffUnixTimeMilliseconds = asOfUtc.Subtract(policy.RawRetention).ToUnixTimeMilliseconds();
        var hourlyCutoffUnixTimeMilliseconds = asOfUtc.Subtract(policy.HourlyRetention).ToUnixTimeMilliseconds();
        var dailyCutoffUnixTimeMilliseconds = asOfUtc.Subtract(policy.DailyRetention).ToUnixTimeMilliseconds();

        var rawQuery = Filter(dbContext.TelemetryRawSamples.AsQueryable(), organizationId, environmentId, deviceAssetId, tagKey)
            .Where(x => x.BucketEndUnixTimeMilliseconds < rawCutoffUnixTimeMilliseconds);
        var hourlyQuery = Filter(dbContext.TelemetryRollups.AsQueryable(), organizationId, environmentId, deviceAssetId, tagKey)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowEndUnixTimeMilliseconds < hourlyCutoffUnixTimeMilliseconds);
        var dailyQuery = Filter(dbContext.TelemetryRollups.AsQueryable(), organizationId, environmentId, deviceAssetId, tagKey)
            .Where(x => x.Grain == TelemetryRollupGrain.Daily)
            .Where(x => x.WindowEndUnixTimeMilliseconds < dailyCutoffUnixTimeMilliseconds);

        var rawDeleted = await DeleteRawSamplesAsync(rawQuery, cancellationToken);
        var hourlyDeleted = await DeleteRollupsAsync(hourlyQuery, cancellationToken);
        var dailyDeleted = await DeleteRollupsAsync(dailyQuery, cancellationToken);
        return new TelemetryRetentionCleanupResult(rawDeleted, hourlyDeleted, dailyDeleted);
    }

    private async Task<int> DeleteRawSamplesAsync(IQueryable<TelemetryRawSample> query, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await query.ExecuteDeleteAsync(cancellationToken);
        }

        var expiredRawSamples = await query.ToArrayAsync(cancellationToken);
        dbContext.TelemetryRawSamples.RemoveRange(expiredRawSamples);
        return expiredRawSamples.Length;
    }

    private async Task<int> DeleteRollupsAsync(IQueryable<TelemetryRollup> query, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await query.ExecuteDeleteAsync(cancellationToken);
        }

        var expiredRollups = await query.ToArrayAsync(cancellationToken);
        dbContext.TelemetryRollups.RemoveRange(expiredRollups);
        return expiredRollups.Length;
    }

    private static TelemetryHistorianRetentionPolicy ToRetentionPolicy(TelemetryTag tag, TelemetryHistorianRetentionPolicy defaultPolicy)
    {
        var samplingPolicy = TelemetrySamplingPolicy.Parse(tag.SamplingPolicy);
        return new TelemetryHistorianRetentionPolicy(
            samplingPolicy.RawRetention ?? defaultPolicy.RawRetention,
            samplingPolicy.HourlyRetention ?? defaultPolicy.HourlyRetention,
            samplingPolicy.DailyRetention ?? defaultPolicy.DailyRetention);
    }

    private static IQueryable<TelemetryRawSample> Filter(
        IQueryable<TelemetryRawSample> query,
        string? organizationId,
        string? environmentId,
        string? deviceAssetId,
        string? tagKey)
    {
        if (organizationId is not null)
        {
            query = query.Where(x => x.OrganizationId == organizationId);
        }

        if (environmentId is not null)
        {
            query = query.Where(x => x.EnvironmentId == environmentId);
        }

        if (deviceAssetId is not null)
        {
            query = query.Where(x => x.DeviceAssetId == deviceAssetId);
        }

        if (tagKey is not null)
        {
            query = query.Where(x => x.TagKey == tagKey);
        }

        return query;
    }

    private static IQueryable<TelemetryRollup> Filter(
        IQueryable<TelemetryRollup> query,
        string? organizationId,
        string? environmentId,
        string? deviceAssetId,
        string? tagKey)
    {
        if (organizationId is not null)
        {
            query = query.Where(x => x.OrganizationId == organizationId);
        }

        if (environmentId is not null)
        {
            query = query.Where(x => x.EnvironmentId == environmentId);
        }

        if (deviceAssetId is not null)
        {
            query = query.Where(x => x.DeviceAssetId == deviceAssetId);
        }

        if (tagKey is not null)
        {
            query = query.Where(x => x.TagKey == tagKey);
        }

        return query;
    }

    private static RawWindowKey ToRawWindowKey(TelemetryRawSample sample)
    {
        return new RawWindowKey(sample.OrganizationId, sample.EnvironmentId, sample.DeviceAssetId, sample.TagKey, TruncateToHour(sample.BucketStartUtc));
    }

    private static RollupWindowKey ToRollupWindowKey(TelemetryRollup rollup)
    {
        return new RollupWindowKey(rollup.OrganizationId, rollup.EnvironmentId, rollup.DeviceAssetId, rollup.TagKey, rollup.WindowStartUtc.Date);
    }

    private sealed record RawWindowKey(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, DateTimeOffset WindowStartUtc);

    private sealed record RollupWindowKey(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, DateTime WindowStartUtc);
}
