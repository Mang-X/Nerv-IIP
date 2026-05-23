using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record ListTelemetryTagsQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId) : IQuery<IReadOnlyCollection<TelemetryTagListItem>>;

public sealed record TelemetryTagListItem(TelemetryTagId TelemetryTagId, string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, string ValueType, string UnitCode, string SamplingPolicy);

public sealed class ListTelemetryTagsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTelemetryTagsQuery, IReadOnlyCollection<TelemetryTagListItem>>
{
    public async Task<IReadOnlyCollection<TelemetryTagListItem>> Handle(ListTelemetryTagsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.TelemetryTags
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.TagKey)
            .Select(x => new TelemetryTagListItem(x.Id, x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.ValueType, x.UnitCode, x.SamplingPolicy))
            .Take(200)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record ListAlarmEventsQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, string? Status) : IQuery<IReadOnlyCollection<AlarmEventListItem>>;

public sealed record AlarmEventListItem(AlarmEventId AlarmEventId, string OrganizationId, string EnvironmentId, string DeviceAssetId, string AlarmCode, string Severity, string Status, DateTimeOffset RaisedAtUtc, DateTimeOffset? ClearedAtUtc, string ExternalAlarmId);

public sealed class ListAlarmEventsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAlarmEventsQuery, IReadOnlyCollection<AlarmEventListItem>>
{
    public async Task<IReadOnlyCollection<AlarmEventListItem>> Handle(ListAlarmEventsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.AlarmEvents
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.Status == null || x.Status == request.Status)
            .OrderByDescending(x => x.RaisedAtUtc)
            .Select(x => new AlarmEventListItem(x.Id, x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.AlarmCode, x.Severity, x.Status, x.RaisedAtUtc, x.ClearedAtUtc, x.ExternalAlarmId))
            .Take(200)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record QueryDeviceStateTimelineQuery(string DeviceAssetId, string? OrganizationId, string? EnvironmentId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) : IQuery<IReadOnlyCollection<DeviceTimelineItem>>;

public sealed record DeviceTimelineItem(string ItemType, string DeviceAssetId, string? TagKey, string Value, DateTimeOffset OccurredAtUtc);

public sealed class QueryDeviceStateTimelineQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryDeviceStateTimelineQuery, IReadOnlyCollection<DeviceTimelineItem>>
{
    public async Task<IReadOnlyCollection<DeviceTimelineItem>> Handle(QueryDeviceStateTimelineQuery request, CancellationToken cancellationToken)
    {
        var states = await dbContext.DeviceStateSnapshots
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.OccurredAtUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.OccurredAtUtc <= request.ToUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new DeviceTimelineItem("state", x.DeviceAssetId, null, x.State, x.OccurredAtUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var summaries = await dbContext.TelemetrySummaries
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.BucketEndUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.BucketStartUtc <= request.ToUtc)
            .OrderByDescending(x => x.BucketEndUtc)
            .Select(x => new DeviceTimelineItem("summary", x.DeviceAssetId, x.TagKey, x.AverageValue.ToString(), x.BucketEndUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);

        return states.Concat(summaries)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(100)
            .ToArray();
    }
}
