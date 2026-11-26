using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3902：备件领用必须带领出工厂与已登记库位。三个写入口（完工 v1、生命周期完工、单独登记备件）
/// 都在校验层拒绝缺失，行上保存领用时选定的工厂与库位，供出库请求按它们扣减库存。
/// </summary>
public sealed class MaintenanceSparePartIssueLocationTests
{
    [Fact]
    public async Task Inventory_rejection_is_visible_on_the_matching_issue_line_only()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var order = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "MWO-001", "DEV-001", "high", "operator");
        order.Accept("technician");
        order.StartWork();
        order.Complete("fixed", "DT-MECH", 10,
            [new("SPARE-01", 1, "pcs", "SITE-01", "LOC-01"), new("SPARE-02", 2, "pcs", "SITE-01", "LOC-01")]);
        db.MaintenanceWorkOrders.Add(order);
        await db.SaveChangesAsync();

        var line = Assert.Single(order.SparePartLines, x => x.SkuCode == "SPARE-01");
        var at = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);
        var rejected = new StockMovementPostingFailedIntegrationEvent(
            "event-rejected", InventoryIntegrationEventTypes.StockMovementPostingFailed, InventoryIntegrationEventVersions.V1,
            at, InventoryIntegrationEventSources.BusinessInventory, order.Id.ToString(), "cause-rejected", "org-001", "env-dev",
            "inventory", "stock-rejection", new(
                InventoryMovementTypes.Outbound, InventoryMovementSourceServices.Maintenance, order.WorkOrderNo,
                line.Id.ToString(), $"maintenance:org-001:env-dev:{order.Id}:{line.Id}", line.SkuCode, "pcs",
                "SITE-01", "LOC-01", null, null, "available", "organization", null, 1,
                "insufficient-stock", "库位库存不足", at));
        var handler = new MarkSparePartPostingFailedHandler(db, new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(rejected with { OrganizationId = "another-org" }, CancellationToken.None);
        await handler.HandleAsync(rejected, CancellationToken.None);
        await handler.HandleAsync(rejected, CancellationToken.None);
        db.ChangeTracker.Clear();

        var lines = await new ListMaintenanceSparePartsQueryHandler(db).Handle(new("org-001", "env-dev"), CancellationToken.None);
        Assert.Equal("库位库存不足", Assert.Single(lines.Items, x => x.SkuCode == "SPARE-01").InventoryPostingFailureReason);
        Assert.Null(Assert.Single(lines.Items, x => x.SkuCode == "SPARE-02").InventoryPostingFailureReason);
        Assert.Equal(1, await db.ProcessedIntegrationEvents.CountAsync());
    }

    public static TheoryData<string?, string?, string> MissingLocations => new()
    {
        { null, "loc-spare-01", nameof(MaintenanceSparePartInput.SiteCode) },
        { "  ", "loc-spare-01", nameof(MaintenanceSparePartInput.SiteCode) },
        { "SITE-001", null, nameof(MaintenanceSparePartInput.LocationCode) },
        { "SITE-001", "", nameof(MaintenanceSparePartInput.LocationCode) },
    };

    [Theory]
    [MemberData(nameof(MissingLocations))]
    public void Complete_v1_rejects_spare_parts_without_issue_location(string? siteCode, string? locationCode, string property)
    {
        var result = new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "fixed",
                "DT-MECH",
                10,
                [new MaintenanceSparePartInput("SPARE-001", 1m, "pcs", siteCode, locationCode)]));

        AssertRejects(result, $"SpareParts[0].{property}");
    }

    [Theory]
    [MemberData(nameof(MissingLocations))]
    public void Lifecycle_complete_rejects_spare_parts_without_issue_location(string? siteCode, string? locationCode, string property)
    {
        var result = new TransitionMaintenanceWorkOrderCommandValidator().Validate(
            new TransitionMaintenanceWorkOrderCommand(
                "org-001",
                "env-dev",
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                MaintenanceWorkOrderAction.Complete,
                "technician-001",
                "repaired",
                "complete-001",
                3,
                Result: "fixed",
                DowntimeReasonCode: "DT-MECH",
                DowntimeMinutes: 10,
                SpareParts: [new MaintenanceSparePartInput("SPARE-001", 1m, "pcs", siteCode, locationCode)]));

        AssertRejects(result, $"SpareParts[0].{property}");
    }

    [Theory]
    [MemberData(nameof(MissingLocations))]
    public void Standalone_spare_part_rejects_missing_issue_location(string? siteCode, string? locationCode, string property)
    {
        var result = new CreateMaintenanceSparePartCommandValidator().Validate(
            new CreateMaintenanceSparePartCommand(
                "org-001",
                "env-dev",
                new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                "SPARE-001",
                1m,
                "pcs",
                siteCode,
                locationCode));

        AssertRejects(result, property);
    }

    [Fact]
    public void All_three_entries_accept_a_complete_issue_location()
    {
        var input = new MaintenanceSparePartInput("SPARE-001", 1m, "pcs", "SITE-001", "loc-spare-01");
        Assert.True(new CompleteMaintenanceWorkOrderCommandValidator().Validate(
            new CompleteMaintenanceWorkOrderCommand(new MaintenanceWorkOrderId(Guid.CreateVersion7()), "fixed", "DT-MECH", 10, [input])).IsValid);
        Assert.True(new TransitionMaintenanceWorkOrderCommandValidator().Validate(
            new TransitionMaintenanceWorkOrderCommand(
                "org-001", "env-dev", new MaintenanceWorkOrderId(Guid.CreateVersion7()), MaintenanceWorkOrderAction.Complete,
                "technician-001", "repaired", "complete-001", 3,
                Result: "fixed", DowntimeReasonCode: "DT-MECH", DowntimeMinutes: 10, SpareParts: [input])).IsValid);
        Assert.True(new CreateMaintenanceSparePartCommandValidator().Validate(
            new CreateMaintenanceSparePartCommand(
                "org-001", "env-dev", new MaintenanceWorkOrderId(Guid.CreateVersion7()), "SPARE-001", 1m, "pcs", "SITE-001", "loc-spare-01")).IsValid);
    }

    [Fact]
    public async Task Complete_v1_persists_the_issue_location_on_each_spare_part_line()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        dbContext.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "DT-MECH", "机械故障", "breakdown", "availability"));
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", $"MWO-T-{Guid.NewGuid():N}", "DEV-CNC-01", "high", "operator-001");
        workOrder.Accept(workOrder.AssignedTechnicianUserId ?? "technician-001");
        workOrder.StartWork();
        dbContext.MaintenanceWorkOrders.Add(workOrder);
        await dbContext.SaveChangesAsync();

        await new CompleteMaintenanceWorkOrderCommandHandler(dbContext).Handle(
            new CompleteMaintenanceWorkOrderCommand(
                workOrder.Id,
                "fixed",
                "DT-MECH",
                10,
                [
                    new MaintenanceSparePartInput("SPARE-001", 1m, "pcs", "SITE-001", "loc-spare-01"),
                    new MaintenanceSparePartInput("SPARE-002", 3m, "pcs", "SITE-001", "loc-spare-02"),
                ],
                OrganizationId: "org-001",
                EnvironmentId: "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var lines = await dbContext.SparePartLines.AsNoTracking().OrderBy(x => x.SkuCode).ToListAsync();
        Assert.Collection(
            lines,
            line => Assert.Equal(("SITE-001", "loc-spare-01"), (line.SiteCode, line.LocationCode)),
            line => Assert.Equal(("SITE-001", "loc-spare-02"), (line.SiteCode, line.LocationCode)));
    }

    private static void AssertRejects(ValidationResult result, string propertyName)
    {
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            x => string.Equals(x.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));
    }
}
