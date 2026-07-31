using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceWorkOrderQueryScopeTests
{
    [Fact]
    public async Task Work_order_filters_apply_status_device_keyword_and_self_scope_before_total_and_paging()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var matching = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV-CNC-01", "high", "reporter", assignedTechnicianUserId: "tech-001");
        matching.Accept("tech-001");
        var wrongTechnician = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV-CNC-01", "high", "reporter", assignedTechnicianUserId: "tech-002");
        wrongTechnician.Accept("tech-002");
        var wrongDevice = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV-CNC-02", "high", "reporter", assignedTechnicianUserId: "tech-001");
        wrongDevice.Accept("tech-001");
        db.MaintenanceWorkOrders.AddRange(matching, wrongTechnician, wrongDevice);
        await db.SaveChangesAsync();

        var result = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery(
                "org-001",
                "env-dev",
                Status: nameof(MaintenanceWorkOrderStatus.Accepted),
                DeviceAssetId: "DEV-CNC-01",
                Keyword: "cnc-01",
                AssignedTechnicianUserIds: "tech-001",
                Skip: 0,
                Take: 1),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(matching.Id, Assert.Single(result.Items).WorkOrderId);
    }

    [Fact]
    public async Task Team_scope_is_distinct_from_self_scope()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var teamOnly = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-001", "high", "reporter");
        teamOnly.Assign(null, "team-001");
        db.MaintenanceWorkOrders.Add(teamOnly);
        await db.SaveChangesAsync();
        var handler = new ListMaintenanceWorkOrdersQueryHandler(db);

        var self = await handler.Handle(
            new ListMaintenanceWorkOrdersQuery("org-001", "env-dev", AssignedTechnicianUserIds: "tech-001"),
            CancellationToken.None);
        var team = await handler.Handle(
            new ListMaintenanceWorkOrdersQuery("org-001", "env-dev", AssignedTeamIds: "team-001"),
            CancellationToken.None);

        Assert.Empty(self.Items);
        Assert.Equal(teamOnly.Id, Assert.Single(team.Items).WorkOrderId);
    }
}
