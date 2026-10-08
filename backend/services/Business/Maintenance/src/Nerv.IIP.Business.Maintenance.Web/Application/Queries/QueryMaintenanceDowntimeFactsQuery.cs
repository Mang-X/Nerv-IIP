using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Queries;

public sealed record QueryMaintenanceDowntimeFactsQuery(MaintenanceDowntimeFactsRequest Request)
    : IQuery<MaintenanceDowntimeFactsResponse>;

public sealed class QueryMaintenanceDowntimeFactsQueryHandler(
    ApplicationDbContext dbContext,
    IRequestHandler<GetMaintenanceRestorePredictionQuery, MaintenanceRestorePredictionResult> restorePredictions)
    : IQueryHandler<QueryMaintenanceDowntimeFactsQuery, MaintenanceDowntimeFactsResponse>
{
    public async Task<MaintenanceDowntimeFactsResponse> Handle(QueryMaintenanceDowntimeFactsQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var from = request.WindowStartUtc.ToUniversalTime();
        var to = request.WindowEndUtc.ToUniversalTime();
        var devices = request.DeviceAssetIds.ToArray();
        var orders = await dbContext.MaintenanceWorkOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => devices.Contains(x.DeviceAssetId))
            .Where(x => x.AssetUnavailable && x.AssetUnavailableFromUtc != null && x.AssetUnavailableFromUtc < to)
            .Select(x => new
            {
                x.Id, x.DeviceAssetId, x.SourceType, x.SourceReferenceId,
                FromUtc = x.AssetUnavailableFromUtc!.Value,
                ReleasedAtUtc = x.CompletedAtUtc ?? x.CancelledAtUtc,
            })
            .Where(x => x.ReleasedAtUtc == null || x.ReleasedAtUtc > from)
            .OrderBy(x => x.FromUtc)
            .ToArrayAsync(cancellationToken);
        var facts = new List<MaintenanceDowntimeFact>();
        foreach (var order in orders)
        {
            var prediction = order.ReleasedAtUtc is null
                ? await restorePredictions.Handle(new(request.OrganizationId, request.EnvironmentId, order.Id), cancellationToken)
                : null;
            facts.Add(new(order.DeviceAssetId, order.Id.ToString(), "maintenance", order.SourceType,
                order.SourceReferenceId, order.FromUtc, order.ReleasedAtUtc,
                prediction?.ExplicitExpectedRestoreAtUtc, prediction?.PredictedRestoreAtUtc,
                prediction?.Source, prediction?.SourceVersion));
        }
        return new(facts);
    }
}
