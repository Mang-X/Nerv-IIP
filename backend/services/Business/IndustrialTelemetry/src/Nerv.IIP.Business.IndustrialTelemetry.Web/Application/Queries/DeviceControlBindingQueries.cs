using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record ListDeviceControlBindingsQuery(
    string? OrganizationId,
    string? EnvironmentId,
    string? DeviceAssetId,
    bool? IsActive,
    int Skip = 0,
    int Take = 100) : IQuery<PagedListResponse<DeviceControlBindingListItem>>;

public sealed record DeviceControlBindingListItem(
    DeviceControlChannelBindingId DeviceControlChannelBindingId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string ConnectorHostId,
    string InstanceKey,
    bool IsActive,
    string? DisabledReason,
    DateTimeOffset UpdatedAtUtc);

public sealed class ListDeviceControlBindingsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDeviceControlBindingsQuery, PagedListResponse<DeviceControlBindingListItem>>
{
    public async Task<PagedListResponse<DeviceControlBindingListItem>> Handle(ListDeviceControlBindingsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.DeviceControlChannelBindings
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.IsActive == null || x.IsActive == request.IsActive);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.DeviceAssetId)
            .Select(x => new DeviceControlBindingListItem(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.ConnectorHostId,
                x.InstanceKey,
                x.IsActive,
                x.DisabledReason,
                x.UpdatedAtUtc))
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArrayAsync(cancellationToken);
        return new PagedListResponse<DeviceControlBindingListItem>(items, total);
    }
}
