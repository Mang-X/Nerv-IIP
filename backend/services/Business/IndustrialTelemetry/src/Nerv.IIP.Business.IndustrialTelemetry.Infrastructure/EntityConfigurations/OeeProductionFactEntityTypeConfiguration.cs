using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class OeeProductionFactEntityTypeConfiguration : IEntityTypeConfiguration<OeeProductionFact>
{
    public void Configure(EntityTypeBuilder<OeeProductionFact> builder)
    {
        builder.ToTable("oee_production_facts", tableBuilder =>
            tableBuilder.HasComment("MES production-report facts projected for explainable IndustrialTelemetry OEE calculations."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("OEE production fact aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
        builder.Property(x => x.SourceReportNo).HasColumnName("source_report_no").IsRequired().HasMaxLength(100).HasComment("MES production report number used as the idempotent projection key.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work center snapshot for the reported operation.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("MES assigned device asset used to scope OEE.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported accepted output quantity; reversals are negative.");
        builder.Property(x => x.ScrapQuantity).HasColumnName("scrap_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported scrap output quantity; reversals are negative.");
        builder.Property(x => x.ReworkQuantity).HasColumnName("rework_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported rework output quantity; reversals are negative.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(30).HasComment("Output quantity unit copied from the MES operation snapshot.");
        builder.Property(x => x.TheoreticalRatePerHour).HasColumnName("theoretical_rate_per_hour").HasPrecision(18, 6).HasComment("Expected output per productive hour from the MES operation planning snapshot.");
        builder.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired().HasComment("UTC instant assigned to the production report.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(100).HasComment("MasterData site code snapshot captured by MES when the report was recorded.");
        builder.Property(x => x.WorkshopCode).HasColumnName("workshop_code").HasMaxLength(100).HasComment("MasterData workshop code snapshot captured by MES when the report was recorded.");
        builder.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(100).HasComment("MasterData production line code snapshot captured by MES when the report was recorded.");
        builder.Property(x => x.ShiftCode).HasColumnName("shift_code").HasMaxLength(100).HasComment("Assigned MasterData shift code snapshot captured by MES when the report was recorded.");
        builder.Property(x => x.SiteTimezone).HasColumnName("site_timezone").HasMaxLength(100).HasComment("IANA site timezone snapshot used for historical day and shift boundaries.");
        builder.Property(x => x.ShiftStartsAt).HasColumnName("shift_starts_at").HasComment("Captured local shift start time.");
        builder.Property(x => x.ShiftEndsAt).HasColumnName("shift_ends_at").HasComment("Captured local shift end time.");
        builder.Property(x => x.ShiftCrossesMidnight).HasColumnName("shift_crosses_midnight").HasComment("Whether the captured shift definition crosses local midnight.");
        builder.Property(x => x.ShiftPaidMinutes).HasColumnName("shift_paid_minutes").HasComment("Paid minutes from the captured shift definition.");
        builder.Property(x => x.ShiftBreakMinutes).HasColumnName("shift_break_minutes").HasComment("Break minutes from the captured shift definition.");
        builder.Property(x => x.BusinessDate).HasColumnName("business_date").HasComment("Site-local calendar date containing the report.");
        builder.Property(x => x.DayBucketStartUtc).HasColumnName("day_bucket_start_utc").HasComment("UTC start of the captured site-local business day.");
        builder.Property(x => x.DayBucketEndUtc).HasColumnName("day_bucket_end_utc").HasComment("UTC end of the captured site-local business day.");
        builder.Property(x => x.ShiftBusinessDate).HasColumnName("shift_business_date").HasComment("Local date on which the captured shift instance starts.");
        builder.Property(x => x.ShiftBucketStartUtc).HasColumnName("shift_bucket_start_utc").HasComment("UTC start of the captured shift instance.");
        builder.Property(x => x.ShiftBucketEndUtc).HasColumnName("shift_bucket_end_utc").HasComment("UTC end of the captured shift instance.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceReportNo })
            .IsUnique()
            .HasDatabaseName("ux_oee_production_facts_scope_source_report_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.ReportedAtUtc })
            .HasDatabaseName("ix_oee_production_facts_scope_device_reported_at");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId, x.ReportedAtUtc })
            .HasDatabaseName("ix_oee_production_facts_scope_work_center_reported_at");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ShiftCode, x.ShiftBucketStartUtc })
            .HasDatabaseName("ix_oee_production_facts_scope_shift_bucket");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SiteCode, x.DayBucketStartUtc })
            .HasDatabaseName("ix_oee_production_facts_scope_day_bucket");
    }
}
