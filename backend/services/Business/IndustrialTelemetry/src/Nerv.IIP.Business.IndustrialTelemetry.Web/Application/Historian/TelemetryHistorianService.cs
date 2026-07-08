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
        int maxPendingHourlyWindows = 50000,
        int maxPendingDailyWindows = 50000)
    {
        var rawSamples = await LoadRawSamplesForPendingHourlyWindowsAsync(
            organizationId,
            environmentId,
            asOfUtc,
            maxPendingHourlyWindows,
            cancellationToken);

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

        var persistedHourlyRollups = await LoadHourlyRollupsForPendingDailyWindowsAsync(
            organizationId,
            environmentId,
            asOfUtc,
            maxPendingDailyWindows,
            cancellationToken);
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

    private async Task<TelemetryRawSample[]> LoadRawSamplesForPendingHourlyWindowsAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        int maxPendingHourlyWindows,
        CancellationToken cancellationToken)
    {
        var pendingKeys = await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .GroupBy(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.TagKey,
                WindowStartUtc = x.HourlyWindowStartUtc
            })
            .Where(group => !dbContext.TelemetryRollups.Any(rollup =>
                rollup.OrganizationId == group.Key.OrganizationId
                && rollup.EnvironmentId == group.Key.EnvironmentId
                && rollup.DeviceAssetId == group.Key.DeviceAssetId
                && rollup.TagKey == group.Key.TagKey
                && rollup.Grain == TelemetryRollupGrain.Hourly
                && rollup.WindowStartUtc == group.Key.WindowStartUtc))
            .OrderBy(group => group.Key.WindowStartUtc)
            .ThenBy(group => group.Key.DeviceAssetId)
            .ThenBy(group => group.Key.TagKey)
            .Take(maxPendingHourlyWindows)
            .Select(group => new
            {
                group.Key.OrganizationId,
                group.Key.EnvironmentId,
                group.Key.DeviceAssetId,
                group.Key.TagKey,
                group.Key.WindowStartUtc
            })
            .ToArrayAsync(cancellationToken);
        if (pendingKeys.Length == 0)
        {
            return [];
        }

        var samples = new List<TelemetryRawSample>();
        foreach (var key in pendingKeys)
        {
            samples.AddRange(await LoadRawWindowAsync(
                new RawWindowKey(key.OrganizationId, key.EnvironmentId, key.DeviceAssetId, key.TagKey, key.WindowStartUtc),
                asOfUtc,
                cancellationToken));
        }

        return samples.ToArray();
    }

    private async Task<TelemetryRawSample[]> LoadRawWindowAsync(RawWindowKey key, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        return await dbContext.TelemetryRawSamples
            .Where(x => x.OrganizationId == key.OrganizationId)
            .Where(x => x.EnvironmentId == key.EnvironmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.HourlyWindowStartUtc == key.WindowStartUtc)
            .Where(x => x.BucketEndUtc <= asOfUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.SourceSequence)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<TelemetryRollup[]> LoadHourlyRollupsForPendingDailyWindowsAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset asOfUtc,
        int maxPendingDailyWindows,
        CancellationToken cancellationToken)
    {
        var pendingKeys = await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.EnvironmentId == environmentId)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.WindowEndUtc <= asOfUtc)
            .GroupBy(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.TagKey,
                WindowStartUtc = x.DailyWindowStartUtc
            })
            .Where(group => !dbContext.TelemetryRollups.Any(rollup =>
                rollup.OrganizationId == group.Key.OrganizationId
                && rollup.EnvironmentId == group.Key.EnvironmentId
                && rollup.DeviceAssetId == group.Key.DeviceAssetId
                && rollup.TagKey == group.Key.TagKey
                && rollup.Grain == TelemetryRollupGrain.Daily
                && rollup.WindowStartUtc == group.Key.WindowStartUtc))
            .OrderBy(group => group.Key.WindowStartUtc)
            .ThenBy(group => group.Key.DeviceAssetId)
            .ThenBy(group => group.Key.TagKey)
            .Take(maxPendingDailyWindows)
            .Select(group => new
            {
                group.Key.OrganizationId,
                group.Key.EnvironmentId,
                group.Key.DeviceAssetId,
                group.Key.TagKey,
                group.Key.WindowStartUtc
            })
            .ToArrayAsync(cancellationToken);
        if (pendingKeys.Length == 0)
        {
            return [];
        }

        var rollups = new List<TelemetryRollup>();
        foreach (var key in pendingKeys)
        {
            rollups.AddRange(await LoadHourlyDayAsync(
                new RollupWindowKey(key.OrganizationId, key.EnvironmentId, key.DeviceAssetId, key.TagKey, key.WindowStartUtc.UtcDateTime),
                asOfUtc,
                cancellationToken));
        }

        return rollups.ToArray();
    }

    private async Task<TelemetryRollup[]> LoadHourlyDayAsync(RollupWindowKey key, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var windowStartUtc = new DateTimeOffset(key.WindowStartUtc, TimeSpan.Zero);
        return await dbContext.TelemetryRollups
            .Where(x => x.OrganizationId == key.OrganizationId)
            .Where(x => x.EnvironmentId == key.EnvironmentId)
            .Where(x => x.DeviceAssetId == key.DeviceAssetId)
            .Where(x => x.TagKey == key.TagKey)
            .Where(x => x.Grain == TelemetryRollupGrain.Hourly)
            .Where(x => x.DailyWindowStartUtc == windowStartUtc)
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

    private sealed record RawWindowKey(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, DateTimeOffset WindowStartUtc);

    private sealed record RollupWindowKey(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, DateTime WindowStartUtc);
}
