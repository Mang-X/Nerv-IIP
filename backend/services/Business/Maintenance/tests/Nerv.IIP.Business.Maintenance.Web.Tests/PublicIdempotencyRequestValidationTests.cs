using FluentValidation;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class PublicIdempotencyRequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Public_maintenance_write_requests_reject_missing_or_blank_idempotency_keys(string? key)
    {
        Assert.False(new CreateMaintenanceWorkOrderRequestValidator().Validate(
            new CreateMaintenanceWorkOrderRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                DeviceAssetId: "DEV-001",
                Priority: "high",
                SourceAlarmId: null,
                OpenedBy: "operator-001",
                AssetUnavailableReason: null,
                IdempotencyKey: key!)).IsValid);

        Assert.False(new CompleteMaintenanceWorkOrderRequestValidator().Validate(
            new CompleteMaintenanceWorkOrderRequest(
                WorkOrderId: new MaintenanceWorkOrderId(Guid.CreateVersion7()),
                Result: "fixed",
                DowntimeReasonCode: "equipment-failure",
                DowntimeMinutes: 10,
                SpareParts: [],
                IdempotencyKey: key!,
                OrganizationId: "org-001",
                EnvironmentId: "env-dev")).IsValid);
    }
}
