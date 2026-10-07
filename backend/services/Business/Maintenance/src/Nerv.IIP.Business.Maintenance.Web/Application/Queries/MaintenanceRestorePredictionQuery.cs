using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Queries;

public sealed class MaintenanceRestorePredictionOptions
{
    public int DefaultDowntimeMinutes { get; set; } = 60;
}

// Service-owned selection for #4127 / ADR 0032 §2; shared EquipmentRuntime mapping belongs to #4128.
public sealed record GetMaintenanceRestorePredictionQuery(
    string OrganizationId,
    string EnvironmentId,
    MaintenanceWorkOrderId WorkOrderId) : IQuery<MaintenanceRestorePredictionResult>;

public sealed record MaintenanceRestorePredictionResult(
    DateTimeOffset FromUtc,
    DateTimeOffset? ExplicitExpectedRestoreAtUtc,
    DateTimeOffset PredictedRestoreAtUtc,
    decimal? SelectedDowntimeMinutes,
    string Source,
    string SourceVersion);

public sealed class GetMaintenanceRestorePredictionQueryHandler(
    ApplicationDbContext dbContext,
    IOptions<MaintenanceRestorePredictionOptions> options)
    : IQueryHandler<GetMaintenanceRestorePredictionQuery, MaintenanceRestorePredictionResult>
{
    public async Task<MaintenanceRestorePredictionResult> Handle(
        GetMaintenanceRestorePredictionQuery request, CancellationToken cancellationToken)
    {
        var order = await dbContext.MaintenanceWorkOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId && x.Id == request.WorkOrderId)
            .Select(x => new { x.DeviceAssetId, x.AssetUnavailableFromUtc, x.ExpectedRestoreAtUtc, x.Version })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Maintenance work order was not found: {request.WorkOrderId}");
        var from = order.AssetUnavailableFromUtc
            ?? throw new KnownException("A restoration prediction requires an actual downtime start.");
        if (order.ExpectedRestoreAtUtc is { } expected)
        {
            return new(from, expected, expected, null, "explicit-etr",
                MaintenanceIdempotencyFingerprints.Hash(new { request.WorkOrderId, order.Version, ExpectedRestoreAtUtc = expected }));
        }

        // All ended downtime history within exactly this organization/environment/device.
        // Completion and cancellation emit actual restore; alarm clearance and repair labor do not.
        var history = await dbContext.MaintenanceWorkOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId && x.DeviceAssetId == order.DeviceAssetId
                && x.AssetUnavailableFromUtc != null)
            .Select(x => new { x.Id, FromUtc = x.AssetUnavailableFromUtc!.Value, ReleasedAtUtc = x.CompletedAtUtc ?? x.CancelledAtUtc })
            .Where(x => x.ReleasedAtUtc != null && x.ReleasedAtUtc > x.FromUtc)
            .ToArrayAsync(cancellationToken);
        if (history.Length > 0)
        {
            var minutes = history.Average(x => (decimal)(x.ReleasedAtUtc!.Value - x.FromUtc).TotalMinutes);
            var version = MaintenanceIdempotencyFingerprints.Hash(history.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal));
            return new(from, null, from.AddMinutes((double)minutes), minutes, "device-mttr", version);
        }

        var fallback = options.Value.DefaultDowntimeMinutes;
        return new(from, null, from.AddMinutes(fallback), fallback, "configuration-default",
            MaintenanceIdempotencyFingerprints.Hash(new { DefaultDowntimeMinutes = fallback }));
    }
}
