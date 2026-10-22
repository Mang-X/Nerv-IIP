using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Sdk.Core;
using Nerv.IIP.Sdk.Notification;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

public sealed class MaintenanceDowntimeEscalationScanner(ApplicationDbContext dbContext, INotificationClient client,
    IOptions<MaintenanceDowntimeEscalationOptions> options, TimeProvider timeProvider)
{
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        // #4131 / ADR 0032 §2: elapsed actual downtime, never elapsed prediction or repair labor.
        var cutoff = timeProvider.GetUtcNow() - options.Value.Threshold;
        foreach (var scope in options.Value.Scopes)
        {
            var orders = await dbContext.MaintenanceWorkOrders.AsNoTracking()
                .Where(x => x.OrganizationId == scope.OrganizationId && x.EnvironmentId == scope.EnvironmentId
                    && x.AssetUnavailable && x.AssetUnavailableFromUtc < cutoff
                    && x.CompletedAtUtc == null && x.CancelledAtUtc == null)
                .Select(x => new { x.Id, x.DeviceAssetId, FromUtc = x.AssetUnavailableFromUtc!.Value })
                .ToArrayAsync(cancellationToken);
            foreach (var order in orders)
            {
                // Stable across rescans, restarts, version/ETR edits; Notification owns durable dedupe.
                var key = $"maintenance-downtime-escalated:{order.Id}:{order.FromUtc.UtcTicks}";
                await client.SubmitIntentAsync(new(
                    SourceService: MaintenanceFacts.ServiceName,
                    SourceEventType: "MaintenanceDowntimeEscalated",
                    SourceEventId: key,
                    IntentType: NotificationContractConstants.IntentTypeMessage,
                    Severity: NotificationContractConstants.SeverityWarning,
                    DedupeKey: key,
                    Resource: new("maintenance-work-order", order.Id.ToString(), null),
                    Title: "设备停机超时，请计划员处理",
                    Summary: $"设备 {order.DeviceAssetId} 自 {order.FromUtc:O} 停机，已超过配置阈值 {options.Value.Threshold}，尚未实际恢复。",
                    SuggestedRecipientRefs: scope.PlannerRecipientRefs),
                    new PlatformRequestContext(scope.OrganizationId, scope.EnvironmentId, key), cancellationToken);
            }
        }
    }
}
