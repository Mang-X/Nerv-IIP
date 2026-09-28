using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3902：备件领用必须带领出工厂与已登记库位。三个写入口（完工 v1、生命周期完工、单独登记备件）
/// 都在校验层拒绝缺失，行上保存领用时选定的工厂与库位，供出库请求按它们扣减库存。
/// </summary>
public sealed class MaintenanceSparePartIssueLocationTests
{
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
        var workOrder = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-01", "high", "operator-001");
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
