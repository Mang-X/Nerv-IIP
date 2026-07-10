using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class MeasuringDeviceTests
{
    [Fact]
    public void Expired_device_moves_to_calibration_and_blocks_when_policy_requires_it()
    {
        var device = MeasuringDevice.Create("org-001", "env-dev", "MD-001", "Micrometer", "0.001mm", 30, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var state = device.EvaluateCalibration(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(MeasuringDeviceStatuses.Calibration, device.Status);
        Assert.Equal(MeasuringDeviceCalibrationStates.Overdue, state);
        Assert.True(MeasuringDeviceInspectionPolicy.Blocks("block", state));
    }

    [Fact]
    public void Calibration_record_restores_in_use_and_preserves_audit_fact()
    {
        var device = MeasuringDevice.Create("org-001", "env-dev", "MD-002", "Caliper", "0.01mm", 90, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        device.RecordCalibration("CAL-001", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), "metrology-user", "certificate-001");

        Assert.Equal(MeasuringDeviceStatuses.InUse, device.Status);
        var record = Assert.Single(device.CalibrationRecords);
        Assert.Equal("CAL-001", record.CalibrationNo);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero), device.CalibrationDueAtUtc);
    }

    [Fact]
    public void Inspection_usage_snapshot_preserves_the_device_calibration_state_at_entry()
    {
        var device = MeasuringDevice.Create("org-001", "env-dev", "MD-003", "Gauge", "0.01mm", 30, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var usage = InspectionMeasuringDeviceUsage.Create(device, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(device.Id, usage.MeasuringDeviceId);
        Assert.Equal("MD-003", usage.MeasuringDeviceCode);
        Assert.Equal(MeasuringDeviceCalibrationStates.Overdue, usage.CalibrationState);
    }
}
