using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class MeasuringDeviceEntityTypeConfiguration : IEntityTypeConfiguration<MeasuringDevice>
{
    public void Configure(EntityTypeBuilder<MeasuringDevice> builder)
    {
        builder.ToTable("measuring_devices", table => table.HasComment("Quality measuring devices with calibration due-date lifecycle."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Measuring device aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").IsRequired().HasMaxLength(128).HasComment("Quality coding-engine allocated measuring device code.");
        builder.Property(x => x.DeviceType).HasColumnName("device_type").IsRequired().HasMaxLength(100).HasComment("Measuring device type.");
        builder.Property(x => x.Accuracy).HasColumnName("accuracy").IsRequired().HasMaxLength(100).HasComment("Declared measuring accuracy.");
        builder.Property(x => x.CalibrationIntervalDays).HasColumnName("calibration_interval_days").IsRequired().HasComment("Calibration cycle in days.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Device lifecycle status: in-use, calibration, disabled or retired.");
        builder.Property(x => x.LastCalibratedAtUtc).HasColumnName("last_calibrated_at_utc").HasComment("UTC time of latest accepted calibration.");
        builder.Property(x => x.CalibrationDueAtUtc).HasColumnName("calibration_due_at_utc").IsRequired().HasComment("UTC time when calibration is due.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CalibrationDueAtUtc, x.Status });
        builder.HasMany(x => x.CalibrationRecords).WithOne().HasForeignKey(x => x.MeasuringDeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CalibrationRecordEntityTypeConfiguration : IEntityTypeConfiguration<CalibrationRecord>
{
    public void Configure(EntityTypeBuilder<CalibrationRecord> builder)
    {
        builder.ToTable("calibration_records", table => table.HasComment("Immutable accepted calibration records for Quality measuring devices."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Calibration record id.");
        builder.Property(x => x.MeasuringDeviceId).HasColumnName("measuring_device_id").IsRequired().HasComment("Owning measuring device id.");
        builder.Property(x => x.CalibrationNo).HasColumnName("calibration_no").IsRequired().HasMaxLength(128).HasComment("Calibration record business code.");
        builder.Property(x => x.CalibratedAtUtc).HasColumnName("calibrated_at_utc").IsRequired().HasComment("UTC time calibration was accepted.");
        builder.Property(x => x.CalibratedBy).HasColumnName("calibrated_by").IsRequired().HasMaxLength(150).HasComment("Calibration operator or external provider reference.");
        builder.Property(x => x.CertificateFileId).HasColumnName("certificate_file_id").HasMaxLength(150).HasComment("Optional File Storage certificate reference.");
        builder.HasIndex(x => new { x.MeasuringDeviceId, x.CalibrationNo }).IsUnique();
    }
}
