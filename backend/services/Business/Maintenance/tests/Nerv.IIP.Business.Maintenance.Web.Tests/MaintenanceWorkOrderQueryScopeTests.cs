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

    [Fact]
    public async Task Device_reference_csv_matches_public_id_and_business_code_before_paging()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        const string publicId = "019f0000-0000-7000-8000-000000000001";
        const string businessCode = "DEV-CNC-01";
        var storedByPublicId = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", publicId, "high", "reporter", assignedTechnicianUserId: "tech-001");
        var storedByBusinessCode = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", businessCode, "high", "reporter", assignedTechnicianUserId: "tech-001");
        db.MaintenanceWorkOrders.AddRange(storedByPublicId, storedByBusinessCode);
        await db.SaveChangesAsync();

        var result = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery(
                "org-001",
                "env-dev",
                DeviceAssetIds: $"{publicId},{businessCode}",
                AssignedTechnicianUserIds: "tech-001",
                Skip: 0,
                Take: 1),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Contains(item.DeviceAssetId, new[] { publicId, businessCode });
    }

    [Fact]
    public async Task Exact_device_references_preserve_commas_and_do_not_match_split_fragments()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var exact = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV,A", "high", "reporter", assignedTechnicianUserId: "tech-001");
        var firstFragment = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV", "high", "reporter", assignedTechnicianUserId: "tech-001");
        var secondFragment = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "A", "high", "reporter", assignedTechnicianUserId: "tech-001");
        db.MaintenanceWorkOrders.AddRange(exact, firstFragment, secondFragment);
        await db.SaveChangesAsync();

        var result = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery(
                "org-001",
                "env-dev",
                DeviceAssetReferences: ["DEV,A"],
                AssignedTechnicianUserIds: "tech-001"),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(exact.Id, Assert.Single(result.Items).WorkOrderId);
    }

    [Fact]
    public async Task Detail_derives_actions_and_block_reasons_from_status_and_persisted_business_data()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var normal = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-NORMAL", "high", "reporter");
        var terminal = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-TERMINAL", "high", "reporter");
        terminal.Cancel();
        var missingData = MaintenanceWorkOrder.OpenManual(
            "org-001", "env-dev", "DEV-MISSING", "high", "reporter", assignedTechnicianUserId: "tech-001");
        missingData.Accept("tech-001");
        missingData.StartWork();
        missingData.Finish("fixed", "failure", 5, [], "tech-001");
        db.Entry(missingData).Property(x => x.CompletionResult).CurrentValue = null;
        db.MaintenanceWorkOrders.AddRange(normal, terminal, missingData);
        await db.SaveChangesAsync();
        var handler = new GetMaintenanceWorkOrderQueryHandler(db);

        var normalDetail = await handler.Handle(
            new GetMaintenanceWorkOrderQuery("org-001", "env-dev", normal.Id), CancellationToken.None);
        var terminalDetail = await handler.Handle(
            new GetMaintenanceWorkOrderQuery("org-001", "env-dev", terminal.Id), CancellationToken.None);
        var missingDetail = await handler.Handle(
            new GetMaintenanceWorkOrderQuery("org-001", "env-dev", missingData.Id), CancellationToken.None);

        Assert.Equal(["assign", "accept", "cancel"], normalDetail.AllowedActions);
        Assert.Empty(normalDetail.BlockReasons);
        Assert.Empty(terminalDetail.AllowedActions);
        Assert.Equal(["terminal-status"], terminalDetail.BlockReasons);
        Assert.Empty(missingDetail.AllowedActions);
        Assert.Equal(["completion-data-incomplete"], missingDetail.BlockReasons);
    }

    [Fact]
    public void Unknown_status_is_read_only_and_explainable()
    {
        var eligibility = MaintenanceWorkOrderEligibility.Evaluate("FutureState", completionDataComplete: true);

        Assert.Empty(eligibility.AllowedActions);
        Assert.Equal(["unknown-status"], eligibility.BlockReasons);
    }
}
