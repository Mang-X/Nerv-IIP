using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceWorkOrderAssetUnavailableReasonTests
{
    [Fact]
    public void Create_request_rejects_an_unavailable_reason_longer_than_the_catalog_code_limit()
    {
        var request = new CreateMaintenanceWorkOrderRequest(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            null,
            "emp010",
            new string('A', 101),
            "asset-unavailable-long-code");

        var result = new CreateMaintenanceWorkOrderRequestValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(request.AssetUnavailableReason));
    }

    [Fact]
    public async Task Dynamic_catalog_code_is_preserved_through_work_order_and_unavailable_event()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.DowntimeReasons.Add(DowntimeReason.Create(
            "org-001",
            "env-dev",
            "DT-CUSTOM-17",
            "Custom spindle stop"));
        await db.SaveChangesAsync();

        await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
            CreateCommand("DT-CUSTOM-17"),
            CancellationToken.None);

        var workOrder = Assert.Single(db.MaintenanceWorkOrders.Local);
        var domainEvent = Assert.Single(workOrder.GetDomainEvents().OfType<AssetUnavailableDomainEvent>());
        var integrationEvent = new AssetUnavailableIntegrationEventConverter().Convert(domainEvent);
        Assert.True(workOrder.AssetUnavailable);
        Assert.Equal("DT-CUSTOM-17", workOrder.AssetUnavailableReason);
        Assert.Equal("DT-CUSTOM-17", domainEvent.ReasonCode);
        Assert.Equal("DT-CUSTOM-17", integrationEvent.Payload.Reason);
    }

    [Theory]
    [InlineData("over temperature")]
    [InlineData("主轴过热")]
    public async Task Free_text_reason_is_rejected_without_creating_an_unavailable_fact(string freeText)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
                CreateCommand(freeText),
                CancellationToken.None));

        Assert.Equal($"Downtime reason was not found: {freeText}", exception.Message);
        Assert.Empty(db.MaintenanceWorkOrders.Local);
        Assert.Equal(0, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Catalog_code_from_another_scope_is_rejected_without_creating_an_unavailable_fact(
        string catalogOrganizationId,
        string catalogEnvironmentId)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.DowntimeReasons.Add(DowntimeReason.Create(
            catalogOrganizationId,
            catalogEnvironmentId,
            "DT-SCOPED-01",
            "Scoped reason"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
                CreateCommand("DT-SCOPED-01"),
                CancellationToken.None));

        Assert.Equal("Downtime reason was not found: DT-SCOPED-01", exception.Message);
        Assert.Empty(db.MaintenanceWorkOrders.Local);
        Assert.Equal(0, await db.MaintenanceWorkOrders.CountAsync());
    }

    [Fact]
    public async Task Null_reason_keeps_the_work_order_available()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        await new CreateMaintenanceWorkOrderCommandHandler(db).Handle(
            CreateCommand(null),
            CancellationToken.None);

        var workOrder = Assert.Single(db.MaintenanceWorkOrders.Local);
        Assert.False(workOrder.AssetUnavailable);
        Assert.Null(workOrder.AssetUnavailableReason);
        Assert.DoesNotContain(workOrder.GetDomainEvents(), x => x is AssetUnavailableDomainEvent);
    }

    private static CreateMaintenanceWorkOrderCommand CreateCommand(string? assetUnavailableReason) =>
        new(
            "org-001",
            "env-dev",
            "DEV-CNC-01",
            "high",
            null,
            "emp010",
            assetUnavailableReason,
            IdempotencyKey: $"asset-unavailable-{Guid.CreateVersion7():N}");
}
