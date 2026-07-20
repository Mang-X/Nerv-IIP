using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string DeviceAssetId = "DEV-CNC-DEMO";
    public const string AlarmExternalId = "ALARM-DEMO-001";
    public const string WorkOrderReference = "MWO-DEMO-001";

    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.MaintenanceWorkOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.SourceAlarmId == AlarmExternalId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.DeviceAssetId != DeviceAssetId ||
                existing.Priority != "critical" ||
                existing.SourceType != MaintenanceWorkOrderSourceTypes.Alarm ||
                existing.SourceReferenceId != WorkOrderReference ||
                existing.Status != MaintenanceWorkOrderStatus.Open ||
                existing.CompletedAtUtc is not null ||
                existing.CompletionResult is not null ||
                existing.AlarmCleared ||
                existing.RepairStartedAtUtc is not null)
            {
                throw Collision();
            }

            return;
        }

        dbContext.MaintenanceWorkOrders.Add(MaintenanceWorkOrder.OpenFromAlarm(
            organizationId,
            environmentId,
            DeviceAssetId,
            AlarmExternalId,
            "critical",
            openedBy: "leader-demo-seed",
            diagnosticDescription: "High spindle temperature rehearsal prerequisite.",
            failureModeCode: "spindle-temperature-high",
            failureCauseCode: "leader-demo-prerequisite",
            estimatedLaborMinutes: 30,
            sourceReferenceId: WorkOrderReference));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InvalidOperationException Collision() =>
        new($"Reserved leader-demo maintenance reference '{WorkOrderReference}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
